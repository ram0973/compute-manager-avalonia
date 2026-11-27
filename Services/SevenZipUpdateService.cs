using System.Security.Cryptography;
using System.Text;

namespace ComputerManager.Services;

public record UpdateResult(string Computer, bool Success, string Message);

/// <summary>
/// Удалённая замена 7-Zip через PsExec. Защита:
///  1) PsExec64.exe и 7-zip.msi сверяются с SHA-256, вшитым в exe при сборке;
///  2) скрипт вшит в код программы, на ПК копируется под случайным именем, и перед запуском его хэш проверяется;
///  3) хэш MSI проверяется на удалённом ПК ещё раз — до удаления старой версии.
/// Файлы: Install\7-zip\7-zip.msi, Install\PsExec64\PsExec64.exe. Только Windows.
/// </summary>
public class SevenZipUpdateService
{
    private static readonly string MsiPath = Path.Combine(AppPaths.InstallDir, "7-zip", "7-zip.msi");

    public static string? Validate()
    {
        if (PsExecService.Validate() is { } e1) return e1;
        return InstallIntegrity.Verify(MsiPath, out _);
    }

    public async Task<UpdateResult> UpdateAsync(string host)
    {
        var log = new StringBuilder();
        void Log(string s) { lock (log) log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}"); }

        var tmp = $@"\\{host}\ADMIN$\Temp";
        var guid = Guid.NewGuid().ToString("N");
        var msiName = $"7zip-{guid}.msi";
        var scriptName = $"7zip-{guid}.ps1";
        bool success = false;
        string message;

        try
        {
            if (!PsExecService.IsSafeHost(host)) throw new ArgumentException($"Недопустимое имя хоста: {host}");
            if (Validate() is { } error) throw new InvalidOperationException(error);
            InstallIntegrity.Verify(MsiPath, out var msiHash);

            var script = RemoteScript.Replace("__MSI_NAME__", msiName).Replace("__MSI_SHA256__", msiHash);
            var scriptBytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(script)).ToArray();
            var scriptHash = Convert.ToHexString(SHA256.HashData(scriptBytes));

            Log("Копирование файлов на ПК…");
            await Task.Run(() =>
            {
                File.Copy(MsiPath, Path.Combine(tmp, msiName), true);
                File.WriteAllBytes(Path.Combine(tmp, scriptName), scriptBytes);
            });

            // сначала проверяем хэш скрипта, и только потом запускаем его
            var bootstrap =
                $"$f='C:\\Windows\\Temp\\{scriptName}'; " +
                $"if ((Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash -ne '{scriptHash}') " +
                "{ Write-Error 'Script hash mismatch'; exit 12 }; & $f; exit $LASTEXITCODE";

            Log("Запуск PsExec…");
            var r = await PsExecService.RunAsync(host,
                ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", bootstrap],
                system: true, TimeSpan.FromMinutes(10), Log);

            success = !r.TimedOut && r.ExitCode == 0;
            message = r.TimedOut ? "таймаут (10 мин)" : success ? "OK" : $"код {r.ExitCode}";
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Log("Ошибка: " + ex.Message);
        }
        finally
        {
            try { File.Delete(Path.Combine(tmp, msiName)); } catch { }
            try { File.Delete(Path.Combine(tmp, scriptName)); } catch { }
        }

        Log($"Итог: {(success ? "успех" : "ошибка")} ({message})");
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            File.WriteAllText(Path.Combine(AppPaths.LogDir, $"7zip-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.log"), log.ToString());
        }
        catch { }

        return new UpdateResult(host, success, message);
    }

    /// <summary>Выполняется НА удалённом ПК от SYSTEM: проверить MSI, закрыть 7-Zip, удалить все версии, поставить MSI.</summary>
    private const string RemoteScript = """"
        $ErrorActionPreference = 'Continue'
        $msi = 'C:\Windows\Temp\__MSI_NAME__'
        $expected = '__MSI_SHA256__'
        $log = 'C:\Windows\Temp\7zip-install.log'

        if (-not (Test-Path -LiteralPath $msi)) { Write-Error "Нет файла $msi"; exit 10 }

        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $msi).Hash
        if ($actual -ne $expected) {
            Write-Error "Хэш MSI не совпадает (ожидался $expected, получен $actual)"
            Remove-Item -LiteralPath $msi -Force -ErrorAction SilentlyContinue
            exit 11
        }
        Write-Host "Хэш MSI совпал: $actual"

        Get-Process 7zFM, 7zG -ErrorAction SilentlyContinue | Stop-Process -Force

        $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
                'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        $old = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
                 Where-Object { $_.DisplayName -like '7-Zip*' })

        if ($old.Count -eq 0) { Write-Host 'Старых версий 7-Zip не найдено' }

        foreach ($app in $old) {
            Write-Host "Удаляю: $($app.DisplayName)"
            if ($app.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') {
                $p = Start-Process msiexec.exe -ArgumentList "/x $($app.PSChildName) /qn /norestart" -Wait -PassThru
                Write-Host "  msiexec /x -> код $($p.ExitCode)"
            }
            else {
                $exe = ($app.UninstallString -replace '"', '').Trim()
                if (Test-Path $exe) {
                    Start-Process $exe -ArgumentList '/S' -Wait
                    Start-Sleep -Seconds 3
                    Write-Host "  Uninstall.exe /S выполнен"
                }
                else { Write-Warning "  Не найден деинсталлятор: $exe" }
            }
        }

        $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$log`"" -Wait -PassThru
        Write-Host "Установка MSI -> код $($p.ExitCode)"

        $exe7z = @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") |
                 Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($exe7z) { Write-Host "Установлена версия: $((Get-Item $exe7z).VersionInfo.ProductVersion)" }

        Remove-Item -LiteralPath $msi -Force -ErrorAction SilentlyContinue
        if ($p.ExitCode -in 0, 3010) { exit 0 } else { exit $p.ExitCode }
        """";
}

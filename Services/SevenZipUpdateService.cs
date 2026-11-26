using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ComputerManager.Services;

public record UpdateResult(string Computer, bool Success, string Message);

/// <summary>
/// Удалённая замена 7-Zip через PsExec: копирует MSI и скрипт в \\ПК\ADMIN$\Temp
/// и запускает скрипт от SYSTEM. Только Windows.
/// Файлы ищутся рядом с программой: Install\7-zip\7-zip.msi и Install\PsExec64\PsExec64.exe
/// (скрипт для удалённого ПК встроен в код — отдельный .ps1 не нужен).
/// </summary>
public class SevenZipUpdateService
{
    private static readonly string InstallDir = Path.Combine(AppContext.BaseDirectory, "Install");
    private static readonly string MsiPath = Path.Combine(InstallDir, "7-zip", "7-zip.msi");
    private static readonly string PsExecPath = Path.Combine(InstallDir, "PsExec64", "PsExec64.exe");

    public static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComputerManager", "logs");

    static SevenZipUpdateService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>null — всё готово; иначе текст ошибки.</summary>
    public static string? Validate()
    {
        if (!OperatingSystem.IsWindows()) return "Обновление 7-Zip через PsExec работает только в Windows.";
        if (!File.Exists(PsExecPath)) return $"Не найден PsExec: {PsExecPath}";
        if (!File.Exists(MsiPath)) return $"Не найден MSI: {MsiPath}";
        return null;
    }

    public async Task<UpdateResult> UpdateAsync(string host)
    {
        var log = new StringBuilder();
        void Log(string s) { lock (log) log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}"); }

        var tmp = $@"\\{host}\ADMIN$\Temp";
        bool success = false;
        string message;

        try
        {
            Log("Копирование файлов на ПК…");
            await Task.Run(() =>
            {
                File.Copy(MsiPath, Path.Combine(tmp, "7-zip.msi"), true);
                File.WriteAllText(Path.Combine(tmp, "Remote-7Zip.ps1"), RemoteScript, new UTF8Encoding(true));
            });

            Log("Запуск PsExec…");
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo(PsExecPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[]
            {
                $@"\\{host}", "-accepteula", "-nobanner", "-s",
                "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", @"C:\Windows\Temp\Remote-7Zip.ps1",
            }) psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
            p.Start();
            p.StandardInput.Close();   // иначе powershell через PsExec может зависнуть, ожидая stdin
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {
                await p.WaitForExitAsync(cts.Token);
                p.WaitForExit(); // дочитать буферы вывода
                success = p.ExitCode == 0;
                message = success ? "OK" : $"код {p.ExitCode}";
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                message = "таймаут (10 мин)";
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Log("Ошибка: " + ex.Message);
        }
        finally
        {
            try { File.Delete(Path.Combine(tmp, "7-zip.msi")); } catch { }
            try { File.Delete(Path.Combine(tmp, "Remote-7Zip.ps1")); } catch { }
        }

        Log($"Итог: {(success ? "успех" : "ошибка")} ({message})");
        try
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, $"7zip-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.log"), log.ToString());
        }
        catch { }

        return new UpdateResult(host, success, message);
    }

    /// <summary>Выполняется НА удалённом ПК от SYSTEM: закрыть 7-Zip, удалить все версии, поставить MSI.</summary>
    private const string RemoteScript = """"
        $ErrorActionPreference = 'Continue'
        $msi = 'C:\Windows\Temp\7-zip.msi'
        $log = 'C:\Windows\Temp\7zip-install.log'

        if (-not (Test-Path $msi)) { Write-Error "Нет файла $msi"; exit 10 }

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

        if ($p.ExitCode -in 0, 3010) { exit 0 } else { exit $p.ExitCode }
        """";
}

using System.Text;
using System.Text.RegularExpressions;
using ComputerManager.Models;

namespace ComputerManager.Services;

/// <summary>
/// Установка пакета из каталога на удалённый ПК через PsExec. Защита:
///  1) установщик сверяется с SHA-256 из каталога на этой машине и ещё раз — на удалённом ПК, до запуска;
///  2) скрипт Scripts\Install-Package.ps1 вшит в exe, на ПК копируется под случайным именем, его хэш проверяется перед запуском;
///  3) параметры пакета передаются как экранированные строки PowerShell.
/// Только Windows.
/// </summary>
public partial class PackageInstallService
{
    private const string ScriptName = "Install-Package.ps1";

    public static string? Validate(PackageInfo p) => PsExecService.Validate() ?? PackageCatalog.Verify(p);

    public async Task<UpdateResult> InstallAsync(string host, PackageInfo p, Action<string>? onLog = null)
    {
        var log = new StringBuilder();
        void Log(string s)
        {
            lock (log) log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}");
            onLog?.Invoke(s);
        }

        var tmp = $@"\\{host}\ADMIN$\Temp";
        var guid = Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(p.Installer).ToLowerInvariant();
        var installerName = $"pkg-{guid}{ext}";
        var scriptName = $"pkg-{guid}.ps1";
        bool success = false;
        string message;

        try
        {
            if (!PsExecService.IsSafeHost(host)) throw new ArgumentException($"Недопустимое имя хоста: {host}");
            if (Validate(p) is { } error) throw new InvalidOperationException(error);

            var kill = (p.KillProcesses ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (kill.Any(k => !SafeProcessRegex().IsMatch(k)))
                throw new ArgumentException("Недопустимое имя процесса в «Закрыть процессы».");

            var scriptBytes = ScriptStore.ToUtf8Bom(ScriptStore.Read(ScriptName));
            var scriptHash = ScriptStore.Sha256Hex(scriptBytes);

            Log($"Копирование {p.Installer} на ПК…");
            await Task.Run(() =>
            {
                File.Copy(p.FullPath, Path.Combine(tmp, installerName), true);
                File.WriteAllBytes(Path.Combine(tmp, scriptName), scriptBytes);
            });

            var bootstrap =
                $"$f='C:\\Windows\\Temp\\{scriptName}'; " +
                $"if ((Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash -ne '{scriptHash}') " +
                "{ Write-Error 'Script hash mismatch'; exit 12 }; " +
                $"& $f -Installer 'C:\\Windows\\Temp\\{installerName}' -Sha256 {Q(p.Sha256.Trim())} " +
                $"-Arguments {Q(p.Arguments)} -UninstallPattern {Q(p.UninstallPattern)} " +
                $"-UninstallArguments {Q(p.UninstallArguments)} -Kill {Q(string.Join(',', kill))}; " +
                "exit $LASTEXITCODE";

            Log("Запуск PsExec…");
            var r = await PsExecService.RunAsync(host,
                ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", bootstrap],
                system: true, TimeSpan.FromMinutes(15), Log);

            success = !r.TimedOut && r.ExitCode == 0;
            message = r.TimedOut ? "таймаут (15 мин)" : success ? "OK" : $"код {r.ExitCode}";
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Log("Ошибка: " + ex.Message);
        }
        finally
        {
            try { File.Delete(Path.Combine(tmp, installerName)); } catch { }
            try { File.Delete(Path.Combine(tmp, scriptName)); } catch { }
        }

        Log($"Итог: {(success ? "успех" : "ошибка")} ({message})");
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            var safeName = Regex.Replace(p.Name, @"[^\w.-]", "_");
            File.WriteAllText(Path.Combine(AppPaths.LogDir, $"install-{safeName}-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.log"), log.ToString());
        }
        catch { }

        return new UpdateResult(host, success, message);
    }

    /// <summary>Строка PowerShell в одинарных кавычках (внутренние кавычки удваиваются, переводы строк убираются).</summary>
    private static string Q(string? s) => "'" + (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("'", "''") + "'";

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeProcessRegex();
}

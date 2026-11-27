using System.Text;

namespace ComputerManager.Services;

/// <summary>Принудительный gpupdate /force на удалённом ПК через PsExec (от SYSTEM — обновляется политика компьютера).</summary>
public class GpUpdateService
{
    public static string? Validate() => PsExecService.Validate();

    public async Task<UpdateResult> UpdateAsync(string host, Action<string>? onLog = null)
    {
        var log = new StringBuilder();
        void Log(string s)
        {
            lock (log) log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}");
            onLog?.Invoke(s);
        }

        bool success = false;
        string message;
        try
        {
            if (!PsExecService.IsSafeHost(host)) throw new ArgumentException($"Недопустимое имя хоста: {host}");
            if (Validate() is { } error) throw new InvalidOperationException(error);

            Log("gpupdate /force…");
            var r = await PsExecService.RunAsync(host, [@"C:\Windows\System32\gpupdate.exe", "/force"],
                system: true, TimeSpan.FromMinutes(5), Log);

            success = !r.TimedOut && r.ExitCode == 0;
            message = r.TimedOut ? "таймаут (5 мин)" : success ? "OK" : $"код {r.ExitCode}";
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Log("Ошибка: " + ex.Message);
        }

        Log($"Итог: {(success ? "успех" : "ошибка")} ({message})");
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            File.WriteAllText(Path.Combine(AppPaths.LogDir, $"gpupdate-{host}-{DateTime.Now:yyyyMMdd-HHmmss}.log"), log.ToString());
        }
        catch { }

        return new UpdateResult(host, success, message);
    }
}

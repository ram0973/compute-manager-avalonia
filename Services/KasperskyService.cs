using System.Runtime.Versioning;
using System.ServiceProcess;

namespace ComputerManager.Services;

public record KasperskyResult(string Text, bool Bad);

/// <summary>
/// Работает ли Касперский на удалённом ПК. Проверяется служба AVP* (она запускает avp.exe)
/// через диспетчер служб по SMB — без PsExec, WMI и WinRM, поэтому проверку можно делать раз в час для всех ПК.
/// </summary>
public static class KasperskyService
{
    public static async Task<KasperskyResult> CheckAsync(string host)
    {
        if (!OperatingSystem.IsWindows()) return new("только Windows", false);

        var work = Task.Run(() => Query(host));
        var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(25)));
        return done == work ? await work : new("таймаут", false);
    }

    [SupportedOSPlatform("windows")]
    private static KasperskyResult Query(string host)
    {
        try
        {
            var avp = ServiceController.GetServices(host)
                .Where(s => s.ServiceName.StartsWith("AVP", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (avp.Count == 0) return new("Не найден", true);
            if (avp.Any(s => s.Status == ServiceControllerStatus.Running)) return new("Работает", false);
            return new($"Остановлен ({avp[0].Status})", true);
        }
        catch (Exception ex)
        {
            return new("ошибка: " + (ex.InnerException?.Message ?? ex.Message), false);
        }
    }
}

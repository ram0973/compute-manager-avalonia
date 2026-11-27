using System.Diagnostics;

namespace ComputerManager.Services;

/// <summary>Открывает оснастки MMC для удалённого ПК (под текущей учёткой) и админ-шару C$. Только Windows.</summary>
public static class ManagementService
{
    /// <summary>tool: compmgmt.msc | services.msc | eventvwr.msc | fsmgmt.msc | explorer-c$</summary>
    public static void Open(string tool, string host)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Доступно только в Windows.");

        if (tool == "explorer-c$")
        {
            var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            explorer.ArgumentList.Add($@"\\{host}\C$");
            Process.Start(explorer);
            return;
        }

        // например: compmgmt.msc /computer=PC-01
        Process.Start(new ProcessStartInfo(tool, $"/computer={host}") { UseShellExecute = true });
    }
}

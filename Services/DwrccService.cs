using System.Diagnostics;

namespace ComputerManager.Services;

/// <summary>Dameware Mini Remote Control (dwrcc.exe).</summary>
public static class DwrccService
{
    private static readonly string[] Candidates =
    [
        @"C:\Program Files (x86)\SolarWinds\Dameware Remote Support\dwrcc.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "SolarWinds", "Dameware Remote Support", "dwrcc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SolarWinds", "Dameware Remote Support", "dwrcc.exe"),
        // старые установки
        @"C:\Program Files\Dameware Development\Dameware mini control\dwrcc.exe",
    ];

    public static string? FindExe() => Candidates.FirstOrDefault(File.Exists);

    public static void Connect(string host)
    {
        var exe = FindExe() ?? throw new FileNotFoundException($"Не найден dwrcc.exe: {Candidates[0]}");

        // -c: подключиться автоматически, -h: обойти настройки Host Entry (используются вместе с -c), -m: имя/IP ПК
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add("-c:");
        psi.ArgumentList.Add("-h:");
        psi.ArgumentList.Add($"-m:{host}");
        Process.Start(psi);
    }
}

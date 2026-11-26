using System.Diagnostics;

namespace ComputerManager.Services;

public static class RdpService
{
    public static void Connect(string host)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{host}") { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Microsoft Remote Desktop
            Start("open", $"rdp://full%20address=s:{host}");
        }
        else
        {
            if (Which("xfreerdp3") is { } x3) Start(x3, $"/v:{host}", "/dynamic-resolution", "+clipboard");
            else if (Which("xfreerdp") is { } x) Start(x, $"/v:{host}", "/dynamic-resolution", "+clipboard");
            else if (Which("remmina") is { } r) Start(r, "-c", $"rdp://{host}");
            else throw new InvalidOperationException("Не найден RDP-клиент (xfreerdp / remmina).");
        }
    }

    /// <summary>Shadow-подключение к сеансу пользователя (mstsc /shadow). Только Windows.</summary>
    public static void Shadow(string host, int sessionId, bool control = true, bool noConsentPrompt = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Shadow RDP работает только в Windows.");

        var psi = new ProcessStartInfo("mstsc.exe") { UseShellExecute = false };
        psi.ArgumentList.Add($"/v:{host}");
        psi.ArgumentList.Add($"/shadow:{sessionId}");
        if (control) psi.ArgumentList.Add("/control");
        if (noConsentPrompt) psi.ArgumentList.Add("/noConsentPrompt");
        Process.Start(psi);
    }

    private static void Start(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    private static string? Which(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
}

using System.Diagnostics;
using System.Text.Json;

namespace ComputerManager.Services;

/// <summary>
/// Dameware Mini Remote Control (dwrcc.exe). Параметры запуска берутся из %AppData%\ComputerManager\dameware.json,
/// поле "Arguments"; подстановки: {ip}, {host} (FQDN), {name} (короткое имя). Файл создаётся при первом запуске.
/// Ключи dwrcc: -c: подключиться автоматически, -h: обойти Host Entry, -m: ПК, -u: -p: -d: учётка, -a: тип аутентификации.
/// </summary>
public static class DwrccService
{
    private const string DefaultArguments = "-c: -h: -m:{ip} -a:0";

    private static readonly string SettingsPath = Path.Combine(AppPaths.DataDir, "dameware.json");

    private static readonly string[] Candidates =
    [
        @"C:\Program Files (x86)\SolarWinds\Dameware Remote Support\dwrcc.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "SolarWinds", "Dameware Remote Support", "dwrcc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SolarWinds", "Dameware Remote Support", "dwrcc.exe"),
        @"C:\Program Files\Dameware Development\Dameware mini control\dwrcc.exe",
    ];

    public static string? FindExe() => Candidates.FirstOrDefault(File.Exists);

    public static void Connect(string host, string? ip)
    {
        var exe = FindExe() ?? throw new FileNotFoundException($"Не найден dwrcc.exe: {Candidates[0]}");
        if (!PsExecService.IsSafeHost(host)) throw new ArgumentException($"Недопустимое имя хоста: {host}");

        var address = string.IsNullOrWhiteSpace(ip) ? host : ip;
        var args = LoadArguments()
            .Replace("{ip}", address)
            .Replace("{host}", host)
            .Replace("{name}", host.Split('.')[0]);

        // рабочая папка = папка dwrcc: без неё он может открыться «пустым»
        Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        });
    }

    private static string LoadArguments()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(SettingsPath,
                    "{\n  \"Arguments\": \"" + DefaultArguments + "\"\n}\n");
                return DefaultArguments;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var value = doc.RootElement.TryGetProperty("Arguments", out var a) ? a.GetString() : null;
            return string.IsNullOrWhiteSpace(value) ? DefaultArguments : value;
        }
        catch { return DefaultArguments; }
    }
}

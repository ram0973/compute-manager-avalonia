using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ComputerManager.Services;

public record PsExecResult(int ExitCode, string Output, bool TimedOut);

/// <summary>Запуск команд на удалённом ПК через Install\PsExec64\PsExec64.exe. Только Windows.</summary>
public static partial class PsExecService
{
    public static readonly string Path64 = Path.Combine(AppPaths.InstallDir, "PsExec64", "PsExec64.exe");

    static PsExecService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>null — PsExec на месте; иначе текст ошибки.</summary>
    public static string? Validate() =>
        !OperatingSystem.IsWindows() ? "PsExec работает только в Windows."
        : !File.Exists(Path64) ? $"Не найден PsExec: {Path64}"
        : null;

    /// <summary>Имя хоста попадает в командную строку — допускаем только безопасные символы.</summary>
    public static bool IsSafeHost(string host) => SafeHostRegex().IsMatch(host);

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,255}$")]
    private static partial Regex SafeHostRegex();

    /// <summary>Интерактивная консоль на удалённом ПК в новом окне. kind: cmd | ps5 | ps7.</summary>
    public static void OpenConsole(string host, string kind, bool system)
    {
        if (Validate() is { } error) throw new InvalidOperationException(error);
        if (!IsSafeHost(host)) throw new ArgumentException($"Недопустимое имя хоста: {host}");

        (string label, string remote) = kind switch
        {
            "cmd" => ("cmd", "cmd.exe"),
            "ps5" => ("PowerShell 5", "powershell.exe -NoLogo"),
            "ps7" => ("PowerShell 7", "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo"),
            _ => throw new ArgumentException($"Неизвестная консоль: {kind}"),
        };

        // cmd /k — чтобы окно не закрылось сразу, если PsExec вернёт ошибку (отказано в доступе и т.п.)
        var args = $"/k title {label} - {host}{(system ? " (SYSTEM)" : "")} & " +
                   $"\"{Path64}\" \\\\{host} -accepteula -nobanner {(system ? "-s " : "")}{remote}";
        Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = false });
    }

    /// <summary>Неинтерактивный запуск команды, ожидание результата, вывод строк через onLine.</summary>
    public static async Task<PsExecResult> RunAsync(string host, IEnumerable<string> remoteArgs, bool system,
        TimeSpan timeout, Action<string>? onLine = null)
    {
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(Path64)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($@"\\{host}");
        psi.ArgumentList.Add("-accepteula");
        psi.ArgumentList.Add("-nobanner");
        if (system) psi.ArgumentList.Add("-s");
        foreach (var a in remoteArgs) psi.ArgumentList.Add(a);

        var output = new StringBuilder();
        void Line(string s)
        {
            lock (output) output.AppendLine(s);
            onLine?.Invoke(s);
        }

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Line(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Line(e.Data); };
        p.Start();
        p.StandardInput.Close();   // иначе консольные программы через PsExec могут зависнуть, ожидая stdin
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            p.WaitForExit(); // дочитать буферы вывода
            return new PsExecResult(p.ExitCode, output.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return new PsExecResult(-1, output.ToString(), true);
        }
    }
}

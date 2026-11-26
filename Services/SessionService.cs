using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ComputerManager.Services;

public record RdpSession(int Id, string UserName, string SessionName, string State)
{
    public bool HasUser => !string.IsNullOrWhiteSpace(UserName);

    public bool IsActive =>
        State.StartsWith("Acti", StringComparison.OrdinalIgnoreCase) ||
        State.StartsWith("Актив", StringComparison.OrdinalIgnoreCase);

    public string Display =>
        $"{(IsActive ? "●" : "○")} {Id} · {UserName} · {State}" +
        (string.IsNullOrWhiteSpace(SessionName) ? "" : $" · {SessionName}");
}

public record SessionQueryResult(bool Success, List<RdpSession> Sessions, string? Error);

/// <summary>Список сеансов удалённого ПК через qwinsta /server:. Только Windows.</summary>
public static class SessionService
{
    static SessionService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task<SessionQueryResult> QueryAsync(string host)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, [], "qwinsta доступен только в Windows.");

        try
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo("qwinsta.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add($"/server:{host}");

            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return new(false, [], "qwinsta: таймаут (15 с)");
            }

            string output = await outTask, error = await errTask;
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                var msg = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .FirstOrDefault() ?? $"qwinsta: код {p.ExitCode}";
                return new(false, [], msg);
            }

            return new(true, Parse(output), null);
        }
        catch (Exception ex) { return new(false, [], ex.Message); }
    }

    /// <summary>
    /// Разбор вывода qwinsta без привязки к языку заголовков:
    /// колонка SESSIONNAME может быть пустой, поэтому имя определяем по тому, начинается ли оно сразу после маркера.
    /// </summary>
    internal static List<RdpSession> Parse(string output)
    {
        var result = new List<RdpSession>();
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in lines.Skip(1)) // первая строка — заголовок
        {
            var line = raw.TrimEnd();
            if (line.Length < 3) continue;

            bool hasName = !char.IsWhiteSpace(line[1]);          // line[0] — маркер '>' или пробел
            var tokens = line[1..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            int idIdx = -1;
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (tokens[i].All(char.IsAsciiDigit)) { idIdx = i; break; }
            }
            if (idIdx < 0 || !int.TryParse(tokens[idIdx], out int id)) continue;

            var before = tokens[..idIdx];
            string name = "", user;
            if (hasName && before.Length > 0)
            {
                name = before[0];
                user = string.Join(' ', before[1..]);
            }
            else user = string.Join(' ', before);

            result.Add(new RdpSession(id, user, name, tokens[idIdx + 1]));
        }
        return result;
    }
}

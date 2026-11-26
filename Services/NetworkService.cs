using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ComputerManager.Services;

public static partial class NetworkService
{
    public record PingResult(bool Online, IPAddress? Ip);

    /// <summary>Резолвит IPv4 (DNS) и пингует по IP. IP возвращается даже если ПК офлайн (из DNS).</summary>
    public static async Task<PingResult> PingAsync(string host, int timeoutMs = 1000)
    {
        IPAddress? ip = null;
        try
        {
            ip = IPAddress.TryParse(host, out var parsed)
                ? parsed
                : (await Dns.GetHostAddressesAsync(host)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip is null) return new PingResult(false, null);

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            return new PingResult(reply.Status == IPStatus.Success, ip);
        }
        catch { return new PingResult(false, ip); }
    }

    /// <summary>
    /// MAC из ARP/neighbor-таблицы ОС. Работает для ПК в том же сегменте сети
    /// и только после успешного пинга (запись должна быть в кеше).
    /// </summary>
    public static async Task<string?> ResolveMacAsync(IPAddress ip)
    {
        try
        {
            string s = ip.ToString();
            string output;
            if (OperatingSystem.IsLinux())
            {
                output = await RunAsync("ip", "neigh", "show", s);
                if (!MacRegex().IsMatch(output)) output = await RunAsync("arp", "-n", s);
            }
            else if (OperatingSystem.IsMacOS())
                output = await RunAsync("arp", "-n", s);
            else
                output = await RunAsync("arp", "-a", s);

            foreach (Match m in MacRegex().Matches(output))
            {
                var mac = string.Join(':', m.Value.Split(':', '-').Select(p => p.PadLeft(2, '0').ToUpperInvariant()));
                if (mac is "FF:FF:FF:FF:FF:FF" or "00:00:00:00:00:00") continue;
                return mac;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string> RunAsync(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    [GeneratedRegex(@"\b(?:[0-9a-f]{1,2}[:-]){5}[0-9a-f]{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MacRegex();
}

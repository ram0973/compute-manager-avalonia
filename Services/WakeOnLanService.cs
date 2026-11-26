using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ComputerManager.Services;

public static class WakeOnLanService
{
    public static async Task SendAsync(string mac)
    {
        byte[] macBytes = ParseMac(mac);

        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (int i = 0; i < 16; i++)
            macBytes.CopyTo(packet, 6 + i * 6);

        using var udp = new UdpClient { EnableBroadcast = true };
        foreach (var target in GetBroadcastTargets())
        {
            try { await udp.SendAsync(packet, new IPEndPoint(target, 9)); }
            catch { /* интерфейс мог пропасть — пробуем остальные */ }
        }
    }

    /// <summary>255.255.255.255 + направленный broadcast каждого активного IPv4-интерфейса.</summary>
    private static HashSet<IPAddress> GetBroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null) continue;

                var ip = ua.Address.GetAddressBytes();
                var mask = ua.IPv4Mask.GetAddressBytes();
                var bc = new byte[4];
                for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                targets.Add(new IPAddress(bc));
            }
        }
        return targets;
    }

    public static byte[] ParseMac(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) throw new FormatException("Неверный MAC-адрес");
        return Convert.FromHexString(hex);
    }
}

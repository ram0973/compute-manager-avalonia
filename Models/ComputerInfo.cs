using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ComputerManager.Models;

public partial class ComputerInfo : ObservableObject
{
    public required string Name { get; init; }
    public string? DnsName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? Description { get; init; }
    public string? OrgUnit { get; init; }
    public DateTime? LastLogon { get; init; }

    /// <summary>Имя, по которому подключаемся (FQDN, если есть).</summary>
    public string Host => string.IsNullOrWhiteSpace(DnsName) ? Name : DnsName;

    [ObservableProperty] public partial string Status { get; set; } = "…";
    [ObservableProperty] public partial bool IsOnline { get; set; }
    [ObservableProperty] public partial bool IsOffline { get; set; }
    [ObservableProperty] public partial string? MacAddress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IpSort))]
    public partial string? IpAddress { get; set; }

    /// <summary>Ключ сортировки IP по числовому значению (а не по тексту: 10.0.0.9 &lt; 10.0.0.10).</summary>
    public uint IpSort =>
        IPAddress.TryParse(IpAddress, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            ? BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes())
            : uint.MaxValue;
}

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ComputerManager.Services;

public record DiskResult(bool Success, long TotalBytes, long FreeBytes, string? Error);

/// <summary>
/// Свободное место на диске удалённого ПК через административную шару (\\ПК\C$) и GetDiskFreeSpaceEx.
/// Нужен только SMB и права администратора — как и для PsExec; WMI/WinRM не требуются. Только Windows.
/// </summary>
public static class DiskService
{
    public static async Task<DiskResult> GetFreeSpaceAsync(string host, char drive = 'C')
    {
        if (!OperatingSystem.IsWindows())
            return new DiskResult(false, 0, 0, "Доступно только в Windows.");

        var work = Task.Run(() =>
        {
            string path = $@"\\{host}\{drive}$\";
            if (!GetDiskFreeSpaceEx(path, out _, out long total, out long free))
                return new DiskResult(false, 0, 0, new Win32Exception(Marshal.GetLastWin32Error()).Message);

            return new DiskResult(true, total, free, null);
        });

        // недоступный ПК может держать вызов десятки секунд — ограничиваем ожидание
        var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(15)));
        return done == work ? await work : new DiskResult(false, 0, 0, "таймаут (15 с)");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out long lpFreeBytesAvailable,
        out long lpTotalNumberOfBytes,
        out long lpTotalNumberOfFreeBytes);
}

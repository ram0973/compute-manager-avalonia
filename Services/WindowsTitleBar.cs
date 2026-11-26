using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Styling;

namespace ComputerManager.Services;

/// <summary>
/// Красит НАТИВНЫЙ заголовок окна в Windows 11 (DWM): цвет панели, цвет текста, светлая/тёмная тема.
/// Кнопки сворачивания/закрытия остаются системными. В Windows 10 и других ОС ничего не делает.
/// </summary>
public static class WindowsTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        bool dark = window.ActualThemeVariant == ThemeVariant.Dark;

        // RGB заголовка / текста (в тон палитре приложения)
        int caption = ToColorRef(dark ? 0x143C60 : 0xE6D6A3);
        int text = ToColorRef(dark ? 0xE4E9F2 : 0x1B1810);
        int useDark = dark ? 1 : 0;

        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    // 0xRRGGBB -> COLORREF (0x00BBGGRR)
    private static int ToColorRef(int rgb) =>
        ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

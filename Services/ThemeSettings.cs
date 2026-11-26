using Avalonia.Styling;

namespace ComputerManager.Services;

/// <summary>Запоминает выбранную тему (Dark по умолчанию).</summary>
public static class ThemeSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComputerManager", "theme.txt");

    public static ThemeVariant Load()
    {
        try
        {
            if (File.ReadAllText(FilePath).Trim() == "Light") return ThemeVariant.Light;
        }
        catch { }
        return ThemeVariant.Dark;
    }

    public static void Save(ThemeVariant variant)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, variant == ThemeVariant.Light ? "Light" : "Dark");
        }
        catch { }
    }
}

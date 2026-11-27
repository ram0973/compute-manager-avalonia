namespace ComputerManager.Services;

public static class AppPaths
{
    /// <summary>Папка Install рядом с программой (7-zip.msi, PsExec64.exe).</summary>
    public static readonly string InstallDir = Path.Combine(AppContext.BaseDirectory, "Install");

    /// <summary>%AppData%\ComputerManager — настройки, кэш MAC, логи.</summary>
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComputerManager");

    public static readonly string LogDir = Path.Combine(DataDir, "logs");
}

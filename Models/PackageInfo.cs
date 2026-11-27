using CommunityToolkit.Mvvm.ComponentModel;
using ComputerManager.Services;

namespace ComputerManager.Models;

/// <summary>Пакет из каталога Install\packages.json: локальный установщик и параметры его установки.</summary>
public partial class PackageInfo : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";

    /// <summary>Путь к .msi/.exe относительно папки Install.</summary>
    [ObservableProperty] public partial string Installer { get; set; } = "";

    /// <summary>Доп. аргументы установки (для .msi добавляются к /qn /norestart, для .exe — например /S).</summary>
    [ObservableProperty] public partial string Arguments { get; set; } = "";

    /// <summary>Маска DisplayName: найденные версии удаляются перед установкой (например 7-Zip*). Пусто — не удалять.</summary>
    [ObservableProperty] public partial string UninstallPattern { get; set; } = "";

    /// <summary>Аргументы тихого удаления для EXE-деинсталляторов (у MSI — msiexec /x).</summary>
    [ObservableProperty] public partial string UninstallArguments { get; set; } = "/S";

    /// <summary>Процессы, которые нужно закрыть перед установкой, через запятую (без .exe): 7zFM,7zG.</summary>
    [ObservableProperty] public partial string KillProcesses { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashShort))]
    public partial string Sha256 { get; set; } = "";

    [ObservableProperty] public partial string HashStatus { get; set; } = "";

    public string HashShort => Sha256.Length >= 16 ? Sha256[..16] + "…" : Sha256;

    public string FullPath => Path.Combine(AppPaths.InstallDir, Installer);
}

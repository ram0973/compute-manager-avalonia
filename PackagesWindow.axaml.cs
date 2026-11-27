using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ComputerManager.Models;
using ComputerManager.Services;
using ComputerManager.ViewModels;

namespace ComputerManager;

/// <summary>Каталог локальных установщиков (Install\packages.json) и установка выбранного пакета на выбранные ПК.</summary>
public partial class PackagesWindow : Window
{
    private readonly MainViewModel? _vm;
    public ObservableCollection<PackageInfo> Packages { get; } = [];

    /// <summary>Нужен загрузчику XAML (иначе предупреждение AVLN3001) и дизайнеру.</summary>
    public PackagesWindow()
    {
        InitializeComponent();
        PackagesGrid.ItemsSource = Packages;
        Opened += (_, _) => WindowsTitleBar.Apply(this);
    }

    public PackagesWindow(MainViewModel vm, IEnumerable<ComputerInfo> preselected) : this()
    {
        _vm = vm;
        DataContext = vm;
        LoadPackages();

        var pre = preselected.ToList();
        Opened += (_, _) => { foreach (var c in pre) PcList.SelectedItems?.Add(c); };
    }

    private void LoadPackages()
    {
        Packages.Clear();
        try
        {
            foreach (var p in PackageCatalog.Load())
            {
                PackageCatalog.Refresh(p);
                Packages.Add(p);
            }
            InfoText.Text = Packages.Count == 0
                ? $"Каталог пуст. Нажмите «Добавить файл…» и выберите .msi/.exe. Каталог: {PackageCatalog.CatalogPath}"
                : $"Каталог: {PackageCatalog.CatalogPath}";
        }
        catch (Exception ex) { InfoText.Text = "Ошибка чтения каталога: " + ex.Message; }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ---------- выбор ПК ----------

    private void OnSelectOnlineClick(object? sender, RoutedEventArgs e)
    {
        PcList.SelectedItems?.Clear();
        foreach (var c in PcList.Items.OfType<ComputerInfo>().Where(c => c.IsOnline))
            PcList.SelectedItems?.Add(c);
    }

    private void OnClearSelectionClick(object? sender, RoutedEventArgs e) => PcList.SelectedItems?.Clear();

    // ---------- каталог ----------

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите установщик (.msi / .exe)",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Установщики") { Patterns = ["*.msi", "*.exe"] }],
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;

            var full = Path.GetFullPath(path);
            var installRoot = Path.GetFullPath(AppPaths.InstallDir) + Path.DirectorySeparatorChar;

            // файл вне Install\ копируем в Install\<имя>\
            if (!full.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.Combine(AppPaths.InstallDir, Path.GetFileNameWithoutExtension(full));
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Path.GetFileName(full));
                File.Copy(full, dest, true);
                full = dest;
            }

            var pkg = new PackageInfo
            {
                Name = Path.GetFileNameWithoutExtension(full),
                Installer = Path.GetRelativePath(AppPaths.InstallDir, full),
                Sha256 = PackageCatalog.Sha256Of(full),
                Arguments = Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "/S" : "",
            };
            PackageCatalog.Refresh(pkg);
            Packages.Add(pkg);
            PackagesGrid.SelectedItem = pkg;
            InfoText.Text = $"Добавлено: {pkg.Name}. Проверьте аргументы тихой установки и нажмите «Сохранить».";
        }
        catch (Exception ex) { InfoText.Text = "Ошибка: " + ex.Message; }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (PackagesGrid.SelectedItem is PackageInfo p) Packages.Remove(p);
    }

    private void OnRehashClick(object? sender, RoutedEventArgs e)
    {
        if (PackagesGrid.SelectedItem is not PackageInfo p) return;
        try
        {
            p.Sha256 = PackageCatalog.Sha256Of(p.FullPath);
            PackageCatalog.Refresh(p);
            InfoText.Text = $"{p.Name}: хэш пересчитан по файлу на диске — это новый эталон. Нажмите «Сохранить».";
        }
        catch (Exception ex) { InfoText.Text = "Ошибка: " + ex.Message; }
    }

    private void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        foreach (var p in Packages) PackageCatalog.Refresh(p);
        var bad = Packages.Count(p => !p.HashStatus.StartsWith('✔'));
        InfoText.Text = bad == 0 ? "Все хэши совпадают." : $"Проблемных пакетов: {bad} (см. колонку «Хэш»).";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            PackageCatalog.Save(Packages);
            InfoText.Text = $"Сохранено: {PackageCatalog.CatalogPath}";
        }
        catch (Exception ex) { InfoText.Text = "Не удалось сохранить (нет прав на запись в Install?): " + ex.Message; }
    }

    // ---------- установка ----------

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        if (PackagesGrid.SelectedItem is not PackageInfo p)
        {
            InfoText.Text = "Выберите пакет в списке слева.";
            return;
        }

        var targets = PcList.SelectedItems?.OfType<ComputerInfo>().ToList() ?? [];
        if (targets.Count == 0)
        {
            InfoText.Text = "Выберите компьютеры справа.";
            return;
        }

        PackageCatalog.Refresh(p);
        if (_vm.CheckPackage(p) is { } error)
        {
            InfoText.Text = error;
            return;
        }

        var names = string.Join(", ", targets.Take(5).Select(t => t.Name)) +
                    (targets.Count > 5 ? $" и ещё {targets.Count - 5}" : "");
        var warn = string.IsNullOrWhiteSpace(p.UninstallPattern)
            ? ""
            : $"\n\nПеред установкой будут удалены все версии по маске «{p.UninstallPattern}».";

        if (!await Dialogs.ConfirmAsync(this, "Установка программ",
                $"Установить «{p.Name}» на {targets.Count} ПК: {names}?{warn}"))
            return;

        InfoText.Text = $"Установка «{p.Name}» запущена — ход выполнения в журнале главного окна.";
        await _vm.InstallPackageAsync(p, targets);
        InfoText.Text = $"Установка «{p.Name}» завершена — итог в журнале главного окна.";
    }
}

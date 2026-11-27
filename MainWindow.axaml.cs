using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComputerManager.Models;
using ComputerManager.Services;
using ComputerManager.ViewModels;
using Avalonia.Input.Platform;

namespace ComputerManager;

public partial class MainWindow : Window
{
    // кеш списка сеансов в подменю (чтобы не дёргать qwinsta при каждом движении мыши)
    private string? _sessionsFor;
    private DateTime _sessionsAt;

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // ПКМ выделяет строку под курсором, чтобы меню работало с нужным элементом
        ComputersGrid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
        UsersGrid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);

        KeyDown += (_, e) => { if (e.Key == Key.F1) ShowHelp(); };

        // журнал: после каждой записи показываем последнюю строку, а ранние доступны прокруткой
        vm.LogLines.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (vm.LogLines.Count > 0) LogList.ScrollIntoView(vm.LogLines[^1]);
        }, DispatcherPriority.Background);

        // нативный заголовок Windows 11 в цветах темы (перекрашивается при смене темы)
        Opened += (_, _) => WindowsTitleBar.Apply(this);
        ActualThemeVariantChanged += (_, _) => WindowsTitleBar.Apply(this);

        Opened += async (_, _) => await vm.RefreshCommand.ExecuteAsync(null);
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    private List<ComputerInfo> SelectedComputers() => ComputersGrid.SelectedItems.OfType<ComputerInfo>().ToList();

    // ---------- журнал ----------

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, Vm.LogLines.Select(l => l.Text));
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private void OnClearLogClick(object? sender, RoutedEventArgs e) => Vm.ClearLog();

    // ---------- тема и справка ----------

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current!;
        var next = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = next;
        ThemeSettings.Save(next);
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e) => ShowHelp();

    private async void ShowHelp() => await new HelpWindow().ShowDialog(this);

    // ---------- контекстное меню компьютеров ----------

    private void OnRdpClick(object? sender, RoutedEventArgs e) => Vm.ConnectRdpCommand.Execute(null);
    private void OnWakeClick(object? sender, RoutedEventArgs e) => Vm.WakeCommand.Execute(null);
    private void OnPingClick(object? sender, RoutedEventArgs e) => Vm.PingSelectedCommand.Execute(null);
    private void OnDwrccClick(object? sender, RoutedEventArgs e) => Vm.ConnectDwrccCommand.Execute(null);

    private void OnManagementClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tool } && Vm.SelectedComputer is { } c)
            Vm.OpenManagement(tool, c);
    }

    private void OnConsoleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || Vm.SelectedComputer is not { } c) return;

        bool system = tag.EndsWith("-s", StringComparison.Ordinal);
        Vm.OpenConsole(system ? tag[..^2] : tag, system, c);
    }

    private async void OnDiskClick(object? sender, RoutedEventArgs e) =>
        await Vm.CheckDiskAsync(SelectedComputers());

    private async void OnWhoClick(object? sender, RoutedEventArgs e) =>
        await Vm.ScanLoggedOnAsync(SelectedComputers());

    private async void OnKasperskyClick(object? sender, RoutedEventArgs e) =>
        await Vm.CheckKasperskyAsync(SelectedComputers(), manual: true);

    private async void OnShadowClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedComputer is not { } c) return;
        await new ShadowDialog(c.Name, c.Host).ShowDialog(this);
    }

    private async void OnGpUpdateClick(object? sender, RoutedEventArgs e)
    {
        var targets = SelectedComputers();
        if (targets.Count == 0) return;

        if (Vm.CheckGpUpdateFiles() is { } error)
        {
            Vm.StatusText = error;
            return;
        }

        if (targets.Count > 1 &&
            !await Dialogs.ConfirmAsync(this, "gpupdate /force", $"Выполнить gpupdate /force на {targets.Count} ПК?"))
            return;

        await Vm.GpUpdateAsync(targets);
    }

    /// <summary>Окно «Установка программ»: каталог пакетов + выбор ПК (выделенные в таблице подставляются заранее).</summary>
    private void OnPackagesClick(object? sender, RoutedEventArgs e) =>
        new PackagesWindow(Vm, SelectedComputers()).Show(this);

    /// <summary>Подменю «Сеансы»: при наведении запрашивает qwinsta; клик по сеансу = shadow-подключение.</summary>
    private async void OnSessionsPointerEntered(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || Vm.SelectedComputer is not { } c) return;

        if (_sessionsFor == c.Name && (DateTime.UtcNow - _sessionsAt).TotalSeconds < 15) return;
        _sessionsFor = c.Name;
        _sessionsAt = DateTime.UtcNow;

        if (c.IsOffline)
        {
            SetItems(menu, Disabled("ПК офлайн"));
            _sessionsFor = null;
            return;
        }

        SetItems(menu, Disabled("Загрузка…"));
        var r = await SessionService.QueryAsync(c.Host);
        if (Vm.SelectedComputer != c) return; // за время запроса выбрали другой ПК

        if (!r.Success)
        {
            SetItems(menu, Disabled("Ошибка: " + r.Error));
            _sessionsFor = null; // при следующем наведении попробуем снова
            return;
        }

        var users = r.Sessions.Where(s => s.HasUser).ToList();
        if (users.Count == 0)
        {
            SetItems(menu, Disabled("Нет сеансов пользователей"));
            return;
        }

        var items = new List<MenuItem>();
        foreach (var s in users)
        {
            var item = new MenuItem { Header = s.Display };
            ToolTip.SetTip(item, "Shadow-подключение к этому сеансу (с управлением)");
            item.Click += (_, _) => Vm.Shadow(c, s.Id, control: true, noConsent: false);
            items.Add(item);
        }
        SetItems(menu, [.. items]);
    }

    private static MenuItem Disabled(string text) => new() { Header = text, IsEnabled = false };

    private static void SetItems(MenuItem menu, params MenuItem[] items)
    {
        menu.Items.Clear();
        foreach (var i in items) menu.Items.Add(i);
    }

    // ---------- вкладка «Пользователи» ----------

    /// <summary>Ищет ПК, где залогинен пользователь, переключает на вкладку «Компьютеры» и выделяет найденные ПК.</summary>
    private async void OnFindUserClick(object? sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserInfo u) return;

        var found = await Vm.FindUserComputersAsync(u);
        if (found.Count == 0) return; // причина — в строке состояния

        Vm.SearchText = "";            // чтобы фильтр не скрыл найденные ПК
        MainTabs.SelectedIndex = 0;

        ComputersGrid.SelectedItems.Clear();
        foreach (var c in found) ComputersGrid.SelectedItems.Add(c);
        ComputersGrid.ScrollIntoView(found[0], null);
        ComputersGrid.Focus();
    }

    // ---------- выделение строки правой кнопкой ----------

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;

        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (row?.DataContext is { } item && !grid.SelectedItems.Contains(item))
            grid.SelectedItem = item; // не сбрасываем мультивыделение, если клик по выделенной строке
    }
}

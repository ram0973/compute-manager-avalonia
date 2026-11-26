using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ComputerManager.Models;
using ComputerManager.Services;
using ComputerManager.ViewModels;

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

        // ПКМ выделяет строку под курсором, чтобы меню работало с нужным ПК
        ComputersGrid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);

        // нативный заголовок Windows 11 в цветах темы (перекрашивается при смене темы)
        Opened += (_, _) => WindowsTitleBar.Apply(this);
        ActualThemeVariantChanged += (_, _) => WindowsTitleBar.Apply(this);

        Opened += async (_, _) => await vm.RefreshCommand.ExecuteAsync(null);
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    // ---------- тема ----------

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current!;
        var next = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = next;
        ThemeSettings.Save(next);
    }

    // ---------- контекстное меню ----------

    private void OnRdpClick(object? sender, RoutedEventArgs e) => Vm.ConnectRdpCommand.Execute(null);
    private void OnWakeClick(object? sender, RoutedEventArgs e) => Vm.WakeCommand.Execute(null);
    private void OnPingClick(object? sender, RoutedEventArgs e) => Vm.PingSelectedCommand.Execute(null);
    private void OnDwrccClick(object? sender, RoutedEventArgs e) => Vm.ConnectDwrccCommand.Execute(null);

    private async void OnShadowClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedComputer is not { } c) return;
        await new ShadowDialog(c.Name, c.Host).ShowDialog(this);
    }

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

    private async void OnUpdate7ZipClick(object? sender, RoutedEventArgs e)
    {
        var targets = ComputersGrid.SelectedItems.OfType<ComputerInfo>().ToList();
        if (targets.Count == 0) return;

        if (Vm.Check7ZipFiles() is { } error)
        {
            Vm.StatusText = error;
            return;
        }

        var names = string.Join(", ", targets.Take(5).Select(t => t.Name)) +
                    (targets.Count > 5 ? $" и ещё {targets.Count - 5}" : "");

        bool ok = await Dialogs.ConfirmAsync(this, "Обновить 7-Zip",
            $"ПК: {targets.Count} ({names})\n\n" +
            "Все установленные версии 7-Zip будут удалены, затем установится версия из Install\\7-zip\\7-zip.msi.");
        if (!ok) return;

        await Vm.Update7ZipAsync(targets);
    }

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ComputersGrid).Properties.IsRightButtonPressed) return;

        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        if (row?.DataContext is ComputerInfo c && !ComputersGrid.SelectedItems.Contains(c))
            ComputersGrid.SelectedItem = c; // не сбрасываем мультивыделение, если клик по выделенной строке
    }
}

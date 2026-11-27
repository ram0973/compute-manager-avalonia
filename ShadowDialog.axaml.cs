using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ComputerManager.Services;

namespace ComputerManager;

/// <summary>Окно выбора сеанса пользователя (qwinsta) для shadow RDP.</summary>
public partial class ShadowDialog : Window
{
    private readonly string _host = "";
    private List<RdpSession> _loaded = [];
    public ObservableCollection<RdpSession> Sessions { get; } = [];

    /// <summary>Нужен загрузчику XAML (иначе предупреждение AVLN3001) и дизайнеру.</summary>
    public ShadowDialog()
    {
        InitializeComponent();
        SessionsGrid.ItemsSource = Sessions;
        Opened += (_, _) => WindowsTitleBar.Apply(this);
    }

    public ShadowDialog(string computerName, string host) : this()
    {
        _host = host;
        Title = $"Shadow RDP — {computerName}";
        HeaderText.Text = $"Выберите сеанс на {computerName}";

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Запрос сеансов (qwinsta)…";
        Sessions.Clear();

        var r = await SessionService.QueryAsync(_host);
        if (!r.Success)
        {
            StatusText.Text = "Ошибка: " + r.Error;
            RefreshButton.IsEnabled = true;
            return;
        }

        _loaded = r.Sessions;
        Fill();
        RefreshButton.IsEnabled = true;
    }

    /// <summary>Показываем только сеансы с пользователем (на экране входа shadow невозможен).</summary>
    private void Fill()
    {
        Sessions.Clear();
        foreach (var s in _loaded.Where(s => s.HasUser)) Sessions.Add(s);

        if (Sessions.Count == 0) StatusText.Text = "Нет сеансов пользователей.";
        else
        {
            StatusText.Text = $"Сеансов: {Sessions.Count}";
            SessionsGrid.SelectedItem = Sessions.FirstOrDefault(s => s.IsActive) ?? Sessions[0];
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await LoadAsync();
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private void OnConnectClick(object? sender, RoutedEventArgs e) => Connect();
    private void OnSessionDoubleTapped(object? sender, RoutedEventArgs e) => Connect();

    private void Connect()
    {
        if (SessionsGrid.SelectedItem is not RdpSession s)
        {
            StatusText.Text = "Выберите сеанс в списке.";
            return;
        }

        try
        {
            RdpService.Shadow(_host, s.Id, ControlCheck.IsChecked == true, NoConsentCheck.IsChecked == true);
            Close();
        }
        catch (Exception ex) { StatusText.Text = "Ошибка: " + ex.Message; }
    }
}

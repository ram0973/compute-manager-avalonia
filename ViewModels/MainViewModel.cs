using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerManager.Models;
using ComputerManager.Services;

namespace ComputerManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AdService _ad = new();
    private readonly MacCache _macCache = MacCache.Load();
    private readonly SevenZipUpdateService _sevenZip = new();
    private List<ComputerInfo> _all = [];

    /// <summary>Отфильтрованный список для таблицы.</summary>
    public ObservableCollection<ComputerInfo> Computers { get; } = [];

    [ObservableProperty] public partial string Server { get; set; } = "";
    [ObservableProperty] public partial string UserName { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial bool UseSsl { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial ComputerInfo? SelectedComputer { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Готово";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var selected = SelectedComputer;
        var q = SearchText.Trim();

        var items = _all.Where(c => q.Length == 0 ||
            c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (c.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.OperatingSystem?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.OrgUnit?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        Computers.Clear();
        foreach (var c in items) Computers.Add(c);

        if (selected is not null && Computers.Contains(selected)) SelectedComputer = selected;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Загрузка из AD…";
        try
        {
            var list = await _ad.GetComputersAsync(Server, UserName, Password, UseSsl);
            foreach (var c in list)
            {
                c.MacAddress = _macCache.Get(c.Name);
                c.PropertyChanged += OnItemChanged;
            }
            _all = list;
            ApplyFilter();

            StatusText = $"Компьютеров: {_all.Count}. Проверка доступности…";
            await PingAllAsync();
            StatusText = $"Компьютеров: {_all.Count}, онлайн: {_all.Count(c => c.IsOnline)}";
        }
        catch (Exception ex) { StatusText = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ConnectRdp()
    {
        if (SelectedComputer is not { } c) return;
        try { RdpService.Connect(c.Host); StatusText = $"RDP: {c.Host}"; }
        catch (Exception ex) { StatusText = "Ошибка RDP: " + ex.Message; }
    }

    [RelayCommand]
    private async Task WakeAsync()
    {
        if (SelectedComputer is not { } c) return;
        if (string.IsNullOrWhiteSpace(c.MacAddress))
        {
            StatusText = $"MAC для {c.Name} неизвестен — введите его в колонке «MAC» (двойной клик).";
            return;
        }

        try
        {
            await WakeOnLanService.SendAsync(c.MacAddress);
            c.Status = "Пакет отправлен…";
            StatusText = $"Magic packet отправлен: {c.Name}";

            // ждём появления в сети до 2 минут
            for (int i = 0; i < 24; i++)
            {
                await Task.Delay(5000);
                if (await CheckAsync(c)) { StatusText = $"{c.Name} включился"; return; }
            }
            StatusText = $"{c.Name} не ответил за 2 минуты";
        }
        catch (Exception ex) { StatusText = "Ошибка WoL: " + ex.Message; }
    }

    [RelayCommand]
    private void ConnectDwrcc()
    {
        if (SelectedComputer is not { } c) return;
        try { DwrccService.Connect(c.Host); StatusText = $"Dameware: {c.Host}"; }
        catch (Exception ex) { StatusText = "Ошибка Dameware: " + ex.Message; }
    }

    public void Shadow(ComputerInfo c, int sessionId, bool control, bool noConsent)
    {
        try
        {
            RdpService.Shadow(c.Host, sessionId, control, noConsent);
            StatusText = $"Shadow RDP: {c.Name}, сеанс {sessionId}";
        }
        catch (Exception ex) { StatusText = "Ошибка shadow RDP: " + ex.Message; }
    }

    public string? Check7ZipFiles() => SevenZipUpdateService.Validate();

    /// <summary>Удаляет все версии 7-Zip и ставит Install\7-zip\7-zip.msi (до 5 ПК параллельно).</summary>
    public async Task Update7ZipAsync(IReadOnlyList<ComputerInfo> targets)
    {
        if (targets.Count == 0) return;

        IsBusy = true;
        int ok = 0, fail = 0;
        try
        {
            using var sem = new SemaphoreSlim(5);
            await Task.WhenAll(targets.Select(async c =>
            {
                await sem.WaitAsync();
                try
                {
                    c.Status = "7-Zip: обновление…";
                    StatusText = $"7-Zip: обновление {c.Name}…";
                    var r = await _sevenZip.UpdateAsync(c.Host);
                    c.Status = r.Success ? "7-Zip: OK" : $"7-Zip: ошибка ({r.Message})";
                    if (r.Success) Interlocked.Increment(ref ok); else Interlocked.Increment(ref fail);
                }
                finally { sem.Release(); }
            }));

            StatusText = $"7-Zip: успешно {ok}, с ошибкой {fail}. Логи: {SevenZipUpdateService.LogDir}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PingSelectedAsync()
    {
        if (SelectedComputer is { } c) await CheckAsync(c);
    }

    private async Task PingAllAsync()
    {
        using var sem = new SemaphoreSlim(50);
        await Task.WhenAll(_all.ToList().Select(async c =>
        {
            await sem.WaitAsync();
            try { await CheckAsync(c); } finally { sem.Release(); }
        }));
    }

    private async Task<bool> CheckAsync(ComputerInfo c)
    {
        var r = await NetworkService.PingAsync(c.Host);
        c.IsOnline = r.Online;
        c.IsOffline = !r.Online;
        c.Status = r.Online ? "Онлайн" : "Офлайн";

        if (r.Ip is { } ip)
        {
            c.IpAddress = ip.ToString();
            if (r.Online && string.IsNullOrWhiteSpace(c.MacAddress))
                c.MacAddress = await NetworkService.ResolveMacAsync(ip);
        }

        return r.Online;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComputerInfo.MacAddress) && sender is ComputerInfo c)
            _macCache.Set(c.Name, c.MacAddress);
    }
}

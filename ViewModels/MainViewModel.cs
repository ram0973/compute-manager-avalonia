using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerManager.Models;
using ComputerManager.Services;

namespace ComputerManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const long GB = 1024L * 1024 * 1024;

    private readonly AdService _ad = new();
    private readonly MacCache _macCache = MacCache.Load();
    private readonly PackageInstallService _install = new();
    private readonly GpUpdateService _gpUpdate = new();

    private List<ComputerInfo> _all = [];
    private List<UserInfo> _allUsers = [];
    private bool _kasperskyRunning;
    private bool _kasperskyFirstDone;
    private bool _lastWasProgress;

    /// <summary>Отфильтрованные списки для таблиц.</summary>
    public ObservableCollection<ComputerInfo> Computers { get; } = [];
    public ObservableCollection<UserInfo> Users { get; } = [];

    [ObservableProperty] public partial string Server { get; set; } = "";
    [ObservableProperty] public partial string UserName { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial bool UseSsl { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string UserSearchText { get; set; } = "";
    [ObservableProperty] public partial ComputerInfo? SelectedComputer { get; set; }
    [ObservableProperty] public partial UserInfo? SelectedUser { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Готово";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    /// <summary>Журнал сообщений (виден в нижней панели; последняя строка всегда прокручена в поле зрения).</summary>
    public ObservableCollection<LogEntry> LogLines { get; } = [];

    public MainViewModel() => _ = KasperskyLoopAsync();

    // ---------------- журнал ----------------

    partial void OnStatusTextChanged(string value) => Log(value, progress: false);

    private void Log(string text, bool progress)
    {
        var entry = new LogEntry($"{DateTime.Now:HH:mm:ss}  {text}");

        // строки прогресса («12/300…») обновляются на месте, чтобы не засорять журнал
        if (_lastWasProgress && LogLines.Count > 0) LogLines[LogLines.Count - 1] = entry;
        else LogLines.Add(entry);

        _lastWasProgress = progress;
        while (LogLines.Count > 1000) LogLines.RemoveAt(0);
    }

    private void Progress(string text) => Log(text, progress: true);

    /// <summary>Вызывается из потоков PsExec — переносим запись в UI-поток.</summary>
    private void LogFromAnyThread(string computer, string line) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _lastWasProgress = false;
            Log($"[{computer}] {line}", progress: false);
        });

    public void ClearLog()
    {
        LogLines.Clear();
        _lastWasProgress = false;
    }

    // ---------------- фильтры ----------------

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnUserSearchTextChanged(string value) => ApplyUserFilter();

    private static bool Has(string? text, string q) => text?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;

    private void ApplyFilter()
    {
        var selected = SelectedComputer;
        var q = SearchText.Trim();

        var items = _all.Where(c => q.Length == 0 ||
            Has(c.Name, q) || Has(c.Description, q) || Has(c.OperatingSystem, q) ||
            Has(c.OrgUnit, q) || Has(c.IpAddress, q) || Has(c.LoggedOn, q));

        Computers.Clear();
        foreach (var c in items) Computers.Add(c);

        if (selected is not null && Computers.Contains(selected)) SelectedComputer = selected;
    }

    private void ApplyUserFilter()
    {
        var q = UserSearchText.Trim();
        var items = _allUsers.Where(u => q.Length == 0 ||
            Has(u.Login, q) || Has(u.FullName, q) || Has(u.Phone, q) || Has(u.Mobile, q));

        Users.Clear();
        foreach (var u in items) Users.Add(u);
    }

    // ---------------- загрузка из AD ----------------

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Загрузка из AD…";
        string? usersNote = null;
        try
        {
            var usersTask = _ad.GetUsersAsync(Server, UserName, Password, UseSsl); // параллельно с компьютерами
            var list = await _ad.GetComputersAsync(Server, UserName, Password, UseSsl);
            foreach (var c in list)
            {
                c.MacAddress = _macCache.Get(c.Name);
                c.PropertyChanged += OnItemChanged;
            }
            _all = list;
            ApplyFilter();

            try { _allUsers = await usersTask; ApplyUserFilter(); }
            catch (Exception ex) { usersNote = "пользователи: " + ex.Message; }

            StatusText = $"Компьютеров: {_all.Count}. Проверка доступности…";
            await PingAllAsync();

            // кто куда залогинен — при каждом обновлении (и при старте) заново
            var online = _all.Where(c => c.IsOnline).ToList();
            var (_, failed) = await ScanAsync(online, "Кто вошёл", null);

            StatusText = $"Компьютеров: {_all.Count}, онлайн: {online.Count}, пользователей: {_allUsers.Count}; " +
                         $"сеансы опрошены на {online.Count - failed} ПК" +
                         (usersNote is null ? "" : $" ({usersNote})");

            // первая проверка Касперского — сразу после первой загрузки, дальше раз в час
            if (!_kasperskyFirstDone)
            {
                _kasperskyFirstDone = true;
                _ = CheckKasperskyAsync(online, manual: false);
            }
        }
        catch (Exception ex) { StatusText = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; }
    }

    // ---------------- подключения ----------------

    [RelayCommand]
    private void ConnectRdp()
    {
        if (SelectedComputer is not { } c) return;
        try { RdpService.Connect(c.Host); StatusText = $"RDP: {c.Host}"; }
        catch (Exception ex) { StatusText = "Ошибка RDP: " + ex.Message; }
    }

    [RelayCommand]
    private void ConnectDwrcc()
    {
        if (SelectedComputer is not { } c) return;
        try { DwrccService.Connect(c.Host, c.IpAddress); StatusText = $"Dameware: {c.IpAddress ?? c.Host}"; }
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

    public void OpenConsole(string kind, bool system, ComputerInfo c)
    {
        try
        {
            PsExecService.OpenConsole(c.Host, kind, system);
            StatusText = $"Консоль {kind}{(system ? " (SYSTEM)" : "")}: {c.Name}";
        }
        catch (Exception ex) { StatusText = "Ошибка консоли: " + ex.Message; }
    }

    public void OpenManagement(string tool, ComputerInfo c)
    {
        try
        {
            ManagementService.Open(tool, c.Host);
            StatusText = $"{tool}: {c.Name}";
        }
        catch (Exception ex) { StatusText = "Ошибка: " + ex.Message; }
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

    // ---------------- диск C: ----------------

    /// <summary>Свободное место на C: у выбранных ПК (до 10 параллельно) — результат в колонке «Диск C:».</summary>
    public async Task CheckDiskAsync(IReadOnlyList<ComputerInfo> targets)
    {
        if (targets.Count == 0) return;

        IsBusy = true;
        StatusText = "Проверка диска C:…";
        int ok = 0, fail = 0;
        try
        {
            using var sem = new SemaphoreSlim(10);
            await Task.WhenAll(targets.Select(async c =>
            {
                await sem.WaitAsync();
                try
                {
                    c.DiskC = "проверка…";
                    var r = await DiskService.GetFreeSpaceAsync(c.Host);
                    if (r.Success)
                    {
                        double pct = r.TotalBytes > 0 ? 100.0 * r.FreeBytes / r.TotalBytes : 0;
                        c.DiskFreeBytes = r.FreeBytes;
                        c.DiskLow = r.FreeBytes < 10L * GB || pct < 10;
                        c.DiskC = $"{r.FreeBytes / (double)GB:0.#} ГБ из {r.TotalBytes / (double)GB:0.#} ({pct:0}%)";
                        Interlocked.Increment(ref ok);
                    }
                    else
                    {
                        c.DiskFreeBytes = -1;
                        c.DiskLow = false;
                        c.DiskC = "ошибка: " + r.Error;
                        Interlocked.Increment(ref fail);
                    }
                }
                finally { sem.Release(); }
            }));

            StatusText = targets.Count == 1
                ? $"Диск C: на {targets[0].Name} — {targets[0].DiskC}"
                : $"Диск C: проверено {ok}, с ошибкой {fail}";
        }
        finally { IsBusy = false; }
    }

    // ---------------- удалённые операции через PsExec ----------------

    public string? CheckGpUpdateFiles() => GpUpdateService.Validate();
    public string? CheckPackage(PackageInfo p) => PackageInstallService.Validate(p);

    /// <summary>Установка пакета из каталога на выбранные ПК (до 5 параллельно).</summary>
    public Task InstallPackageAsync(PackageInfo p, IReadOnlyList<ComputerInfo> targets) =>
        RunRemoteAsync(targets, p.Name, c => _install.InstallAsync(c.Host, p, line => LogFromAnyThread(c.Name, line)));

    /// <summary>gpupdate /force на выбранных ПК (до 5 параллельно).</summary>
    public Task GpUpdateAsync(IReadOnlyList<ComputerInfo> targets) =>
        RunRemoteAsync(targets, "gpupdate", c => _gpUpdate.UpdateAsync(c.Host, line => LogFromAnyThread(c.Name, line)));

    private async Task RunRemoteAsync(IReadOnlyList<ComputerInfo> targets, string title, Func<ComputerInfo, Task<UpdateResult>> action)
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
                    c.Status = $"{title}: выполняется…";
                    StatusText = $"{title}: {c.Name}…";
                    var r = await action(c);
                    c.Status = r.Success ? $"{title}: OK" : $"{title}: ошибка ({r.Message})";
                    StatusText = $"{title}: {c.Name} — {(r.Success ? "OK" : "ошибка (" + r.Message + ")")}";
                    if (r.Success) Interlocked.Increment(ref ok); else Interlocked.Increment(ref fail);
                }
                finally { sem.Release(); }
            }));

            StatusText = $"{title}: успешно {ok}, с ошибкой {fail}. Журналы: {AppPaths.LogDir}";
        }
        finally { IsBusy = false; }
    }

    // ---------------- кто вошёл / поиск ПК пользователя ----------------

    /// <summary>qwinsta на выбранных ПК → колонка «Вошли».</summary>
    public async Task ScanLoggedOnAsync(IReadOnlyList<ComputerInfo> targets)
    {
        if (targets.Count == 0) return;

        var (_, failed) = await ScanAsync(targets, "Кто вошёл", null);
        StatusText = targets.Count == 1
            ? $"{targets[0].Name}: " + (string.IsNullOrEmpty(targets[0].LoggedOn) ? "нет сеансов пользователей" : targets[0].LoggedOn)
            : $"Кто вошёл: опрошено {targets.Count - failed}, недоступно {failed}";
    }

    /// <summary>Опрашивает все онлайн-ПК (qwinsta) и возвращает те, где залогинен пользователь.</summary>
    public async Task<List<ComputerInfo>> FindUserComputersAsync(UserInfo u)
    {
        var targets = _all.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusText = "Нет онлайн-ПК для поиска — сначала обновите список (F5).";
            return [];
        }

        var (found, failed) = await ScanAsync(targets, $"Поиск {u.Login}", u.Login);
        u.Computers = found.Count == 0 ? "не найден" : string.Join(", ", found.Select(c => c.Name));

        StatusText = found.Count == 0
            ? $"{u.Login} не найден на {targets.Count - failed} ПК (недоступно для опроса: {failed})"
            : $"{u.Login}: {u.Computers} (опрошено {targets.Count - failed}, недоступно {failed})";
        return found;
    }

    private async Task<(List<ComputerInfo> Found, int Failed)> ScanAsync(
        IReadOnlyList<ComputerInfo> targets, string title, string? login)
    {
        var found = new List<ComputerInfo>();
        int done = 0, failed = 0;
        IsBusy = true;
        try
        {
            using var sem = new SemaphoreSlim(25);
            await Task.WhenAll(targets.Select(async c =>
            {
                await sem.WaitAsync();
                try
                {
                    var r = await SessionService.QueryAsync(c.Host);
                    if (r.Success)
                    {
                        var names = r.Sessions.Where(s => s.HasUser).Select(s => s.UserName)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        c.LoggedOn = string.Join(", ", names);
                        if (login is not null && names.Contains(login, StringComparer.OrdinalIgnoreCase))
                            lock (found) found.Add(c);
                    }
                    else Interlocked.Increment(ref failed);
                }
                finally
                {
                    sem.Release();
                    Progress($"{title}: {Interlocked.Increment(ref done)}/{targets.Count}, найдено {found.Count}");
                }
            }));
        }
        finally { IsBusy = false; }

        return (found, failed);
    }

    // ---------------- Касперский ----------------

    private async Task KasperskyLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (IsBusy || _all.Count == 0) continue;

                await PingAllAsync(); // статусы онлайн/офлайн тоже обновляем раз в час
                await CheckKasperskyAsync(_all.Where(c => c.IsOnline).ToList(), manual: false);
            }
        }
        catch { /* таймер остановлен при закрытии */ }
    }

    public async Task CheckKasperskyAsync(IReadOnlyList<ComputerInfo> targets, bool manual)
    {
        if (targets.Count == 0)
        {
            if (manual) StatusText = "Нет ПК для проверки Касперского.";
            return;
        }
        if (_kasperskyRunning) return;

        _kasperskyRunning = true;
        var bad = new List<string>();
        try
        {
            using var sem = new SemaphoreSlim(20);
            await Task.WhenAll(targets.Select(async c =>
            {
                await sem.WaitAsync();
                try
                {
                    c.Kaspersky = "проверка…";
                    var r = await KasperskyService.CheckAsync(c.Host);
                    c.Kaspersky = $"{r.Text} · {DateTime.Now:HH:mm}";
                    c.KasperskyBad = r.Bad;
                    if (r.Bad) lock (bad) bad.Add(c.Name);
                }
                finally { sem.Release(); }
            }));

            StatusText = bad.Count == 0
                ? $"🛡 Касперский: проверено {targets.Count} ПК, проблем нет ({DateTime.Now:HH:mm})"
                : $"🛡 Касперский не работает на {bad.Count} из {targets.Count}: " +
                  string.Join(", ", bad.Take(10)) + (bad.Count > 10 ? "…" : "");
        }
        finally { _kasperskyRunning = false; }
    }

    // ---------------- пинг ----------------

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

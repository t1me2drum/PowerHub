using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using PowerHub.Api;
using PowerHub.Models;
using PowerHub.Protocol;

namespace PowerHub.Services;

/// <summary>
/// Фоновий сервіс моніторингу станцій через MQTT.
/// Тримає з'єднання (з повторним входом і наростаючою затримкою), збирає телеметрію,
/// раз на хвилину пише історію та перевіряє правила сповіщень.
/// </summary>
public static class MonitorService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan QuotaRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Як часто перечитувати список станцій з акаунта (назви, нові й видалені станції)
    /// </summary>
    private static readonly TimeSpan StationSyncInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Останні параметри станції. Словник після публікації не змінюється,
    /// тому його можна читати без блокування.
    /// </summary>
    private sealed record Snapshot(DeviceParams Params, DateTime LastSeen);

    /// <summary>
    /// Станції, від яких у цьому запуску вже прийшли дані (для журналу діагностики)
    /// </summary>
    private static readonly HashSet<string> _heard = new();

    // Усі поля нижче захищені _lock
    private static readonly object _lock = new();
    private static CancellationTokenSource? _cts;
    private static MqttLink? _link;
    private static string? _userId;
    private static HashSet<string> _subscribed = new();
    private static Dictionary<string, Snapshot> _snapshots = new();

    private static DispatcherQueue? _dispatcher;
    private static long _lastRecordedMinute;
    private static DateTime _lastQuotaRequest = DateTime.MinValue;
    private static DateTime _lastStationSync = DateTime.MinValue;
    private static DateTime _lastCleanup = DateTime.MinValue;

    /// <summary>
    /// Поточний стан з'єднання для інтерфейсу
    /// </summary>
    public static string Status { get; private set; } = "Зупинено";

    /// <summary>
    /// Вид стану з'єднання (для банера на головному екрані)
    /// </summary>
    public static ConnectionState State { get; private set; } = ConnectionState.Stopped;

    /// <summary>
    /// Змінився стан з'єднання. Викликається в UI-потоці.
    /// </summary>
    public static event Action<string>? StatusChanged;

    /// <summary>
    /// Оновилися параметри станцій (раз на такт і після команди). Викликається в UI-потоці.
    /// </summary>
    public static event Action? ParamsUpdated;

    public static bool IsRunning
    {
        get { lock (_lock) return _cts != null; }
    }

    /// <summary>
    /// Запам'ятати UI-потік: на ньому оновлюються станції та показуються сповіщення
    /// </summary>
    public static void Initialize(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        App.Repository.DevicesChanged += OnDevicesChanged;
    }

    /// <summary>
    /// Запустити моніторинг
    /// </summary>
    public static void Start()
    {
        CancellationToken token;
        lock (_lock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _ = Task.Run(() => ConnectLoopAsync(token));
        _ = Task.Run(() => TickLoopAsync(token));

        if (App.Repository.Credentials?.HasDeveloperKeys == true)
        {
            _lastStationSync = DateTime.Now;
            _ = Task.Run(SyncStationsSafeAsync);
        }
    }

    /// <summary>
    /// Зупинити моніторинг
    /// </summary>
    public static void Stop()
    {
        CancellationTokenSource? cts;
        MqttLink? link;
        lock (_lock)
        {
            cts = _cts;
            link = _link;
            _cts = null;
            _link = null;
            _userId = null;
            _subscribed = new HashSet<string>();
            _snapshots = new Dictionary<string, Snapshot>();
            _heard.Clear();
        }

        cts?.Cancel();
        link?.Dispose();
        SetStatus(ConnectionState.Stopped, "Зупинено");

        // Без моніторингу станції не можуть вважатися онлайн
        _dispatcher?.TryEnqueue(() =>
        {
            foreach (var device in App.Repository.Devices)
            {
                device.SetOffline();
            }
        });
    }

    #region З'єднання

    private static async Task ConnectLoopAsync(CancellationToken token)
    {
        var backoff = MinBackoff;

        while (!token.IsCancellationRequested)
        {
            var credentials = App.Repository.Credentials;
            if (credentials == null)
            {
                SetStatus(ConnectionState.Stopped, "Не виконано вхід");
                return;
            }

            SetStatus(ConnectionState.Connecting, "Підключення…");
            MqttLink? link = null;

            try
            {
                // Токен і облікові дані MQTT отримуємо заново при кожному підключенні
                Session session;
                MqttCredentials mqtt;
                using (var cloud = new EcoflowCloud())
                {
                    session = await cloud.LoginAsync(credentials.ApiHost, credentials.Email, credentials.Password);
                    mqtt = await cloud.GetMqttCredentialsAsync(credentials.ApiHost, session);
                }

                var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                link = new MqttLink(mqtt, session.UserId, OnMessage, OnConnectionChanged,
                    reason => failure.TrySetResult(reason));

                var serials = App.Repository.ActiveDevices.Select(d => d.SerialNumber).ToHashSet();
                lock (_lock)
                {
                    token.ThrowIfCancellationRequested();
                    _link = link;
                    _userId = session.UserId;
                    _subscribed = serials;
                }

                await link.ConnectAsync(serials.SelectMany(sn => TopicsFor(session.UserId, sn)));
                backoff = MinBackoff;
                DiagLog.Log("conn", "підписка: " + string.Join(", ",
                    App.Repository.ActiveDevices.Select(d => $"{d.Model.GetDisplayName()} {d.SerialNumber}")));

                // Далі MqttLink перепідключається сам; сюди повертаємось лише після фатальної помилки
                var reason = await failure.Task.WaitAsync(token);
                SetStatus(ConnectionState.Reconnecting, $"Перепідключення… ({reason})");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                ReleaseLink(link);
                return;
            }
            catch (EcoflowException ex) when (ex.IsAuthError)
            {
                // Пароль змінено або обліковий запис недоступний: повтори не допоможуть,
                // зупиняємось, щоб після нового входу Start() запустив моніторинг знову
                ReleaseLink(link);
                StopOwned(token);
                SetStatus(ConnectionState.Failed, $"Помилка входу: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                DiagLog.Log("conn", "connect failed", ex);
                SetStatus(ConnectionState.Reconnecting, $"Перепідключення… ({ex.Message})");
            }

            ReleaseLink(link);

            try
            {
                await Task.Delay(backoff, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>
    /// Зупинити запуск, якому належить token (якщо його ще не замінив новий Start)
    /// </summary>
    private static void StopOwned(CancellationToken token)
    {
        CancellationTokenSource? cts = null;
        lock (_lock)
        {
            if (_cts != null && _cts.Token == token)
            {
                cts = _cts;
                _cts = null;
            }
        }
        cts?.Cancel();
    }

    private static void ReleaseLink(MqttLink? link)
    {
        if (link == null) return;
        lock (_lock)
        {
            if (_link == link)
            {
                _link = null;
                _userId = null;
            }
        }
        link.Dispose();
    }

    private static void OnConnectionChanged(bool connected)
    {
        if (connected)
            SetStatus(ConnectionState.Connected, "Підключено");
        else
            SetStatus(ConnectionState.Reconnecting, "Зв'язок втрачено, перепідключення…");
        if (connected)
        {
            _ = RequestAllQuotasAsync();
        }
    }

    private static IEnumerable<string> TopicsFor(string userId, string sn) => new[]
    {
        $"/app/device/property/{sn}",
        $"/app/{userId}/{sn}/thing/property/get_reply",
        $"/app/{userId}/{sn}/thing/property/set_reply"
    };

    /// <summary>
    /// Привести підписки у відповідність до списку станцій
    /// </summary>
    private static async void OnDevicesChanged()
    {
        try
        {
            MqttLink? link;
            string? userId;
            List<string> added, removed;

            var current = App.Repository.ActiveDevices.Select(d => d.SerialNumber).ToHashSet();
            lock (_lock)
            {
                link = _link;
                userId = _userId;
                if (link == null || userId == null)
                    return;

                added = current.Except(_subscribed).ToList();
                removed = _subscribed.Except(current).ToList();
                _subscribed = current;

                if (removed.Count > 0)
                {
                    _snapshots = _snapshots
                        .Where(kv => !removed.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            }

            await link.SubscribeAsync(added.SelectMany(sn => TopicsFor(userId, sn)));
            await link.UnsubscribeAsync(removed.SelectMany(sn => TopicsFor(userId, sn)));

            foreach (var sn in added)
            {
                var device = App.Repository.GetDevice(sn);
                if (device != null)
                    await RequestQuotaAsync(link, userId, device);
            }
        }
        catch (Exception ex)
        {
            DiagLog.Log("conn", "subscription sync failed", ex);
        }
    }

    #endregion

    #region Повідомлення

    /// <summary>
    /// Обробка MQTT повідомлення (потік MQTT)
    /// </summary>
    private static void OnMessage(string topic, byte[] payload)
    {
        string sn;
        TopicKind kind;

        if (topic.StartsWith("/app/device/property/", StringComparison.Ordinal))
        {
            sn = topic[(topic.LastIndexOf('/') + 1)..];
            kind = TopicKind.Data;
        }
        else if (topic.EndsWith("/get_reply", StringComparison.Ordinal) || topic.EndsWith("/set_reply", StringComparison.Ordinal))
        {
            // /app/{userId}/{sn}/thing/property/get_reply
            var parts = topic.Split('/');
            if (parts.Length < 4) return;
            sn = parts[3];
            kind = topic.EndsWith("/get_reply", StringComparison.Ordinal) ? TopicKind.GetReply : TopicKind.SetReply;
        }
        else
        {
            return;
        }

        var device = App.Repository.GetDevice(sn);
        if (device == null)
            return;

        DeviceParams parsed;
        try
        {
            parsed = Protocols.For(device.Model).Parse(kind, payload);
        }
        catch (Exception ex)
        {
            // Короткий hex-префікс, щоб за журналом можна було впізнати невідомий формат кадру
            DiagLog.Log("parse", $"{device.Model.GetDisplayName()} {sn} {kind} {payload.Length} B: {DiagLog.Hex(payload)}", ex);
            parsed = new DeviceParams();
        }

        lock (_lock)
        {
            if (_cts == null)
                return;

            if (_heard.Add(sn))
            {
                DiagLog.Log("data", $"перші дані від {device.Model.GetDisplayName()} {sn}: {kind}, {payload.Length} B, полів {parsed.Count}" +
                    (parsed.Count == 0 ? $", {DiagLog.Hex(payload)}" : ""));
            }

            // Новий словник замість зміни старого: читачі тримають незмінну копію
            _snapshots.TryGetValue(sn, out var old);
            var merged = old != null ? new DeviceParams(old.Params) : new DeviceParams();
            foreach (var kv in parsed)
            {
                merged[kv.Key] = kv.Value;
            }

            // Будь-який трафік від станції означає, що вона на зв'язку, навіть якщо кадр не розібрано
            _snapshots[sn] = new Snapshot(merged, DateTime.Now);
        }
    }

    private static async Task RequestAllQuotasAsync()
    {
        MqttLink? link;
        string? userId;
        lock (_lock)
        {
            link = _link;
            userId = _userId;
        }
        if (link == null || userId == null)
            return;

        _lastQuotaRequest = DateTime.Now;
        foreach (var device in App.Repository.ActiveDevices)
        {
            await RequestQuotaAsync(link, userId, device);
        }
    }

    /// <summary>
    /// Запросити повний стан станції (сторінка станції робить це під час відкриття і кнопкою «Оновити»)
    /// </summary>
    public static Task<bool> RequestQuotaAsync(Device device)
    {
        MqttLink? link;
        string? userId;
        lock (_lock)
        {
            link = _link;
            userId = _userId;
        }
        return link == null || userId == null ? Task.FromResult(false) : RequestQuotaAsync(link, userId, device);
    }

    private static Task<bool> RequestQuotaAsync(MqttLink link, string userId, Device device)
    {
        var request = Protocols.For(device.Model).QuotaRequest(device.SerialNumber);
        return link.PublishAsync($"/app/{userId}/{device.SerialNumber}/thing/property/get", request.Payload);
    }

    #endregion

    #region Керування

    /// <summary>
    /// Останні параметри станції (незмінна копія) — з них читають значення елементи керування
    /// </summary>
    public static DeviceParams GetParams(string serialNumber)
    {
        lock (_lock)
        {
            return _snapshots.TryGetValue(serialNumber, out var snap) ? snap.Params : new DeviceParams();
        }
    }

    /// <summary>
    /// Надіслати команду станції. Після успішної публікації значення оптимістично
    /// підставляється в знімок, доки станція не надішле власне (як Repository.send в Android).
    /// build отримує поточні параметри й повертає пакет команди та оптимістичні значення.
    /// </summary>
    public static async Task<bool> SendAsync(Device device, Func<DeviceParams, (OutgoingMessage Command, DeviceParams Optimistic)> build)
    {
        MqttLink? link;
        string? userId;
        lock (_lock)
        {
            link = _link;
            userId = _userId;
        }
        if (link == null || userId == null)
            return false;

        var (command, optimistic) = build(GetParams(device.SerialNumber));
        var ok = await link.PublishAsync($"/app/{userId}/{device.SerialNumber}/thing/property/set", command.Payload);
        if (!ok)
        {
            DiagLog.Log("cmd", $"publish failed for {device.Model.GetDisplayName()} {device.SerialNumber}");
            return false;
        }

        lock (_lock)
        {
            if (_cts == null)
                return true;

            // LastSeen не чіпаємо: наша власна команда не означає, що станція на зв'язку
            _snapshots.TryGetValue(device.SerialNumber, out var old);
            var merged = old != null ? new DeviceParams(old.Params) : new DeviceParams();
            foreach (var kv in optimistic)
            {
                merged[kv.Key] = kv.Value;
            }
            _snapshots[device.SerialNumber] = new Snapshot(merged, old?.LastSeen ?? DateTime.MinValue);
        }

        RaiseParamsUpdated();
        return true;
    }

    public static Task<bool> ToggleAsync(Device device, ToggleControl control, bool on) =>
        SendAsync(device, p => (control.Command(on, p), control.Optimistic(on)));

    public static Task<bool> SetValueAsync(Device device, SliderControl control, int value) =>
        SendAsync(device, p => (control.Command(value, p), control.Optimistic(value)));

    public static Task<bool> ChooseAsync(Device device, ChoiceControl control, int value) =>
        SendAsync(device, p => (control.Command(value, p), control.Optimistic(value)));

    private static void RaiseParamsUpdated()
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
            ParamsUpdated?.Invoke();
        else
            _dispatcher.TryEnqueue(() => ParamsUpdated?.Invoke());
    }

    #endregion

    #region Періодична обробка

    private static async Task TickLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                DiagLog.Log("service", "tick failed", ex);
            }

            try
            {
                await Task.Delay(TickInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task TickAsync()
    {
        var now = DateTime.Now;
        var minute = now.Ticks / TimeSpan.TicksPerMinute;
        var record = minute != _lastRecordedMinute;

        Dictionary<string, Snapshot> snapshots;
        lock (_lock)
        {
            snapshots = _snapshots;
        }

        var weakGridVolt = App.Repository.Settings.WeakGridVolt;
        var updates = new List<(Device Device, DeviceState State, bool Online, bool HasData, DateTime LastSeen)>();
        foreach (var device in App.Repository.ActiveDevices)
        {
            snapshots.TryGetValue(device.SerialNumber, out var snap);
            var online = snap != null && now - snap.LastSeen < OfflineAfter;
            var state = Protocols.For(device.Model).GetState(snap?.Params ?? new DeviceParams());
            var lastSeen = snap != null && snap.LastSeen > DateTime.MinValue ? snap.LastSeen : DateTime.MinValue;
            updates.Add((device, state, online, lastSeen > DateTime.MinValue, lastSeen));

            if (record && online && state.Soc.HasValue)
            {
                await App.Repository.AddHistoryEntryAsync(new HistoryEntry
                {
                    SerialNumber = device.SerialNumber,
                    Timestamp = new DateTime(minute * TimeSpan.TicksPerMinute),
                    BatteryLevel = state.Soc.Value,
                    BatteryWatts = NetBatteryWatts(state),
                    InputWatts = state.InputW,
                    OutputWatts = state.OutputW,
                    SolarWatts = state.SolarW,
                    AcInWatts = state.AcInW,
                    Temperature = state.BatteryTempC,
                    HasAcInput = state.GridConnected ?? false,
                    Grid = state.GridConnected
                });
            }
        }
        if (record)
            _lastRecordedMinute = minute;

        // Станції надсилають лише зміни; періодичний запит повного стану оновлює рідкісні поля
        if (now - _lastQuotaRequest > QuotaRefreshInterval)
            await RequestAllQuotasAsync();

        // Назви станцій беруться з акаунта: перейменування в офіційному застосунку підхоплюється тут
        if (now - _lastStationSync > StationSyncInterval && App.Repository.Credentials?.HasDeveloperKeys == true)
        {
            _lastStationSync = now;
            await SyncStationsSafeAsync();
        }

        if (now - _lastCleanup > TimeSpan.FromDays(1))
        {
            _lastCleanup = now;
            await App.Repository.CleanupOldHistoryAsync();
        }

        // Властивості станцій прив'язані до інтерфейсу — змінюємо їх лише в UI-потоці
        _dispatcher?.TryEnqueue(() =>
        {
            foreach (var (device, state, online, hasData, lastSeen) in updates)
            {
                device.UpdateTelemetry(state, online, hasData, lastSeen, weakGridVolt);
                NotificationService.Evaluate(device, state, online);
            }
            ParamsUpdated?.Invoke();
        });
    }

    /// <summary>
    /// Баланс батареї: вхід мінус вихід (додатне — заряджається)
    /// </summary>
    private static int? NetBatteryWatts(DeviceState state) =>
        state.InputW.HasValue || state.OutputW.HasValue
            ? (state.InputW ?? 0) - (state.OutputW ?? 0)
            : null;

    #endregion

    /// <summary>
    /// Короткий підсумок для підказки іконки в треї (як summary() постійного сповіщення в Android)
    /// </summary>
    public static string Summary()
    {
        if (State != ConnectionState.Connected)
            return Status;

        var devices = App.Repository.ActiveDevices;
        if (devices.Count == 0)
            return "Додайте станцію";

        return string.Join("\n", devices.Select(d =>
        {
            var weak = d.GridStatus == Protocol.GridStatus.Weak ? $" ⚠{d.State.AcInVolt}В" : "";
            var online = d.IsOnline ? "" : " (офлайн)";
            return $"{d.Name}: {d.BatteryText} ↓{d.InputWatts ?? 0} ↑{d.OutputWatts ?? 0} Вт{weak}{online}";
        }));
    }

    private static async Task SyncStationsSafeAsync()
    {
        try
        {
            var r = await App.Repository.SyncStationsAsync();
            if (r.Added + r.Updated + r.Removed > 0)
                DiagLog.Log("sync", $"stations: +{r.Added} ~{r.Updated} -{r.Removed}, unsupported {r.Unsupported.Count}");

            // Назви змінено без сповіщення (синхронізація йде у фоні) — оновити їх на відкритих сторінках
            if (r.Updated > 0)
            {
                _dispatcher?.TryEnqueue(() =>
                {
                    foreach (var device in App.Repository.ActiveDevices)
                        device.NotifyNameChanged();
                });
            }
        }
        catch (Exception ex)
        {
            DiagLog.Log("sync", "station sync failed", ex);
        }
    }

    private static void SetStatus(ConnectionState state, string status)
    {
        if (Status != status)
            DiagLog.Log("conn", status);

        State = state;
        Status = status;
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
            StatusChanged?.Invoke(status);
        else
            _dispatcher.TryEnqueue(() => StatusChanged?.Invoke(status));
    }
}

/// <summary>
/// Вид стану з'єднання з хмарою
/// </summary>
public enum ConnectionState
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

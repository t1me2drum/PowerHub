using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerHub.Protocol;

namespace PowerHub.Models;

/// <summary>
/// Модель зарядної станції EcoFlow.
/// Зберігаються лише ідентифікація та налаштування; телеметрія живе тільки в пам'яті
/// і оновлюється з UI-потоку, тому на неї можна прив'язувати інтерфейс.
/// </summary>
public class Device : ObservableObject
{
    public string SerialNumber { get; set; } = string.Empty;
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Змінити назву без сповіщення інтерфейсу (з фонового потоку; список перебудовується за DevicesChanged)
    /// </summary>
    public void SetNameSilently(string name) => _name = name;

    /// <summary>
    /// Повідомити інтерфейс про назву, змінену через SetNameSilently (лише з UI-потоку)
    /// </summary>
    public void NotifyNameChanged() => OnPropertyChanged(nameof(Name));
    public DeviceModel Model { get; set; }

    /// <summary>
    /// Станція прийшла зі списку акаунта (синхронізація її оновлює і видаляє)
    /// </summary>
    public bool IsImported { get; set; }

    // Налаштування
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Видалена користувачем: синхронізація з акаунтом не повертає її назад
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Назву задано в PowerHub (лише для станцій, доданих вручну; станції з акаунта беруть назву звідти)
    /// </summary>
    public bool CustomName { get; set; }

    /// <summary>
    /// Перейменовувати в PowerHub можна лише станції, яких немає в акаунті
    /// </summary>
    [JsonIgnore] public bool CanRename => !IsImported;

    #region Телеметрія (не зберігається)

    private DeviceState _state = new();
    private bool _isOnline;
    private bool _hasData;
    private DateTime _lastSeen;
    private int _weakGridVolt = DeviceStateLogic.DefaultWeakGridVolt;

    /// <summary>
    /// Оновити телеметрію (лише з UI-потоку). Змінюється багато похідних рядків одразу,
    /// тому сповіщаємо про зміну всіх властивостей.
    /// </summary>
    public void UpdateTelemetry(DeviceState state, bool online, bool hasData, DateTime lastSeen, int weakGridVolt)
    {
        _state = state;
        _isOnline = online;
        _hasData = hasData;
        _lastSeen = lastSeen;
        _weakGridVolt = weakGridVolt;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Моніторинг зупинено: станція не може вважатися онлайн
    /// </summary>
    public void SetOffline()
    {
        _isOnline = false;
        OnPropertyChanged(string.Empty);
    }

    [JsonIgnore] public DeviceState State => _state;
    [JsonIgnore] public bool IsOnline => _isOnline;
    [JsonIgnore] public bool IsOffline => !_isOnline;
    [JsonIgnore] public bool HasData => _hasData;
    [JsonIgnore] public DateTime LastSeen => _lastSeen;
    [JsonIgnore] public int WeakGridVolt => _weakGridVolt;

    [JsonIgnore] public int? BatteryLevel => _state.Soc;
    [JsonIgnore] public int? InputWatts => _state.InputW;
    [JsonIgnore] public int? OutputWatts => _state.OutputW;
    [JsonIgnore] public int? SolarWatts => _state.SolarW;
    [JsonIgnore] public int? Temperature => _state.BatteryTempC;
    [JsonIgnore] public int? Cycles => _state.Cycles;
    [JsonIgnore] public bool? GridConnected => _state.GridConnected;

    [JsonIgnore] public GridStatus? GridStatus => _state.GetGridStatus(_weakGridVolt);
    [JsonIgnore] public BatteryFlow Flow => _state.GetBatteryFlow();

    /// <summary>
    /// Анімація заряджання: станція на нормальній мережі й батарея не розряджається
    /// </summary>
    [JsonIgnore] public bool IsChargingFromGrid => _isOnline && _state.IsChargingFromGrid(_weakGridVolt);

    // Готові рядки для інтерфейсу
    [JsonIgnore] public string BatteryText => _state.Soc.HasValue ? $"{_state.Soc}%" : "—";
    [JsonIgnore] public double BatteryPercent => _state.Soc ?? 0;
    [JsonIgnore] public string InputText => Format.Watts(_state.InputW);
    [JsonIgnore] public string OutputText => Format.Watts(_state.OutputW);
    [JsonIgnore] public string ModelName => Model.GetDisplayName();
    [JsonIgnore] public string OnlineText => _isOnline ? "Онлайн" : "Офлайн";

    /// <summary>
    /// Рядок 2 картки: потужності й стан мережі, коли він у нормі (або чому даних немає)
    /// </summary>
    [JsonIgnore]
    public string CardPowerText => !_isOnline
        ? _hasData ? "Не на зв'язку" : "Очікування даних…"
        : $"↓ {Format.Watts(_state.InputW)} · ↑ {Format.Watts(_state.OutputW)} · " +
          (GridLevel == 0 ? Format.GridShort(GridStatus, _state.AcInVolt) : string.Empty);

    /// <summary>
    /// Рядок 2 картки: слабка мережа або її відсутність (показується кольором)
    /// </summary>
    [JsonIgnore]
    public string CardGridAlertText => _isOnline && GridLevel != 0 ? Format.GridShort(GridStatus, _state.AcInVolt) : string.Empty;

    /// <summary>0 — норма або невідомо, 1 — слабка мережа, 2 — мережі немає (для кольору)</summary>
    [JsonIgnore]
    public int GridLevel => GridStatus switch
    {
        Protocol.GridStatus.Weak => 1,
        Protocol.GridStatus.None => 2,
        _ => 0
    };

    /// <summary>Рядок 3 картки: що робить батарея, або коли були останні дані</summary>
    [JsonIgnore]
    public string CardStatusText => _isOnline
        ? Format.FlowText(Flow)
        : _lastSeen > DateTime.MinValue ? $"останні дані о {_lastSeen:HH:mm}" : "—";

    #endregion
}

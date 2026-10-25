using System;
using System.Collections.Generic;

namespace PowerHub.Protocol;

/// <summary>
/// Параметри пристрою (плоский словник ключ-значення)
/// </summary>
public class DeviceParams : Dictionary<string, object?>
{
    public DeviceParams() { }

    public DeviceParams(IDictionary<string, object?> source) : base(source) { }
}

/// <summary>
/// Тип MQTT топіку
/// </summary>
public enum TopicKind
{
    Data,       // Телеметрія
    GetReply,   // Відповідь на запит стану
    SetReply    // Підтвердження команди
}

/// <summary>
/// Нормалізований стан пристрою для відображення
/// </summary>
public record DeviceState
{
    public int? Soc { get; init; }                  // Заряд батареї %
    public int? InputW { get; init; }               // Загальний вхід
    public int? OutputW { get; init; }              // Загальний вихід
    public int? AcInW { get; init; }                // AC вхід
    public int? AcOutW { get; init; }               // AC вихід
    public int? SolarW { get; init; }               // Сонячні панелі
    public int? DcOutW { get; init; }               // DC вихід
    public int? UsbOutW { get; init; }              // USB вихід
    public int? BatteryW { get; init; }             // Потужність на клемах батареї, без знака
    public int? ChargeRemainMin { get; init; }      // Час до повного заряду
    public int? DischargeRemainMin { get; init; }   // Час до розряду
    public int? BatteryTempC { get; init; }         // Температура батареї
    public int? AcInVolt { get; init; }             // Напруга AC входу
    public bool? GridConnected { get; init; }       // Підключено до мережі
    public int? Cycles { get; init; }               // Кількість циклів
    public int? Soh { get; init; }                  // State of Health %
}

/// <summary>
/// Вихідне повідомлення для публікації
/// </summary>
public class OutgoingMessage
{
    public byte[] Payload { get; }

    public OutgoingMessage(byte[] payload)
    {
        Payload = payload;
    }
}

/// <summary>
/// Інтерфейс протоколу пристрою
/// </summary>
public interface IDeviceProtocol
{
    /// <summary>
    /// Розпарсити MQTT повідомлення
    /// </summary>
    DeviceParams Parse(TopicKind kind, byte[] payload);

    /// <summary>
    /// Створити запит повного стану
    /// </summary>
    OutgoingMessage QuotaRequest(string serialNumber);

    /// <summary>
    /// Отримати нормалізований стан з параметрів
    /// </summary>
    DeviceState GetState(DeviceParams parameters);

    /// <summary>
    /// Отримати список доступних елементів керування
    /// </summary>
    List<IControl> GetControls(string serialNumber);
}

/// <summary>
/// Секція елементів керування
/// </summary>
public enum ControlSection
{
    Outputs,    // Виходи
    Charging,   // Заряджання
    Backup,     // Резерв
    System      // Система
}

/// <summary>
/// Базовий інтерфейс елементу керування
/// </summary>
public interface IControl
{
    string Id { get; }
    string Label { get; }
    ControlSection Section { get; }
}

/// <summary>
/// Перемикач (on/off)
/// </summary>
public record ToggleControl : IControl
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public ControlSection Section { get; init; }
    public Func<DeviceParams, bool?> Read { get; init; } = _ => null;
    public Func<bool, DeviceParams, OutgoingMessage> Command { get; init; } = (_, _) => new OutgoingMessage(Array.Empty<byte>());
    public Func<bool, DeviceParams> Optimistic { get; init; } = _ => new DeviceParams();
}

/// <summary>
/// Повзунок (діапазон значень)
/// </summary>
public record SliderControl : IControl
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public ControlSection Section { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; }
    public string Unit { get; init; } = string.Empty;
    public Func<DeviceParams, int?> Read { get; init; } = _ => null;
    public Func<int, DeviceParams, OutgoingMessage> Command { get; init; } = (_, _) => new OutgoingMessage(Array.Empty<byte>());
    public Func<int, DeviceParams> Optimistic { get; init; } = _ => new DeviceParams();
}

/// <summary>
/// Вибір зі списку опцій
/// </summary>
public record ChoiceControl : IControl
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public ControlSection Section { get; init; }
    public List<(string Label, int Value)> Options { get; init; } = new();
    public Func<DeviceParams, int?> Read { get; init; } = _ => null;
    public Func<int, DeviceParams, OutgoingMessage> Command { get; init; } = (_, _) => new OutgoingMessage(Array.Empty<byte>());
    public Func<int, DeviceParams> Optimistic { get; init; } = _ => new DeviceParams();
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerHub.Protocol;

/// <summary>
/// Розширення для роботи з параметрами пристрою
/// </summary>
public static class DeviceParamsExtensions
{
    /// <summary>
    /// Отримати числове значення (підтримує будь-які числові типи з JSON і protobuf)
    /// </summary>
    public static double? GetNumber(this DeviceParams parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return null;

        return value switch
        {
            bool b => b ? 1.0 : 0.0,
            string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            IConvertible c => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>
    /// Отримати ціле число
    /// </summary>
    public static int? GetInt(this DeviceParams parameters, string key)
    {
        var num = parameters.GetNumber(key);
        return num.HasValue ? (int)Math.Round(num.Value) : null;
    }

    /// <summary>
    /// Отримати абсолютне значення (вихідна потужність у protobuf-моделях буває від'ємною)
    /// </summary>
    public static int? GetAbsInt(this DeviceParams parameters, string key)
    {
        var num = parameters.GetNumber(key);
        return num.HasValue ? (int)Math.Round(Math.Abs(num.Value)) : null;
    }

    /// <summary>
    /// Отримати булеве значення
    /// </summary>
    public static bool? GetFlag(this DeviceParams parameters, string key)
    {
        var num = parameters.GetNumber(key);
        return num.HasValue ? num.Value != 0.0 : null;
    }

    /// <summary>
    /// Сума значень кількох ключів
    /// </summary>
    public static int? SumOf(this DeviceParams parameters, params string[] keys) =>
        parameters.SumOf(false, keys);

    /// <summary>
    /// Сума значень кількох ключів (за модулем, якщо abs)
    /// </summary>
    public static int? SumOf(this DeviceParams parameters, bool abs, params string[] keys)
    {
        var values = keys
            .Select(k => abs ? parameters.GetAbsInt(k) : parameters.GetInt(k))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        return values.Count > 0 ? values.Sum() : null;
    }

    /// <summary>
    /// Валідувати хвилини: 5939 і більше означає «немає оцінки», справжні оцінки менші
    /// </summary>
    /// <summary>
    /// Потужність батареї (без знака) для JSON-станцій. Готове поле outputWatts беремо, лише коли воно
    /// не нуль: у Delta Max воно завжди 0. Інакше напруга × струм (мВ × мА) по головній батареї
    /// та додаткових, якщо вони є; null, якщо даних немає
    /// </summary>
    public static int? BatteryPowerW(this DeviceParams parameters, string wattsKey, params string[] packs)
    {
        if (parameters.GetAbsInt(wattsKey) is int watts && watts > 0)
            return watts;

        double? sum = null;
        foreach (var pack in packs)
        {
            if (parameters.GetNumber($"{pack}.vol") is double mv && parameters.GetNumber($"{pack}.amp") is double ma)
                sum = (sum ?? 0) + Math.Abs(mv * ma) / 1_000_000;
        }
        return sum.HasValue ? (int)Math.Round(sum.Value) : parameters.GetAbsInt(wattsKey);
    }

    /// <summary>
    /// Мережа для protobuf-станцій (Delta 3, Delta Pro 3). Прапорець plug_in_info_ac_in_flag буває 0,
    /// хоча станція заряджається від розетки (бачили на Delta Pro 3), тож фізичні ознаки мають перевагу:
    /// напруга на вході AC або потужність, що надходить з AC, означають, що мережа є
    /// </summary>
    public static bool? ProtoGridConnected(this DeviceParams parameters, int? acInVolt, int? acInW)
    {
        if (acInVolt > 100 || acInW > 20)
            return true;
        return parameters.GetFlag("plug_in_info_ac_in_flag") ?? (acInVolt.HasValue ? false : null);
    }

    public static int? ValidMinutes(int? minutes)
    {
        return minutes is >= 1 and < 5939 ? minutes : null;
    }
}

/// <summary>
/// Загальні опції для елементів керування
/// </summary>
public static class ControlOptions
{
    public static readonly List<(string, int)> ScreenTimeoutOptions = new()
    {
        ("Ніколи", 0),
        ("10 с", 10),
        ("30 с", 30),
        ("1 хв", 60),
        ("5 хв", 300),
        ("30 хв", 1800)
    };

    public static readonly List<(string, int)> StandbyOptions = new()
    {
        ("Ніколи", 0),
        ("30 хв", 30),
        ("1 год", 60),
        ("2 год", 120),
        ("4 год", 240),
        ("6 год", 360),
        ("12 год", 720),
        ("24 год", 1440)
    };
}

/// <summary>
/// Фабрики елементів керування: команда отримує значення 1/0 для перемикачів
/// </summary>
public static class Controls
{
    public static ToggleControl Toggle(
        string id, string label, ControlSection section, string key,
        Func<int, DeviceParams, OutgoingMessage> command) => new()
    {
        Id = id,
        Label = label,
        Section = section,
        Read = p => p.GetFlag(key),
        Command = (on, p) => command(on ? 1 : 0, p),
        Optimistic = on => new DeviceParams { [key] = on ? 1 : 0 }
    };

    public static SliderControl Slider(
        string id, string label, ControlSection section, string key,
        int min, int max, int step, string unit,
        Func<int, DeviceParams, OutgoingMessage> command) => new()
    {
        Id = id,
        Label = label,
        Section = section,
        Min = min,
        Max = max,
        Step = step,
        Unit = unit,
        Read = p => p.GetInt(key),
        Command = command,
        Optimistic = v => new DeviceParams { [key] = v }
    };

    public static ChoiceControl Choice(
        string id, string label, ControlSection section, string key,
        List<(string Label, int Value)> options,
        Func<int, DeviceParams, OutgoingMessage> command) => new()
    {
        Id = id,
        Label = label,
        Section = section,
        Options = options,
        Read = p => p.GetInt(key),
        Command = command,
        Optimistic = v => new DeviceParams { [key] = v }
    };
}

using System.Collections.Generic;
using System.Linq;
using static PowerHub.Protocol.Controls;

namespace PowerHub.Protocol;

/// <summary>
/// Delta Max: старіша JSON-прошивка з ключами `bmsMaster.*` / `ems.*` і числовими "TCP"-командами.
/// За tolwi/hassio-ecoflow-cloud (internal/delta_max.py).
/// </summary>
public sealed class DeltaMaxProtocol : Delta2Family
{
    public static readonly DeltaMaxProtocol Instance = new();

    protected override string[] SolarKeys { get; } = { "mppt.inWatts" };

    public override DeviceState GetState(DeviceParams p)
    {
        var acInVoltRaw = p.GetNumber("inv.acInVol");
        int? acInVolt = acInVoltRaw.HasValue ? (int)(acInVoltRaw.Value / 1000) : null;
        var acIn = p.GetInt("inv.inputWatts");

        return new DeviceState
        {
            Soc = p.GetInt("ems.lcdShowSoc") ?? p.GetInt("bmsMaster.soc"),
            InputW = p.GetInt("pd.wattsInSum"),
            OutputW = p.GetInt("pd.wattsOutSum"),
            AcInW = acIn,
            AcOutW = p.GetInt("inv.outputWatts"),
            SolarW = p.GetInt("mppt.inWatts"),
            DcOutW = p.GetInt("mppt.outWatts"),
            UsbOutW = p.SumOf("pd.typec1Watts", "pd.typec2Watts", "pd.usb1Watts",
                "pd.usb2Watts", "pd.qcUsb1Watts", "pd.qcUsb2Watts"),
            ChargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("ems.chgRemainTime")),
            DischargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("ems.dsgRemainTime")),
            BatteryTempC = p.GetInt("bmsMaster.temp"),
            AcInVolt = acInVolt,
            GridConnected = acInVolt == null && acIn == null
                ? null
                : (acInVolt ?? 0) > 100 || (acIn ?? 0) > 0,
            BatteryW = p.GetAbsInt("bmsMaster.outputWatts"),
            Cycles = p.GetInt("bmsMaster.cycles"),
            Soh = p.GetInt("bmsMaster.soh")
        };
    }

    private static OutgoingMessage Tcp(int moduleType, int id, params (string Key, object Value)[] items)
    {
        var args = Args(items);
        args["id"] = id;
        return JsonMessages.Command(moduleType, "TCP", args);
    }

    public override List<IControl> GetControls(string serialNumber) => new List<IControl>
    {
        Toggle("ac_out", "AC вихід", ControlSection.Outputs, "inv.cfgAcEnabled", (v, _) => Tcp(0, 66, ("enabled", v))),
        Toggle("xboost", "X-Boost", ControlSection.Outputs, "inv.cfgAcXboost", (v, _) => Tcp(5, 66, ("xboost", v))),
        Toggle("dc_out", "DC 12V вихід", ControlSection.Outputs, "mppt.carState", (v, _) => Tcp(0, 81, ("enabled", v))),
        Toggle("usb_out", "USB виходи", ControlSection.Outputs, "pd.dcOutState", (v, _) => Tcp(0, 34, ("enabled", v))),
        Toggle("ac_auto", "AC завжди увімкнений", ControlSection.Outputs, "pd.acAutoOnCfg",
            (v, _) => JsonMessages.Command(1, "acAutoOn", Args(("cfg", v)))),
        Slider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "inv.cfgSlowChgWatts", 100, 2000, 100, "Вт",
            (v, _) => Tcp(0, 69, ("slowChgPower", v))),
        Slider("max_chg", "Макс. рівень заряду", ControlSection.Charging, "ems.maxChargeSoc", 50, 100, 1, "%",
            (v, _) => Tcp(2, 49, ("maxChgSoc", v))),
        Slider("min_dsg", "Мін. рівень розряду", ControlSection.Charging, "ems.minDsgSoc", 0, 30, 1, "%",
            (v, _) => Tcp(2, 51, ("minDsgSoc", v))),
        Toggle("pv_prio", "Пріоритет сонячного заряду", ControlSection.Charging, "pd.pvChgPrioSet",
            (v, _) => JsonMessages.Command(1, "pvChangePrio", Args(("pvChangeSet", v)))),
        Toggle("quiet", "Беззвучний режим", ControlSection.System, "pd.beepState", (v, _) => Tcp(5, 38, ("enabled", v)))
    };
}

/// <summary>
/// River 2 Max: та сама JSON-структура, що й у Delta 2, з власними командами таймаутів і режиму DC.
/// За tolwi/hassio-ecoflow-cloud (internal/river2_max.py).
/// </summary>
public sealed class River2MaxProtocol : Delta2Family
{
    public static readonly River2MaxProtocol Instance = new();

    protected override string[] SolarKeys { get; } = { "mppt.inWatts" };

    public override DeviceState GetState(DeviceParams p) =>
        base.GetState(p) with { DcOutW = p.GetInt("pd.carWatts") ?? p.GetInt("mppt.outWatts") };

    public override List<IControl> GetControls(string serialNumber) => new IControl[]
    {
        Toggle("ac_out", "AC вихід", ControlSection.Outputs, "mppt.cfgAcEnabled",
            (v, _) => JsonMessages.Command(5, "acOutCfg", Args(("enabled", v), ("out_voltage", -1), ("out_freq", 255), ("xboost", 255)))),
        Toggle("xboost", "X-Boost", ControlSection.Outputs, "mppt.cfgAcXboost",
            (v, _) => JsonMessages.Command(5, "acOutCfg", Args(("enabled", 255), ("out_voltage", -1), ("out_freq", 255), ("xboost", v)))),
        Toggle("dc_out", "DC 12V вихід", ControlSection.Outputs, "pd.carState",
            (v, _) => JsonMessages.Command(5, "mpptCar", Args(("enabled", v)))),
        Toggle("ac_auto", "AC завжди увімкнений", ControlSection.Outputs, "pd.acAutoOutConfig",
            (v, p) => JsonMessages.Command(1, "acAutoOutConfig",
                Args(("acAutoOutConfig", v), ("minAcOutSoc", (p.GetInt("bms_emsStatus.minDsgSoc") ?? 0) + 5)))),
        Slider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "mppt.cfgChgWatts", 50, 660, 10, "Вт",
            (v, _) => JsonMessages.Command(5, "acChgCfg", Args(("chgWatts", v), ("chgPauseFlag", 255)))),
        Choice("dc_chg_a", "Струм заряду DC", ControlSection.Charging, "mppt.dcChgCurrent",
            new() { ("4 А", 4000), ("6 А", 6000), ("8 А", 8000) },
            (v, _) => JsonMessages.Command(5, "dcChgCfg", Args(("dcChgCfg", v)))),
        Choice("dc_mode", "Режим DC-входу", ControlSection.Charging, "mppt.cfgChgType",
            new() { ("Авто", 0), ("Сонце", 1), ("Автомобіль", 2) },
            (v, _) => JsonMessages.Command(5, "chaType", Args(("chaType", v))))
    }
    .Concat(SocLimits(null))
    .Concat(Backup())
    .Concat(new IControl[]
    {
        Choice("screen", "Вимкнення екрана", ControlSection.System, "mppt.scrStandbyMin", ControlOptions.ScreenTimeoutOptions,
            (v, _) => JsonMessages.Command(5, "lcdCfg", Args(("brighLevel", 255), ("delayOff", v)))),
        Choice("unit_standby", "Автовимкнення станції", ControlSection.System, "mppt.powStandbyMin", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(5, "standby", Args(("standbyMins", v)))),
        Choice("ac_standby", "Автовимкнення AC", ControlSection.System, "mppt.acStandbyMins", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(5, "acStandby", Args(("standbyMins", v))))
    })
    .ToList();
}

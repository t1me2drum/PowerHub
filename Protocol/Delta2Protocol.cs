using System.Collections.Generic;
using System.Linq;
using static PowerHub.Protocol.Controls;

namespace PowerHub.Protocol;

/// <summary>
/// Delta 2 і Delta 2 Max працюють з JSON через MQTT API застосунку.
/// Назви ключів і команди — за tolwi/hassio-ecoflow-cloud (internal/delta2.py, delta2_max.py).
/// </summary>
public abstract class Delta2Family : IDeviceProtocol
{
    protected abstract string[] SolarKeys { get; }

    public virtual DeviceParams Parse(TopicKind kind, byte[] payload) => kind switch
    {
        TopicKind.Data or TopicKind.GetReply => JsonMessages.Parse(payload),
        _ => new DeviceParams()
    };

    public OutgoingMessage QuotaRequest(string serialNumber) => JsonMessages.LatestQuotas();

    public virtual DeviceState GetState(DeviceParams p)
    {
        var acInVoltRaw = p.GetNumber("inv.acInVol");
        int? acInVolt = acInVoltRaw.HasValue ? (int)(acInVoltRaw.Value / 1000) : null;
        var acIn = p.GetInt("inv.inputWatts");

        return new DeviceState
        {
            Soc = p.GetInt("bms_emsStatus.lcdShowSoc") ?? p.GetInt("bms_bmsStatus.soc"),
            InputW = p.GetInt("pd.wattsInSum"),
            OutputW = p.GetInt("pd.wattsOutSum"),
            AcInW = acIn,
            AcOutW = p.GetInt("inv.outputWatts"),
            SolarW = p.SumOf(SolarKeys),
            DcOutW = p.GetInt("mppt.outWatts") ?? p.GetInt("pd.carWatts"),
            UsbOutW = p.SumOf("pd.typec1Watts", "pd.typec2Watts", "pd.usb1Watts",
                "pd.usb2Watts", "pd.qcUsb1Watts", "pd.qcUsb2Watts"),
            ChargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("bms_emsStatus.chgRemainTime")),
            DischargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("bms_emsStatus.dsgRemainTime")),
            BatteryTempC = p.GetInt("bms_bmsStatus.temp"),
            AcInVolt = acInVolt,
            GridConnected = acInVolt == null && acIn == null
                ? null
                : (acInVolt ?? 0) > 100 || (acIn ?? 0) > 0,
            BatteryW = p.BatteryPowerW("bms_bmsStatus.outputWatts", "bms_bmsStatus"),
            Cycles = p.GetInt("bms_bmsStatus.cycles"),
            Soh = p.GetInt("bms_bmsStatus.soh")
        };
    }

    public abstract List<IControl> GetControls(string serialNumber);

    protected static Dictionary<string, object> Args(params (string Key, object Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);

    protected static IEnumerable<IControl> SocLimits(string? moduleSn) => new IControl[]
    {
        Slider("max_chg", "Макс. рівень заряду", ControlSection.Charging, "bms_emsStatus.maxChargeSoc", 50, 100, 1, "%",
            (v, _) => JsonMessages.Command(2, "upsConfig", Args(("maxChgSoc", v)), moduleSn)),
        Slider("min_dsg", "Мін. рівень розряду", ControlSection.Charging, "bms_emsStatus.minDsgSoc", 0, 30, 1, "%",
            (v, _) => JsonMessages.Command(2, "dsgCfg", Args(("minDsgSoc", v)), moduleSn))
    };

    protected static IEnumerable<IControl> Backup() => new IControl[]
    {
        Toggle("bp_enabled", "Резерв заряду", ControlSection.Backup, "pd.watchIsConfig",
            (v, _) => JsonMessages.Command(1, "watthConfig",
                Args(("bpPowerSoc", v * 50), ("minChgSoc", 0), ("isConfig", v), ("minDsgSoc", 0)))),
        Slider("bp_level", "Рівень резерву", ControlSection.Backup, "pd.bpPowerSoc", 5, 100, 5, "%",
            (v, _) => JsonMessages.Command(1, "watthConfig",
                Args(("isConfig", 1), ("bpPowerSoc", v), ("minDsgSoc", 0), ("minChgSoc", 0))))
    };
}

public sealed class Delta2Protocol : Delta2Family
{
    public static readonly Delta2Protocol Instance = new();

    protected override string[] SolarKeys { get; } = { "mppt.inWatts" };

    public override List<IControl> GetControls(string serialNumber) => new IControl[]
    {
        Toggle("ac_out", "AC вихід", ControlSection.Outputs, "mppt.cfgAcEnabled",
            (v, _) => JsonMessages.Command(5, "acOutCfg", Args(("enabled", v), ("out_voltage", -1), ("out_freq", 255), ("xboost", 255)))),
        Toggle("xboost", "X-Boost", ControlSection.Outputs, "mppt.cfgAcXboost",
            (v, _) => JsonMessages.Command(5, "acOutCfg", Args(("enabled", 255), ("out_voltage", -1), ("out_freq", 255), ("xboost", v)))),
        Toggle("dc_out", "DC 12V вихід", ControlSection.Outputs, "pd.carState",
            (v, _) => JsonMessages.Command(5, "mpptCar", Args(("enabled", v)))),
        Toggle("usb_out", "USB виходи", ControlSection.Outputs, "pd.dcOutState",
            (v, _) => JsonMessages.Command(1, "dcOutCfg", Args(("enabled", v)))),
        Toggle("ac_auto", "AC завжди увімкнений", ControlSection.Outputs, "pd.acAutoOutConfig",
            (v, p) => JsonMessages.Command(1, "acAutoOutConfig",
                Args(("acAutoOutConfig", v), ("minAcOutSoc", (p.GetInt("bms_emsStatus.minDsgSoc") ?? 0) + 5)))),
        Slider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "mppt.cfgChgWatts", 200, 1200, 100, "Вт",
            (v, _) => JsonMessages.Command(5, "acChgCfg", Args(("chgWatts", v), ("chgPauseFlag", 255)))),
        Choice("dc_chg_a", "Струм заряду DC", ControlSection.Charging, "mppt.dcChgCurrent",
            new() { ("4 А", 4000), ("6 А", 6000), ("8 А", 8000) },
            (v, _) => JsonMessages.Command(5, "dcChgCfg", Args(("dcChgCfg", v)))),
        Toggle("pv_prio", "Пріоритет сонячного заряду", ControlSection.Charging, "pd.pvChgPrioSet",
            (v, _) => JsonMessages.Command(1, "pvChangePrio", Args(("pvChangeSet", v))))
    }
    .Concat(SocLimits(null))
    .Concat(Backup())
    .Concat(new IControl[]
    {
        Toggle("quiet", "Беззвучний режим", ControlSection.System, "mppt.beepState",
            (v, _) => JsonMessages.Command(5, "quietMode", Args(("enabled", v)))),
        Choice("screen", "Вимкнення екрана", ControlSection.System, "pd.lcdOffSec", ControlOptions.ScreenTimeoutOptions,
            (v, _) => JsonMessages.Command(1, "lcdCfg", Args(("brighLevel", 255), ("delayOff", v)))),
        Choice("unit_standby", "Автовимкнення станції", ControlSection.System, "pd.standbyMin", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(1, "standbyTime", Args(("standbyMin", v)))),
        Choice("ac_standby", "Автовимкнення AC", ControlSection.System, "mppt.acStandbyMins", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(5, "standbyTime", Args(("standbyMins", v)))),
        Choice("dc_standby", "Автовимкнення DC", ControlSection.System, "mppt.carStandbyMin", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(5, "carStandby", Args(("standbyMins", v))))
    })
    .ToList();
}

public sealed class Delta2MaxProtocol : Delta2Family
{
    public static readonly Delta2MaxProtocol Instance = new();

    protected override string[] SolarKeys { get; } = { "mppt.inWatts", "mppt.pv2InWatts" };

    public override List<IControl> GetControls(string sn) => new IControl[]
    {
        Toggle("ac_out", "AC вихід", ControlSection.Outputs, "inv.cfgAcEnabled",
            (v, _) => JsonMessages.Command(3, "acOutCfg", Args(("enabled", v), ("out_voltage", -1), ("out_freq", 255), ("xboost", 255)), sn)),
        Toggle("xboost", "X-Boost", ControlSection.Outputs, "inv.cfgAcXboost",
            (v, _) => JsonMessages.Command(3, "acOutCfg", Args(("xboost", v)), sn)),
        Toggle("dc_out", "DC 12V вихід", ControlSection.Outputs, "pd.carState",
            (v, _) => JsonMessages.Command(5, "mpptCar", Args(("enabled", v)))),
        Toggle("usb_out", "USB виходи", ControlSection.Outputs, "pd.dcOutState",
            (v, _) => JsonMessages.Command(1, "dcOutCfg", Args(("enabled", v)), sn)),
        Toggle("ac_auto", "AC завжди увімкнений", ControlSection.Outputs, "pd.newAcAutoOnCfg",
            (v, _) => JsonMessages.Command(1, "newAcAutoOnCfg", Args(("enabled", v), ("minAcSoc", 5)), sn)),
        Slider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "inv.SlowChgWatts", 200, 2400, 100, "Вт",
            (v, _) => JsonMessages.Command(3, "acChgCfg", Args(("slowChgWatts", v), ("fastChgWatts", 2000), ("chgPauseFlag", 0)), sn))
    }
    .Concat(SocLimits(sn))
    .Concat(Backup())
    .Concat(new IControl[]
    {
        Toggle("quiet", "Беззвучний режим", ControlSection.System, "pd.beepMode",
            (v, _) => JsonMessages.Command(1, "quietCfg", Args(("enabled", v)), sn)),
        Choice("screen", "Вимкнення екрана", ControlSection.System, "pd.lcdOffSec", ControlOptions.ScreenTimeoutOptions,
            (v, _) => JsonMessages.Command(1, "lcdCfg", Args(("brighLevel", 255), ("delayOff", v)), sn)),
        Choice("unit_standby", "Автовимкнення станції", ControlSection.System, "inv.standbyMin", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(1, "standbyTime", Args(("standbyMin", v)), sn)),
        Choice("dc_standby", "Автовимкнення DC", ControlSection.System, "mppt.carStandbyMin", ControlOptions.StandbyOptions,
            (v, _) => JsonMessages.Command(5, "standbyTime", Args(("standbyMins", v)), sn))
    })
    .ToList();
}

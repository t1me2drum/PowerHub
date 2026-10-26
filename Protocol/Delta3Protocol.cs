using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PowerHub.Protocol.Proto;

namespace PowerHub.Protocol;

/// <summary>
/// Delta 3 / Delta 3 Max: protobuf через MQTT API застосунку.
/// Ідентифікатори повідомлень і особливості полів — за tolwi/hassio-ecoflow-cloud (internal/delta3.py).
/// </summary>
public sealed class Delta3Protocol : IDeviceProtocol
{
    public static readonly Delta3Protocol Standard = new(maxAcChargeW: 1500);
    public static readonly Delta3Protocol Max = new(maxAcChargeW: 2400);

    private static readonly HashSet<(int, int)> BmsHeartbeat = new()
    {
        (3, 1), (3, 2), (3, 30), (3, 50), (32, 1), (32, 3), (32, 50), (32, 51), (32, 52)
    };

    private static readonly string[] UsbFlows = { "flow_info_qcusb1", "flow_info_qcusb2", "flow_info_typec1", "flow_info_typec2" };

    private readonly int _maxAcChargeW;

    private Delta3Protocol(int maxAcChargeW)
    {
        _maxAcChargeW = maxAcChargeW;
    }

    public DeviceParams Parse(TopicKind kind, byte[] payload)
    {
        var output = new DeviceParams();
        foreach (var frame in ProtoCodec.Frames(payload))
        {
            foreach (var kv in Decode(frame))
                output[kv.Key] = kv.Value;
        }
        return output;
    }

    private static DeviceParams Decode(ProtoCodec.Frame f)
    {
        var key = (f.CmdFunc, f.CmdId);

        if (key == (254, 21))
        {
            var display = ProtoCodec.DecodeFlat(Delta3DisplayPropertyUpload.Parser, f.Pdata);
            ProtoCodec.DeriveFlag(display, new[] { "flow_info_12v" }, "cfg_dc12v_out_open");
            ProtoCodec.DeriveFlag(display, new[] { "flow_info_ac_out" }, "cfg_ac_out_open");
            ProtoCodec.DeriveFlag(display, UsbFlows, "cfg_usb_open");
            return display;
        }
        if (key == (254, 22))
            return ProtoCodec.DecodeFlat(Delta3RuntimePropertyUpload.Parser, f.Pdata);
        if (key == (254, 18))
        {
            var reply = ProtoCodec.DecodeFlat(Delta3SetReply.Parser, f.Pdata);
            return reply.TryGetValue("config_ok", out var ok) && ok is true ? reply : new DeviceParams();
        }
        if (key == (32, 2))
            return ProtoCodec.DecodeFlat(Delta3CMSHeartBeatReport.Parser, f.Pdata);
        if (BmsHeartbeat.Contains(key))
            return ProtoCodec.DecodeFlat(Delta3BMSHeartBeatReport.Parser, f.Pdata);

        return new DeviceParams();
    }

    public OutgoingMessage QuotaRequest(string serialNumber) => ProtoCodec.SnapshotRequest();

    public DeviceState GetState(DeviceParams p)
    {
        // Без мережі станція надсилає дрібні значення (бачили 1 В на Delta 3): напругою їх не вважаємо
        var acInVolt = p.GetInt("plug_in_info_ac_in_vol") is int v && v >= 50 ? v : (int?)null;
        return new DeviceState
        {
            Soc = p.GetInt("cms_batt_soc") ?? p.GetInt("bms_batt_soc"),
            InputW = p.GetInt("pow_in_sum_w"),
            OutputW = p.GetInt("pow_out_sum_w"),
            AcInW = p.GetInt("pow_get_ac_in"),
            AcOutW = p.GetAbsInt("pow_get_ac_out"),
            SolarW = p.GetInt("pow_get_pv"),
            DcOutW = p.GetAbsInt("pow_get_12v"),
            UsbOutW = p.SumOf(true, "pow_get_qcusb1", "pow_get_qcusb2", "pow_get_typec1", "pow_get_typec2"),
            ChargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("cms_chg_rem_time") ?? p.GetInt("bms_chg_rem_time")),
            DischargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("cms_dsg_rem_time") ?? p.GetInt("bms_dsg_rem_time")),
            BatteryTempC = p.GetInt("bms_max_cell_temp"),
            AcInVolt = acInVolt,
            GridConnected = p.ProtoGridConnected(acInVolt, p.GetInt("pow_get_ac_in")),
            BatteryW = p.GetAbsInt("pow_get_bms"),
            Cycles = p.GetInt("cycles"),
            Soh = p.GetInt("bms_batt_soh")
        };
    }

    private static OutgoingMessage Set(string sn, string field, int value, int? dataLen = null)
    {
        var pdata = ProtoCodec.SetCommandPdata<Delta3SetCommand>(field, value);
        return ProtoCodec.SetPacket(sn, pdata, dataLen ?? pdata.Length);
    }

    /// <summary>
    /// Прошивка ігнорує саме поле 54; застосунок завжди додає поле 125 = 0 як ознаку підтвердження
    /// </summary>
    private static OutgoingMessage AcChargePower(string sn, int watts)
    {
        var pdata = new byte[] { 0xB0, 0x03 }
            .Concat(ProtoCodec.EncodeVarint(watts))
            .Concat(new byte[] { 0xE8, 0x07, 0x00 })
            .ToArray();
        return ProtoCodec.SetPacket(sn, pdata);
    }

    private static OutgoingMessage EnergyBackup(string sn, int? enabled, int startSoc)
    {
        var backup = new Delta3CfgEnergyBackup { EnergyBackupStartSoc = (uint)startSoc };
        if (enabled.HasValue)
            backup.EnergyBackupEn = (uint)enabled.Value;

        var command = new Delta3SetCommand { CfgEnergyBackup = backup };
        return ProtoCodec.SetPacket(sn, command.ToByteArray());
    }

    private static ToggleControl Toggle(string sn, string id, string label, ControlSection section, string field, int? dataLen = null) =>
        Controls.Toggle(id, label, section, field, (v, _) => Set(sn, field, v, dataLen));

    private static ChoiceControl Choice(string sn, string id, string label, ControlSection section, string field,
        List<(string, int)> options) =>
        Controls.Choice(id, label, section, field, options, (v, _) => Set(sn, field, v));

    public List<IControl> GetControls(string sn) => new()
    {
        Toggle(sn, "ac_out", "AC вихід", ControlSection.Outputs, "cfg_ac_out_open"),
        Toggle(sn, "xboost", "X-Boost", ControlSection.Outputs, "xboost_en"),
        Toggle(sn, "dc_out", "DC 12V вихід", ControlSection.Outputs, "cfg_dc12v_out_open"),
        Toggle(sn, "usb_out", "USB виходи", ControlSection.Outputs, "cfg_usb_open"),
        Toggle(sn, "power_memory", "Пам'ять стану виходів", ControlSection.Outputs, "output_power_off_memory"),
        Controls.Slider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "plug_in_info_ac_in_chg_pow_max",
            100, _maxAcChargeW, 50, "Вт", (v, _) => AcChargePower(sn, v)),
        Controls.Slider("max_chg", "Макс. рівень заряду", ControlSection.Charging, "cms_max_chg_soc", 50, 100, 1, "%",
            (v, _) => Set(sn, "cms_max_chg_soc", v)),
        Controls.Slider("min_dsg", "Мін. рівень розряду", ControlSection.Charging, "cms_min_dsg_soc", 0, 30, 1, "%",
            (v, _) => Set(sn, "cms_min_dsg_soc", v)),
        Choice(sn, "pv_amp", "Макс. струм сонячного заряду", ControlSection.Charging, "plug_in_info_pv_dc_amp_max",
            new() { ("4 А", 4), ("5 А", 5), ("6 А", 6), ("7 А", 7), ("8 А", 8) }),
        Controls.Toggle("bp_enabled", "Резерв заряду", ControlSection.Backup, "energy_backup_en",
            (v, p) => EnergyBackup(sn, v, p.GetInt("energy_backup_start_soc") ?? 5)),
        Controls.Slider("bp_level", "Рівень резерву", ControlSection.Backup, "energy_backup_start_soc", 5, 100, 5, "%",
            (v, _) => EnergyBackup(sn, 1, v)),
        Toggle(sn, "beep", "Звуковий сигнал", ControlSection.System, "en_beep", dataLen: 2),
        Choice(sn, "screen", "Вимкнення екрана", ControlSection.System, "screen_off_time", ControlOptions.ScreenTimeoutOptions),
        Choice(sn, "unit_standby", "Автовимкнення станції", ControlSection.System, "dev_standby_time", ControlOptions.StandbyOptions),
        Choice(sn, "ac_standby", "Автовимкнення AC", ControlSection.System, "ac_standby_time", ControlOptions.StandbyOptions),
        Choice(sn, "dc_standby", "Автовимкнення DC", ControlSection.System, "dc_standby_time", ControlOptions.StandbyOptions)
    };
}

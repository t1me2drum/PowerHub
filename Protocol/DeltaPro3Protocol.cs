using System.Collections.Generic;
using PowerHub.Protocol.Proto;
using static PowerHub.Protocol.Controls;

namespace PowerHub.Protocol;

/// <summary>
/// Delta Pro 3: телеметрія приходить у protobuf, команди — JSON "TCP"-повідомлення з числовим id параметра.
/// За tolwi/hassio-ecoflow-cloud (internal/delta_pro_3.py).
/// </summary>
public sealed class DeltaPro3Protocol : IDeviceProtocol
{
    public static readonly DeltaPro3Protocol Instance = new();

    private static readonly HashSet<(int, int)> BmsHeartbeat = new()
    {
        (3, 1), (3, 2), (3, 30), (3, 50),
        (254, 24), (254, 25), (254, 26), (254, 27), (254, 28), (254, 29), (254, 30),
        (32, 1), (32, 3), (32, 50), (32, 51), (32, 52)
    };

    public DeviceParams Parse(TopicKind kind, byte[] payload)
    {
        if (JsonMessages.LooksLikeJson(payload))
            return kind == TopicKind.SetReply ? new DeviceParams() : JsonMessages.Parse(payload);

        var output = new DeviceParams();
        foreach (var f in ProtoCodec.Frames(payload))
        {
            var key = (f.CmdFunc, f.CmdId);
            DeviceParams decoded;

            if (key == (254, 21))
            {
                decoded = ProtoCodec.DecodeFlat(DP3DisplayPropertyUpload.Parser, f.Pdata);
                ProtoCodec.DeriveFlag(decoded, new[] { "flow_info_ac_hv_out" }, "cfg_hv_ac_out_open");
                ProtoCodec.DeriveFlag(decoded, new[] { "flow_info_ac_lv_out" }, "cfg_lv_ac_out_open");
                ProtoCodec.DeriveFlag(decoded, new[] { "flow_info_12v" }, "cfg_dc_12v_out_open");
                ProtoCodec.DeriveFlag(decoded, new[] { "flow_info_24v" }, "cfg_dc_24v_out_open");
            }
            else if (key == (254, 22))
                decoded = ProtoCodec.DecodeFlat(DP3RuntimePropertyUpload.Parser, f.Pdata);
            else if (key == (32, 2))
                decoded = ProtoCodec.DecodeFlat(DP3CMSHeartBeatReport.Parser, f.Pdata);
            else if (BmsHeartbeat.Contains(key))
                decoded = ProtoCodec.DecodeFlat(DP3BMSHeartBeatReport.Parser, f.Pdata);
            else
                continue;

            foreach (var kv in decoded)
                output[kv.Key] = kv.Value;
        }
        return output;
    }

    public OutgoingMessage QuotaRequest(string serialNumber) => JsonMessages.LatestQuotas();

    public DeviceState GetState(DeviceParams p)
    {
        var acInVolt = p.GetInt("plug_in_info_ac_in_vol");
        return new DeviceState
        {
            Soc = p.GetInt("cms_batt_soc") ?? p.GetInt("bms_batt_soc"),
            InputW = p.GetInt("pow_in_sum_w"),
            OutputW = p.GetInt("pow_out_sum_w"),
            AcInW = p.GetInt("pow_get_ac_in"),
            AcOutW = p.SumOf(true, "pow_get_ac_hv_out", "pow_get_ac_lv_out") ?? p.GetAbsInt("pow_get_ac"),
            SolarW = p.SumOf("pow_get_pv_h", "pow_get_pv_l"),
            DcOutW = p.SumOf(true, "pow_get_12v", "pow_get_24v"),
            UsbOutW = p.SumOf(true, "pow_get_qcusb1", "pow_get_qcusb2", "pow_get_typec1", "pow_get_typec2"),
            ChargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("cms_chg_rem_time") ?? p.GetInt("bms_chg_rem_time")),
            DischargeRemainMin = DeviceParamsExtensions.ValidMinutes(p.GetInt("cms_dsg_rem_time") ?? p.GetInt("bms_dsg_rem_time")),
            BatteryTempC = p.GetInt("bms_max_cell_temp"),
            AcInVolt = acInVolt,
            GridConnected = p.ProtoGridConnected(acInVolt, p.GetInt("pow_get_ac_in")),
            Cycles = p.GetInt("cycles"),
            Soh = p.GetInt("bms_batt_soh")
        };
    }

    private static OutgoingMessage Tcp(int id, string param, int value) =>
        JsonMessages.Command(0, "TCP", new Dictionary<string, object> { ["id"] = id, [param] = value });

    private static ToggleControl TcpToggle(string id, string label, ControlSection section, string key, int cmdId, string param) =>
        Toggle(id, label, section, key, (v, _) => Tcp(cmdId, param, v));

    private static SliderControl TcpSlider(string id, string label, ControlSection section, string key, int cmdId, string param,
        int min, int max, int step, string unit) =>
        Slider(id, label, section, key, min, max, step, unit, (v, _) => Tcp(cmdId, param, v));

    private static ChoiceControl TcpChoice(string id, string label, ControlSection section, string key, int cmdId, string param,
        List<(string, int)> options) =>
        Choice(id, label, section, key, options, (v, _) => Tcp(cmdId, param, v));

    public List<IControl> GetControls(string serialNumber) => new()
    {
        TcpToggle("ac_hv", "AC вихід (230 В)", ControlSection.Outputs, "cfg_hv_ac_out_open", 66, "cfgHvAcOutOpen"),
        TcpToggle("ac_lv", "AC вихід (низьковольтний)", ControlSection.Outputs, "cfg_lv_ac_out_open", 66, "cfgLvAcOutOpen"),
        TcpToggle("xboost", "X-Boost", ControlSection.Outputs, "xboost_en", 66, "xboostEn"),
        TcpToggle("dc12", "DC 12V вихід", ControlSection.Outputs, "cfg_dc_12v_out_open", 81, "cfgDc12vOutOpen"),
        TcpToggle("dc24", "DC 24V вихід", ControlSection.Outputs, "cfg_dc_24v_out_open", 81, "cfgDc24vOutOpen"),
        TcpToggle("ac_saving", "Енергозбереження AC", ControlSection.Outputs, "ac_energy_saving_open", 95, "acEnergySavingOpen"),
        TcpSlider("ac_chg_w", "Потужність заряду від мережі", ControlSection.Charging, "plug_in_info_ac_in_chg_pow_max", 69, "plugInInfoAcInChgPowMax", 200, 3000, 100, "Вт"),
        TcpSlider("max_chg", "Макс. рівень заряду", ControlSection.Charging, "cms_max_chg_soc", 49, "cmsMaxChgSoc", 50, 100, 1, "%"),
        TcpSlider("min_dsg", "Мін. рівень розряду", ControlSection.Charging, "cms_min_dsg_soc", 51, "cmsMinDsgSoc", 0, 30, 1, "%"),
        TcpToggle("beep", "Звуковий сигнал", ControlSection.System, "en_beep", 38, "enBeep"),
        TcpChoice("screen", "Вимкнення екрана", ControlSection.System, "screen_off_time", 39, "screenOffTime", ControlOptions.ScreenTimeoutOptions),
        TcpChoice("ac_standby", "Автовимкнення AC", ControlSection.System, "ac_standby_time", 10, "acStandbyTime", ControlOptions.StandbyOptions),
        TcpChoice("dc_standby", "Автовимкнення DC", ControlSection.System, "dc_standby_time", 33, "dcStandbyTime", ControlOptions.StandbyOptions)
    };
}

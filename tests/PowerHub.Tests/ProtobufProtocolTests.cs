using System;
using System.Linq;
using Google.Protobuf;
using PowerHub.Protocol;
using PowerHub.Protocol.Proto;
using Xunit;

namespace PowerHub.Tests;

/// <summary>
/// Перенесено з Android-версії (ProtobufProtocolTest.kt)
/// </summary>
public class ProtobufProtocolTests
{
    /// <summary>
    /// Обгорнути pdata так, як це робить станція: за потреби XOR з молодшим байтом seq
    /// </summary>
    internal static byte[] Frame(int cmdFunc, int cmdId, byte[] pdata, bool xor = true, int seq = 0x1234)
    {
        var key = (byte)(seq & 0xFF);
        var wire = xor ? pdata.Select(b => (byte)(b ^ key)).ToArray() : pdata;
        var header = new Delta3Header
        {
            Src = 2,
            EncType = xor ? 1 : 0,
            Seq = seq,
            CmdFunc = cmdFunc,
            CmdId = cmdId,
            Pdata = ByteString.CopyFrom(wire)
        };
        var message = new Delta3HeaderMessage();
        message.Header.Add(header);
        return message.ToByteArray();
    }

    [Fact]
    public void Delta3DisplayUpload_IsXorDecodedAndFlattened()
    {
        var pdata = new Delta3DisplayPropertyUpload { CmsBattSoc = 87f, PowOutSumW = 120f, FlowInfoAcOut = 14 }.ToByteArray();
        var p = Delta3Protocol.Standard.Parse(TopicKind.Data, Frame(254, 21, pdata));
        Assert.Equal(87.0, p["cms_batt_soc"]);
        Assert.Equal(120.0, p["pow_out_sum_w"]);
        // Стан виходу виводиться з flow_info: 14 означає «увімкнено»
        Assert.Equal(1, p["cfg_ac_out_open"]);
        Assert.Equal(87, Delta3Protocol.Standard.GetState(p).Soc);
    }

    [Fact]
    public void Base64WrappedFrames_AreAccepted()
    {
        var pdata = new Delta3DisplayPropertyUpload { CmsBattSoc = 42f }.ToByteArray();
        var b64 = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(Frame(254, 21, pdata)));
        Assert.Equal(42.0, Delta3Protocol.Standard.Parse(TopicKind.Data, b64)["cms_batt_soc"]);
    }

    [Fact]
    public void FailedSetReply_IsIgnored()
    {
        var ok = new Delta3SetReply { ConfigOk = true, XboostEn = 1 }.ToByteArray();
        var bad = new Delta3SetReply { ConfigOk = false, XboostEn = 1 }.ToByteArray();
        Assert.Equal(1, Convert.ToInt32(Delta3Protocol.Standard.Parse(TopicKind.SetReply, Frame(254, 18, ok))["xboost_en"]));
        Assert.Empty(Delta3Protocol.Standard.Parse(TopicKind.SetReply, Frame(254, 18, bad)));
    }

    [Fact]
    public void Delta3Toggle_BuildsSetPacketForDevice()
    {
        var toggle = Delta3Protocol.Standard.GetControls("SN3").OfType<ToggleControl>().First(c => c.Id == "ac_out");
        var packet = Delta3SendHeaderMsg.Parser.ParseFrom(toggle.Command(true, new DeviceParams()).Payload);
        var h = packet.Msg[0];
        Assert.Equal(254, h.CmdFunc);
        Assert.Equal(17, h.CmdId);
        Assert.Equal(32, h.Src);
        Assert.Equal("SN3", h.DeviceSn);
        Assert.Equal(1u, Delta3SetCommand.Parser.ParseFrom(h.Pdata).CfgAcOutOpen);
    }

    [Fact]
    public void Delta3AcChargePower_CarriesCommitField()
    {
        var slider = Delta3Protocol.Standard.GetControls("SN3").OfType<SliderControl>().First(c => c.Id == "ac_chg_w");
        var h = Delta3SendHeaderMsg.Parser.ParseFrom(slider.Command(1200, new DeviceParams()).Payload).Msg[0];
        // поле 54 = 1200 (varint b0 09), далі поле 125 = 0
        Assert.Equal(new byte[] { 0xB0, 0x03, 0xB0, 0x09, 0xE8, 0x07, 0x00 }, h.Pdata.ToByteArray());
    }

    [Fact]
    public void DeltaPro3Telemetry_DecodesAndDerivesOutputs()
    {
        var pdata = new DP3DisplayPropertyUpload
        {
            CmsBattSoc = 63f, PowGetAcIn = 0f, PlugInInfoAcInFlag = 0, FlowInfo12V = 4
        }.ToByteArray();
        var p = DeltaPro3Protocol.Instance.Parse(TopicKind.Data, Frame(254, 21, pdata));
        var s = DeltaPro3Protocol.Instance.GetState(p);
        Assert.Equal(63, s.Soc);
        Assert.False(s.GridConnected);
        Assert.Equal(0, p["cfg_dc_12v_out_open"]);
    }

    [Fact]
    public void DeltaPro3_ChargingFromAcWithZeroFlag_HasGrid()
    {
        // Станція заряджається від розетки, але надсилає plug_in_info_ac_in_flag = 0
        var pdata = new DP3DisplayPropertyUpload
        {
            PowInSumW = 1550f, PowGetAcIn = 1550f, PlugInInfoAcInFlag = 0
        }.ToByteArray();
        var s = DeltaPro3Protocol.Instance.GetState(DeltaPro3Protocol.Instance.Parse(TopicKind.Data, Frame(254, 21, pdata)));
        Assert.True(s.GridConnected);
        Assert.Equal(GridStatus.Ok, s.GetGridStatus(180));
    }

    [Fact]
    public void Delta3_SelfConsumption_FromBmsPower()
    {
        // Від батареї 64 Вт, на AC-виходи 48 Вт: інвертор бере ≈ 16 Вт (потужності виходу protobuf надсилає зі знаком «−»)
        var pdata = new Delta3DisplayPropertyUpload
        {
            PowInSumW = 0f, PowOutSumW = 48f, PowGetAcOut = -48f, PowGetBms = -64f
        }.ToByteArray();
        var s = Delta3Protocol.Standard.GetState(Delta3Protocol.Standard.Parse(TopicKind.Data, Frame(254, 21, pdata)));
        Assert.Equal(64, s.BatteryW);
        Assert.Equal(16, s.SelfConsumptionW());
    }

    [Fact]
    public void VarintEncoding()
    {
        Assert.Equal(new byte[] { 0x00 }, ProtoCodec.EncodeVarint(0));
        Assert.Equal(new byte[] { 0x7F }, ProtoCodec.EncodeVarint(127));
        Assert.Equal(new byte[] { 0x80, 0x01 }, ProtoCodec.EncodeVarint(128));
    }
}

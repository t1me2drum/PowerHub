using PowerHub.Models;
using PowerHub.Protocol;
using Xunit;

namespace PowerHub.Tests;

/// <summary>
/// Перенесено з Android-версії (DeviceStateTest.kt)
/// </summary>
public class DeviceStateTests
{
    // ---- потік енергії: напрямок визначає потужність, а не те, яку оцінку надіслала станція ----

    [Fact]
    public void NoGridWithStaleTimeToFull_IsDischarging()
    {
        // Delta Pro 3 продовжує надсилати останню оцінку заряду після зникнення мережі
        var s = new DeviceState { Soc = 60, InputW = 0, OutputW = 150, ChargeRemainMin = 90 };
        Assert.Equal(new BatteryFlow.Discharging(null), s.GetBatteryFlow());
    }

    [Fact]
    public void Charging_UsesOnlyChargeEstimate()
    {
        var s = new DeviceState { Soc = 40, InputW = 800, OutputW = 100, ChargeRemainMin = 70, DischargeRemainMin = 300 };
        Assert.Equal(new BatteryFlow.Charging(70), s.GetBatteryFlow());
    }

    [Fact]
    public void Discharging_UsesOnlyDischargeEstimate()
    {
        var s = new DeviceState { Soc = 70, InputW = 0, OutputW = 200, ChargeRemainMin = 45, DischargeRemainMin = 255 };
        Assert.Equal(new BatteryFlow.Discharging(255), s.GetBatteryFlow());
    }

    [Fact]
    public void GridPassThroughWithLoad_IsIdleAndKeepsLoad()
    {
        var s = new DeviceState { Soc = 80, InputW = 16, OutputW = 16 };
        Assert.Equal(new BatteryFlow.Idle(16), s.GetBatteryFlow());
    }

    [Fact]
    public void SmallDifferenceInsideDeadband_IsIdle()
    {
        Assert.Equal(new BatteryFlow.Idle(0), new DeviceState { Soc = 80, InputW = 5, OutputW = 0 }.GetBatteryFlow());
    }

    [Fact]
    public void FullBatteryOnGrid_IsFull()
    {
        Assert.Equal(new BatteryFlow.Full(), new DeviceState { Soc = 100, InputW = 60, OutputW = 20 }.GetBatteryFlow());
        Assert.Equal(new BatteryFlow.Full(), new DeviceState { Soc = 100, InputW = 0, OutputW = 0 }.GetBatteryFlow());
    }

    [Fact]
    public void NoPowerData_IsUnknown()
    {
        Assert.Equal(new BatteryFlow.Unknown(), new DeviceState { Soc = 50 }.GetBatteryFlow());
    }

    // ---- власне споживання ----

    [Fact]
    public void SelfConsumption_OnBatteryOnly()
    {
        // AC увімкнено, нічого не під'єднано: батарея все одно живить інвертор на холостому ходу
        var idle = new DeviceState { InputW = 0, OutputW = 16, AcOutW = 0, DcOutW = 0, UsbOutW = 0, BatteryW = 18 };
        Assert.Equal(18, idle.SelfConsumptionW());
        // Без показника батареї джерелом є загальний вихід
        Assert.Equal(16, (idle with { BatteryW = null }).SelfConsumptionW());
        Assert.Equal(30, new DeviceState { InputW = 0, AcOutW = 200, UsbOutW = 10, BatteryW = 240 }.SelfConsumptionW());
    }

    [Fact]
    public void SelfConsumption_UnknownWhileAnythingFlowsIn()
    {
        Assert.Null(new DeviceState { InputW = 300, AcOutW = 0, BatteryW = 280 }.SelfConsumptionW());
        Assert.Null(new DeviceState { InputW = 0, BatteryW = 20 }.SelfConsumptionW());
        Assert.Equal(0, new DeviceState { InputW = 0, AcOutW = 100, BatteryW = 90 }.SelfConsumptionW());
    }

    [Fact]
    public void ChargeLoss_InputMinusBatteryMinusOutputs()
    {
        // Delta 3: з мережі 525 Вт, у батарею 464 Вт, на AC 30 Вт → втрати 31 Вт
        var s = new DeviceState { Soc = 91, InputW = 525, OutputW = 30, AcOutW = 30, DcOutW = 0, UsbOutW = 0, BatteryW = 464 };
        Assert.Equal(31, s.ChargeLossW());
        Assert.Null(s.SelfConsumptionW());
        Assert.Null((s with { BatteryW = null }).ChargeLossW());
        Assert.Null(new DeviceState { InputW = 0, OutputW = 50, AcOutW = 50, BatteryW = 60 }.ChargeLossW());
    }

    // ---- стан мережі ----

    [Fact]
    public void WeakGridBelowThreshold()
    {
        Assert.Equal(GridStatus.Weak, new DeviceState { GridConnected = true, AcInVolt = 140 }.GetGridStatus(180));
    }

    [Fact]
    public void LowVoltageWithAcInput_IsOk()
    {
        // Delta Max заряджалась 456 Вт за 176 В при порозі 180 В
        var s = new DeviceState { Soc = 93, GridConnected = true, AcInVolt = 176, AcInW = 456, InputW = 454, OutputW = 94 };
        Assert.Equal(GridStatus.Ok, s.GetGridStatus(180));
        Assert.True(s.IsChargingFromGrid(180));
    }

    [Fact]
    public void NormalGridAtOrAboveThreshold()
    {
        Assert.Equal(GridStatus.Ok, new DeviceState { GridConnected = true, AcInVolt = 180 }.GetGridStatus(180));
        Assert.Equal(GridStatus.Ok, new DeviceState { GridConnected = true, AcInVolt = 228 }.GetGridStatus(180));
    }

    [Fact]
    public void GridWithoutVoltage_IsOk()
    {
        Assert.Equal(GridStatus.Ok, new DeviceState { GridConnected = true }.GetGridStatus(180));
    }

    [Fact]
    public void NoGridAndUnknownGrid()
    {
        Assert.Equal(GridStatus.None, new DeviceState { GridConnected = false }.GetGridStatus(180));
        Assert.Null(new DeviceState().GetGridStatus(180));
    }

    // ---- анімація заряджання ----

    [Fact]
    public void WeakGrid_NeverCountsAsCharging()
    {
        var s = new DeviceState { Soc = 50, GridConnected = true, AcInVolt = 140, AcInW = 0, InputW = 0, OutputW = 30 };
        Assert.False(s.IsChargingFromGrid(180));
    }

    [Fact]
    public void NormalGridWithAcInput_Charges()
    {
        var s = new DeviceState { Soc = 50, GridConnected = true, AcInVolt = 228, AcInW = 600, InputW = 600, OutputW = 50 };
        Assert.True(s.IsChargingFromGrid(180));
    }

    [Fact]
    public void LoadLargerThanGridInput_IsNotCharging()
    {
        var s = new DeviceState { Soc = 50, GridConnected = true, AcInVolt = 228, AcInW = 300, InputW = 300, OutputW = 900 };
        Assert.False(s.IsChargingFromGrid(180));
    }

    // ---- допоміжне ----

    [Fact]
    public void RemainingTimeSentinels_AreDropped()
    {
        Assert.Null(DeviceParamsExtensions.ValidMinutes(5939));
        Assert.Null(DeviceParamsExtensions.ValidMinutes(0));
        Assert.Null(DeviceParamsExtensions.ValidMinutes(null));
        Assert.Equal(125, DeviceParamsExtensions.ValidMinutes(125));
    }

    [Fact]
    public void ModelDetection_BySerialPrefixAndProductName()
    {
        Assert.Equal(DeviceModel.Delta2, DeviceModelExtensions.DetectFromSerial("R331ZEB5SG8X0271"));
        Assert.Equal(DeviceModel.Delta2Max, DeviceModelExtensions.DetectFromSerial("R351ZE1APH7K0022", "DELTA 2 Max"));
        Assert.Equal(DeviceModel.Delta3, DeviceModelExtensions.DetectFromSerial("P231ZE1APJ3E2930"));
        Assert.Equal(DeviceModel.DeltaPro3, DeviceModelExtensions.DetectFromSerial("MR51ZES5PG8K0043", "DELTA Pro 3"));
        Assert.Equal(DeviceModel.DeltaMax, DeviceModelExtensions.DetectFromSerial("DA0000000001400", "DELTA Max"));
        Assert.Equal(DeviceModel.River2Max, DeviceModelExtensions.DetectFromSerial("R611111111111424", "RIVER 2 Max"));
        Assert.Equal(DeviceModel.Delta3Max, DeviceModelExtensions.DetectFromSerial("XXXX0000", "DELTA 3 Max"));
        Assert.Equal(DeviceModel.Unknown, DeviceModelExtensions.DetectFromSerial("ZZZZ0000", "PowerStream"));
    }
}

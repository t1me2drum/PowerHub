using System;

namespace PowerHub.Protocol;

/// <summary>
/// Стан мережі з урахуванням напруги
/// </summary>
public enum GridStatus
{
    None,   // Мережі немає
    Weak,   // Мережа є, але напруга нижча за поріг — станція не заряджається
    Ok      // Мережа в нормі
}

/// <summary>
/// Що насправді робить батарея: визначається за потоком енергії, а не за тим, яку оцінку часу надіслала станція
/// </summary>
public abstract record BatteryFlow
{
    /// <summary>Батарея отримує енергію. Minutes = null, якщо станція не дала правильної оцінки</summary>
    public sealed record Charging(int? Minutes) : BatteryFlow;

    public sealed record Discharging(int? Minutes) : BatteryFlow;

    public sealed record Full : BatteryFlow;

    /// <summary>
    /// Батарея ані заряджається, ані розряджається. LoadW — що ще споживають виходи:
    /// від мережі навантаження живиться напряму (вхід ≈ вихід), тож у спокої воно може бути
    /// </summary>
    public sealed record Idle(int LoadW) : BatteryFlow;

    public sealed record Unknown : BatteryFlow;
}

/// <summary>
/// Похідні стани (як Model.kt в Android-версії)
/// </summary>
public static class DeviceStateLogic
{
    public const int DefaultWeakGridVolt = 180;

    /// <summary>
    /// Різниця потужностей, меншу за цю, вважаємо спокоєм: власне споживання й шум датчиків
    /// </summary>
    private const int FlowDeadbandW = 10;

    /// <summary>
    /// Вхід AC, більший за цей, означає, що станція справді бере енергію з мережі
    /// </summary>
    private const int AcInFlowW = 20;

    /// <summary>
    /// Мережа є, але нижче weakBelowVolt: за такої напруги станція зазвичай не приймає AC,
    /// тож показувати її як таку, що заряджається, не можна. Якщо ж енергія з AC надходить
    /// (деякі станції заряджаються й за 170–180 В), мережу вважаємо нормальною
    /// </summary>
    public static GridStatus? GetGridStatus(this DeviceState s, int weakBelowVolt) => s.GridConnected switch
    {
        null => null,
        false => GridStatus.None,
        true => s.AcInVolt is int v && v >= 1 && v < weakBelowVolt && (s.AcInW ?? 0) <= AcInFlowW
            ? GridStatus.Weak
            : GridStatus.Ok
    };

    /// <summary>
    /// Заряджається від мережі, лише коли AC надходить за нормальної напруги і батарея не втрачає енергію
    /// (навантаження, більше за вхід від мережі, розряджає її попри мережу)
    /// </summary>
    public static bool IsChargingFromGrid(this DeviceState s, int weakBelowVolt) =>
        s.GetGridStatus(weakBelowVolt) == GridStatus.Ok &&
        (s.AcInW ?? 0) > 5 &&
        s.GetBatteryFlow() is not BatteryFlow.Discharging;

    /// <summary>
    /// Що станція витрачає сама на себе (інвертор на холостому ходу, електроніка): потужність з батареї
    /// мінус те, що віддають виходи. Лише поки станція працює тільки від батареї: з будь-яким входом
    /// домішуються втрати зарядного пристрою і різниця нічого не означає. null, якщо визначити не можна
    /// </summary>
    public static int? SelfConsumptionW(this DeviceState s)
    {
        if (s.InputW is not int input || input > FlowDeadbandW)
            return null;
        if (s.AcOutW == null && s.DcOutW == null && s.UsbOutW == null)
            return null;
        if ((s.BatteryW ?? s.OutputW) is not int source)
            return null;
        return Math.Max(0, source - ((s.AcOutW ?? 0) + (s.DcOutW ?? 0) + (s.UsbOutW ?? 0)));
    }

    /// <summary>
    /// Станції продовжують надсилати останню оцінку заряду/розряду навіть після зміни напрямку
    /// (напр. Delta Pro 3 показує час до повного після зникнення мережі), тому напрямок визначаємо
    /// за входом і виходом і беремо лише відповідну оцінку
    /// </summary>
    public static BatteryFlow GetBatteryFlow(this DeviceState s)
    {
        if (s.InputW == null && s.OutputW == null)
            return new BatteryFlow.Unknown();

        var net = (s.InputW ?? 0) - (s.OutputW ?? 0);
        if (net > FlowDeadbandW)
            return (s.Soc ?? 0) >= 100 ? new BatteryFlow.Full() : new BatteryFlow.Charging(s.ChargeRemainMin);
        if (net < -FlowDeadbandW)
            return new BatteryFlow.Discharging(s.DischargeRemainMin);
        if ((s.Soc ?? 0) >= 100)
            return new BatteryFlow.Full();
        return new BatteryFlow.Idle(s.OutputW ?? 0);
    }
}

namespace ItamiBen.Core;

/// <summary>
/// 整分对齐。纯函数，不碰时钟——每一个时刻都是参数传进来的。
/// </summary>
public static class TimeGrid
{
    /// <summary>
    /// 抹掉秒及更细的部分，落到当前所在的那个整分上。
    ///
    /// **一轮的 <c>StartedAt</c> 用它**：23:13:27 点 Start ⇒ 从 23:13:00 起算。
    /// 这样 120 个格子跟钟面上的 120 个分钟刻度**逐格对齐**，DESIGN §4.3 那句
    /// 「转完两圈就是到点，显示即规则」才成立——否则格子 0 只有 33 秒，
    /// 分针的角度跟格子索引从第一秒起就对不上。
    ///
    /// ⚠️ **知情代价**：点 Start 之前的那最多 59 秒没人看着，属于「没采到」，
    /// 按 DESIGN §4.1 既不计入专注也不算跑偏，但**它照样占环上的格子**，也就是照样
    /// 吃余量（<see cref="Round.SlackSeconds"/>）。专注 50 分钟时余量是 3600 秒，
    /// 这 59 秒占 1.6%。换来的是钟面和账本是同一个东西。
    /// </summary>
    public static DateTimeOffset FloorToMinute(DateTimeOffset t)
        => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);
}

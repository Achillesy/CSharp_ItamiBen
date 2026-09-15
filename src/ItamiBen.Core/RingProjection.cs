namespace ItamiBen.Core;

/// <summary>
/// 钟面上那三段连续色块的位置，单位一律是**从 Start 起的秒**（DESIGN §5）。
///
/// <code>
/// 分针 ──[灰色 承诺弧]──[淡蓝 休息块]────空白────▶ 环终点
///        ↑还要专注多久     ↑休息多久       ↑还剩多少余量
/// </code>
///
/// ⚠️ **算在 Core 里而不是渲染层**，因为这三段是同一条规则的三种读法
/// （DECISIONS D1：改成「只在休息阶段才画那块蓝」，②③ **当场塌掉而且不报错**）。
/// 让渲染层自己去推，这条规则就会存在两份。
///
/// 恒等式（<see cref="Round.RingSeconds"/> − <see cref="BreakEndSeconds"/> ==
/// <see cref="SlackSeconds"/>）不是巧合，而是「淡蓝尾撞到环终点 = 结束」
/// 和「余量耗尽 = 结束」本来就是同一句话。有测试钉着。
/// </summary>
/// <param name="HandSeconds">分针位置。</param>
/// <param name="CommitEndSeconds">灰色承诺弧的终点 = 按 100% 效率算的**预计达成点**。</param>
/// <param name="BreakStartSeconds">淡蓝块起点。达成之前它跟着预计达成点走，达成之后钉死。</param>
/// <param name="BreakEndSeconds">淡蓝块终点。</param>
/// <param name="SlackSeconds">淡蓝块尾到环终点 = 还能浪费多少秒。</param>
public readonly record struct RingProjection(
    int HandSeconds,
    int CommitEndSeconds,
    int BreakStartSeconds,
    int BreakEndSeconds,
    int SlackSeconds)
{
    /// <summary>灰色承诺弧的长度 = 还要专注多久。</summary>
    public int CommitSeconds => CommitEndSeconds - HandSeconds;

    /// <summary>淡蓝块的长度 = 休息多久。全程不变。</summary>
    public int BreakSeconds => BreakEndSeconds - BreakStartSeconds;

    /// <summary>
    /// 分针已经走进淡蓝块了吗——DECISIONS D2 要的那个区分：
    /// **预告**（淡一档 / 描边）还是**已发生**（填实）。
    ///
    /// 不分开的话，分针走进休息区时画面毫无变化，用户无法确认休息到底开始没有。
    /// </summary>
    public bool BreakStarted => HandSeconds >= BreakStartSeconds;
}

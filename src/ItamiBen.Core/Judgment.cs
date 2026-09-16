namespace ItamiBen.Core;

/// <summary>
/// 一秒的判定结果。**当场定死，永不改写**（DESIGN §4.2）。
///
/// 四个取值：<c>OffTask</c> / <c>Focused</c> 进格子的 <c>sampledSeconds</c>，
/// <c>Unread</c> / <c>Away</c> **整秒丢掉**——既不计入也不算跑偏。
///
/// ⚠️ 后两个分开只是为了日志里能看出「这一秒为什么没算」和格子上画法不同
/// （<c>Away</c> 画空心虚线框，<c>Unread</c> 什么都不画）；**判定层面它们等价**。
///
/// ⚠️ **这里没有「前台是 ItamiBen 自己」这一档**（2026-09-16 用户拍板删的，DECISIONS C9）。
/// 盯着自己的钟面就是跑偏，跟盯着别的什么一样——想让它算专注，去 rules.json 里
/// 自己写一条规则，这是配置不是特例。
/// </summary>
public enum SecondOutcome : byte
{
    /// <summary>
    /// 连 app 名都没读到 ⇒ 这一秒等于没采。
    ///
    /// 这不是「宽容一下」的政策分支（DECISIONS C2 反对的是那个），而是 DESIGN §4.1
    /// 自己举的例子：**锁屏、睡着、程序没跑**都长这样。Windows 锁屏时
    /// `GetForegroundWindow()` 返回 0，macOS 登录窗口时没有 frontmostApplication——
    /// 把这些算成「跑偏」是冤枉人，算成「专注」是送人情，所以一秒都不算。
    /// </summary>
    Unread,

    /// <summary>
    /// 人不在（键鼠空闲超过 <see cref="AwayMap.ThresholdSeconds"/> 秒）⇒ 这一秒等于没采。
    ///
    /// ⚠️ **它压过屏幕上是什么**，而且必须如此：macOS 锁屏时前台读到的是
    /// `loginwindow` / `Login`（2026-09-16 实测，不是空字符串），一行一判的话
    /// **锁屏一小时就是一小时红格**——v3 3.9.x 那个大 bug 正是这个形状。
    ///
    /// ⚠️ 判断它需要跨行回溯（门槛是事后才跨过的），所以由 <see cref="AwayMap"/>
    /// 算好了传进来，**不要在这里拿单行的 idle 去比门槛**。
    /// </summary>
    Away,

    /// <summary>采到了，但不匹配任何一个选中的目标。红格。</summary>
    OffTask,

    /// <summary>采到了，且命中某个选中的目标。绿格。</summary>
    Focused,
}

/// <summary>一秒的判定 + 这一秒算在哪个目标头上（只有 <see cref="SecondOutcome.Focused"/> 才有目标）。</summary>
public readonly record struct SecondJudgment(SecondOutcome Outcome, string? Goal)
{
    /// <summary>这一秒进不进格子的 <c>sampledSeconds</c>。</summary>
    public bool Sampled => Outcome is SecondOutcome.OffTask or SecondOutcome.Focused;

    public bool Focused => Outcome is SecondOutcome.Focused;
}

/// <summary>
/// 把一次前台窗口采样判成一秒。**纯函数**：同样的输入永远同样的输出，不碰时间。
/// </summary>
public static class Judgment
{
    /// <summary>
    /// 判一秒。
    ///
    /// 顺序只有一条讲究：**人不在压过一切**（见 <see cref="SecondOutcome.Away"/>）。
    /// 其余就是「读到了没」→「命中规则没」，没有第三条政策分支。
    /// </summary>
    /// <param name="app">前台 app 名。空 = 平台层什么都没读到。</param>
    /// <param name="title">前台窗口标题。读不到就传空字符串（DECISIONS C2）。</param>
    /// <param name="away">人在不在。由 <see cref="AwayMap"/> 跨行算好传进来。</param>
    /// <param name="goals">本轮勾选的目标，**按勾选顺序**。</param>
    /// <param name="rules">本轮开始时锁定的规则。</param>
    public static SecondJudgment Judge(string app, string title, bool away,
                                       IReadOnlyList<string> goals, GoalRules rules)
    {
        // 人不在就压过一切——屏幕上是什么都无所谓了（见 SecondOutcome.Away）
        if (away) return new SecondJudgment(SecondOutcome.Away, null);
        if (string.IsNullOrEmpty(app)) return new SecondJudgment(SecondOutcome.Unread, null);

        // 多选时算在**第一个命中的目标**头上——顺序是用户给的，所以结果是确定的。
        // 一秒只能算给一个目标，否则同一秒会被两个目标各记一遍，总数虚高。
        foreach (var goal in goals)
            if (rules.Matches(goal, app, title))
                return new SecondJudgment(SecondOutcome.Focused, goal);

        return new SecondJudgment(SecondOutcome.OffTask, null);
    }
}

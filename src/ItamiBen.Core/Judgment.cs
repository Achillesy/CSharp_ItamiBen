using System.Text.RegularExpressions;

namespace ItamiBen.Core;

/// <summary>
/// 一秒的判定结果。**当场定死，永不改写**（DESIGN §4.2）。
///
/// 五个取值，但格子上只有两个计数（DECISIONS C7）：
/// <c>OffTask</c> / <c>Focused</c> 进 <c>sampledSeconds</c>，
/// <c>Unread</c> / <c>SelfExempt</c> / <c>Away</c> **整秒丢掉**——既不计入也不算跑偏。
///
/// ⚠️ 分成五个而不是三个，只是为了日志里能看出「这一秒为什么没算」；
/// **判定层面它们完全等价**，别在 <see cref="Round"/> 里给两者分叉。
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
    /// 前台就是 ItamiBen 自己 ⇒ 这一秒等于没采（DECISIONS A4 的「自身豁免」）。
    ///
    /// ⚠️ **它压过用户写的规则**：哪怕 rules.json 里有一条专门匹配 ItamiBen 的规则，
    /// 盯着钟面也换不来一秒专注。少了这条会掉进「看一眼还剩多久 → 判跑偏 → 更想看」
    /// 的死循环；反过来如果算成专注，盯着钟就能刷满番茄钟，卖点当场归零。
    /// **两头都堵死，只有「这一秒不存在」是对的。**
    /// </summary>
    SelfExempt,

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
    /// 认出「前台是我自己」。
    ///
    /// ⚠️ 这个正则和 `AssemblyName=ItamiBen`（csproj）/ `Name="ItamiBen"`（App.axaml）
    /// 是**同一件事的三个写法**，DECISIONS A4 把它们钉死了：macOS 读到的是
    /// `NSRunningApplication.localizedName`（`ItamiBen`），Windows 读到的是
    /// 进程名加后缀（`ItamiBen.exe`）。改了任何一处**都不报错**，只会让程序开始
    /// 判自己跑偏。
    /// </summary>
    private static readonly Regex Self = new(
        @"^ItamiBen(\.exe)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 判一秒。
    ///
    /// 顺序是有讲究的：**人不在压过一切，自身豁免排在规则匹配前面**
    /// （见 <see cref="SecondOutcome.Away"/> / <see cref="SecondOutcome.SelfExempt"/>）。
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
        if (Self.IsMatch(app)) return new SecondJudgment(SecondOutcome.SelfExempt, null);

        // 多选时算在**第一个命中的目标**头上——顺序是用户给的，所以结果是确定的。
        // 一秒只能算给一个目标，否则同一秒会被两个目标各记一遍，总数虚高。
        foreach (var goal in goals)
            if (rules.Matches(goal, app, title))
                return new SecondJudgment(SecondOutcome.Focused, goal);

        return new SecondJudgment(SecondOutcome.OffTask, null);
    }
}

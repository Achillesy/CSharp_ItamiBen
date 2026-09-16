namespace ItamiBen.Core;

/// <summary>这一分钟的红，主要是哪扇窗口造成的。</summary>
/// <param name="App">前台 app 名。</param>
/// <param name="Title">窗口标题。读不到时是空字符串。</param>
/// <param name="Seconds">它在这一分钟里贡献了多少秒跑偏。</param>
public readonly record struct OffTaskCulprit(string App, string Title, int Seconds);

/// <summary>
/// 归因：**这一分钟的红格是哪扇窗口造成的**，写进日志。
///
/// ⚠️ **只做诊断，永不参与判定。** 它的结果一个字节都不回流到环上、账本上。
/// 存在的理由很实际：环上只告诉你「这一分钟红了」，而两小时后你根本想不起来当时开着什么。
/// 有了这一行，日志能回答「这一分钟为什么没算」，不用自己去翻 `samples.db`。
///
/// 它跟真正的判定**调的是同一个 <see cref="Judgment.Judge"/>**，参数也是同一套
/// （同一批行、同一张 <see cref="AwayMap"/>、同一份规则、同一组目标）。
/// 所以「为什么」永远跟「算没算」一致——**别为了省事在这里另写一套匹配**，
/// 那样两边迟早分家，而分家之后日志会**理直气壮地给出错误的解释**，比没有更糟。
///
/// ⚠️ v3 那边的教训值得单记：它这行日志比归因本身还早就有了，但因为索引取了
/// <c>cells[^1]</c>（指向的是承诺弧的灰色投影尾，不是刚过去那一分钟），
/// 读到的 `OffTaskSeconds` 永远是 0——**这行日志在生产里一次都没打出来过**，
/// 而整个历史日志里零匹配这件事，看起来跟「一直没有跑偏」一模一样。
/// **一条从不触发的诊断，长得和「没什么可报的」完全一样。**
/// </summary>
public static class OffTaskAttribution
{
    /// <summary>
    /// 在 <paramref name="minuteStart"/> 起的那一分钟里，找出贡献跑偏秒数最多的那扇窗口。
    /// 这一分钟一秒都没跑偏就返回 null。
    ///
    /// ⚠️ **平局要有确定的解**：秒数相同时取**先出现**的那扇窗口。
    /// 靠字典的枚举顺序是不行的——那不是合约，改个实现就悄悄换了答案，
    /// 而这种「同样的输入两次跑出不同日志」的东西，事后根本没法查。
    /// </summary>
    /// <param name="rows">这一轮读回来的观测行（可以比这一分钟多，多余的会被跳过）。</param>
    /// <param name="away">跨行算好的离开区间——**必须跟判定用的是同一张**。</param>
    /// <param name="goals">本轮选中的目标。</param>
    /// <param name="rules">本轮锁定的规则。</param>
    /// <param name="minuteStart">要归因的那一分钟的起点。</param>
    public static OffTaskCulprit? Biggest(IReadOnlyList<Observation> rows, AwayMap away,
                                          IReadOnlyList<string> goals, GoalRules rules,
                                          DateTimeOffset minuteStart)
    {
        var from = TimeGrid.FloorToMinute(minuteStart);
        var to = from.AddMinutes(1);

        var seconds = new Dictionary<(string App, string Title), int>();
        var firstSeen = new Dictionary<(string App, string Title), int>();

        for (var i = 0; i < rows.Count; i++)
        {
            var o = rows[i];
            if (o.At < from || o.At >= to) continue;
            if (Judgment.Judge(o.App, o.Title, away.Covers(o.At), goals, rules).Outcome != SecondOutcome.OffTask)
                continue;

            var key = (o.App, o.Title);
            seconds[key] = seconds.GetValueOrDefault(key) + 1;
            if (!firstSeen.ContainsKey(key)) firstSeen[key] = i;
        }

        if (seconds.Count == 0) return null;

        var best = seconds
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => firstSeen[kv.Key])
            .First();

        return new OffTaskCulprit(best.Key.App, best.Key.Title, best.Value);
    }
}

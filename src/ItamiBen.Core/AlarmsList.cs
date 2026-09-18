namespace ItamiBen.Core;

/// <summary>
/// 一次到点：整分钟的时刻 + 原样进提示条和系统通知的文字，外加**命中的那条表达式原文**。
///
/// <paramref name="Expression"/> 只进日志（<c>Alarms list fired 23:55 [55 23 1 * *] 月度对账</c>）
/// ——那行历史是唯一的反馈渠道，多条规则时不带表达式就只知道"响了"、不知道是哪一行响的。
/// 默认空串：表盘红圈那条路（<see cref="AlarmsList.DotPosition"/>）不关心它。
/// </summary>
/// <param name="Run">要跑的命令名。null = 这条只提醒，不跑任何东西。</param>
public readonly record struct AlarmEntry(DateTime At, string Text, string Expression = "", string? Run = null);

/// <summary>清单里的一行：一条 crontab 时间表达式 + 提醒文字 + 可选的命令名。</summary>
/// <remarks>
/// ⚠️ <see cref="Text"/> **永远只是文字，永远不会被执行**（v3 的 J17）。会跑的东西
/// 只能来自 <see cref="Run"/>，而 <see cref="Run"/> 里存的是**名字不是正文**——
/// 正文只住在 `commands.json` 里。这两条合起来，一份看着人畜无害的提醒文件
/// **仍然关不了机器**：它最多能点名一条用户自己写进命令表的命令。
///
/// ⚠️ J17 原话是「将来若要向 Linux 看齐做成可执行，**必须单独设计、单独确认，
/// 不许顺手统一**」。2026-09-18 走完了那个流程（用户逐条拍板），落点是：
/// 命令写在行尾、以 `!` 开头、**提醒文字必填**。最后那条是这次设计的核心——
/// 它让「机器自己做了事却没说为什么」在语法上就写不出来。
/// </remarks>
/// <param name="Run">要跑的命令名（`commands.json` 里的键）。null = 只提醒。</param>
public sealed record CronEntry(Cron Schedule, string Text, string? Run = null);

/// <summary>
/// Alarms 清单（DESIGN §10）。**从 v3 原样搬过来**，跟 AW 无关：解析 <c>schedule.md</c>、挑出该响哪一条。**纯函数，
/// <c>now</c> 永远是参数**，跟 <see cref="AlarmClock"/> 一样的路数，不用等真实时间就能测。
///
/// 3.7.0 起数据源从 Markdown 清单（一堆展开好的绝对时间戳）换成一份**标准 crontab**
/// ——用了一段时间之后发现重复的事情占绝大多数，而"上游会把每天 14:00 铺成一串具体
/// 日期"那个上游从来没存在过（推翻 v3 的 J2/J3/J4，理由记在 `../ItamiTimer/DECISIONS.md`）。
///
/// **视野统一成 12 小时**：<see cref="Next"/> / <see cref="NextDue"/> 只在
/// <c>(now, now+12h]</c> 里往前找，跟表盘红圈的门槛（<see cref="DotPosition"/>）是同一个
/// 数。好处是"永不成立的表达式（比如 2 月 30 日）要能停下来"这类顾虑天然消失——720 次
/// 谓词测试封顶，不需要另设搜索上限。代价（知情）：钟面之外没有任何地方能看到更远的
/// 安排，想看去翻文件。
///
/// 程序**只读**这份文件，从不回写、不清理、不生成——"勾选跳过"那套随 Markdown 一起
/// 没了，"这条暂时不响"现在就是 crontab 自己的注释语法：行首加 <c>#</c>。
/// </summary>
public static class AlarmsList
{
    /// <summary>视野 = 12 小时，见类注释。跟 <see cref="DotPosition"/> 的门槛是同一个数。</summary>
    public const int HorizonMinutes = 12 * 60;

    /// <summary>
    /// 一份 <c>schedule.cron</c> 读出来的结果：认得的条目，以及**读不懂的行和原因**。
    /// </summary>
    /// <param name="Skipped">
    /// 一行一条，形如 <c>line 7: a command needs reminder text before it</c>。
    /// ⚠️ 调用方负责把它写进 `error.log`（DECISIONS I29）——Core 不碰文件。
    /// </param>
    public sealed record ScheduleFile(
        IReadOnlyList<CronEntry> Entries, IReadOnlyList<string> Skipped);

    /// <summary>
    /// 解析整份文件。**每一行独立**，一行读不懂不拖累其余。
    ///
    /// 行的形状：五个空白分隔的字段（或一个 <c>@</c> 别名），然后是提醒文字，
    /// **行尾可以再跟一个 <c>!命令名</c>**：
    ///
    /// <code>
    /// 0 9  * * 1   海贼王
    /// 0 23 * * *   该睡了 !sleep
    /// </code>
    ///
    /// ⚠️ **提醒文字必填**（2026-09-18 用户定）：只有命令没有文字的行**不合法**，
    /// 跳过并记一条原因。这条是结构性的——它让「机器自己做了事却没说为什么」
    /// 写不出来，比在文档里叮嘱一句硬得多。
    ///
    /// ⚠️ **命令放行尾不放行首**：标准 crontab 的第 6 字段就是命令、就在行尾，
    /// 放行首反而破坏 cron 的阅读习惯。代价是一条可陈述的约束——
    /// **提醒文字不能以 `!` 开头的词结尾**（`快去做作业!` 不受影响，那个 `!` 不在词首）。
    ///
    /// ⚠️ **不校验命令名的形状**。`!Sleep` 照样当成命令引用交出去，让它在到点那一刻
    /// 因为「没有这个名字」而失败——那条路会记日志，而且**提醒照常弹**。
    /// 在这里拦下来的话，一个大小写错误会把整行连同提醒一起吞掉。
    ///
    /// 空行和 <c>#</c> 注释**不算读不懂**，安静跳过不记账：注释掉正是 crontab 里
    /// 「这条先别响」的惯用法。
    /// </summary>
    public static ScheduleFile Read(string text)
    {
        var entries = new List<CronEntry>();
        var skipped = new List<string>();
        var no = 0;

        foreach (var raw in text.Split('\n'))
        {
            no++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;   // 注释和空行不算错

            var (schedule, label) = line[0] == '@' ? ParseAlias(line) : ParseFields(line);
            if (schedule is null)
            {
                skipped.Add($"line {no}: not a valid crontab line");
                continue;
            }

            var (reminder, run) = SplitLabel(label);
            if (reminder.Length == 0)
            {
                skipped.Add(run is null
                    ? $"line {no}: no reminder text"
                    : $"line {no}: a command needs reminder text before it");
                continue;
            }

            entries.Add(new CronEntry(schedule, reminder, run));
        }
        return new ScheduleFile(entries, skipped);
    }

    /// <summary>
    /// 只要条目的那个便捷入口。**实现只有 <see cref="Read"/> 一处**，这里不重复逻辑
    /// ——「一个文件两条读取路径」是这个项目栽过两次的形状（v3 的 §15.4）。
    /// </summary>
    public static IReadOnlyList<CronEntry> Parse(string text) => Read(text).Entries;

    /// <summary>
    /// 把第 6 字段切成「提醒文字」和「命令名」：**最后一个词以 <c>!</c> 开头就是命令**。
    /// </summary>
    private static (string Text, string? Run) SplitLabel(string label)
    {
        var cut = label.LastIndexOfAny([' ', '\t']);
        var last = cut < 0 ? label : label[(cut + 1)..];
        if (last.Length < 2 || last[0] != '!') return (label, null);

        // `!` 之前的全是文字；只有命令没有文字时这里是空串，调用方据此判不合法
        return (cut < 0 ? "" : label[..cut].TrimEnd(), last[1..]);
    }

    /// <summary>
    /// 只解析**表达式本身**（库里 `schedule.cron` 那一列），不带提醒文字。
    /// 认不出就是 null——调用方跳过这一条。
    ///
    /// ⚠️ 走的是跟 <see cref="Parse"/> **同一个** <see cref="Cron"/> 解析器：
    /// 库里和老文件里的 cron 语义必须逐字一致，否则迁移过来的条目会安静地改变行为。
    /// </summary>
    public static Cron? ParseExpression(string expression)
    {
        var line = expression.Trim();
        if (line.Length == 0) return null;
        if (line[0] == '@') return ParseAlias(line + " x").Item1;

        var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return f.Length < 5 ? null : Cron.TryParse(f[0], f[1], f[2], f[3], f[4]);
    }

    /// <summary><c>@daily 每日回顾</c>：第一个词是别名，剩下的是文字。</summary>
    private static (Cron?, string) ParseAlias(string line)
    {
        var cut = line.IndexOfAny([' ', '\t']);
        if (cut < 0) return (null, "");   // 只有别名没有文字
        return (Cron.TryParseAlias(line[..cut]), line[(cut + 1)..].Trim());
    }

    /// <summary><c>0 14 * * * 吃药</c>：前五个词是字段，剩下的到行尾是文字。</summary>
    private static (Cron?, string) ParseFields(string line)
    {
        var fields = new string[5];
        var at = 0;
        for (var i = 0; i < 5; i++)
        {
            while (at < line.Length && (line[at] == ' ' || line[at] == '\t')) at++;
            var start = at;
            while (at < line.Length && line[at] != ' ' && line[at] != '\t') at++;
            if (at == start) return (null, "");   // 不够 5 个字段
            fields[i] = line[start..at];
        }
        return (Cron.TryParse(fields[0], fields[1], fields[2], fields[3], fields[4]),
                line[at..].Trim());
    }

    /// <summary>
    /// <c>(after, now]</c> 区间内到点的条目，按时间排序、同一分钟内按文件顺序。
    /// **调用方自己推进 <paramref name="after"/> 这个水位线**（本类不持有任何状态）
    /// ——纯内存、一次性，程序一关就没了也无所谓：已经定了"不补响、只看未来"，重启后
    /// 本来就该从"此刻往后"重新数（v3 的 J7）。
    ///
    /// 回溯封顶 <see cref="HorizonMinutes"/> 分钟：正常情况这个窗口就是 1 分钟，但休眠
    /// 唤醒或者 <c>_minuteBusy</c> 跳拍之后可能落后很多，而 crontab 规则是**无限**的
    /// （<c>*/5</c> 一天就是 288 次），不封顶等于让一次唤醒吐出成百上千条。
    /// </summary>
    public static IReadOnlyList<AlarmEntry> Due(
        IReadOnlyList<CronEntry> entries, DateTime after, DateTime now)
    {
        var due = new List<AlarmEntry>();
        if (entries.Count == 0) return due;

        var last = Truncate(now);
        var first = Truncate(after).AddMinutes(1);   // 水位线那一分钟本身不再算（严格大于）
        var floor = last.AddMinutes(-(HorizonMinutes - 1));
        if (first < floor) first = floor;

        for (var m = first; m <= last; m = m.AddMinutes(1))
            foreach (var entry in entries)
                if (entry.Schedule.Matches(m))
                {
                    // ⚠️ **带命令的条目错过了就不补**：合盖两小时再打开，一条 22:00 的关机
                    //    会当场执行。只提醒的条目照旧补放——那正是提醒该有的行为，
                    //    而「补跑一条命令」几乎永远不是。
                    if (entry.Run is not null && m != last) continue;
                    due.Add(new AlarmEntry(m, entry.Text, entry.Schedule.Expression, entry.Run));
                }

        return due;
    }

    /// <summary>
    /// 下一个会响的分钟上的**全部**条目（按文件顺序），12 小时以内没有就是空。
    ///
    /// 返回一整组而不是一条，是因为同一分钟多条这件事在 crontab 下变得寻常，而
    /// 表盘红圈要据此画成双圈（≥2 条）、点红圈要一次列全（DESIGN §10）。
    /// **命中即停**——有日常规则时通常几十次谓词就出结果，只有 12 小时内一件事都没有
    /// 时才会走满 720 分钟。
    /// </summary>
    public static IReadOnlyList<AlarmEntry> NextDue(IReadOnlyList<CronEntry> entries, DateTime now)
    {
        if (entries.Count == 0) return [];

        var m = Truncate(now).AddMinutes(1);
        for (var i = 0; i < HorizonMinutes; i++, m = m.AddMinutes(1))
        {
            List<AlarmEntry>? hit = null;
            foreach (var entry in entries)
                if (entry.Schedule.Matches(m))
                    (hit ??= []).Add(new AlarmEntry(m, entry.Text, entry.Schedule.Expression));

            if (hit is not null) return hit;
        }
        return [];
    }

    /// <summary>最早的一条未来条目；12 小时内什么都没有时返回 null。</summary>
    public static AlarmEntry? Next(IReadOnlyList<CronEntry> entries, DateTime now)
    {
        var next = NextDue(entries, now);
        return next.Count > 0 ? next[0] : null;
    }

    /// <summary>
    /// 表盘小红圈的角度位置（0-719 分钟，跟 <see cref="AlarmClock.Position"/> 同一个换算：
    /// 时间点对 12 小时取余）。**只在下一条落在未来 12 小时以内时才返回非 null**——
    /// 黄针的 mod-12 换算从不骗人，前提是被取余的数从没超过 12 小时（<see cref="AlarmClock.NextRing"/>
    /// 保证这一点）；Alarms 清单的"下一条"没有这个保证，直接取余会把"5 天后 14 点"画成
    /// "2 点钟方向"、看着像"再等 6 小时"，这个误导正是要避开的（v3 的 J8）。
    ///
    /// 顺带一个不明显但很有用的性质：**在这个 12 小时窗口内，mod-12 是双射**（窗口内
    /// 任意两个时刻相差必然小于 12 小时，不可能取余到同一个角度）。所以表盘上两条重叠
    /// 当且仅当它们**真的在同一分钟**——而那一种由双圈来表示，不会被误读成"两件事撞在
    /// 一个角度上"。
    /// </summary>
    public static double? DotPosition(AlarmEntry? next, DateTime now)
    {
        if (next is not { } n) return null;
        if (n.At - now >= TimeSpan.FromHours(12)) return null;
        return (n.At.Hour % 12) * 60 + n.At.Minute;
    }

    /// <summary>砍掉秒和更细的部分：crontab 的粒度就是分钟。</summary>
    private static DateTime Truncate(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
}

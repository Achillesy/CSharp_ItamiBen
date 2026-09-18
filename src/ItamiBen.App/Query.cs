using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// **出了问题直接查数据库**（2026-09-16 用户定的方向，DECISIONS I11）。
///
/// 这个程序原来往文本日志里写很多东西，理由是 v3 的判定数据在 ActivityWatch 那边、
/// 本程序基本只读，日志**就是**唯一的现场记录。v4 自己就是记录者，
/// **同一条理由反过来成立了**：`samples.db` 随时都在、随时能查，
/// 再单开一份文本就是第二个真相来源，而两份真相迟早对不上。
///
/// 所以「为什么这一分钟红了」不再是一行日志，而是**一次查询**——
/// 需要的时候现算，不需要的时候一个字节都不占。
///
/// ⚠️ **只有库是二进制的东西才配有这么一条出口**（2026-09-16 用户定，DECISIONS I24）：
/// `itamiben.log` 是纯文本，用户直接把文件给 AI 就行——为它做一条 `--query log`
/// 等于**把 `cat` 包装了一遍，还得教人怎么用**。
/// 这几条剩下来是因为它们读的是二进制库，而 `minutes` 更是**非它不可**：
/// 它要把判定引擎重放一遍，任何 SQL 都算不出来。
///
/// <code>
/// ItamiBen --query apps                  见过的每一个程序名（写 App 规则用）
/// ItamiBen --query samples  [起] [止]    一秒一行的原始观测
/// ItamiBen --query minutes  [起] [止]    每一轮逐分钟的构成，红的还给出是哪扇窗口
/// ItamiBen --query rounds   [起] [止]    开过哪些轮、怎么结束的
/// </code>
///
/// 不给区间就是**今天**。程序开着也能查：SQLite 走 WAL，读不挡写。
///
/// ⚠️ **这是调试出口，不是产品功能**（DECISIONS A3 禁的是 v3 那种独立 CLI 工具）：
/// 跑完就退，正常启动路径一点都不经过它。
/// </summary>
internal static class Query
{
    public static void Run(string what, string? from, string? to)
    {
        var path = AppData.DbPath();
        if (!File.Exists(path)) { Console.Error.WriteLine($"no database at {path}"); return; }

        var start = Parse(from) ?? new DateTimeOffset(DateTime.Today, DateTimeOffset.Now.Offset);
        var end = Parse(to) ?? start.AddDays(1);

        using var db = SampleStore.Open(path);
        Console.WriteLine($"# {path}");

        // ⚠️ `apps` **不受区间约束**（见 SampleStore.AppNames），所以表头不印区间——
        //    印了会让人以为「换个日期能查出别的」，而那是假的。
        //    ⚠️ **没有 `--query titles`**：标题不是系统报的标识符，是用户自己挑的语义片段
        //    （`经济学`），不需要查；真要看实际标题，`minutes` 早就在红格后面印了。
        //    而单开一个入口等于给「屏幕上出现过的一切」做一键导出——
        //    这个程序里最敏感的数据，不该有专用出口。
        if (what != "apps")
            Console.WriteLine($"# {start:yyyy-MM-dd HH:mm} → {end:yyyy-MM-dd HH:mm}");

        switch (what)
        {
            case "apps": Names(db.AppNames(), "application"); break;
            case "samples": Samples(db, start, end); break;
            case "rounds": Rounds(db, start, end); break;
            case "minutes": Minutes(db, start, end); break;
            default:
                Console.Error.WriteLine($"unknown query '{what}' — try: apps | samples | rounds | minutes");
                break;
        }

        static DateTimeOffset? Parse(string? s) => DateTimeOffset.TryParse(s, out var t) ? t : null;
    }


    /// <summary>
    /// 见过的名字，多的在前。**给写规则用**：`App` / `Title` 正则照着这里抄，不用猜。
    ///
    /// ⚠️ 只有**专注阶段**才采样（DECISIONS F4）。没在任何一轮里用过的程序不会出现，
    /// 所以表头要说清楚——否则「我明明一直开着 Chrome，这里怎么没有」会被当成 bug。
    /// </summary>
    private static void Names(List<SampleStore.SeenName> rows, string what)
    {
        Console.WriteLine($"# {rows.Count} {what}(s) seen while a round was running");
        Console.WriteLine("# seconds  last seen          name");
        foreach (var n in rows)
            Console.WriteLine($"{n.Seconds,9}  {n.Last:yyyy-MM-dd HH:mm}  {n.Text}");
    }

    private static void Samples(SampleStore db, DateTimeOffset from, DateTimeOffset to)
    {
        var rows = db.Read(from, to);
        Console.WriteLine($"# {rows.Count} samples");
        foreach (var o in rows)
            Console.WriteLine($"{o.At:yyyy-MM-dd HH:mm:ss}\t{o.IdleSeconds}\t{o.App}\t{o.Title}");
    }


    private static void Rounds(SampleStore db, DateTimeOffset from, DateTimeOffset to)
    {
        var rounds = db.Rounds(from, to);
        Console.WriteLine($"# {rounds.Count} rounds");
        foreach (var r in rounds)
            Console.WriteLine($"{r.StartedAt:yyyy-MM-dd HH:mm} → {r.EndedAt:HH:mm}\t{r.FocusMinutes,3}min\t"
                            + $"{r.EndReason ?? "(still running)"}\t{string.Join(",", r.Goals)}");
    }

    /// <summary>
    /// 每一轮逐分钟的构成，没满格的那些还给出**红在哪扇窗口上**。
    ///
    /// ⚠️ **规则用的是库里当前那份**，不是那一轮当时锁定的那份——但**你要是改过规则，
    /// 回头看老轮次就会跟当时的判定对不上**。这一条必须说清楚，因为它不报错。
    /// </summary>
    private static void Minutes(SampleStore db, DateTimeOffset from, DateTimeOffset to)
    {
        // ⚠️ 规则用的是**库里当前**那份，不是那一轮当时的——配置改过的话，
        //    回头看老轮次会跟当时的判定对不上。它不报错，所以这里明说一句。
        var rules = GoalRules.Of(db.Goals(), db.Commands());

        var rounds = db.Rounds(from, to);
        if (rounds.Count == 0) { Console.WriteLine("# no rounds in this window"); return; }
        Console.WriteLine("# 规则用的是**库里当前**那份；改过规则的话，老轮次会跟当时的判定对不上");

        foreach (var r in rounds)
        {
            var until = r.EndedAt ?? to;
            var rows = db.Read(r.StartedAt, until.AddSeconds(1));
            var away = AwayMap.Of(rows);

            var round = new Round(r.StartedAt, r.FocusMinutes, r.Goals, rules);
            foreach (var o in rows) round.Observe(o.At, o.App, o.Title, away.Covers(o.At));
            round.Advance(until);

            Console.WriteLine();
            Console.WriteLine($"== {r.StartedAt:yyyy-MM-dd HH:mm} → {r.EndedAt:HH:mm}  {r.FocusMinutes}min  "
                            + $"{string.Join(",", r.Goals)}  {r.EndReason ?? "(still running)"}  "
                            + $"focused={round.FocusedSeconds}s slack={round.SlackSeconds}s");

            var minutes = (int)Math.Ceiling((until - r.StartedAt).TotalMinutes);
            for (var i = 0; i < Math.Min(minutes, Round.RingMinutes); i++)
            {
                var cell = round.Cell(i);
                var at = r.StartedAt.AddMinutes(i);
                var why = cell.OffTaskSeconds > 0
                          && OffTaskAttribution.Biggest(rows, away, r.Goals, rules, at) is { } who
                    ? $"  ← {who.Seconds}s [{who.App}] {who.Title}"
                    : "";
                Console.WriteLine($"{at:HH:mm}  focus={cell.FocusedSeconds,-3} off={cell.OffTaskSeconds,-3} "
                                + $"away={cell.AwaySeconds,-3} blank={cell.UnrecordedSeconds,-3} {cell.Tier,-10}{why}");
            }
        }
    }
}

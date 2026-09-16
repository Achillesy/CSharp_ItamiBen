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
/// <code>
/// ItamiBen --query config   [起] [止]    当前的规则 / 命令 / 计划表（起止不管用）
/// ItamiBen --query log      [起] [止]    itamiben.log：每次手动改配置，以及够不着库时的求救
/// ItamiBen --query samples  [起] [止]    一秒一行的原始观测
/// ItamiBen --query events   [起] [止]    别处留不下痕迹的事（闹钟 / 提醒 / 命令 / 出错）
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
        if (!File.Exists(path)) { Console.Error.WriteLine($"no samples.db at {path}"); return; }

        var start = Parse(from) ?? new DateTimeOffset(DateTime.Today, DateTimeOffset.Now.Offset);
        var end = Parse(to) ?? start.AddDays(1);

        using var db = SampleStore.Open(path);
        Console.WriteLine($"# {path}   {start:yyyy-MM-dd HH:mm} → {end:yyyy-MM-dd HH:mm}");

        switch (what)
        {
            case "config": ConfigDump(db); break;
            case "log": TextLog(); break;
            case "samples": Samples(db, start, end); break;
            case "events": Events(db, start, end); break;
            case "rounds": Rounds(db, start, end); break;
            case "minutes": Minutes(db, start, end); break;
            default:
                Console.Error.WriteLine($"unknown query '{what}' — try: config | log | samples | events | rounds | minutes");
                break;
        }

        static DateTimeOffset? Parse(string? s) => DateTimeOffset.TryParse(s, out var t) ? t : null;
    }

    /// <summary>
    /// 当前配置。**智能体改完应该跑一遍这个看看自己改对没有**——
    /// 比它自己拼 SQL 去查省事，也保证看到的跟程序读到的是同一份。
    /// </summary>
    private static void ConfigDump(SampleStore db)
    {
        Console.WriteLine($"# config version {db.ConfigVersion}");
        Console.WriteLine();
        Console.WriteLine("## goals");
        foreach (var g in db.Goals())
        {
            Console.WriteLine($"  {(g.Enabled ? "on " : "off")} {g.Name}");
            foreach (var r in g.Rules)
                Console.WriteLine($"        app={r.App ?? "*"}  title={r.Title ?? "*"}");
        }
        Console.WriteLine();
        Console.WriteLine("## commands");
        foreach (var c in db.Commands())
            Console.WriteLine($"  {c.Name}\n        macos={c.MacOS ?? "(none)"}\n        windows={c.Windows ?? "(none)"}");
        Console.WriteLine();
        Console.WriteLine("## schedule (enabled only)");
        foreach (var e in db.Schedule())
            Console.WriteLine($"  {e.Cron,-16} text={e.Text ?? "(none)"}  run={e.Run ?? "(none)"}");
    }

    /// <summary>
    /// 文本日志：每次手动改配置，以及够不着库时的求救。
    ///
    /// ⚠️ 它住在**库外面**（DECISIONS I21）：SQLite 够不着普通文件，所以这份记录不在
    /// 任何一句外来 SQL 的射程之内；库坏了要修的时候，它也不在那个坏掉的库里面。
    /// **「配置为什么长这样」唯一的答案**——`--query config` 只给现状。
    /// </summary>
    private static void TextLog()
    {
        var text = Log.Read();
        Console.WriteLine($"# {AppData.Dir}/itamiben.log");
        Console.Write(text.Length == 0 ? "# (empty)\n" : text);
    }

    private static void Samples(SampleStore db, DateTimeOffset from, DateTimeOffset to)
    {
        var rows = db.Read(from, to);
        Console.WriteLine($"# {rows.Count} samples");
        foreach (var o in rows)
            Console.WriteLine($"{o.At:yyyy-MM-dd HH:mm:ss}\t{o.IdleSeconds}\t{o.App}\t{o.Title}");
    }

    private static void Events(SampleStore db, DateTimeOffset from, DateTimeOffset to)
    {
        var rows = db.Events(from, to);
        Console.WriteLine($"# {rows.Count} events");
        foreach (var e in rows)
            Console.WriteLine($"{e.At:yyyy-MM-dd HH:mm:ss}\t{e.Level,-5}\t{e.Kind,-10}\t{e.Text}");
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

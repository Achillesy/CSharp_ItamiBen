using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 观测库。⚠️ 一律开内存库——**测试碰不到用户真实的 samples.db**
/// （v3 的 I5：单测把用户的账本冲成了测试数据，而且悄无声息）。
/// </summary>
public class SampleStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static SampleStore Memory() => SampleStore.Open(":memory:");

    [Fact]
    public void 写进去的就是读回来的()
    {
        using var db = Memory();
        db.Write(T0, "Code", "Round.cs", 0);

        var rows = db.Read(T0, T0.AddSeconds(1));
        var row = Assert.Single(rows);
        Assert.Equal(T0, row.At);
        Assert.Equal("Code", row.App);
        Assert.Equal("Round.cs", row.Title);
        Assert.Equal(0, row.IdleSeconds);
    }

    [Fact]
    public void 同一秒写两次只留一行()
    {
        using var db = Memory();
        Assert.True(db.Write(T0, "Code", "Round.cs", 0));
        Assert.False(db.Write(T0, "Safari", "淘宝", 99));      // 主键挡住，**先到的那条不被改写**

        var row = Assert.Single(db.Read(T0, T0.AddSeconds(1)));
        Assert.Equal("Code", row.App);
        Assert.Equal(0, row.IdleSeconds);
    }

    [Fact]
    public void 秒内的小数部分被抹掉所以同一秒还是同一行()
    {
        using var db = Memory();
        Assert.True(db.Write(T0.AddMilliseconds(120), "Code", "", 0));
        Assert.False(db.Write(T0.AddMilliseconds(870), "Code", "", 0));
        Assert.Equal(1, db.Counts.Samples);
    }

    [Fact]
    public void 读不到前台窗口时存空读回来也是空()
    {
        using var db = Memory();
        db.Write(T0, "", "", 12);

        var row = Assert.Single(db.Read(T0, T0.AddSeconds(1)));
        Assert.Equal("", row.App);
        Assert.Equal("", row.Title);
        Assert.Equal(12, row.IdleSeconds);
    }

    [Fact]
    public void 区间是左闭右开()
    {
        using var db = Memory();
        for (var i = 0; i < 5; i++) db.Write(T0.AddSeconds(i), "Code", "", 0);

        Assert.Equal(3, db.Read(T0.AddSeconds(1), T0.AddSeconds(4)).Count);
        Assert.Empty(db.Read(T0, T0));
    }

    [Fact]
    public void 缺的秒就是缺着绝不补()
    {
        // ⚠️ 这是整个设计的地基：**缺一行 = 那一秒没人在看**，跟「看见了空闲」是两回事
        using var db = Memory();
        db.Write(T0, "Code", "", 0);
        db.Write(T0.AddSeconds(9), "Code", "", 0);

        var rows = db.Read(T0, T0.AddSeconds(10));
        Assert.Equal(2, rows.Count);
        Assert.Equal(9, (rows[1].At - rows[0].At).TotalSeconds);
    }

    [Fact]
    public void 读出来天然按时间序不用排序()
    {
        using var db = Memory();
        foreach (var i in new[] { 7, 2, 9, 0, 4 }) db.Write(T0.AddSeconds(i), "Code", "", 0);

        var rows = db.Read(T0, T0.AddSeconds(10));
        Assert.Equal([0, 2, 4, 7, 9], rows.Select(r => (int)(r.At - T0).TotalSeconds));
    }

    [Fact]
    public void 字符串去重六百行同一个标题只占一行()
    {
        // 这就是分表的全部理由：实测标题 67 字节，盯十分钟不去重是 40KB
        using var db = Memory();
        for (var i = 0; i < 600; i++)
            db.Write(T0.AddSeconds(i), "Google Chrome", "曼昆经济学原理-习题解析.pdf - Right view - Google Chrome", 0);

        var (apps, titles, samples) = db.Counts;
        Assert.Equal(1, apps);
        Assert.Equal(1, titles);
        Assert.Equal(600, samples);
    }

    [Fact]
    public void 端点查得到空库是null()
    {
        using var db = Memory();
        Assert.Null(db.Oldest);
        Assert.Null(db.Newest);

        db.Write(T0.AddSeconds(30), "Code", "", 0);
        db.Write(T0, "Code", "", 0);
        db.Write(T0.AddSeconds(10), "Code", "", 0);

        Assert.Equal(T0, db.Oldest);
        Assert.Equal(T0.AddSeconds(30), db.Newest);
    }

    [Fact]
    public void 时刻在边界上归一到本地时区()
    {
        // v3 栽过：AW 的 UTC 偏移一路流到显示层，日志打出「16:37:35 达成」而本地是 00:37:35
        using var db = Memory();
        db.Write(T0.ToUniversalTime(), "Code", "", 0);

        var row = Assert.Single(db.Read(T0.AddSeconds(-1), T0.AddSeconds(1)));
        Assert.Equal(DateTimeOffset.Now.Offset, row.At.Offset);
        Assert.Equal(T0, row.At);                       // 同一个瞬间
    }

    // ── 轮次：本轮状态进库 ⇒ 崩溃不丢本轮 ────────────────────────────────

    [Fact]
    public void 开了一轮之后查得到没终结的那一轮()
    {
        using var db = Memory();
        Assert.Null(db.OpenRound());

        db.BeginRound(T0, 25, ["编程", "读书"]);

        var open = db.OpenRound();
        Assert.NotNull(open);
        Assert.Equal(T0, open!.Value.StartedAt);
        Assert.Equal(25, open.Value.FocusMinutes);
        Assert.Equal(["编程", "读书"], open.Value.Goals);
    }

    [Fact]
    public void 终结之后就查不到了()
    {
        using var db = Memory();
        db.BeginRound(T0, 25, ["编程"]);
        db.EndRound(T0, T0.AddMinutes(30), "GaveUp");
        Assert.Null(db.OpenRound());
    }

    [Fact]
    public void 同一分钟里再开一轮后开的赢()
    {
        // 起点抹到整分，所以 Give up 之后马上再开会撞主键——两轮的环起点本来就是同一个
        using var db = Memory();
        db.BeginRound(T0, 25, ["编程"]);
        db.BeginRound(T0, 10, ["读书"]);

        var open = db.OpenRound();
        Assert.Equal(10, open!.Value.FocusMinutes);
        Assert.Equal(["读书"], open.Value.Goals);
    }

    [Fact]
    public void 目标名里有逗号也存得住()
    {
        // 用换行分隔不是逗号，正是为了这个
        using var db = Memory();
        db.BeginRound(T0, 25, ["写作, 修订", "编程"]);
        Assert.Equal(["写作, 修订", "编程"], db.OpenRound()!.Value.Goals);
    }

    [Fact]
    public void 崩溃之后整轮都能从库里重放回来()
    {
        var path = Path.Combine(Path.GetTempPath(), $"itamiben-test-{Guid.NewGuid():N}.db");
        try
        {
            // 第一次运行：开一轮，攒 100 秒专注，然后「崩了」（没有 EndRound）
            using (var db = SampleStore.Open(path))
            {
                db.BeginRound(T0, 10, ["编程"]);
                for (var i = 0; i < 100; i++) db.Write(T0.AddSeconds(i), "Code", "Round.cs", 0);
            }

            // 第二次运行：接回来
            using (var db = SampleStore.Open(path))
            {
                var rec = db.OpenRound();
                Assert.NotNull(rec);

                var rows = db.Read(rec!.Value.StartedAt, T0.AddSeconds(200));
                var away = AwayMap.Of(rows);
                var round = new Round(rec.Value.StartedAt, rec.Value.FocusMinutes, rec.Value.Goals, TestRules.Rules);
                foreach (var o in rows) round.Observe(o.At, o.App, o.Title, away.Covers(o.At));
                round.Advance(T0.AddSeconds(199));

                Assert.Equal(100, round.FocusedSeconds);        // 一秒没丢
                Assert.Equal(100, round.WastedSeconds);         // 崩掉那 100 秒照样吃余量
                Assert.Equal(RoundPhase.Focusing, round.Phase);
            }
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void 关掉再开数据还在()
    {
        var path = Path.Combine(Path.GetTempPath(), $"itamiben-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = SampleStore.Open(path)) db.Write(T0, "Code", "Round.cs", 0);
            using (var db = SampleStore.Open(path))
                Assert.Equal("Round.cs", Assert.Single(db.Read(T0, T0.AddSeconds(1))).Title);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                try { File.Delete(f); } catch { }
        }
    }
}

/// <summary>
/// 账本和设置也住在这个库里（DECISIONS I12 / I13）。
/// ⚠️ 一律开内存库——**测试碰不到用户真实的账本**。
/// </summary>
public class StoreLedgerTests
{
    private static SampleStore Memory() => SampleStore.Open(":memory:");

    [Fact]
    public void 累计是加不是写()
    {
        using var db = Memory();
        db.AddTotals(new Dictionary<string, int> { ["编程"] = 600 });
        db.AddTotals(new Dictionary<string, int> { ["编程"] = 300, ["读书"] = 42 });

        var t = db.Totals();
        Assert.Equal(900, t["编程"]);   // 两轮加起来，不是后一轮盖掉前一轮
        Assert.Equal(42, t["读书"]);
    }

    [Fact]
    public void 零秒的目标不开账()
    {
        using var db = Memory();
        db.AddTotals(new Dictionary<string, int> { ["编程"] = 0 });

        Assert.Empty(db.Totals());
    }

    [Fact]
    public void 迁移用的覆盖写是覆盖不是加()
    {
        using var db = Memory();
        db.PutTotals(new Dictionary<string, long> { ["编程"] = 100 });
        db.PutTotals(new Dictionary<string, long> { ["编程"] = 57203 });

        Assert.Equal(57203, db.Totals()["编程"]);
    }

    [Fact]
    public void 设置逐键覆盖互不影响()
    {
        using var db = Memory();
        db.PutSettings(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        db.PutSettings(new Dictionary<string, string> { ["b"] = "3" });

        var s = db.Settings();
        Assert.Equal("1", s["a"]);   // 没提到的键不该被清掉
        Assert.Equal("3", s["b"]);
    }

    [Fact]
    public void 事件按时间读得回来()
    {
        using var db = Memory();
        var t0 = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));
        db.Note(t0, "info", "alarm", "响了");
        db.Note(t0, "warn", "db", "对不上");          // 同一秒可以有好几件事
        db.Note(t0.AddHours(2), "info", "stop", "退出");

        var rows = db.Events(t0, t0.AddHours(1));
        Assert.Equal(2, rows.Count);
        Assert.Equal("alarm", rows[0].Kind);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(t0), rows[0].At.Offset);
    }
}

/// <summary>
/// 外来 SQL 的护栏（DECISIONS I16）。**这组是那扇「在线修改配置」窗口能不能存在的前提**：
/// 它最终会把一段网页 AI 写的 SQL 跑在用户的库上。
/// </summary>
public class ApplySqlTests
{
    private static SampleStore Memory()
    {
        var db = SampleStore.Open(":memory:");
        db.AddTotals(new Dictionary<string, int> { ["编程"] = 57203 });
        db.Write(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8)), "Code", "x", 0);
        return db;
    }

    [Fact]
    public void 改配置的_SQL_跑得通_而且顺手把版本号加一()
    {
        using var db = Memory();
        var before = db.ConfigVersion;

        var r = db.ApplySql("INSERT INTO goal (name) VALUES ('Coding');");

        Assert.True(r.Ok);
        Assert.Equal(before + 1, db.ConfigVersion);   // AI 忘了写那句也没关系，程序兜底
        Assert.Contains(db.Goals(), g => g.Name == "Coding");
    }

    [Fact]
    public void 动了账本就整个回滚_连同一起提交的配置改动()
    {
        using var db = Memory();
        var version = db.ConfigVersion;

        // ⚠️ 前一句是正当的配置改动，后一句动账本。**两句必须一起作废**——
        //    只挡住后一句、放行前一句，会留下一个谁都没预期的半吊子状态
        var r = db.ApplySql("""
            INSERT INTO goal (name) VALUES ('Coding');
            UPDATE total SET seconds = 0;
            """);

        Assert.False(r.Ok);
        Assert.Contains("ledger", r.Message);
        Assert.Equal(57203, db.Totals()["编程"]);     // 账本一秒没动
        Assert.Empty(db.Goals());                      // 配置那一句也没落
        Assert.Equal(version, db.ConfigVersion);       // 版本号也没动
    }

    [Fact]
    public void 删采样也算动账本()
    {
        using var db = Memory();
        Assert.False(db.ApplySql("DELETE FROM sample;").Ok);
        Assert.Single(db.Read(DateTimeOffset.MinValue.AddDays(1), DateTimeOffset.MaxValue.AddDays(-1)));
    }

    [Fact]
    public void 语法错就一行都不落()
    {
        using var db = Memory();
        var version = db.ConfigVersion;

        var r = db.ApplySql("INSERT INTO goal (name) VALUES ('ok'); 这不是 SQL;");

        Assert.False(r.Ok);
        Assert.Empty(db.Goals());
        Assert.Equal(version, db.ConfigVersion);
    }

    [Fact]
    public void 导出的配置不含窗口标题也不含逐秒记录()
    {
        // ⚠️ 这一条是**隐私边界**：导出的东西要被贴进网页对话框。
        //    程序名可以给（AI 写 app 正则需要），窗口标题不行——
        //    前者泄露「装了什么」，后者泄露「在干什么」。
        using var db = Memory();
        db.Write(new DateTimeOffset(2026, 9, 16, 10, 0, 1, TimeSpan.FromHours(8)),
                 "Google Chrome", "某个很私人的网页标题", 0);

        var dump = db.DumpConfig(["layout"]);

        Assert.DoesNotContain("某个很私人的网页标题", dump);
        Assert.DoesNotContain("INSERT INTO sample", dump);
        Assert.DoesNotContain("INSERT INTO title", dump);
        Assert.Contains("Google Chrome", dump);          // 程序名要给
        Assert.DoesNotContain("INSERT INTO app", dump);  // 但写成注释，别诱导 AI 往账本里插
    }
}

/// <summary>
/// 账本护栏在把审计挪出数据库之后**依然成立**（DECISIONS I21）：
/// 记录搬去文件了，但 `sample` / `round` / `total` / `event` 该挡的照挡。
/// </summary>
public class LedgerStillGuardedTests
{
    [Fact]
    public void 挪走审计表之后账本照样挡得住()
    {
        using var db = SampleStore.Open(":memory:");
        db.AddTotals(new Dictionary<string, int> { ["编程"] = 100 });
        // ⚠️ 得真有一行采样：指纹比的是**数量**，空表上 `DELETE` 删掉 0 行、
        //    指纹自然没变，那是正确行为，不是漏网
        db.Write(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8)), "Code", "x", 0);

        Assert.False(db.ApplySql("UPDATE total SET seconds = 0;").Ok);
        Assert.False(db.ApplySql("DELETE FROM sample;").Ok);
        Assert.Equal(100, db.Totals()["编程"]);
    }
}

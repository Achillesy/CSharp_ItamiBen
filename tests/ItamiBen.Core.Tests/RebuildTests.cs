using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// **从库里重建整个环**——这条链是新架构的地基（DESIGN §9）：
/// <c>SampleStore</c> → <c>AwayMap</c> → <c>Round</c>。
/// </summary>
public class RebuildTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    /// <summary>产品里 MainWindow.Rebuild 干的就是这件事，这里照抄一遍好让它可测。</summary>
    private static Round Rebuild(SampleStore db, DateTimeOffset now, int focusMinutes = 10)
    {
        var rows = db.Read(T0, now.AddSeconds(1));
        var away = AwayMap.Of(rows);
        var round = new Round(T0, focusMinutes, ["编程"], TestRules.Rules);
        foreach (var o in rows) round.Observe(o.At, o.App, o.Title, away.Covers(o.At));
        round.Advance(now);
        return round;
    }

    [Fact]
    public void 重建多少次结果都一样()
    {
        using var db = SampleStore.Open(":memory:");
        for (var i = 0; i < 300; i++)
            db.Write(T0.AddSeconds(i), i % 3 == 0 ? "Code" : "Safari", "Round.cs", 0);

        var a = Rebuild(db, T0.AddSeconds(300));
        var b = Rebuild(db, T0.AddSeconds(300));

        Assert.Equal(a.FocusedSeconds, b.FocusedSeconds);
        Assert.Equal(a.WastedSeconds, b.WastedSeconds);
        Assert.Equal(a.Cells.Select(c => (c.FocusedSeconds, c.SampledSeconds)),
                     b.Cells.Select(c => (c.FocusedSeconds, c.SampledSeconds)));
    }

    [Fact]
    public void 重建出来的跟一秒一秒喂进去的一样()
    {
        using var db = SampleStore.Open(":memory:");
        var live = new Round(T0, 10, ["编程"], TestRules.Rules);

        for (var i = 0; i < 300; i++)
        {
            var app = i % 3 == 0 ? "Code" : "Safari";
            db.Write(T0.AddSeconds(i), app, "Round.cs", 0);
            live.Observe(T0.AddSeconds(i), app, "Round.cs");
        }

        var rebuilt = Rebuild(db, T0.AddSeconds(299));
        Assert.Equal(live.FocusedSeconds, rebuilt.FocusedSeconds);
        Assert.Equal(live.WastedSeconds, rebuilt.WastedSeconds);
    }

    [Fact]
    public void 锁屏那一小时重建之后是空白不是红格()
    {
        // 2026-09-16 实测：macOS 锁屏时前台是 loginwindow / Login，不是空字符串。
        // 这条要是不成立，锁屏一小时就是一小时红格——v3 3.9.x 那个大 bug
        using var db = SampleStore.Open(":memory:");
        for (var i = 0; i < 3600; i++)
            db.Write(T0.AddSeconds(i), "loginwindow", "Login", i);

        var r = Rebuild(db, T0.AddSeconds(3599), focusMinutes: 50);

        Assert.Equal(0, r.FocusedSeconds);

        // ⚠️ 只有 1 秒是红的：**按下锁屏那一下**——那一秒 idle=0，人确实在，
        //    而屏幕上已经是 loginwindow 了。剩下 3599 秒一格红都没有
        Assert.Equal(1, r.Cells.Sum(c => c.OffTaskSeconds));
        Assert.Equal(3600, r.WastedSeconds);                                    // 但那一小时确实过去了
    }

    [Fact]
    public void 跨过门槛前那一百八十秒会被回溯纠正()
    {
        // 实时那条路把它们判成跑偏（当时 idle 还没到门槛），重建时要纠正过来
        using var db = SampleStore.Open(":memory:");
        var live = new Round(T0, 10, ["编程"], TestRules.Rules);

        for (var i = 0; i < 200; i++)
        {
            db.Write(T0.AddSeconds(i), "Safari", "淘宝", i);
            live.Observe(T0.AddSeconds(i), "Safari", "淘宝", away: i >= AwayMap.ThresholdSeconds);
        }

        // 实时：前 180 秒被判成红格
        Assert.Equal(180, live.Cell(0).OffTaskSeconds + live.Cell(1).OffTaskSeconds
                        + live.Cell(2).OffTaskSeconds + live.Cell(3).OffTaskSeconds);

        // 重建：整段都追认成「人不在」，只剩最后动过的那一秒是红的
        var rebuilt = Rebuild(db, T0.AddSeconds(199));
        Assert.Equal(1, rebuilt.Cells.Sum(c => c.OffTaskSeconds));
        Assert.Single(AwayMap.Of(db.Read(T0, T0.AddSeconds(200))).Spans);
    }

    [Fact]
    public void 库里最后一行到此刻之间的空白照样吃余量()
    {
        // 程序没跑 / 电脑睡了那一段：既不计入也不算跑偏，但它从环上过去了
        using var db = SampleStore.Open(":memory:");
        for (var i = 0; i < 60; i++) db.Write(T0.AddSeconds(i), "Code", "Round.cs", 0);

        var r = Rebuild(db, T0.AddSeconds(3600));
        Assert.Equal(60, r.FocusedSeconds);
        Assert.Equal(3601 - 60, r.WastedSeconds);
        Assert.Equal(0, r.Cell(30).SampledSeconds);     // 中间那些分钟一格都没有
    }

    [Fact]
    public void 隔了两小时再重建直接触底()
    {
        using var db = SampleStore.Open(":memory:");
        db.Write(T0, "Code", "Round.cs", 0);

        var r = Rebuild(db, T0.AddHours(3));
        Assert.Equal(EndReason.RanOut, r.Ending);
    }
}

using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>采样 → 格子 → 累计：一轮怎么把秒钉进环里。</summary>
public class RoundTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static Round NewRound(int focusMinutes = 10, params string[] goals)
        => new(T0, focusMinutes, goals.Length > 0 ? goals : ["编程"], TestRules.Rules);

    /// <summary>从第 <paramref name="from"/> 秒起连着专注 <paramref name="count"/> 秒。</summary>
    private static void Work(Round r, int from, int count, string app = "Code", string title = "Round.cs")
    {
        for (var i = 0; i < count; i++) r.Observe(T0.AddSeconds(from + i), app, title);
    }

    // ── 采样：当场定死，永不改写 ──────────────────────────────────────────

    [Fact]
    public void 同一秒调两次只记一次()
    {
        var r = NewRound();
        r.Observe(T0.AddSeconds(5), "Code", "");
        Assert.Null(r.Observe(T0.AddSeconds(5.4), "Code", ""));   // 高频采样的第二拍
        Assert.Equal(1, r.FocusedSeconds);
    }

    [Fact]
    public void 漏掉的拍永远空着绝不补记()
    {
        var r = NewRound();
        r.Observe(T0, "Code", "");
        r.Observe(T0.AddSeconds(10), "Code", "");     // 中间 9 拍漏了

        Assert.Equal(2, r.FocusedSeconds);
        var cell = r.Cell(0);
        Assert.Equal(2, cell.SampledSeconds);
        Assert.Equal(58, cell.UnrecordedSeconds);
    }

    [Fact]
    public void 没采到的秒不算跑偏()
    {
        var r = NewRound();
        r.Observe(T0.AddSeconds(30), "Code", "");
        Assert.Equal(0, r.Cell(0).OffTaskSeconds);    // 前 30 秒没人看着，但没人被冤枉
    }

    [Fact]
    public void 跑偏的秒进采样计数不进专注计数()
    {
        var r = NewRound();
        Work(r, 0, 10, "Code");
        Work(r, 10, 20, "Safari", "淘宝");

        var cell = r.Cell(0);
        Assert.Equal(10, cell.FocusedSeconds);
        Assert.Equal(30, cell.SampledSeconds);
        Assert.Equal(20, cell.OffTaskSeconds);
        Assert.Equal(10, r.FocusedSeconds);
    }

    [Fact]
    public void 自身豁免的秒两个计数都不加()
    {
        var r = NewRound();
        Work(r, 0, 10, "ItamiBen", "ItamiBen");

        Assert.Equal(0, r.Cell(0).SampledSeconds);
        Assert.Equal(0, r.Cell(0).FocusedSeconds);
        Assert.Equal(0, r.FocusedSeconds);
    }

    [Fact]
    public void 盯着钟面看也吃余量只是不被判跑偏()
    {
        // 「不冤枉人」跟「不占环上的格子」是两回事——后者没有例外
        var r = NewRound();
        Work(r, 0, 60, "ItamiBen", "ItamiBen");
        Assert.Equal(0, r.Cell(0).OffTaskSeconds);
        Assert.Equal(60, r.WastedSeconds);
    }

    [Fact]
    public void 人不在的秒不进采样计数但单独记着()
    {
        var r = NewRound();
        for (var i = 0; i < 60; i++) r.Observe(T0.AddSeconds(i), "loginwindow", "Login", away: true);

        var cell = r.Cell(0);
        Assert.Equal(0, cell.SampledSeconds);         // 不画红格：不冤枉人
        Assert.Equal(60, cell.AwaySeconds);           // 但**记着**——它要画成空心虚线框
        Assert.Equal(0, cell.UnrecordedSeconds);      // 跟「一秒都没记」是两回事
        Assert.Equal(CellTier.Away, cell.Tier);
        Assert.Equal(0, r.FocusedSeconds);
        Assert.Equal(60, r.WastedSeconds);            // 那一分钟确实从环上过去了
    }

    [Theory]
    [InlineData(60, CellTier.FocusFull)]
    [InlineData(41, CellTier.FocusFull)]
    [InlineData(40, CellTier.FocusMid)]
    [InlineData(21, CellTier.FocusMid)]
    [InlineData(20, CellTier.FocusLow)]
    [InlineData(1, CellTier.FocusLow)]
    public void 有专注就按秒数分三档(int focused, CellTier expected)
        => Assert.Equal(expected, new MinuteCell(0, focused, 60, 0).Tier);

    [Fact]
    public void 一秒专注都没有时取最大的那一类平局倒向更靠后的档位()
    {
        // ⚠️ 是 argmax 不是「过半数」：三类混在一起可能谁都不过半，
        //    按阈值判会默认落进红色——那样「29 秒离开 + 28 秒跑偏」会被整格判成红的，
        //    而**把离开画成跑偏是冤枉人**
        Assert.Equal(CellTier.Away, new MinuteCell(0, 0, 28, 29).Tier);
        Assert.Equal(CellTier.OffTask, new MinuteCell(0, 0, 30, 29).Tier);
        Assert.Equal(CellTier.OffTask, new MinuteCell(0, 0, 29, 29).Tier);   // 平局倒向红
        Assert.Equal(CellTier.NotDrawn, new MinuteCell(0, 0, 0, 0).Tier);    // 整格没记
        Assert.Equal(CellTier.NotDrawn, new MinuteCell(0, 0, 10, 10).Tier);  // 没记的那 40 秒最多
    }

    // ── 格子 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 格子索引就是从Start起的第几分钟()
    {
        var r = NewRound();
        r.Observe(T0.AddSeconds(59), "Code", "");
        r.Observe(T0.AddSeconds(60), "Code", "");
        r.Observe(T0.AddMinutes(90), "Code", "");

        Assert.Equal(1, r.Cell(0).FocusedSeconds);
        Assert.Equal(1, r.Cell(1).FocusedSeconds);
        Assert.Equal(1, r.Cell(90).FocusedSeconds);
        Assert.Equal(90, r.CurrentMinute);
    }

    [Fact]
    public void 环一开始就整个存在一共一百二十格()
    {
        var r = NewRound();
        Assert.Equal(120, r.Cells.Count);
        Assert.Equal(119, r.Cells[^1].Index);
    }

    // ── StartedAt 抹到整分 ───────────────────────────────────────────────

    [Fact]
    public void 起点抹到整分让格子跟钟面刻度逐格对齐()
    {
        var r = new Round(T0.AddSeconds(27), 10, ["编程"], TestRules.Rules);
        Assert.Equal(T0, r.StartedAt);
    }

    [Fact]
    public void 点Start之前的那几十秒没人看着所以照样吃余量()
    {
        var r = new Round(T0.AddSeconds(27), 10, ["编程"], TestRules.Rules);
        r.Observe(T0.AddSeconds(27), "Code", "");

        Assert.Equal(1, r.FocusedSeconds);
        Assert.Equal(27, r.WastedSeconds);          // 0~26 秒既不计入也不算跑偏，但过去了就是过去了
        Assert.Equal(0, r.Cell(0).OffTaskSeconds);
    }

    [Fact]
    public void 时钟往回跳不算走过()
    {
        var r = NewRound();
        r.Observe(T0.AddSeconds(-30), "Code", "");
        Assert.True(r.WastedSeconds >= 0);
    }

    // ── 累计 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 累计按目标拆开()
    {
        var r = NewRound(10, "编程", "读书");
        Work(r, 0, 10, "Code", "Round.cs");
        Work(r, 10, 5, "Preview", "曼昆经济学原理.pdf");

        Assert.Equal(15, r.FocusedSeconds);
        Assert.Equal(10, r.FocusedSecondsByGoal["编程"]);
        Assert.Equal(5, r.FocusedSecondsByGoal["读书"]);
    }

    // ── 阶段 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 攒够目标就进休息而且休息从达成那一拍起算()
    {
        var r = NewRound(1);                        // 目标 60 秒，休息 1 分钟
        Work(r, 0, 60);

        Assert.Equal(RoundPhase.Resting, r.Phase);
        Assert.Equal(T0.AddSeconds(59), r.AchievedAt);
        Assert.Equal(60, r.FocusedSeconds);
    }

    [Fact]
    public void 休息期间不再累计专注()
    {
        var r = NewRound(1);
        Work(r, 0, 60);
        Work(r, 60, 30);                            // 继续用 Code 干活

        Assert.Equal(60, r.FocusedSeconds);         // 封顶在目标上，一秒都不多
        Assert.Equal(0, r.Cell(1).SampledSeconds);
    }

    [Fact]
    public void 休息期间余量冻结()
    {
        var r = NewRound(1);
        Work(r, 0, 60);
        var slack = r.SlackSeconds;
        r.Observe(T0.AddSeconds(90), "Safari", "淘宝");
        Assert.Equal(slack, r.SlackSeconds);
    }

    [Fact]
    public void 休息走完这一轮就结束()
    {
        var r = NewRound(1);
        Work(r, 0, 60);                             // 达成于 T0+59s
        r.Observe(T0.AddSeconds(118), "Safari", "");
        Assert.Equal(RoundPhase.Resting, r.Phase);

        r.Observe(T0.AddSeconds(119), "Safari", "");   // 59s + 60s
        Assert.Equal(RoundPhase.Ended, r.Phase);
        Assert.Equal(EndReason.Completed, r.Ending);
    }

    [Fact]
    public void 终结之后再采样什么都不会变()
    {
        var r = NewRound();
        Work(r, 0, 10);
        r.End(T0.AddSeconds(10), EndReason.GaveUp);

        Assert.Null(r.Observe(T0.AddSeconds(11), "Code", ""));
        Assert.Equal(10, r.FocusedSeconds);
        Assert.Equal(EndReason.GaveUp, r.Ending);
    }

    [Fact]
    public void 一轮只终结一次()
    {
        var r = NewRound();
        r.End(T0.AddSeconds(10), EndReason.GaveUp);
        r.End(T0.AddSeconds(20), EndReason.Closed);

        Assert.Equal(EndReason.GaveUp, r.Ending);
        Assert.Equal(T0.AddSeconds(10), r.EndedAt);
    }

    [Fact]
    public void 算过的秒无论怎么结束都不会被拿走()
    {
        // DECISIONS C5：四个出口执行同一个动作，没有「give up 打折」这种分支
        foreach (var reason in new[] { EndReason.GaveUp, EndReason.Closed, EndReason.RanOut })
        {
            var r = NewRound();
            Work(r, 0, 42);
            r.End(T0.AddSeconds(42), reason);
            Assert.Equal(42, r.FocusedSeconds);
            Assert.Equal(42, r.FocusedSecondsByGoal["编程"]);
        }
    }

    // ── 只推进时间、不记秒（从库重建时补最后一段用）──────────────────────

    [Fact]
    public void 只推进时间不会记下任何一秒()
    {
        var r = NewRound();
        Work(r, 0, 10);
        r.Advance(T0.AddSeconds(100));

        Assert.Equal(10, r.FocusedSeconds);
        Assert.Equal(10, r.Cell(0).SampledSeconds);
        Assert.Equal(0, r.Cell(1).SampledSeconds);
        Assert.Equal(91, r.WastedSeconds);          // 101 秒过去了，专注 10 秒
    }

    [Fact]
    public void 只推进时间也会触底()
    {
        // 程序睡了两小时再回来：那段时间照样从环上过去了
        var r = NewRound(10);
        r.Advance(T0.AddHours(3));
        Assert.Equal(EndReason.RanOut, r.Ending);
    }

    [Fact]
    public void 只推进时间也会结束休息()
    {
        var r = NewRound(1);
        Work(r, 0, 60);
        Assert.Equal(RoundPhase.Resting, r.Phase);

        r.Advance(T0.AddSeconds(119));
        Assert.Equal(EndReason.Completed, r.Ending);
    }

    [Fact]
    public void 重放同一段观测结果完全一样()
    {
        // 「任何时候从库里重建 120 分钟的环，结果都一样」——这条性质靠哨兵只进不退
        static Round Build(int times)
        {
            var r = NewRound(10, "编程");
            for (var pass = 0; pass < times; pass++)
                for (var i = 0; i < 90; i++)
                    r.Observe(T0.AddSeconds(i), i % 3 == 0 ? "Code" : "Safari", "Round.cs");
            return r;
        }

        var once = Build(1);
        var thrice = Build(3);
        Assert.Equal(once.FocusedSeconds, thrice.FocusedSeconds);
        Assert.Equal(once.WastedSeconds, thrice.WastedSeconds);
        Assert.Equal(once.Cells.Select(c => (c.FocusedSeconds, c.SampledSeconds)),
                     thrice.Cells.Select(c => (c.FocusedSeconds, c.SampledSeconds)));
    }

    // ── 提交时就该拒绝的设定 ──────────────────────────────────────────────

    [Fact]
    public void 一个目标都不勾直接拒绝()
        => Assert.Throws<ArgumentException>(() => new Round(T0, 10, [], TestRules.Rules));

    [Fact]
    public void 勾一个禁用的或不存在的目标直接拒绝()
    {
        Assert.Throws<ArgumentException>(() => new Round(T0, 10, ["去年的目标"], TestRules.Rules));
        Assert.Throws<ArgumentException>(() => new Round(T0, 10, ["莫须有"], TestRules.Rules));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]      // 101 + ⌈101/5⌉ = 122 > 120，塞不进环里
    public void 塞不进环里的专注长度直接拒绝(int focusMinutes)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new Round(T0, focusMinutes, ["编程"], TestRules.Rules));
}

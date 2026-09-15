using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>两小时环：达成截止线、余量、以及钟面上那三段读数。</summary>
public class RingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static Round NewRound(int focusMinutes)
        => new(T0, focusMinutes, ["编程"], TestRules.Rules);

    private static void Work(Round r, int from, int count)
    {
        for (var i = 0; i < count; i++) r.Observe(T0.AddSeconds(from + i), "Code", "Round.cs");
    }

    // ── 休息长度 ─────────────────────────────────────────────────────────

    [Fact]
    public void 休息分钟数是专注的五分之一上取整()
    {
        // ⚠️ 逐个值都测，不是只测 5 的倍数：v3 就是因为守着 ⌊f/5⌋+1 的测试全用 5 的倍数，
        //    滑块步长从 5 改成 1 之后 8 个值错了 6 个，测试一直是绿的
        for (var f = 1; f <= Round.MaxFocusMinutes; f++)
            Assert.Equal((int)Math.Ceiling(f / 5.0), Round.BreakMinutesFor(f));
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(25, 5)]
    [InlineData(50, 10)]
    public void 界面上那三档的休息长度(int focus, int rest)
        => Assert.Equal(rest, Round.BreakMinutesFor(focus));

    // ── 达成截止线 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 2, 118, 6480)]
    [InlineData(25, 5, 115, 5400)]
    [InlineData(50, 10, 110, 3600)]
    public void 达成截止线是第一百二十减休息分钟(int focus, int rest, int deadline, int budget)
    {
        var r = NewRound(focus);
        Assert.Equal(rest, r.BreakMinutes);
        Assert.Equal(deadline, r.DeadlineMinute);
        Assert.Equal(budget, r.BudgetSeconds);
        Assert.Equal(r.DeadlineMinute * 60, r.BudgetSeconds + r.TargetSeconds);
    }

    [Theory]
    [InlineData(10, 8.5)]
    [InlineData(25, 21.7)]
    [InlineData(50, 45.5)]
    public void 难度曲线是从截止线自然长出来的不是调出来的(int focus, double percent)
    {
        var r = NewRound(focus);
        var required = 100.0 * r.TargetSeconds / (r.DeadlineMinute * 60);
        Assert.Equal(percent, Math.Round(required, 1));
    }

    [Fact]
    public void 一百分钟是能塞进环里的上限()
    {
        Assert.Equal(120, Round.MaxFocusMinutes + Round.BreakMinutesFor(Round.MaxFocusMinutes));
        Assert.True(Round.MaxFocusMinutes + 1 + Round.BreakMinutesFor(Round.MaxFocusMinutes + 1) > Round.RingMinutes);
    }

    // ── 触底 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 余量刚好用光还活着()
    {
        var r = NewRound(10);
        r.Observe(T0.AddSeconds(6479), "Safari", "淘宝");   // 第 6480 秒
        Assert.Equal(0, r.SlackSeconds);
        Assert.Equal(RoundPhase.Focusing, r.Phase);
    }

    [Fact]
    public void 余量再少一秒就触底()
    {
        var r = NewRound(10);
        r.Observe(T0.AddSeconds(6480), "Safari", "淘宝");   // 第 6481 秒
        Assert.Equal(RoundPhase.Ended, r.Phase);
        Assert.Equal(EndReason.RanOut, r.Ending);
    }

    [Fact]
    public void 程序被挂起很久再回来当场触底()
    {
        var r = NewRound(50);
        r.Observe(T0.AddHours(5), "Code", "Round.cs");     // 合盖五小时
        Assert.Equal(EndReason.RanOut, r.Ending);
    }

    [Fact]
    public void 最晚达成时休息正好塞满环尾()
    {
        // 休息永远塞得下——这个边界情况是被设计掉的，不是被处理的（DECISIONS C6）
        var r = NewRound(1);                               // 目标 60 秒，休息 1 分钟，余量 7080
        r.Observe(T0.AddSeconds(7079), "Safari", "淘宝");
        Assert.Equal(0, r.SlackSeconds);

        Work(r, 7080, 60);                                 // 贴着截止线把 60 秒攒够
        Assert.Equal(RoundPhase.Resting, r.Phase);
        Assert.Equal(Round.RingSeconds, r.Project().BreakEndSeconds);

        r.Observe(T0.AddSeconds(7199), "Safari", "");
        Assert.Equal(EndReason.Completed, r.Ending);
    }

    // ── 三段读数（DESIGN §5）────────────────────────────────────────────

    [Fact]
    public void 分针到淡蓝起点是还要专注多久()
    {
        var r = NewRound(10);
        Work(r, 0, 120);                                   // 专注了 2 分钟
        var p = r.Project();
        Assert.Equal(600 - 120, p.CommitSeconds);
    }

    [Fact]
    public void 淡蓝块的长度就是休息多久()
    {
        var r = NewRound(50);
        Assert.Equal(10 * 60, r.Project().BreakSeconds);
    }

    [Fact]
    public void 淡蓝块尾到环终点就是余量()
    {
        var r = NewRound(25);
        r.Observe(T0.AddSeconds(300), "Safari", "淘宝");
        var p = r.Project();
        Assert.Equal(r.SlackSeconds, p.SlackSeconds);
        Assert.Equal(Round.RingSeconds - p.BreakEndSeconds, p.SlackSeconds);
    }

    [Fact]
    public void 三段读数在任何一刻都拼满整个环()
    {
        // 「淡蓝尾撞到环终点 = 结束」和「余量耗尽 = 结束」本来就是同一句话
        var r = NewRound(25);
        for (var s = 0; s < 3000; s += 7)
        {
            r.Observe(T0.AddSeconds(s), s % 3 == 0 ? "Code" : "Safari", "Round.cs");
            var p = r.Project();
            Assert.Equal(Round.RingSeconds, p.HandSeconds + p.CommitSeconds + p.BreakSeconds + p.SlackSeconds);
        }
    }

    [Fact]
    public void 摸鱼一秒淡蓝块就往环尾退一秒()
    {
        // ⚠️ 这块蓝往环尾退，这就是惩罚本身（DESIGN §5）
        var r = NewRound(10);
        Work(r, 0, 10);
        var before = r.Project().BreakStartSeconds;

        for (var i = 0; i < 30; i++) r.Observe(T0.AddSeconds(10 + i), "Safari", "淘宝");
        Assert.Equal(before + 30, r.Project().BreakStartSeconds);
    }

    [Fact]
    public void 专注一秒淡蓝块就原地不动()
    {
        var r = NewRound(10);
        Work(r, 0, 10);
        var before = r.Project().BreakStartSeconds;
        Work(r, 10, 30);
        Assert.Equal(before, r.Project().BreakStartSeconds);   // 分针推进多少，承诺弧就缩短多少
    }

    [Fact]
    public void 达成之后淡蓝块钉死不再动()
    {
        var r = NewRound(1);
        Work(r, 0, 60);
        var p1 = r.Project();
        r.Observe(T0.AddSeconds(90), "Safari", "淘宝");
        var p2 = r.Project();

        Assert.Equal(p1.BreakStartSeconds, p2.BreakStartSeconds);
        Assert.Equal(p1.BreakEndSeconds, p2.BreakEndSeconds);
    }

    [Fact]
    public void 分针走进淡蓝块之前是预告走进去之后是已发生()
    {
        // 不分开的话，分针走进休息区时画面毫无变化（DECISIONS D2）
        var r = NewRound(1);
        Work(r, 0, 59);
        Assert.False(r.Project().BreakStarted);

        Work(r, 59, 1);                                    // 第 60 秒达成
        Assert.True(r.Project().BreakStarted);
    }
}

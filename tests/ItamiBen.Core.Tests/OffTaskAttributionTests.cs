using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 归因只做诊断，但**它的答案必须跟判定一致**——否则日志会理直气壮地给出错误的解释，
/// 比没有更糟。
///
/// ⚠️ 这一组里最要紧的不是「找得对不对」，是**它真的会触发**：v3 那边同样一行日志
/// 因为索引取错，在整个生产历史里**一次都没打出来过**，而「零匹配」看起来跟
/// 「一直没跑偏」一模一样。所以这里连「这一分钟没有跑偏时返回 null」也要钉住，
/// 免得哪天它变成永远返回 null 而没人发现。
/// </summary>
public class OffTaskAttributionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static readonly GoalRules Rules = GoalRules.Parse(
        """{ "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } } }""");

    private static readonly string[] 编程 = ["编程"];

    private static List<Observation> Rows(params (int Offset, string App, string Title)[] rows)
        => rows.Select(r => new Observation(T0.AddSeconds(r.Offset), r.App, r.Title, 0)).ToList();

    [Fact]
    public void 挑出贡献跑偏秒数最多的那扇窗口()
    {
        var rows = Rows(
            (0, "Google Chrome", "bilibili"), (1, "Google Chrome", "bilibili"), (2, "Google Chrome", "bilibili"),
            (3, "Finder", ""),
            (4, "Code", "Round.cs"));          // 这一秒是专注，不该被算进来

        var culprit = OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0);

        Assert.NotNull(culprit);
        Assert.Equal("Google Chrome", culprit!.Value.App);
        Assert.Equal("bilibili", culprit.Value.Title);
        Assert.Equal(3, culprit.Value.Seconds);
    }

    [Fact]
    public void 同一个_app_的不同标题分开算()
    {
        // 「开着 Chrome」不是有用的诊断，「开着哪个标签」才是
        var rows = Rows(
            (0, "Google Chrome", "bilibili"),
            (1, "Google Chrome", "X"), (2, "Google Chrome", "X"));

        var culprit = OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0);

        Assert.Equal("X", culprit!.Value.Title);
        Assert.Equal(2, culprit.Value.Seconds);
    }

    [Fact]
    public void 平局取先出现的那个()
    {
        var rows = Rows(
            (0, "Finder", ""),
            (1, "Google Chrome", "X"));

        var culprit = OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0);

        Assert.Equal("Finder", culprit!.Value.App);
    }

    [Fact]
    public void 只看这一分钟别的分钟不算()
    {
        var rows = Rows(
            (0, "Finder", ""),
            (60, "Google Chrome", "X"), (61, "Google Chrome", "X"), (62, "Google Chrome", "X"));

        // 第一分钟：只有 Finder 那一秒
        Assert.Equal("Finder", OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0)!.Value.App);
        // 第二分钟：只有 Chrome 那三秒
        Assert.Equal(3, OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0.AddMinutes(1))!.Value.Seconds);
    }

    [Fact]
    public void 人不在的那些秒不算跑偏()
    {
        // 空闲已经跨过门槛，屏幕上是什么都无所谓了——这一分钟不该有任何归因
        var rows = new List<Observation>
        {
            new(T0, "loginwindow", "Login", AwayMap.ThresholdSeconds + 5),
            new(T0.AddSeconds(1), "loginwindow", "Login", AwayMap.ThresholdSeconds + 6),
        };

        Assert.Null(OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0));
    }

    [Fact]
    public void 这一分钟全在专注时返回_null()
    {
        var rows = Rows((0, "Code", "Round.cs"), (1, "Code", "Round.cs"));

        Assert.Null(OffTaskAttribution.Biggest(rows, AwayMap.Of(rows), 编程, Rules, T0));
    }

    /// <summary>
    /// 归因和判定**用的是同一个 Judgment**，所以两边的秒数必须对得上：
    /// 把同一批行喂给 <see cref="Round"/>，那一分钟格子里的 <c>OffTaskSeconds</c>
    /// 应当不小于归因给出的秒数（归因只报最大的那一扇窗口，不是全部）。
    /// </summary>
    [Fact]
    public void 归因的秒数不会超过格子里真实的跑偏秒数()
    {
        var rows = Rows(
            (0, "Google Chrome", "bilibili"), (1, "Google Chrome", "bilibili"),
            (2, "Finder", ""),
            (3, "Code", "Round.cs"));

        var round = new Round(T0, focusMinutes: 25, 编程, Rules);
        var away = AwayMap.Of(rows);
        foreach (var o in rows) round.Observe(o.At, o.App, o.Title, away.Covers(o.At));

        var culprit = OffTaskAttribution.Biggest(rows, away, 编程, Rules, T0);

        Assert.Equal(3, round.Cell(0).OffTaskSeconds);
        Assert.Equal(2, culprit!.Value.Seconds);
        Assert.True(culprit.Value.Seconds <= round.Cell(0).OffTaskSeconds);
    }
}

/// <summary>
/// 「刚过去的那一分钟」这一步单独钉住——v3 就是在这一步取错索引，
/// 结果那行日志在生产里**一次都没打出来过**。
/// </summary>
public class PreviousMinuteTests
{
    [Theory]
    [InlineData(10, 0, 0, 9, 59)]    // 整点那一秒：刚过去的是 9:59
    [InlineData(10, 0, 30, 9, 59)]   // 分钟中间：还是 9:59
    [InlineData(10, 0, 59, 9, 59)]   // 这一分钟的最后一秒：仍然是 9:59，本分钟还没走完
    [InlineData(10, 1, 0, 10, 0)]    // 跨到下一分钟，刚过去的才变成 10:00
    [InlineData(0, 0, 0, 23, 59)]    // 跨零点
    public void 刚过去的那一分钟(int h, int m, int s, int expectH, int expectM)
    {
        var now = new DateTimeOffset(2026, 9, 16, h, m, s, TimeSpan.FromHours(8));
        var prev = TimeGrid.PreviousMinute(now);

        Assert.Equal(expectH, prev.Hour);
        Assert.Equal(expectM, prev.Minute);
        Assert.Equal(0, prev.Second);
    }
}

/// <summary>
/// 「这个时刻落在哪一格」——v3 就是在调用方自己算这个索引时算错的。
/// </summary>
public class CellAtTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static Round Fresh() => new(T0, 25, ["编程"],
        GoalRules.Parse("""{ "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } } }"""));

    [Theory]
    [InlineData(0, 0)]        // 开始那一秒
    [InlineData(59, 0)]       // 第一分钟的最后一秒，还是第 0 格
    [InlineData(60, 1)]       // 跨到第 1 格
    [InlineData(7199, 119)]   // 环的最后一秒
    public void 落在对应的那一格(int secondsIn, int expectIndex)
    {
        var cell = Fresh().CellAt(T0.AddSeconds(secondsIn));
        Assert.NotNull(cell);
        Assert.Equal(expectIndex, cell!.Value.Index);
    }

    [Fact]
    public void 早于开始时刻返回_null()
        => Assert.Null(Fresh().CellAt(T0.AddSeconds(-1)));

    [Fact]
    public void 超出两小时环返回_null()
        => Assert.Null(Fresh().CellAt(T0.AddMinutes(Round.RingMinutes)));

    [Fact]
    public void 读到的就是那一分钟真实的构成()
    {
        var r = Fresh();
        for (var i = 0; i < 20; i++) r.Observe(T0.AddMinutes(1).AddSeconds(i), "Code", "Round.cs");
        for (var i = 20; i < 50; i++) r.Observe(T0.AddMinutes(1).AddSeconds(i), "Google Chrome", "X");

        var cell = r.CellAt(T0.AddMinutes(1).AddSeconds(30));

        Assert.Equal(20, cell!.Value.FocusedSeconds);
        Assert.Equal(30, cell.Value.OffTaskSeconds);
        Assert.Equal(10, cell.Value.UnrecordedSeconds);
    }
}

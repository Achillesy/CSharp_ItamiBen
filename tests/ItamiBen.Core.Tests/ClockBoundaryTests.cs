using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 时区的护栏——v3 的 `ClockDisplayTests` 在 v4 的对应物。
///
/// v3 那个 bug 的形状：报告里「专注达成于」印的是 06:40:45，实际是 14:40:45。同一份报告
/// 混了两个时区——`StartedAt` 来自 `DateTimeOffset.Now`（本地偏移），而完成时刻是从
/// ActivityWatch 的事件推出来的，AW 返回 UTC，`DateTimeOffset.Parse` 原样留着 `+00:00`。
/// **修了一次没修干净，2026-07-28 又犯了一遍**：第一次只把 CLI 的渲染收口到一处，
/// App 那条日志没盖到——这恰好证明了「每个显示的地方各自记得转换」**不是一个能信的约定**。
///
/// v4 没有 AW，但**同一个形状的洞还在**：时间进 <c>samples.db</c> 时压成 Unix 秒
/// （不带时区），读回来时 <c>FromUnixTimeSeconds</c> 给的是 **UTC**。要是让它就这么流进
/// 判定和界面，症状就是 v3 那个——**指针位置差整整一个时区，而且不报错**。
///
/// 所以这里钉住的是 v3 最终采用的那个解法：**在边界上归一**。
/// <see cref="SampleStore"/> 每一个把 Unix 秒变回时刻的地方都当场 <c>ToLocalTime()</c>，
/// 于是 UTC 这个概念根本流不出存储层，其余代码一处都不用记得转换。
/// </summary>
public class ClockBoundaryTests
{
    private static SampleStore Memory() => SampleStore.Open(":memory:");

    /// <summary>
    /// 采样读回来必须是**本地时间**。
    ///
    /// ⚠️ 断言 <c>Offset</c> 而不只是断言时刻相等：<c>DateTimeOffset</c> 的 <c>==</c>
    /// 比的是**瞬间**，UTC 的 10:00+00:00 和本地的 18:00+08:00 是同一个瞬间、`==` 为真。
    /// 也就是说**只比相等的话，这个 bug 会从测试底下溜过去**——v3 栽的正是「看起来对」。
    /// </summary>
    [Fact]
    public void 从观测库读回来的时刻是本地时间()
    {
        using var db = Memory();
        var at = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));
        db.Write(at, "Code", "Round.cs", 0);

        var row = Assert.Single(db.Read(at, at.AddSeconds(1)));

        Assert.Equal(at, row.At);                                   // 同一个瞬间
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(at), row.At.Offset);   // **而且带的是本地偏移**
    }

    /// <summary>
    /// 崩溃后接回来的那一轮，起始时刻也必须是本地时间。
    ///
    /// ⚠️ 这一条比上一条更要命：<c>OpenRound</c> 的返回值直接决定**重建出来的环画在哪**。
    /// 偏了一个时区，指针和休息区就整体错位，而程序一句话都不会说。
    /// </summary>
    [Fact]
    public void 接回未结束的轮次时起始时刻也是本地时间()
    {
        using var db = Memory();
        var started = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));
        db.BeginRound(started, 25, ["编程"]);

        var open = db.OpenRound();

        Assert.NotNull(open);
        Assert.Equal(started, open!.Value.StartedAt);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(started), open.Value.StartedAt.Offset);
        Assert.Equal(25, open.Value.FocusMinutes);
        Assert.Equal(["编程"], open.Value.Goals);
    }

    /// <summary>
    /// 一轮的完成时刻**来自喂进去的那一拍**，不是从账本里推出来的。
    ///
    /// 这是 v3 第二版才补上的那个根上的窟窿：完成时刻一旦是「从记录里算出来的」，
    /// 账本被重写时它就会往回跳。v4 里 <see cref="Round"/> 只认调用方传进来的 `now`，
    /// **时间是参数不是环境**——这一条把那个保证钉住。
    /// </summary>
    [Fact]
    public void 达成时刻就是喂进去的那一拍()
    {
        var start = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));
        var rules = GoalRules.Parse("""{ "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } } }""");
        var round = new Round(start, focusMinutes: 1, ["编程"], rules);

        DateTimeOffset last = start;
        for (var i = 0; i < 60; i++)
        {
            last = start.AddSeconds(i);
            round.Observe(last, "Code", "Round.cs");
        }

        Assert.NotNull(round.AchievedAt);
        Assert.Equal(last, round.AchievedAt!.Value);
        Assert.Equal(start.Offset, round.AchievedAt.Value.Offset);
    }
}

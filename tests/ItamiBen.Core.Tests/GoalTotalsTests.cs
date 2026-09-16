using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

public class GoalTotalsTests
{
    [Fact]
    public void 读得出设计里那一行()
    {
        var t = GoalTotals.Parse("""{ "goals": { "编程": { "seconds": 57203 } } }""");
        Assert.Equal(57203, t["编程"]);
    }

    [Fact]
    public void 没记过的目标就是零()
        => Assert.Equal(0, GoalTotals.Parse("{}")["编程"]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ 这不是 json")]
    public void 文件读坏了就当空账本绝不把程序搞崩(string? json)
        => Assert.Empty(GoalTotals.Parse(json).Goals);

    [Fact]
    public void 一轮的成绩按目标加进来()
    {
        var t = GoalTotals.Parse("""{ "goals": { "编程": { "seconds": 100 } } }""");
        t.Add(new Dictionary<string, int> { ["编程"] = 20, ["读书"] = 5 });

        Assert.Equal(120, t["编程"]);
        Assert.Equal(5, t["读书"]);
    }

    [Fact]
    public void 只加不减()
    {
        var t = new GoalTotals();
        t.Add("编程", 10);
        t.Add("编程", -5);
        t.Add("编程", 0);
        Assert.Equal(10, t["编程"]);
    }

    /// <summary>
    /// 老 `during.json` 还读得回来——**这是迁移那一次唯一的入口**，
    /// 读错了就是几十上百个小时凭空消失（DECISIONS I13）。
    /// </summary>
    [Fact]
    public void 老的_during_json_还读得回来()
    {
        var t = GoalTotals.Parse("""
            { "goals": { "编程": { "seconds": 57203 }, "读书": { "seconds": 42 } } }
            """);

        Assert.Equal(57203, t["编程"]);
        Assert.Equal(42, t["读书"]);
        Assert.Equal(["编程", "读书"], t.Goals);
    }

    [Fact]
    public void 从库里读回来的读数()
    {
        var t = GoalTotals.Of(new Dictionary<string, long> { ["编程"] = 57203, ["读书"] = 0 });

        Assert.Equal(57203, t["编程"]);
        Assert.Equal(0, t["读书"]);
        // 0 秒的目标不开账：库里不该有它，读回来也不该凭空多一个
        Assert.Equal(["编程"], t.Goals);
    }

    [Fact]
    public void 落盘的就是屏幕上那个数()
    {
        // DECISIONS C5：显示 = 文件里的总数 + 本轮实时累计，写的就是同一个数
        var start = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));
        var r = new Round(start, 10, ["编程"], TestRules.Rules);
        for (var i = 0; i < 42; i++) r.Observe(start.AddSeconds(i), "Code", "Round.cs");

        var totals = GoalTotals.Parse("""{ "goals": { "编程": { "seconds": 1000 } } }""");
        var onScreen = totals["编程"] + r.FocusedSecondsByGoal["编程"];

        r.End(start.AddSeconds(42), EndReason.GaveUp);
        totals.Add(r.FocusedSecondsByGoal);

        Assert.Equal(onScreen, totals["编程"]);
    }
}

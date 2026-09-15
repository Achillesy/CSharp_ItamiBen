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

    [Fact]
    public void 写出来再读回去还是同一份账()
    {
        var t = new GoalTotals();
        t.Add("编程", 57203);
        t.Add("读书", 42);

        var back = GoalTotals.Parse(t.ToJson());
        Assert.Equal(57203, back["编程"]);
        Assert.Equal(42, back["读书"]);
    }

    [Fact]
    public void 中文目标名不转义因为这个文件是给人看的()
    {
        var t = new GoalTotals();
        t.Add("编程", 1);
        Assert.Contains("编程", t.ToJson());
    }

    [Fact]
    public void 目标顺序稳定这样diff才好看()
    {
        var t = new GoalTotals();
        t.Add("读书", 1);
        t.Add("编程", 1);
        Assert.Equal(t.ToJson(), GoalTotals.Parse(t.ToJson()).ToJson());
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

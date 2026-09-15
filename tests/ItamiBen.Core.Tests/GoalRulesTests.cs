using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

public class GoalRulesTests
{
    [Fact]
    public void 组内任意一条规则命中就算命中()
    {
        Assert.True(TestRules.Rules.Matches("编程", "Code", "Round.cs"));
        Assert.True(TestRules.Rules.Matches("编程", "Google Chrome", "Achillesy/ItamiBen - GitHub"));
    }

    [Fact]
    public void 一条规则里写了两边就两边都要中()
    {
        Assert.False(TestRules.Rules.Matches("编程", "Google Chrome", "淘宝"));
    }

    [Fact]
    public void 没写的那一边不设约束()
    {
        // App 写了、Title 没写 ⇒ 任何标题都行，包括读不到标题的空串
        Assert.True(TestRules.Rules.Matches("编程", "Code", ""));
    }

    [Fact]
    public void 读不到标题就是空串自然匹配不上写了标题的规则()
    {
        // DECISIONS C2：没有「宽容一下」的分支，读到什么就是什么
        Assert.False(TestRules.Rules.Matches("读书", "Preview", ""));
        Assert.True(TestRules.Rules.Matches("读书", "Preview", "曼昆经济学原理.pdf"));
    }

    [Fact]
    public void 禁用的目标既不可选也永不命中()
    {
        Assert.False(TestRules.Rules.IsSelectable("去年的目标"));
        Assert.DoesNotContain("去年的目标", TestRules.Rules.SelectableGoals);
        Assert.False(TestRules.Rules.Matches("去年的目标", "Xcode", ""));
    }

    [Fact]
    public void 可选目标的顺序就是文件里的书写顺序()
    {
        Assert.Equal(["编程", "读书"], TestRules.Rules.SelectableGoals);
    }

    [Fact]
    public void 不存在的目标不命中也不可选()
    {
        Assert.False(TestRules.Rules.IsSelectable("莫须有"));
        Assert.False(TestRules.Rules.Matches("莫须有", "Code", ""));
    }

    [Fact]
    public void 空规则的目标会匹配一切所以直接拒绝加载()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => GoalRules.Parse("""{ "Groups": { "摸鱼": { "Rules": [] } } }"""));
        Assert.Contains("matches everything", ex.Message);
    }

    [Fact]
    public void 两边都不写的规则会匹配一切所以直接拒绝加载()
    {
        Assert.Throws<InvalidDataException>(
            () => GoalRules.Parse("""{ "Groups": { "摸鱼": { "Rules": [ { } ] } } }"""));
    }

    [Fact]
    public void 正则写错了要指名道姓地报出来是哪个目标()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => GoalRules.Parse("""{ "Groups": { "编程": { "Rules": [ { "App": "^(Code$" } ] } } }"""));
        Assert.Contains("编程", ex.Message);
    }

    [Fact]
    public void 注释和尾逗号照收因为这个文件是用户手写的()
    {
        var rules = GoalRules.Parse("""
        {
          "Groups": {
            // 上个月加的
            "编程": { "Rules": [ { "App": "^Code$" }, ] },
          }
        }
        """);
        Assert.True(rules.Matches("编程", "Code", ""));
    }
}

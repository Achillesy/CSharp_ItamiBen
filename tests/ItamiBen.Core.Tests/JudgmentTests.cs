using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

public class JudgmentTests
{
    private static readonly string[] 编程 = ["编程"];
    private static readonly string[] 两个 = ["编程", "读书"];

    [Fact]
    public void 命中选中的目标就是专注并且记在那个目标头上()
    {
        var j = Judgment.Judge("Code", "Round.cs", 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.Focused, j.Outcome);
        Assert.Equal("编程", j.Goal);
        Assert.True(j.Sampled);
    }

    [Fact]
    public void 采到了但不命中就是跑偏()
    {
        var j = Judgment.Judge("Safari", "淘宝", 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.OffTask, j.Outcome);
        Assert.Null(j.Goal);
        Assert.True(j.Sampled);      // 跑偏也是「采到了」——红格的高度靠它
    }

    [Fact]
    public void 没勾选的目标即便命中规则也算跑偏()
    {
        var j = Judgment.Judge("Preview", "曼昆经济学原理.pdf", 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.OffTask, j.Outcome);
    }

    [Fact]
    public void 读不到app这一秒等于没采既不计入也不算跑偏()
    {
        // Windows 锁屏时 GetForegroundWindow() 返回 0；macOS 登录窗口没有 frontmostApplication
        var j = Judgment.Judge("", "", 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.Unread, j.Outcome);
        Assert.False(j.Sampled);
        Assert.False(j.Focused);
    }

    [Theory]
    [InlineData("ItamiBen")]        // macOS 的 localizedName
    [InlineData("ItamiBen.exe")]    // Windows 的进程名
    public void 前台是自己这一秒等于没采(string self)
    {
        var j = Judgment.Judge(self, "ItamiBen", 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.SelfExempt, j.Outcome);
        Assert.False(j.Sampled);
    }

    [Fact]
    public void 自身豁免压过用户写的规则()
    {
        // 用户偏要写一条匹配 ItamiBen 的规则：盯着钟面也换不来一秒专注
        var rules = GoalRules.Parse("""{ "Groups": { "刷钟面": { "Rules": [ { "App": "^ItamiBen" } ] } } }""");
        var j = Judgment.Judge("ItamiBen", "", ["刷钟面"], rules);
        Assert.Equal(SecondOutcome.SelfExempt, j.Outcome);
    }

    [Fact]
    public void 一秒同时命中多个目标时算给勾选顺序靠前的那个()
    {
        // 一秒只能算给一个目标，否则同一秒被两个目标各记一遍，总数虚高
        var rules = GoalRules.Parse("""
        {
          "Groups": {
            "编程": { "Rules": [ { "App": "^Code$" } ] },
            "读书": { "Rules": [ { "App": "^Code$" } ] }
          }
        }
        """);
        Assert.Equal("编程", Judgment.Judge("Code", "", ["编程", "读书"], rules).Goal);
        Assert.Equal("读书", Judgment.Judge("Code", "", ["读书", "编程"], rules).Goal);
    }

    [Fact]
    public void 勾了两个目标时命中任意一个都算专注()
    {
        Assert.Equal("读书", Judgment.Judge("Preview", "曼昆经济学原理.pdf", 两个, TestRules.Rules).Goal);
        Assert.Equal("编程", Judgment.Judge("Code", "Round.cs", 两个, TestRules.Rules).Goal);
    }
}

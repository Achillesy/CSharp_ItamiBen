using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

public class JudgmentTests
{
    private static readonly string[] 编程 = ["编程"];
    private static readonly string[] 两个 = ["编程", "读书"];

    [Fact]
    public void 命中选中的目标就是专注并且记在那个目标头上()
    {
        var j = Judgment.Judge("Code", "Round.cs", false, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.Focused, j.Outcome);
        Assert.Equal("编程", j.Goal);
        Assert.True(j.Sampled);
    }

    [Fact]
    public void 采到了但不命中就是跑偏()
    {
        var j = Judgment.Judge("Safari", "淘宝", false, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.OffTask, j.Outcome);
        Assert.Null(j.Goal);
        Assert.True(j.Sampled);      // 跑偏也是「采到了」——红格的高度靠它
    }

    [Fact]
    public void 没勾选的目标即便命中规则也算跑偏()
    {
        var j = Judgment.Judge("Preview", "曼昆经济学原理.pdf", false, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.OffTask, j.Outcome);
    }

    [Fact]
    public void 读不到app这一秒等于没采既不计入也不算跑偏()
    {
        // Windows 锁屏时 GetForegroundWindow() 返回 0；macOS 登录窗口没有 frontmostApplication
        var j = Judgment.Judge("", "", false, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.Unread, j.Outcome);
        Assert.False(j.Sampled);
        Assert.False(j.Focused);
    }

    [Theory]
    [InlineData("ItamiBen")]        // macOS 的 localizedName
    [InlineData("ItamiBen.exe")]    // Windows 的进程名
    public void 前台是自己没有任何特例(string self)
    {
        // 2026-09-16 用户拍板删掉自身豁免（C9）：盯着自己的钟面就是跑偏，
        // 跟盯着别的什么一样。这条测试守着「别又把特例加回来」。
        var j = Judgment.Judge(self, "ItamiBen", false, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.OffTask, j.Outcome);
        Assert.True(j.Sampled);
    }

    [Fact]
    public void 想让盯钟面算专注就自己写条规则()
    {
        // 删掉硬编码豁免之后，这件事回到 rules.md 里——是配置，不是特例（v3 的做法）
        var rules = GoalRules.Parse("""{ "Groups": { "刷钟面": { "Rules": [ { "App": "^ItamiBen" } ] } } }""");
        var j = Judgment.Judge("ItamiBen", "", false, ["刷钟面"], rules);
        Assert.Equal(SecondOutcome.Focused, j.Outcome);
        Assert.Equal("刷钟面", j.Goal);
    }

    [Fact]
    public void 人不在时这一秒等于没采()
    {
        var j = Judgment.Judge("Code", "Round.cs", away: true, 编程, TestRules.Rules);
        Assert.Equal(SecondOutcome.Away, j.Outcome);
        Assert.False(j.Sampled);
        Assert.False(j.Focused);
    }

    [Fact]
    public void 人不在压过屏幕上是什么()
    {
        // macOS 锁屏读到的是 loginwindow（实测），一行一判会把锁屏画成红格
        Assert.Equal(SecondOutcome.Away, Judgment.Judge("loginwindow", "Login", true, 编程, TestRules.Rules).Outcome);
        Assert.Equal(SecondOutcome.OffTask, Judgment.Judge("loginwindow", "Login", false, 编程, TestRules.Rules).Outcome);
    }

    [Fact]
    public void 人不在压过读不到和前台是自己()
    {
        // Away 排在最前面：连「前台是 ItamiBen 自己」都盖过去
        Assert.Equal(SecondOutcome.Away, Judgment.Judge("ItamiBen", "", true, 编程, TestRules.Rules).Outcome);
        Assert.Equal(SecondOutcome.Away, Judgment.Judge("", "", true, 编程, TestRules.Rules).Outcome);
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
        Assert.Equal("编程", Judgment.Judge("Code", "", false, ["编程", "读书"], rules).Goal);
        Assert.Equal("读书", Judgment.Judge("Code", "", false, ["读书", "编程"], rules).Goal);
    }

    [Fact]
    public void 勾了两个目标时命中任意一个都算专注()
    {
        Assert.Equal("读书", Judgment.Judge("Preview", "曼昆经济学原理.pdf", false, 两个, TestRules.Rules).Goal);
        Assert.Equal("编程", Judgment.Judge("Code", "Round.cs", false, 两个, TestRules.Rules).Goal);
    }
}

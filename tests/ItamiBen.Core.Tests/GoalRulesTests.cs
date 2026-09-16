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

    // ── 命令清单（2026-09-16 起住在库里，JSON 那半只剩迁移，DECISIONS I15）────

    [Fact]
    public void 库里的命令按名字取_只给本系统那一条()
    {
        var rules = GoalRules.Of(
            [new SampleStore.GoalRow("Coding", true, [new SampleStore.RuleRow("^Code$", null)])],
            [new SampleStore.CommandRow("sleep", "pmset displaysleepnow", "rundll32.exe …")]);

        var expected = OperatingSystem.IsWindows() ? "rundll32.exe …" : "pmset displaysleepnow";
        Assert.Equal(expected, rules.CommandNamed("sleep"));
        Assert.Equal(["sleep"], rules.CommandNames);
    }

    [Fact]
    public void 名字找不到或没给本系统写就是_null_不猜()
    {
        // 只给了另一个系统的那条：本系统上就是「没有」，不退而求其次
        var other = OperatingSystem.IsWindows()
            ? new SampleStore.CommandRow("x", "只有 macOS 的", null)
            : new SampleStore.CommandRow("x", null, "只有 Windows 的");
        var rules = GoalRules.Of([], [other]);

        Assert.Null(rules.CommandNamed("x"));
        Assert.Null(rules.CommandNamed("莫须有"));
        Assert.Null(rules.CommandNamed(null));
    }

    [Fact]
    public void 迁移把老清单的第零条搬成一条叫_alarm_的命令()
    {
        // ⚠️ 老文件是**无名的有序清单、只跑第 0 条**；库里按名字引用。
        //    其余几条留在改名后的旧文件里，不会凭空消失。
        var rules = GoalRules.Parse("""
        { "Groups": {},
          "executeCommand": { "macos": ["第零条", "第一条"], "windows": ["w0", "w1"] } }
        """);

        Assert.Equal(["alarm"], rules.CommandNames);
        Assert.Equal(OperatingSystem.IsWindows() ? "w0" : "第零条", rules.CommandNamed("alarm"));
    }

    [Fact]
    public void 迁移能把规则原样摊回成库里的行()
    {
        // ⚠️ 正则原文从 Regex.ToString() 取回来——迁移**不需要第二条解析路径**
        var (goals, _) = GoalRules.Parse("""
        { "Groups": { "编程": { "Rules": [ { "App": "^Code$", "Title": "GitHub" } ] },
                      "停用的": { "Disabled": true, "Rules": [ { "Title": "x" } ] } } }
        """).ToRows();

        Assert.Equal(2, goals.Count);
        Assert.True(goals[0].Enabled);
        Assert.Equal("^Code$", goals[0].Rules[0].App);
        Assert.Equal("GitHub", goals[0].Rules[0].Title);
        Assert.False(goals[1].Enabled);
        Assert.Null(goals[1].Rules[0].App);
    }


    [Fact]
    public void 命令里带双引号的也原样存得住()
    {
        // ⚠️ 默认那几条 macOS 命令自己就带双引号，而 v3 曾经因为把它拼进
        //    `sh -c "..."` 而把脚本截断（它的 L1）——存这一步先不能出错
        var rules = GoalRules.Parse("""
        { "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } },
          "executeCommand": { "macos": "osascript -e 'tell application \"System Events\" to sleep'" } }
        """);
        var mac = rules.ToRows().Commands.Single(c => c.Name == "alarm").MacOS;
        Assert.Contains("\"System Events\"", mac);
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

/// <summary>
/// `rules.json` 里那两个外观开关（2026-09-16 从 layout.json 并进来，DECISIONS I14）。
///
/// ⚠️ 要紧的不是「读得对」，是**读错一个字段不能把整份规则带走**：
/// 这是用户手写的文件，而目标列表就在同一份文件里。
/// </summary>
public class RulesLayoutTests
{
    [Fact]
    public void 档位和透明度跟规则写在同一份文件里()
    {
        var r = GoalRules.Parse("""
            {
              // 上个月调的
              "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } },
              "LAYOUT": "compact",
              "Opacity": 40,
            }
            """);

        // ⚠️ 手写文件的三件套（注释 / 尾逗号 / 键名大小写）少认一样，
        //    就是「写了注释就静默失效」那类事故
        Assert.Equal("compact", r.LayoutName);
        Assert.Equal(40, r.OpacityPercent);
        Assert.Equal(["编程"], r.SelectableGoals);
    }

    [Fact]
    public void 带引号的数字也认()
        // 手写 JSON，写成 "50" 完全可能
        => Assert.Equal(50, GoalRules.Parse("""{ "Groups": {}, "opacity": "50" }""").OpacityPercent);

    [Fact]
    public void 透明度写错不牵连档位也不牵连目标()
    {
        // ⚠️ 这就是 opacity 声明成 JsonElement 而不是 double? 的全部理由：
        //    声明成 double? 的话整份反序列化当场抛，**连 Groups 都跟着丢**——
        //    那就成了「改了个外观开关，所有目标都不见了」
        var r = GoalRules.Parse("""
            {
              "Groups": { "编程": { "Rules": [ { "App": "^Code$" } ] } },
              "layout": "compact",
              "opacity": "写错了"
            }
            """);

        Assert.Null(r.OpacityPercent);
        Assert.Equal("compact", r.LayoutName);
        Assert.Equal(["编程"], r.SelectableGoals);
    }

    [Fact]
    public void 两个都没写就是没写()
    {
        var r = GoalRules.Parse("""{ "Groups": {} }""");
        Assert.Null(r.LayoutName);
        Assert.Null(r.OpacityPercent);
    }
}

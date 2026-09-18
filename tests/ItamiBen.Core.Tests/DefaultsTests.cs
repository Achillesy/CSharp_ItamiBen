using System.Text.Json;
using Xunit;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 出厂默认配置的**防漂测试**。
///
/// ⚠️ 默认值原来是 `Config.EnsureSeeded` 在代码里播种的，那样有个性质：
/// **形状永远跟代码一致，不可能漂**。改成「随安装包发一份文件」之后这个性质没了，
/// 这几个测试就是把它买回来——拿**真解析器**读出厂文件，漂了当场红。
///
/// ⚠️ 别把这里改成「读一份测试自己写的样例」：那样测的是测试自己，
/// 而真正会被用户装上的那几个文件照样可以烂掉。
/// </summary>
public class DefaultsTests
{
    /// <summary>读出厂参考件里**标记过的那个配置块**——跟程序走的是同一条路。</summary>
    private static string Read(string name)
        => MarkdownConfig.Extract(
               File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "defaults", name)));

    [Fact]
    public void 出厂的_rules_真解析器读得懂_而且至少留一个可选目标()
    {
        var rules = GoalRules.Parse(Read("rules.sample.md"));

        // ⚠️ 一个可选目标都没有的话，全新安装是一台**按不下 Start** 的钟
        Assert.NotEmpty(rules.SelectableGoals);

        // 默认那条认 ItamiBen 自己：装完就能按 Start 看见绿色，它同时是「规则长什么样」的样例。
        // ⚠️ 两个平台的名字都要中——这正是出厂文件该示范的写法
        var goal = rules.SelectableGoals[0];
        Assert.True(rules.Matches(goal, "ItamiBen", ""));
        Assert.True(rules.Matches(goal, "ItamiBen.exe", ""));
    }

    [Fact]
    public void 出厂的_schedule_读得懂_而且一条都不生效()
    {
        // ⚠️ **样例不该有副作用**（跟库里那条默认 schedule 设成 enabled=0 是同一条理由）：
        //    用户没要求过的东西，不该装完就每小时打扰一次。
        //    crontab 里「停用」的惯用法就是注释掉，所以这里应当解析出 0 条。
        Assert.Empty(AlarmsList.Parse(Read("schedule.sample.md")));
    }

    [Fact]
    public void 出厂的_commands_闹钟绑的命令真的存在()
    {
        using var doc = JsonDocument.Parse(Read("commands.sample.md"));
        var root = doc.RootElement;

        var names = root.GetProperty("Commands").EnumerateObject().Select(p => p.Name).ToList();
        Assert.NotEmpty(names);

        // ⚠️ 命令名就是编号，规矩是 ^[a-z0-9-]+$：它要作为裸词被 schedule.cron 引用
        Assert.All(names, n => Assert.Matches("^[a-z0-9-]+$", n));

        // ⚠️ **跨字段引用要对得上**：Alarm 指的名字必须在 Commands 里真有一条。
        //    指了个不存在的名字不会报错，只会到点什么都不发生。
        foreach (var os in new[] { "MacOS", "Windows" })
        {
            var bound = root.GetProperty("Alarm").GetProperty(os).GetString();
            if (!string.IsNullOrEmpty(bound)) Assert.Contains(bound, names);
        }
    }

    [Fact]
    public void 出厂的_layout_两个键都在()
    {
        using var doc = JsonDocument.Parse(Read("layout.sample.md"));
        Assert.Contains(doc.RootElement.GetProperty("Layout").GetString(),
                        new[] { "standard", "compact" });
        var p = doc.RootElement.GetProperty("OpacityPercent").GetDouble();
        Assert.InRange(p, 10, 100);
    }
}

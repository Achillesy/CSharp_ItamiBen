using System.Text.Json;
using Xunit;

namespace ItamiBen.Core.Tests;

public class CommandTableTests
{
    private const string Json = """
    {
      "Alarm": { "MacOS": "sleep", "Windows": "restart" },
      "Commands": {
        "sleep":   { "MacOS": "pmset sleepnow", "Windows": "rundll32.exe powrprof.dll,SetSuspendState 0,1,0" },
        "restart": { "MacOS": "osascript -e 'tell application \"System Events\" to restart'",
                     "Windows": "shutdown /r /t 0", "Note": "untested" },
        "mac-only": { "MacOS": "say hello" }
      }
    }
    """;

    [Fact]
    public void 按名字取到的是本机那一条()
    {
        var t = CommandTable.Parse(Json);
        var expected = OperatingSystem.IsWindows()
            ? "rundll32.exe powrprof.dll,SetSuspendState 0,1,0" : "pmset sleepnow";

        Assert.Equal(expected, t.TextFor("sleep"));
    }

    [Fact]
    public void 只给了另一个系统的那条_在本机就是没有_不退而求其次()
    {
        // ⚠️ 返回 null，**不是**掉头去用另一个系统那条。调用方拿 null 当「没有」处理
        var t = CommandTable.Parse(Json);
        if (OperatingSystem.IsWindows()) Assert.Null(t.TextFor("mac-only"));
        else Assert.Equal("say hello", t.TextFor("mac-only"));
    }

    [Fact]
    public void 闹钟绑定是分系统的_两台机器可以指向不同的命令()
    {
        // ⚠️ 命令名保持语义纯净（sleep 两边都是睡眠），分歧放在**绑定**上
        var t = CommandTable.Parse(Json);
        Assert.Equal(OperatingSystem.IsWindows() ? "restart" : "sleep", t.AlarmName);
    }

    [Fact]
    public void 名字不存在_或者传_null_一律是没有()
    {
        var t = CommandTable.Parse(Json);
        Assert.Null(t.TextFor("does-not-exist"));
        Assert.Null(t.TextFor(null));
    }

    [Fact]
    public void 带注释或尾逗号的_JSON_直接失败_不宽容()
    {
        // ⚠️ 宽松解析的代价是「在别的工具里报错、在这儿不报错」。
        //    配置文件要能被任何 JSON 工具打开，那就得跟它们一样严。
        Assert.ThrowsAny<JsonException>(() => CommandTable.Parse("""{ "Commands": {} , }"""));
        Assert.ThrowsAny<JsonException>(() => CommandTable.Parse("""{ /* hi */ "Commands": {} }"""));
    }

    [Fact]
    public void 出厂那份读得出五条_而且闹钟绑的那条真的在里面()
    {
        var t = CommandTable.Parse(
            MarkdownConfig.Extract(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "defaults", "commands.sample.md"))));

        Assert.Equal(5, t.Names.Count);
        Assert.Equal("open-config", t.AlarmName);
        Assert.NotNull(t.TextFor(t.AlarmName));   // 绑的名字在本机有正文
    }
}

public class LayoutFileTests
{
    [Fact]
    public void 两个键都读得出来()
    {
        var f = LayoutFile.Parse("""{ "Layout": "compact", "OpacityPercent": 60 }""");
        Assert.Equal("compact", f.Layout);
        Assert.Equal(60, f.OpacityPercent);
    }

    [Fact]
    public void 缺键就是_null_由解释那一层去定默认()
    {
        // ⚠️ 这里只读不解释：越界怎么办、缺了用什么，归 WindowLayout 一处说了算
        var f = LayoutFile.Parse("{}");
        Assert.Null(f.Layout);
        Assert.Null(f.OpacityPercent);
    }

    [Fact]
    public void 出厂那份是_standard_和_90_跟代码里的默认值一致()
    {
        var f = LayoutFile.Parse(
            MarkdownConfig.Extract(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "defaults", "layout.sample.md"))));
        Assert.Equal("standard", f.Layout);
        Assert.Equal(90, f.OpacityPercent);
    }
}

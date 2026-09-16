using ItamiBen.App;

namespace ItamiBen.App.Tests;

/// <summary>
/// `layout.json` 的解析。两个函数都是纯的（文件读取在外面），所以不碰磁盘。
/// </summary>
public class WindowLayoutTests
{
    [Theory]
    [InlineData("""{ "layout": "compact" }""", LayoutMode.Compact)]
    [InlineData("""{ "layout": "COMPACT" }""", LayoutMode.Compact)]
    [InlineData("""{ "layout": "standard" }""", LayoutMode.Standard)]
    [InlineData("""{ "layout": "莫须有" }""", LayoutMode.Standard)]
    [InlineData("""{ }""", LayoutMode.Standard)]
    [InlineData(null, LayoutMode.Standard)]
    [InlineData("这不是 json", LayoutMode.Standard)]
    public void 认不出的档位一律退回标准档(string? json, LayoutMode expected)
        => Assert.Equal(expected, WindowLayout.ParseMode(json));

    [Fact]
    public void 手写文件的三件套都要认_注释尾逗号大小写()
    {
        // ⚠️ 少认一样就是「写了注释就静默失效」那类事故
        var json = """
        {
          // 上个月调的
          "LAYOUT": "compact",
          "Opacity": 40,
        }
        """;
        Assert.Equal(LayoutMode.Compact, WindowLayout.ParseMode(json));
        Assert.Equal(0.40, WindowLayout.ParseOpacity(json), 3);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(35)]
    [InlineData(90)]
    [InlineData(100)]
    public void 范围内的百分数原样生效(double pct)
        => Assert.Equal(pct / 100.0, WindowLayout.ParseOpacity($$"""{ "opacity": {{pct}} }"""), 3);

    [Theory]
    [InlineData("""{ "opacity": 5 }""")]        // 低于下限
    [InlineData("""{ "opacity": 120 }""")]      // 高于上限
    [InlineData("""{ "opacity": true }""")]     // 类型不对
    [InlineData("""{ }""")]                     // 没写
    [InlineData(null)]
    public void 写错的透明度强制回九十而不是夹到边界(string? json)
    {
        // ⚠️ 夹到边界会让「我写了 5」和「我写了 10」看起来一样——
        //    用户以为生效了，其实是被悄悄改掉的
        Assert.Equal(WindowLayout.DefaultOpacityPercent / 100.0, WindowLayout.ParseOpacity(json), 3);
    }

    [Fact]
    public void 带引号的数字也认()
    {
        // 手写 JSON，写成 "50" 完全可能
        Assert.Equal(0.50, WindowLayout.ParseOpacity("""{ "opacity": "50" }"""), 3);
    }

    [Fact]
    public void 一个字段写错不牵连另一个()
    {
        // ⚠️ 这就是 opacity 声明成 JsonElement 而不是 double? 的全部理由：
        //    声明成 double? 的话反序列化整个抛异常，连 layout 那一档也跟着丢
        var json = """{ "layout": "compact", "opacity": "写错了" }""";
        Assert.Equal(LayoutMode.Compact, WindowLayout.ParseMode(json));
        Assert.Equal(WindowLayout.DefaultOpacityPercent / 100.0, WindowLayout.ParseOpacity(json), 3);
    }
}

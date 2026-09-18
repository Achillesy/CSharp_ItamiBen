using Xunit;

namespace ItamiBen.Core.Tests;

/// <summary>
/// `--query apps` / `--query titles` 背后的两条查询。它们存在的理由只有一个：
/// **写 `App` / `Title` 正则时不用猜**——照着这台机器真实见过的名字抄。
/// </summary>
public class SeenNamesTests
{
    private static SampleStore Filled()
    {
        var db = SampleStore.Open(":memory:");
        var t = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(8));
        for (var i = 0; i < 5; i++) db.Write(t.AddSeconds(i), "Code", "main.cs", 0);
        for (var i = 5; i < 8; i++) db.Write(t.AddSeconds(i), "Google Chrome", "GitHub", 0);
        db.Write(t.AddDays(1), "Obsidian", "笔记", 0);       // 第二天，另一个程序
        return db;
    }

    [Fact]
    public void 程序名按观测秒数排_多的在前()
    {
        using var db = Filled();
        var rows = db.AppNames();

        Assert.Equal(["Code", "Google Chrome", "Obsidian"], rows.Select(r => r.Text));
        Assert.Equal(5, rows[0].Seconds);
        Assert.Equal(3, rows[1].Seconds);
    }

    [Fact]
    public void 程序名不受时间区间约束_列的是有史以来的全部()
    {
        // ⚠️ 「这个程序在这台机器上叫什么」是跟今天无关的事实。
        //    第二天才出现的 Obsidian 也必须在列表里。
        using var db = Filled();
        Assert.Contains("Obsidian", db.AppNames().Select(r => r.Text));
    }

    [Fact]
    public void 标题受区间约束_查得到也查得漏()
    {
        using var db = Filled();
        var day1 = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.FromHours(8));

        var first = db.TitleTexts(day1, day1.AddDays(1)).Select(r => r.Text).ToList();
        Assert.Equal(["main.cs", "GitHub"], first);
        Assert.DoesNotContain("笔记", first);              // 第二天的，不在这个区间

        Assert.Contains("笔记", db.TitleTexts(day1, day1.AddDays(2)).Select(r => r.Text));
    }

    [Fact]
    public void 最后一次见到的时刻是对的()
    {
        using var db = Filled();
        var code = db.AppNames().First(r => r.Text == "Code");
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 10, 0, 4, TimeSpan.FromHours(8)), code.Last);
    }
}

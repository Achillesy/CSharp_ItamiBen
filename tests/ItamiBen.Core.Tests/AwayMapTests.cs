using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

public class AwayMapTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static Observation Row(int second, int idle, string app = "Code")
        => new(T0.AddSeconds(second), app, "Round.cs", idle);

    [Fact]
    public void 没到门槛不算离开()
    {
        // ⚠️ 179 秒不动**不算离开**——那是在看内容
        var map = AwayMap.Of(Enumerable.Range(0, 180).Select(i => Row(i, i)));
        Assert.Empty(map.Spans);
    }

    [Fact]
    public void 跨过门槛之后回溯到输入停止那一刻()
    {
        // 第 180 秒时 idle=180 ⇒ 最后一次输入在第 0 秒（那一秒人还在）⇒ 离开是 [1, 180]
        var map = AwayMap.Of(Enumerable.Range(0, 181).Select(i => Row(i, i)));

        var span = Assert.Single(map.Spans);
        Assert.Equal(T0.AddSeconds(1), span.From);
        Assert.Equal(T0.AddSeconds(180), span.To);
        Assert.False(map.Covers(T0));                     // 最后一次输入那一秒，人在
        Assert.True(map.Covers(T0.AddSeconds(90)));       // 门槛跨过之前的秒也被追认
        Assert.True(map.Covers(T0.AddSeconds(180)));
        Assert.False(map.Covers(T0.AddSeconds(181)));
    }

    [Fact]
    public void 同一次离开合并成一段()
    {
        // idle 在一次离开里是单调增的，最后那行的区间覆盖整段
        var map = AwayMap.Of(Enumerable.Range(0, 600).Select(i => Row(i, i)));

        var span = Assert.Single(map.Spans);
        Assert.Equal(T0.AddSeconds(1), span.From);
        Assert.Equal(T0.AddSeconds(599), span.To);
    }

    [Fact]
    public void 中间回来动过一下就是两段()
    {
        var rows = new List<Observation>();
        for (var i = 0; i < 200; i++) rows.Add(Row(i, i));                  // 第一次离开
        for (var i = 200; i < 600; i++) rows.Add(Row(i, i - 200));          // 第 200 秒动了一下，然后又离开

        var map = AwayMap.Of(rows);
        Assert.Equal(2, map.Spans.Count);

        // ⚠️ **只有真正敲过键盘的那一秒才算人在**。第 250 秒当时 idle 才 50、没跨门槛，
        //    但那之后再没动过，所以它**被追认为离开**——这正是回溯语义本身，
        //    不是 bug。少了这条追认，锁屏前那 180 秒就会留下一截红格
        Assert.False(map.Covers(T0.AddSeconds(200)));
        Assert.True(map.Covers(T0.AddSeconds(250)));
        Assert.True(map.Covers(T0.AddSeconds(500)));
        Assert.Equal(T0.AddSeconds(201), map.Spans[1].From);
    }

    [Fact]
    public void 锁屏那一小时不该算跑偏()
    {
        // 2026-09-16 实测：macOS 锁屏时前台读到的是 loginwindow / Login，**不是空字符串**。
        // 一行一判的话这一小时全是红格——v3 3.9.x 那个大 bug 的形状
        var rows = Enumerable.Range(0, 3600).Select(i => Row(i, i, "loginwindow")).ToList();
        var map = AwayMap.Of(rows);

        var span = Assert.Single(map.Spans);
        Assert.Equal(T0.AddSeconds(1), span.From);
        Assert.True(map.Covers(T0.AddSeconds(10)));      // 开头那 180 秒也被追认
        Assert.True(map.Covers(T0.AddSeconds(3599)));
    }

    [Fact]
    public void 空列表就是空的()
    {
        Assert.Empty(AwayMap.Of([]).Spans);
        Assert.False(AwayMap.Empty.Covers(T0));
    }
}

using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 录制器。前台窗口和空闲都是委托，所以这里一次平台调用都不做。
/// </summary>
public class RecorderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    /// <summary>记一下前台窗口被读了几次——「同一秒别读第二遍」是可观测的行为，不是内部细节。</summary>
    private sealed class Probe
    {
        public int Reads;
        public string App = "Code";
        public string Title = "Round.cs";
        public int Idle;

        public (string, string) Read() { Reads++; return (App, Title); }
    }

    [Fact]
    public void 一秒写一行()
    {
        using var db = SampleStore.Open(":memory:");
        var probe = new Probe();
        var rec = new Recorder(db, probe.Read, () => probe.Idle);

        for (var i = 0; i < 5; i++) Assert.True(rec.Record(T0.AddSeconds(i)));
        Assert.Equal(5, db.Counts.Samples);
    }

    [Fact]
    public void 同一秒调十次只写一行而且只读一次前台()
    {
        // ⚠️ 采样是 100ms 一拍，一秒会调十次。读标题要走 AX 同步 IPC，
        //    一秒做十遍是实打实的浪费——所以这条是行为要求，不是优化
        using var db = SampleStore.Open(":memory:");
        var probe = new Probe();
        var rec = new Recorder(db, probe.Read, () => probe.Idle);

        Assert.True(rec.Record(T0));
        for (var i = 1; i < 10; i++) Assert.False(rec.Record(T0.AddMilliseconds(i * 100)));

        Assert.Equal(1, db.Counts.Samples);
        Assert.Equal(1, probe.Reads);
    }

    [Fact]
    public void 漏掉的拍永远空着绝不补记()
    {
        using var db = SampleStore.Open(":memory:");
        var rec = new Recorder(db, new Probe().Read, () => 0);

        rec.Record(T0);
        rec.Record(T0.AddSeconds(30));      // 中间 29 秒没调

        Assert.Equal(2, db.Counts.Samples);
    }

    [Fact]
    public void 读不到前台窗口就写空不猜也不沿用上一次()
    {
        // DECISIONS B2：沿用上一次的标题「凑合一下」正是 v3 把全屏 mame 判成「学习经济学」的那个动作
        using var db = SampleStore.Open(":memory:");
        var probe = new Probe();
        var rec = new Recorder(db, probe.Read, () => probe.Idle);

        rec.Record(T0);
        probe.App = "";
        probe.Title = "";
        rec.Record(T0.AddSeconds(1));

        var rows = db.Read(T0, T0.AddSeconds(2));
        Assert.Equal("Code", rows[0].App);
        Assert.Equal("", rows[1].App);
        Assert.Equal("", rows[1].Title);
    }

    [Fact]
    public void 空闲秒数原样落库不在写入时算afk()
    {
        // ⚠️ 门槛是事后才跨过的：写入时就编码成 afk，前面那 180 秒永远找不回来
        using var db = SampleStore.Open(":memory:");
        var probe = new Probe();
        var rec = new Recorder(db, probe.Read, () => probe.Idle);

        for (var i = 0; i < 200; i++)
        {
            probe.Idle = i;
            rec.Record(T0.AddSeconds(i));
        }

        var rows = db.Read(T0, T0.AddSeconds(200));
        Assert.Equal(0, rows[0].IdleSeconds);
        Assert.Equal(179, rows[179].IdleSeconds);       // 还没跨门槛
        Assert.Equal(180, rows[180].IdleSeconds);       // 正好跨过
        Assert.Equal(199, rows[199].IdleSeconds);
    }

    [Fact]
    public void 时钟往回跳不会写坏已有的行()
    {
        using var db = SampleStore.Open(":memory:");
        var probe = new Probe();
        var rec = new Recorder(db, probe.Read, () => probe.Idle);

        rec.Record(T0);
        probe.App = "Safari";
        rec.Record(T0.AddSeconds(-5));      // 对时 / 夏令时：往回跳
        rec.Record(T0);                     // 又回到已经写过的那一秒

        Assert.Equal(2, db.Counts.Samples);
        Assert.Equal("Code", db.Read(T0, T0.AddSeconds(1))[0].App);   // 先到的那条没被改写
    }
}

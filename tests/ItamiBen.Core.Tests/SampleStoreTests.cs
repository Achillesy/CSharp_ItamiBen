using ItamiBen.Core;

namespace ItamiBen.Core.Tests;

/// <summary>
/// 观测库。⚠️ 一律开内存库——**测试碰不到用户真实的 samples.db**
/// （v3 的 I5：单测把用户的账本冲成了测试数据，而且悄无声息）。
/// </summary>
public class SampleStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static SampleStore Memory() => SampleStore.Open(":memory:");

    [Fact]
    public void 写进去的就是读回来的()
    {
        using var db = Memory();
        db.Write(T0, "Code", "Round.cs", 0);

        var rows = db.Read(T0, T0.AddSeconds(1));
        var row = Assert.Single(rows);
        Assert.Equal(T0, row.At);
        Assert.Equal("Code", row.App);
        Assert.Equal("Round.cs", row.Title);
        Assert.Equal(0, row.IdleSeconds);
    }

    [Fact]
    public void 同一秒写两次只留一行()
    {
        using var db = Memory();
        Assert.True(db.Write(T0, "Code", "Round.cs", 0));
        Assert.False(db.Write(T0, "Safari", "淘宝", 99));      // 主键挡住，**先到的那条不被改写**

        var row = Assert.Single(db.Read(T0, T0.AddSeconds(1)));
        Assert.Equal("Code", row.App);
        Assert.Equal(0, row.IdleSeconds);
    }

    [Fact]
    public void 秒内的小数部分被抹掉所以同一秒还是同一行()
    {
        using var db = Memory();
        Assert.True(db.Write(T0.AddMilliseconds(120), "Code", "", 0));
        Assert.False(db.Write(T0.AddMilliseconds(870), "Code", "", 0));
        Assert.Equal(1, db.Counts.Samples);
    }

    [Fact]
    public void 读不到前台窗口时存空读回来也是空()
    {
        using var db = Memory();
        db.Write(T0, "", "", 12);

        var row = Assert.Single(db.Read(T0, T0.AddSeconds(1)));
        Assert.Equal("", row.App);
        Assert.Equal("", row.Title);
        Assert.Equal(12, row.IdleSeconds);
    }

    [Fact]
    public void 区间是左闭右开()
    {
        using var db = Memory();
        for (var i = 0; i < 5; i++) db.Write(T0.AddSeconds(i), "Code", "", 0);

        Assert.Equal(3, db.Read(T0.AddSeconds(1), T0.AddSeconds(4)).Count);
        Assert.Empty(db.Read(T0, T0));
    }

    [Fact]
    public void 缺的秒就是缺着绝不补()
    {
        // ⚠️ 这是整个设计的地基：**缺一行 = 那一秒没人在看**，跟「看见了空闲」是两回事
        using var db = Memory();
        db.Write(T0, "Code", "", 0);
        db.Write(T0.AddSeconds(9), "Code", "", 0);

        var rows = db.Read(T0, T0.AddSeconds(10));
        Assert.Equal(2, rows.Count);
        Assert.Equal(9, (rows[1].At - rows[0].At).TotalSeconds);
    }

    [Fact]
    public void 读出来天然按时间序不用排序()
    {
        using var db = Memory();
        foreach (var i in new[] { 7, 2, 9, 0, 4 }) db.Write(T0.AddSeconds(i), "Code", "", 0);

        var rows = db.Read(T0, T0.AddSeconds(10));
        Assert.Equal([0, 2, 4, 7, 9], rows.Select(r => (int)(r.At - T0).TotalSeconds));
    }

    [Fact]
    public void 字符串去重六百行同一个标题只占一行()
    {
        // 这就是分表的全部理由：实测标题 67 字节，盯十分钟不去重是 40KB
        using var db = Memory();
        for (var i = 0; i < 600; i++)
            db.Write(T0.AddSeconds(i), "Google Chrome", "曼昆经济学原理-习题解析.pdf - Right view - Google Chrome", 0);

        var (apps, titles, samples) = db.Counts;
        Assert.Equal(1, apps);
        Assert.Equal(1, titles);
        Assert.Equal(600, samples);
    }

    [Fact]
    public void 端点查得到空库是null()
    {
        using var db = Memory();
        Assert.Null(db.Oldest);
        Assert.Null(db.Newest);

        db.Write(T0.AddSeconds(30), "Code", "", 0);
        db.Write(T0, "Code", "", 0);
        db.Write(T0.AddSeconds(10), "Code", "", 0);

        Assert.Equal(T0, db.Oldest);
        Assert.Equal(T0.AddSeconds(30), db.Newest);
    }

    [Fact]
    public void 时刻在边界上归一到本地时区()
    {
        // v3 栽过：AW 的 UTC 偏移一路流到显示层，日志打出「16:37:35 达成」而本地是 00:37:35
        using var db = Memory();
        db.Write(T0.ToUniversalTime(), "Code", "", 0);

        var row = Assert.Single(db.Read(T0.AddSeconds(-1), T0.AddSeconds(1)));
        Assert.Equal(DateTimeOffset.Now.Offset, row.At.Offset);
        Assert.Equal(T0, row.At);                       // 同一个瞬间
    }

    [Fact]
    public void 关掉再开数据还在()
    {
        var path = Path.Combine(Path.GetTempPath(), $"itamiben-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = SampleStore.Open(path)) db.Write(T0, "Code", "Round.cs", 0);
            using (var db = SampleStore.Open(path))
                Assert.Equal("Round.cs", Assert.Single(db.Read(T0, T0.AddSeconds(1))).Title);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                try { File.Delete(f); } catch { }
        }
    }
}

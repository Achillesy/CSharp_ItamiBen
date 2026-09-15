namespace ItamiBen.Core;

/// <summary>
/// **每秒往库里写一行。** 这就是「aw 模拟器」录的那一半——它取代了
/// `aw-watcher-window` 和 `aw-watcher-afk` 两个 watcher。
///
/// ⚠️ **一条线索，不是两条。** AW 那边窗口和 afk 是两个 watcher、两个桶、两套写入节奏，
/// 而它们节奏不同正是 v3 最后那个大 bug 的根因（窗口桶每 10 秒必跳、afk 桶能 35 秒不动，
/// 各自判断各自的 `last_updated`，于是「这一拍没取到 afk」就没人把红格盖回来）。
/// 这里一秒一行、一次写完，那个失败模式**结构上不存在**。
///
/// ⚠️ **不常驻。** 按下 Start 才开始录，专注达成就停。库是跨轮持久的，所以任何时候
/// 都能把整个环重建出来；但程序没跑的时间**永远是空白**，谁也补不回来
/// （DECISIONS A2，这是知情接受的代价）。
///
/// <b>前台窗口和键鼠空闲都是委托注进来的</b>——平台调用一律留在 App 层
/// （CLAUDE.md 的硬性约束），于是这个类本身不碰任何平台 API，直接可测。
/// </summary>
/// <param name="store">写到哪儿。</param>
/// <param name="foreground">读前台窗口。读不到就返回空串——**别猜、别沿用上一次**（DECISIONS B2）。</param>
/// <param name="idleSeconds">距上次键鼠输入多少秒。</param>
public sealed class Recorder(
    SampleStore store,
    Func<(string App, string Title)> foreground,
    Func<int> idleSeconds)
{
    /// <summary>已经写过的那一秒。**纯粹是省事用的**，不是正确性依赖——主键约束才是。</summary>
    private long _lastSecond = long.MinValue;

    /// <summary>
    /// 记下这一秒。**每一拍都可以调**（采样是 100ms 一拍），同一秒里的第 2~10 次
    /// 直接返回，连前台窗口都不去读——读标题要走 AX 同步 IPC，不该一秒做十遍。
    ///
    /// 漏掉的拍**永远空着，绝不补记**：那一秒确实没人在看，库里缺一行就是它诚实的样子。
    /// </summary>
    /// <returns>这一拍真的写了一行没有。</returns>
    public bool Record(DateTimeOffset now)
    {
        var second = now.ToUnixTimeSeconds();
        if (second == _lastSecond) return false;
        _lastSecond = second;

        var (app, title) = foreground();
        return store.Write(now, app, title, idleSeconds());
    }
}

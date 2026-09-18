namespace ItamiBen.App;

/// <summary>
/// 值得记一笔的事，写进 <see cref="Log"/> 的两份文本文件。
///
/// <b>两条规矩，按顺序判：</b>
/// <list type="number">
///   <item><b>能从 `sample` / `round` 推出来的，一行都不记。</b>
///   每秒的 app / 标题 / 空闲是 `sample` 的列；专注秒数、余量、每一格的构成、判定结果
///   全是它们的纯函数，而且重放幂等（`RebuildTests` 守着）。记了就是副本，
///   而副本迟早跟正本对不上。</item>
///   <item><b>别处留不下痕迹、而且值得注意的</b>才记：启动退出、闹钟响了、提醒到点了、
///   命令跑了（跑出什么退出码）、配置重装了、出错了。</item>
/// </list>
///
/// 正常跑完一轮，`error.log` **不该长出任何东西**。
/// </summary>
public static class Events
{
    private static readonly Lock Gate = new();

    /// <summary>同一句话在这段时间里只记一次。</summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最近记过的那几句，用来去重。
    ///
    /// ⚠️ **不是优化，是防洪**：<see cref="Sampler"/> 里那两个 catch 每拍都可能进来，
    /// 平台层一坏就是**一秒十条**——真正要看的那几行会被冲得找不着。
    /// </summary>
    private static readonly Dictionary<string, DateTimeOffset> Recent = [];

    public static void Info(string kind, string text) => Note("info", kind, text);

    public static void Warn(string kind, string text) => Note("warn", kind, text);

    public static void Error(string kind, string what, Exception e)
        => Note("error", kind, $"{what}: {e.GetType().Name} {e.Message}");

    private static void Note(string level, string kind, string text)
    {
        var now = DateTimeOffset.Now;
        lock (Gate)
        {
            var key = $"{level}|{kind}|{text}";
            if (Recent.TryGetValue(key, out var last) && now - last < Throttle) return;
            Recent[key] = now;

            // 去重表本身也不能无限长：过期的顺手清掉。条数很少，全扫一遍最省事
            if (Recent.Count > 64)
                foreach (var k in Recent.Where(kv => now - kv.Value >= Throttle).Select(kv => kv.Key).ToList())
                    Recent.Remove(k);
        }
        Log.Write(level, kind, text);
    }
}

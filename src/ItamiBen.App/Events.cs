using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 值得记一笔的事，**写进 `samples.db` 的 `event` 表**（2026-09-16 用户定的方向）。
///
/// 这个项目原来有一份文本日志，理由很硬：v3 的判定数据在 ActivityWatch 那边、本程序
/// 基本只读，日志**就是**唯一的现场记录。v4 自己就是记录者，**同一条理由反过来成立了**
/// ——数据库随时都在、随时能查，再单开一份文本就是第二个真相来源。
///
/// <b>三条规矩，按顺序判：</b>
/// <list type="number">
///   <item><b>能从 `sample` / `round` 推出来的，一行都不记。</b>
///   每秒的 app / 标题 / 空闲是 `sample` 的列；专注秒数、余量、每一格的构成、判定结果
///   全是它们的纯函数，而且重放幂等（`RebuildTests` 守着）。记了就是副本，
///   而副本迟早跟正本对不上。</item>
///   <item><b>别处留不下痕迹、而且值得注意的</b>，写进 `event` 表：闹钟响了、提醒到点了、
///   命令跑了（跑出什么退出码）、出错了。</item>
///   <item><b>够不着数据库的</b>（数据库自己打不开、被单实例挡回去、启动早期就崩了），
///   才落到那份**极小的**文本文件里（<see cref="Log"/>）。它不是日志，是**最后的求救信**。</item>
/// </list>
///
/// 正常跑完一轮，`event` 表和文本文件**都不该长出东西**。
/// </summary>
public static class Events
{
    private static readonly Lock Gate = new();
    private static SampleStore? _store;

    /// <summary>同一句话在这段时间里只记一次。</summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最近记过的那几句，用来去重。
    ///
    /// ⚠️ **不是优化，是防洪**：<see cref="Sampler"/> 里那两个 catch 每拍都可能进来，
    /// 平台层一坏就是**一秒十条**。数据库不像文本文件那样「写大了就滚存」——
    /// 灌进去的垃圾会一直躺在那儿，还把真正要看的那几行冲没。
    /// </summary>
    private static readonly Dictionary<string, DateTimeOffset> Recent = [];

    /// <summary>观测库开好之后挂上去。挂上之前的事都走 <see cref="Log"/>。</summary>
    public static void Bind(SampleStore store)
    {
        lock (Gate) _store = store;
    }

    /// <summary>库要关了。之后再有事就没处记了，落回文本。</summary>
    public static void Unbind()
    {
        lock (Gate) _store = null;
    }

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

            if (_store is { } store) store.Note(now, level, kind, text);
            else Log.Fallback($"{level} {kind}: {text}");   // 库还没开或已经关了
        }
    }
}

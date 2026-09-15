namespace ItamiBen.Core;

/// <summary>
/// **「人不在」的区间**，从每一行的原始 <see cref="Observation.IdleSeconds"/> 算出来。
///
/// ## 为什么必须跨行算，不能一行一判
///
/// 门槛是 180 秒，意味着**「人不在」永远是事后才知道的**：跨过门槛那一刻回头看，
/// 前面 180 秒其实一直没人。可那 180 行当时写下的 <c>idle</c> 分别是 1、2、……179，
/// 一行一判的话它们全都低于门槛，会被判成跑偏——**锁屏一小时画成一小时红格**，
/// 正是 v3 3.9.x 那个大 bug 的形状。
///
/// 所以离开区间的起点要**回溯到输入停止那一刻**（<c>at − idle</c>），而不是门槛跨过
/// 那一刻。这是 AW 的语义：v3 2026-09-13 实测过，本地算出 14:55:19、AW 那条 afk 事件
/// 的起点也是 14:55:19，两边独立得出同一个数。
///
/// ⚠️ **知情代价**：跨过门槛那一刻会**回溯改写**前 180 秒的格子——刚才还是红的会变成
/// 空白。这是语义不是 bug。实时那条路只能按当前这一秒判（最多红 180 秒），
/// **每分钟从库重建时会纠正过来**——这也正是重建存在的理由之一。
///
/// 纯逻辑，输入是观测列表，所以可测。
/// </summary>
public sealed class AwayMap
{
    /// <summary>
    /// 门槛 180 秒，跟 AW 的默认值一致。
    /// ⚠️ **179 秒不动不算离开**——那是在看内容。
    /// </summary>
    public const int ThresholdSeconds = 180;

    private readonly List<(DateTimeOffset From, DateTimeOffset To)> _spans;

    private AwayMap(List<(DateTimeOffset, DateTimeOffset)> spans) => _spans = spans;

    public static AwayMap Empty { get; } = new([]);

    /// <summary>区间（闭区间，两端那一秒也算离开）。按时间序，互不重叠。</summary>
    public IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Spans => _spans;

    /// <summary><paramref name="rows"/> 必须按时间序——<see cref="SampleStore.Read"/> 出来的天然就是。</summary>
    public static AwayMap Of(IEnumerable<Observation> rows, int thresholdSeconds = ThresholdSeconds)
    {
        var spans = new List<(DateTimeOffset From, DateTimeOffset To)>();

        foreach (var o in rows)
        {
            if (o.IdleSeconds < thresholdSeconds) continue;

            // ⚠️ 起点是 `at − idle + 1` 不是 `at − idle`：`idle = n` 的含义是
            //    「最后一次输入在 at − n」，**那一秒人是在的**，离开是从下一秒开始的。
            //    差这一秒不只是精度问题——它会让两次独立的离开（中间真的动过一下）
            //    被下面的相邻合并错误地并成一段。
            var from = o.At.AddSeconds(-o.IdleSeconds + 1);

            // 跟上一段接上或重叠就并进去。同一次离开里 idle 是单调增的，所以最后那一行
            // 的区间覆盖整段——合并之后自然只剩一段
            if (spans.Count > 0 && from <= spans[^1].To.AddSeconds(1))
                spans[^1] = (spans[^1].From, o.At);
            else
                spans.Add((from, o.At));
        }

        return new AwayMap(spans);
    }

    /// <summary>这一秒人在不在。区间很少（一轮通常 0~5 段），线性扫就够。</summary>
    public bool Covers(DateTimeOffset at)
    {
        foreach (var (from, to) in _spans)
            if (at >= from && at <= to) return true;
        return false;
    }
}

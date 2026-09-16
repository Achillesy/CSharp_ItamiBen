using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItamiBen.Core;

/// <summary>
/// 每个目标的累计专注秒数。
///
/// ⚠️ **账本本身住在 `samples.db` 的 `total` 表里**（2026-09-16 起，DECISIONS I13）；
/// 这个类只是它在内存里的读数视图。<see cref="Parse"/> 留着**只为迁移**——
/// 把老的 `during.json` 搬进库那一次，格式是：
///
/// <code>
/// { "goals": { "编程": { "seconds": 57203 } } }
/// </code>
///
/// **一行语义，没有别的。** v3 的 <c>recordedThrough</c> checkpoint 不再有存在理由
/// （DECISIONS C5）：没有回填就不需要水位线，留着只会让人以为还能回填。
///
/// ⚠️ **这个类不碰磁盘，一个 <c>File.</c> 都没有。** 读写文件是 App 层的事。
/// v3 在这里栽过（它的 I5）：「纯逻辑 + 写盘」写在一个方法里，单元测试调一次就把用户
/// 真实的账本冲成了测试数据，而且悄无声息。现在这件事**由类型系统保证**——
/// Core 里根本没有那一半。
///
/// ⚠️ **不导入 v3 的 during.json**（DECISIONS C3）：那 57203 秒是 AW 历史回填出来的，
/// 口径跟「本程序亲眼看到的秒」根本不是一回事，混在一起两边都不可信。
/// </summary>
public sealed class GoalTotals
{
    private sealed class Entry
    {
        [JsonPropertyName("seconds")]
        public long Seconds { get; set; }
    }

    private sealed class File_
    {
        [JsonPropertyName("goals")]
        public Dictionary<string, Entry> Goals { get; set; } = [];
    }

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, long> _seconds = new(StringComparer.Ordinal);

    /// <summary>某个目标已落盘的累计秒数。没有这条记录就是 0。</summary>
    public long this[string goal] => _seconds.GetValueOrDefault(goal);

    /// <summary>文件里出现过的目标，按名字排序——写出来的文件顺序稳定，diff 才好看。</summary>
    public IReadOnlyList<string> Goals => [.. _seconds.Keys.OrderBy(k => k, StringComparer.Ordinal)];

    /// <summary>从库里读回来的读数。</summary>
    public static GoalTotals Of(IReadOnlyDictionary<string, long> byGoal)
    {
        var totals = new GoalTotals();
        foreach (var (goal, seconds) in byGoal)
            if (seconds > 0)
                totals._seconds[goal] = seconds;
        return totals;
    }

    /// <summary>
    /// 老 `during.json` 的解析，**只给迁移用**。
    /// 文件不存在 / 是空的 / 读坏了，一律当空账本——**记不上时间绝不能把程序搞崩**。
    /// </summary>
    public static GoalTotals Parse(string? json)
    {
        var totals = new GoalTotals();
        if (string.IsNullOrWhiteSpace(json)) return totals;

        File_? file;
        try { file = JsonSerializer.Deserialize<File_>(json, ReadOpts); }
        catch (JsonException) { return totals; }
        if (file is null) return totals;

        foreach (var (goal, entry) in file.Goals)
            if (entry is not null && entry.Seconds > 0)
                totals._seconds[goal] = entry.Seconds;

        return totals;
    }

    /// <summary>
    /// 把一轮的成绩加进来。<paramref name="byGoal"/> 直接传
    /// <see cref="Round.FocusedSecondsByGoal"/>。
    ///
    /// ⚠️ **只加不减**：算过的秒不能再拿走，无论这一轮是怎么结束的（DECISIONS C5）。
    /// </summary>
    public void Add(IReadOnlyDictionary<string, int> byGoal)
    {
        foreach (var (goal, seconds) in byGoal) Add(goal, seconds);
    }

    public void Add(string goal, long seconds)
    {
        if (seconds <= 0) return;
        _seconds[goal] = _seconds.GetValueOrDefault(goal) + seconds;
    }

}

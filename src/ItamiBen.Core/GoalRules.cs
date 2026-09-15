using System.Text.Json;
using System.Text.RegularExpressions;

namespace ItamiBen.Core;

/// <summary>rules.json 里的一条匹配规则。App / Title 至少要有一个。</summary>
public sealed class MatchRule
{
    public string? App { get; init; }
    public string? Title { get; init; }
}

/// <summary>
/// 一个小目标。**组内任意一条规则命中就算命中**。
///
/// ⚠️ 这里没有「累计秒数」字段：rules.json 是**用户手写的**，程序只读不写
/// （写一次注释就全没了——`JsonCommentHandling.Skip` 读的时候就扔了，序列化时
/// 没有任何东西能还原）。累计值在 <see cref="GoalTotals"/> 里，另一个文件。
/// </summary>
public sealed class GoalGroup
{
    /// <summary>不再用的目标是禁用而不是删除——禁用的组永不命中，也不出现在可选列表里。</summary>
    public bool Disabled { get; init; }

    public IReadOnlyList<MatchRule> Rules { get; init; } = [];
}

/// <summary>
/// rules.json 的整份类型模型。**这个文件有什么，这个类就要有什么字段。**
///
/// ⚠️ v3 在这里栽过（它的 §15.4）：`executeCommand` 一度不在这个类里，App 另起一条
/// `JsonDocument` 路径再读一遍——**一个文件两条读取路径、两套解析选项，靠人记得同步**，
/// 咬了两次，症状都是「半个文件正常工作、另半个安静地失效，程序启动时若无其事」。
/// 要加新段落就往这个类加字段，别开第二条读取路径。
/// </summary>
public sealed class RulesFile
{
    /// <summary>
    /// ⚠️ **键名保持 v3 的 <c>Groups</c>**：DESIGN §2.1 实测「标题跟 AW 记的逐字节相同」，
    /// 结论是「现有 rules.json 可以原样搬过来」——改键名就等于把这个结论作废。
    /// 文件本身仍然是**两份**（DECISIONS A6：运行时目录跟 v3 完全隔离），共用的只是格式。
    /// </summary>
    public Dictionary<string, GoalGroup> Groups { get; init; } = [];
}

/// <summary>
/// 编译好的规则。纯逻辑，不碰时间也不碰网络。
///
/// 它只回答一个问题：**这个 (app, title) 命中那个目标吗？** 判定的其余部分
/// （命中算 Focused、自身豁免、读不到 app 算没采到）在 <see cref="Judgment"/> 里，
/// 这个类不掺和。
/// </summary>
public sealed class GoalRules
{
    private sealed record CompiledRule(Regex? App, Regex? Title);
    private sealed record CompiledGroup(string Name, bool Disabled, IReadOnlyList<CompiledRule> Rules);

    private readonly IReadOnlyList<CompiledGroup> _groups;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private GoalRules(IReadOnlyList<CompiledGroup> groups) => _groups = groups;

    /// <summary>空规则——一个目标都没有。界面在还没有 rules.json 时用它顶着。</summary>
    public static GoalRules Empty { get; } = new([]);

    public static GoalRules Parse(string json)
    {
        var file = JsonSerializer.Deserialize<RulesFile>(json, JsonOpts)
                   ?? throw new InvalidDataException("rules.json is empty.");

        var groups = new List<CompiledGroup>();
        foreach (var (name, g) in file.Groups)
        {
            // ⚠️ 空组匹配一切 = 约束当场归零，这正是这个程序唯一的卖点。宁可启动失败
            if (g.Rules.Count == 0)
                throw new InvalidDataException(
                    $"Goal \"{name}\" has no rules. An empty goal matches everything, which disables the constraint.");

            var rules = new List<CompiledRule>();
            foreach (var r in g.Rules)
            {
                if (r.App is null && r.Title is null)
                    throw new InvalidDataException(
                        $"Goal \"{name}\" has a rule with neither app nor title; it would match everything.");
                rules.Add(new CompiledRule(Compile(r.App, name), Compile(r.Title, name)));
            }
            groups.Add(new CompiledGroup(name, g.Disabled, rules));
        }

        return new GoalRules(groups);
    }

    private static Regex? Compile(string? pattern, string where)
    {
        if (pattern is null) return null;
        try { return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
        catch (ArgumentException e)
        {
            throw new InvalidDataException($"Invalid regex in \"{where}\": {pattern}  ({e.Message})", e);
        }
    }

    /// <summary>界面上可勾选的目标（禁用的不出现）。顺序就是 rules.json 里的书写顺序。</summary>
    public IReadOnlyList<string> SelectableGoals
        => _groups.Where(g => !g.Disabled).Select(g => g.Name).ToList();

    /// <summary>这个名字是一个能选的目标吗（存在、且没被禁用）。</summary>
    public bool IsSelectable(string goal) => _groups.Any(g => g.Name == goal && !g.Disabled);

    /// <summary>一条规则：App / Title 都写了就都要中；哪一边没写哪一边就不设约束。</summary>
    private static bool RuleMatches(CompiledRule r, string app, string title)
        => (r.App is null || r.App.IsMatch(app))
        && (r.Title is null || r.Title.IsMatch(title));

    /// <summary>
    /// 组内**任意**一条规则命中就算命中；禁用的组永不命中。
    ///
    /// ⚠️ <paramref name="title"/> 读不到时传空字符串就行，**不要**特殊处理：
    /// 空标题自然匹配不上写了 Title 的规则，这正是 DECISIONS C2 说的
    /// 「读到什么就是什么」——没有政策可选，也就没有政策要维护。
    /// </summary>
    public bool Matches(string goal, string app, string title)
    {
        var g = _groups.FirstOrDefault(x => x.Name == goal);
        if (g is null || g.Disabled) return false;
        return g.Rules.Any(r => RuleMatches(r, app, title));
    }
}

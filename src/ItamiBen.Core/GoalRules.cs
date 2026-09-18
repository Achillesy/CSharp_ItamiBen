using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ItamiBen.Core;

/// <summary>`rules.md` 里的一条匹配规则。App / Title 至少要有一个。</summary>
public sealed class MatchRule
{
    public string? App { get; init; }
    public string? Title { get; init; }
}

/// <summary>
/// 一个小目标。**组内任意一条规则命中就算命中**。
///
/// ⚠️ 这里没有「累计秒数」字段：`rules.md` 是**人和 AI 写的，程序只读不写**。
/// 累计值在 <see cref="GoalTotals"/> 里，住在库中——**谁写谁放哪**，这是 2026-09-18
/// 重画的那条分界线（DECISIONS I30）。
/// </summary>
public sealed class GoalGroup
{
    /// <summary>不再用的目标是禁用而不是删除——禁用的组永不命中，也不出现在可选列表里。</summary>
    public bool Disabled { get; init; }

    public IReadOnlyList<MatchRule> Rules { get; init; } = [];
}

/// <summary>
/// `rules.md` 里那个配置块的整份类型模型。**块里有什么，这个类就要有什么字段。**
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
    /// 结论是「v3 的规则可以原样搬过来」——改键名就等于把这个结论作废。
    /// 文件本身仍然是**两份**（DECISIONS A6：运行时目录跟 v3 完全隔离），共用的只是格式。
    /// </summary>
    public Dictionary<string, GoalGroup> Groups { get; init; } = [];

    /// <summary>
    /// 闹钟到点要跑的命令，按操作系统分。**一个值可以是单条字符串，也可以是一串**。
    ///
    /// ⚠️ **永远只执行第 0 条**：这是个收藏夹不是配置格式，想换命令就去文件里重排顺序，
    /// **不做界面去选**（v3 的 E9）。
    /// </summary>
    [JsonConverter(typeof(CommandTableConverter))]
    public Dictionary<string, IReadOnlyList<string>>? ExecuteCommand { get; init; }

    /// <summary>
    /// 窗口档位：<c>standard</c>（默认）或 <c>compact</c>。
    ///
    /// ⚠️ 2026-09-16 从单独的 `layout.json` 并进来（DECISIONS I14）：**少一个要手写的文件**。
    /// 它必须长在这个类上、跟着同一个解析器走——v3 的 §15.4 就是同一份文件两条读取路径，
    /// 咬了两次，症状都是半个文件安静地失效。
    /// </summary>
    [JsonPropertyName("layout")]
    public string? Layout { get; init; }

    /// <summary>
    /// 不透明度百分数。
    ///
    /// ⚠️ 声明成 <see cref="JsonElement"/> 而不是 <c>double?</c>，是为了**一个字段写错
    /// 不牵连另一个字段**：这是份手写 JSON，有人写成 <c>"50"</c>（带引号）完全可能；
    /// 声明成 <c>double?</c> 的话整份反序列化当场抛，连 `Groups` 都跟着丢了——
    /// 那就成了「改了个外观开关，所有目标都不见了」。
    /// </summary>
    [JsonPropertyName("opacity")]
    public JsonElement? Opacity { get; init; }
}

/// <summary>
/// <c>executeCommand</c> 怎么读：一个值**单条字符串或一串都收**，统一成列表。
/// 操作系统名（windows / macos）大小写不敏感，跟这份文件其余部分一致。
/// </summary>
internal sealed class CommandTableConverter : JsonConverter<Dictionary<string, IReadOnlyList<string>>>
{
    public override Dictionary<string, IReadOnlyList<string>> Read(
        ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var table = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("executeCommand must be an object keyed by OS.");

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var os = reader.GetString()!;
            reader.Read();
            var list = new List<string>();
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(reader.GetString()!);
            }
            else if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    if (reader.TokenType == JsonTokenType.String)
                        list.Add(reader.GetString()!);
            }
            else throw new JsonException($"executeCommand.{os} must be a string or an array of strings.");

            table[os] = list;
        }
        return table;
    }

    public override void Write(Utf8JsonWriter w, Dictionary<string, IReadOnlyList<string>> v, JsonSerializerOptions o)
        => throw new NotSupportedException("Configuration is written through the database, not this type.");
}

/// <summary>
/// 编译好的规则。纯逻辑，不碰时间也不碰网络。
///
/// 它只回答一个问题：**这个 (app, title) 命中那个目标吗？** 判定的其余部分
/// （人不在压过一切、读不到 app 算没采到）在 <see cref="Judgment"/> 里，这个类不掺和。
/// </summary>
public sealed class GoalRules
{
    private sealed record CompiledRule(Regex? App, Regex? Title);
    private sealed record CompiledGroup(string Name, bool Disabled, IReadOnlyList<CompiledRule> Rules);

    private readonly IReadOnlyList<CompiledGroup> _groups;
    /// <summary>名字 → 这条命令在两个系统上各是什么。**可执行的文本只住在这里。**</summary>
    private readonly IReadOnlyDictionary<string, (string? MacOS, string? Windows)> _commands;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private GoalRules(IReadOnlyList<CompiledGroup> groups,
                      IReadOnlyDictionary<string, (string? MacOS, string? Windows)> commands,
                      string? layout, double? opacityPercent)
    {
        _groups = groups;
        _commands = commands;
        LayoutName = layout;
        OpacityPercent = opacityPercent;
    }

    /// <summary>
    /// 某个操作系统的命令表。⚠️ **调用方只该用第 0 条**（v3 的 E9）——这是个常用命令的
    /// 收藏夹，换命令靠重排文件里的顺序，不靠界面。没配就是空列表。
    /// </summary>
    /// <summary>清单里有哪些命令，按名字。</summary>
    public IReadOnlyList<string> CommandNames => [.. _commands.Keys.OrderBy(k => k, StringComparer.Ordinal)];

    /// <summary>
    /// 按名字取这台机器上该跑的那条命令。名字为空、找不到、或者这条命令没给本系统写，
    /// 一律返回 null——**调用方拿 null 当「没有」处理，不猜也不退而求其次**。
    /// </summary>
    public string? CommandNamed(string? name)
    {
        if (name is null || !_commands.TryGetValue(name, out var c)) return null;
        var cmd = OperatingSystem.IsWindows() ? c.Windows : c.MacOS;
        return string.IsNullOrWhiteSpace(cmd) ? null : cmd;
    }

    /// <summary>这台机器上到点会跑的那一条；没配就是 null。</summary>

    /// <summary>空规则——一个目标都没有。界面在库还没打开时用它顶着。</summary>
    public static GoalRules Empty { get; } = new([], new Dictionary<string, (string?, string?)>(), null, null);

    /// <summary>
    /// 从库里的行装配——**只剩一次性迁移在用**（DECISIONS I30 之后正常路径是
    /// <see cref="Parse"/>，从 `rules.md` 读）。
    /// </summary>
    public static GoalRules Of(IReadOnlyList<SampleStore.GoalRow> goals,
                               IReadOnlyList<SampleStore.CommandRow> commands,
                               string? layout = null, double? opacityPercent = null)
    {
        var compiled = new List<CompiledGroup>();
        foreach (var g in goals)
        {
            var rules = new List<CompiledRule>();
            foreach (var r in g.Rules)
                rules.Add(new CompiledRule(Compile(r.App, $"{g.Name}.app"),
                                           Compile(r.Title, $"{g.Name}.title")));
            compiled.Add(new CompiledGroup(g.Name, !g.Enabled, rules));
        }

        var table = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var c in commands) table[c.Name] = (c.MacOS, c.Windows);

        return new GoalRules(compiled, table, layout, opacityPercent);
    }

    /// <summary>
    /// 解析 `rules.md` 里那个配置块——**这是正常路径**（2026-09-18 起配置回到文件，
    /// DECISIONS I30）。<see cref="Of"/> 只剩一次性迁移在用。
    ///
    /// ⚠️ 传进来的是**块的正文**，不是整份 `.md`：抠块那一步在
    /// <see cref="MarkdownConfig.Extract"/> 里，一个文件只有一条读取路径。
    /// </summary>
    public static GoalRules Parse(string json)
    {
        var file = JsonSerializer.Deserialize<RulesFile>(json, JsonOpts)
                   ?? throw new InvalidDataException("the rules block is empty.");

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

        var legacy = file.ExecuteCommand ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var table = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        var mac = legacy.GetValueOrDefault("macos")?.FirstOrDefault();
        var win = legacy.GetValueOrDefault("windows")?.FirstOrDefault();
        if (mac is not null || win is not null) table["alarm"] = (mac, win);

        return new GoalRules(groups, table, file.Layout, ReadPercent(file.Opacity));
    }

    /// <summary>
    /// 摊回成库里的行，**只给迁移用**。
    ///
    /// ⚠️ 正则的原文从 <c>Regex.ToString()</c> 取回来——它返回的就是当初传进去的模式串，
    /// 一个字节不差。所以迁移**不需要第二条解析路径**（v3 的 §15.4：同一份文件两条读取
    /// 路径，咬了两次）。
    /// </summary>
    public (List<SampleStore.GoalRow> Goals, List<SampleStore.CommandRow> Commands) ToRows()
    {
        var goals = new List<SampleStore.GoalRow>();
        foreach (var g in _groups)
            goals.Add(new SampleStore.GoalRow(g.Name, !g.Disabled,
                [.. g.Rules.Select(r => new SampleStore.RuleRow(r.App?.ToString(), r.Title?.ToString()))]));

        var commands = new List<SampleStore.CommandRow>();
        foreach (var (name, c) in _commands)
            commands.Add(new SampleStore.CommandRow(name, c.MacOS, c.Windows));

        return (goals, commands);
    }

    /// <summary>`layout` 那个词，原样给出来——怎么解释是界面层的事。</summary>
    public string? LayoutName { get; }

    /// <summary>
    /// `opacity` 那个百分数。写了个认不出的东西（对象、布尔、乱码）就是 null，
    /// **不抛也不猜**——调用方拿 null 当「没写」处理。
    /// </summary>
    public double? OpacityPercent { get; }

    private static double? ReadPercent(JsonElement? raw)
    {
        if (raw is not { } e) return null;
        return e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetDouble(out var n) => n,
            // 手写文件里 "50" 带引号太常见了，认它
            JsonValueKind.String when double.TryParse(e.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
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

    /// <summary>界面上可勾选的目标（禁用的不出现）。顺序就是 `goal.position`。</summary>
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

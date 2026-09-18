using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItamiBen.Core;

/// <summary>
/// 命令清单：**名字 → 这台机器该跑的那条正文**，外加闹钟绑的是哪一条。
/// 对应 `commands.json`（2026-09-18 起；在那之前是库里的 `command` 表 + `setting.alarmCommand`）。
///
/// ⚠️ **可执行的正文只住在这一处。** 计划表和闹钟都**按名字**引用，永远不存正文，
/// 所以「这台机器能被自动跑什么」永远只要看一个地方。
///
/// ⚠️ **一个名字在两个系统上必须做同一件事**，所以名字描述的是**动作**
/// （`shutdown` / `restart` / `sleep`）而不是时机（`alarm` 就错了——这个项目真发过
/// 一版 `alarm`，macOS 上是重启、Windows 上是关机，而界面只显示你站着的那一半）。
/// 想让两台机器行为不同，那是 <see cref="AlarmName"/> 分系统绑定要解决的事，
/// 不是靠一个名字两副面孔。
///
/// ⚠️ Core 不碰文件：调用方把文本读进来传给 <see cref="Parse"/>。
/// </summary>
public sealed class CommandTable
{
    /// <summary>
    /// ⚠️ **严格标准 JSON**：不认注释、不认尾逗号。配置文件要能被任何 JSON 工具
    /// 和任何文本编辑器无障碍打开，宽松解析只会让「在别处报错、在这儿不报错」。
    /// 属性名大小写不敏感是留着的——它零成本，挡掉智能体写 `macos` / `MacOS` 这一整类错。
    /// </summary>
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    private class PerOs
    {
        public string? MacOS { get; init; }
        public string? Windows { get; init; }
    }

    private sealed class Entry : PerOs
    {
        /// <summary>没验过的那一半（`"untested"` / `"Windows untested"`）。程序不读它，给人和智能体看。</summary>
        public string? Note { get; init; }
    }

    private sealed class Shape
    {
        public PerOs? Alarm { get; init; }
        public Dictionary<string, Entry> Commands { get; init; } = [];
    }

    private readonly Dictionary<string, (string? MacOS, string? Windows)> _table;

    private CommandTable(Dictionary<string, (string?, string?)> table, string? alarm)
    {
        _table = table;
        AlarmName = string.IsNullOrWhiteSpace(alarm) ? null : alarm;
    }

    /// <summary>一条命令都没有。文件读不到、或者库没开时用它顶着。</summary>
    public static CommandTable Empty { get; } = new([], null);

    /// <summary>解析 `commands.json`。</summary>
    public static CommandTable Parse(string json)
    {
        var f = JsonSerializer.Deserialize<Shape>(json, Opts)
                ?? throw new InvalidDataException("commands.json is empty.");

        var table = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var (name, e) in f.Commands) table[name] = (e.MacOS, e.Windows);

        return new CommandTable(table, Pick(f.Alarm?.MacOS, f.Alarm?.Windows));
    }

    /// <summary>从库里的行装配——**过渡期用**，文件不存在时的后备路径。</summary>
    public static CommandTable Of(IReadOnlyList<SampleStore.CommandRow> rows, string? alarm)
    {
        var table = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var r in rows) table[r.Name] = (r.MacOS, r.Windows);
        return new CommandTable(table, alarm);
    }

    /// <summary>闹钟到点该跑哪一条（**本机**这一侧的绑定）。没绑就是 null。</summary>
    public string? AlarmName { get; }

    /// <summary>清单里的全部名字，按字典序。</summary>
    public IReadOnlyList<string> Names => [.. _table.Keys.OrderBy(k => k, StringComparer.Ordinal)];

    /// <summary>
    /// 按名字取**这台机器**上该跑的那条正文。名字为空、找不到、或者这条没给本系统写，
    /// 一律返回 null——**调用方拿 null 当「没有」处理，不猜也不退而求其次**。
    /// </summary>
    public string? TextFor(string? name)
        => name is not null && _table.TryGetValue(name, out var c) ? Pick(c.MacOS, c.Windows) : null;

    private static string? Pick(string? macOS, string? windows)
    {
        var v = OperatingSystem.IsWindows() ? windows : macOS;
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}

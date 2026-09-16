using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItamiBen.App;

/// <summary>标准（默认）/ 紧凑。</summary>
public enum LayoutMode { Standard, Compact }

/// <summary>一档的尺寸。**唯一定义处**——XAML 里一个都不写死，免得同一个量两处定义。</summary>
public sealed record LayoutMetrics(double WindowWidth, double WindowHeight);

/// <summary>
/// 窗口外观的开关，放在运行时目录的 <c>layout.json</c>。从 v3 搬过来（它的 K25）。
///
/// <code>
/// { "layout": "compact", "opacity": 75 }
/// </code>
///
/// ⚠️ **这是用户手写的 JSON，程序只读不写**，所以解析必须跟 `rules.json` 同一套三件套
/// （注释 / 尾逗号 / 键名大小写不敏感）——少一个就是「写了注释就静默失效」那类事故。
///
/// ⚠️ **别搬进 `settings.json`**：那个文件程序随时整份重写，手改会被下一次写盘覆盖掉。
///
/// ⚠️ **只在启动时读一次，运行中改了不生效**——这是用户要的语义，也顺带免掉了
/// 「运行中换档要重新夹回屏幕、提示条正显示着怎么办」的一整类边界情况。
/// </summary>
public static class WindowLayout
{
    public const string FileName = "layout.json";

    /// <summary>不透明度的下限。再低就只剩一团看不清的影子了。</summary>
    public const double MinOpacityPercent = 10;

    public const double MaxOpacityPercent = 100;

    /// <summary>
    /// 没写、写错、超出范围时用的值。
    /// ⚠️ **强制 90，不是夹到边界**（v3 的用户 2026-09-08 定）：夹到边界会让
    /// 「我写了 5」和「我写了 10」看起来一样，用户以为生效了其实没有。
    /// </summary>
    public const double DefaultOpacityPercent = 90;

    private const double DefaultOpacity = DefaultOpacityPercent / 100.0;

    private static readonly LayoutMetrics Standard = new(420, 700);
    private static readonly LayoutMetrics Compact = new(340, 560);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class LayoutFile
    {
        [JsonPropertyName("layout")] public string? Layout { get; set; }

        /// <summary>
        /// ⚠️ 声明成 <see cref="JsonElement"/> 而不是 <c>double?</c>，是为了**一个字段
        /// 写错不牵连另一个字段**：这是份手写 JSON，有人把它写成 <c>"50"</c>（带引号）
        /// 完全可能；声明成 <c>double?</c> 的话反序列化整个抛异常，连 <c>layout</c>
        /// 那一档也跟着丢了。现在数字和数字字符串都认，别的类型安静退回默认值。
        /// </summary>
        [JsonPropertyName("opacity")] public JsonElement? Opacity { get; set; }
    }

    public sealed record LayoutSettings(LayoutMode Mode, double Opacity);

    /// <summary>
    /// **文件只读一次**，档位和透明度一起出来——不是两个 Lazy 各读一遍。
    ///
    /// 用 <see cref="Lazy{T}"/> 而不是字段初始化器：后者会在**类型初始化**时就去碰
    /// 文件系统和日志，那样单元测试碰一下任何静态成员都会被拖下水。
    /// </summary>
    private static readonly Lazy<LayoutSettings> LazyFile = new(Load);

    public static LayoutMode Mode => LazyFile.Value.Mode;

    /// <summary>这一次启动的不透明度（0~1）。</summary>
    public static double Opacity => LazyFile.Value.Opacity;

    public static LayoutMetrics Current => Mode == LayoutMode.Compact ? Compact : Standard;

    private static LayoutSettings Load()
    {
        var path = Path.Combine(AppData.Dir, FileName);
        try
        {
            var exists = File.Exists(path);
            var text = exists ? File.ReadAllText(path) : null;
            var settings = new LayoutSettings(ParseMode(text), ParseOpacity(text));
            Log.Line($"layout: {settings.Mode.ToString().ToLowerInvariant()}, "
                   + $"opacity {settings.Opacity * 100:0}%"
                   + (exists ? $" (from {path})" : " (no layout.json)"));
            return settings;
        }
        catch (Exception e)
        {
            // 读不到就用标准档 + 默认透明度——**绝不因为一个可选的外观开关起不来**
            Log.Error($"Failed to read {path}; using the standard layout", e);
            return new LayoutSettings(LayoutMode.Standard, DefaultOpacity);
        }
    }

    /// <summary>认不出的一律标准档。**纯函数，文件读取在外面**，所以能测。</summary>
    public static LayoutMode ParseMode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return LayoutMode.Standard;
        try
        {
            var value = JsonSerializer.Deserialize<LayoutFile>(json, JsonOpts)?.Layout;
            return string.Equals(value, "compact", StringComparison.OrdinalIgnoreCase)
                ? LayoutMode.Compact : LayoutMode.Standard;
        }
        catch (JsonException) { return LayoutMode.Standard; }
    }

    /// <summary>
    /// 认那个百分数。**不在 10~100 里、没写、写成别的类型——一律
    /// <see cref="DefaultOpacityPercent"/>**（不夹到边界，理由见那里）。
    /// </summary>
    public static double ParseOpacity(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DefaultOpacity;
        try { return FromPercent(JsonSerializer.Deserialize<LayoutFile>(json, JsonOpts)?.Opacity); }
        catch (JsonException) { return DefaultOpacity; }
    }

    private static double FromPercent(JsonElement? raw)
    {
        if (raw is not { } e) return DefaultOpacity;

        double pct;
        switch (e.ValueKind)
        {
            case JsonValueKind.Number when e.TryGetDouble(out pct):
                break;
            case JsonValueKind.String when double.TryParse(
                    e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out pct):
                break;
            default:
                return DefaultOpacity;
        }

        return pct >= MinOpacityPercent && pct <= MaxOpacityPercent ? pct / 100.0 : DefaultOpacity;
    }
}

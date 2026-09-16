using Avalonia;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>标准（默认）/ 紧凑。</summary>
public enum LayoutMode { Standard, Compact }

/// <summary>
/// 一档的尺寸。**唯一定义处**——XAML 里一个都不写死，免得同一个量两处定义。
/// 窗口高度不在这里：它是 <c>SizeToContent="Height"</c> 自己长出来的，
/// rules.json 有几个目标就有几行，窗口跟着走。
/// </summary>
/// <param name="BannerMaxLines">提示条最多列几条，同时也是 <c>TextBlock.MaxLines</c>。</param>
/// <param name="BannerMaxWidth">提示条正文的折行宽度。</param>
/// <param name="DominoMargin">
/// 骨牌行的外边距，**下边是负的**（v3 的 K16）。
///
/// ⚠️ **必须分档，不能在 XAML 里写死一个数**（2026-09-16 用户报「骨牌底部陷进下面的
/// 卡片里」就是写死的后果）：两档的骨牌高度不一样（76 / 56），而骨牌的底线画在
/// <c>H * 0.965</c> 上，所以「底线到控件下沿还剩多少」两档也不一样——负边距正是
/// 用来把这段差额吃掉的，它**跟 DominoRow 的 0.965 和底边那道渐变淡出绑在一起**，
/// 一档一个值。
///
/// ⚠️ 紧凑档的**上**边距特意放大到 8（标准档 2）：提示条跟骨牌叠在同一格里，
/// 紧凑档只让排一行，得给它留出高度。
///
/// ⚠️ 两档的下边距**出处不同，别去「统一」它们**：标准档的 −4 是 v3 定的、v3 上验过；
/// 紧凑档的 −2 是用户 2026-09-16 对着 v4 的窗口现场调的（v3 那边是 −3，他看下来还是
/// 陷得深了一点）。哪档改动哪档，不要为了看起来整齐去动没人验过的那一档。
/// </param>
public sealed record LayoutMetrics(
    double WindowWidth, double DialHeight, double DominoHeight,
    int BannerMaxLines, double BannerMaxWidth, Thickness DominoMargin);

/// <summary>
/// 窗口外观的开关。**写在 `rules.json` 的顶层**（2026-09-16 从单独的 `layout.json`
/// 并进来，DECISIONS I14）：
///
/// <code>
/// { "Groups": { ... }, "layout": "compact", "opacity": 75 }
/// </code>
///
/// ⚠️ **这个类自己不读文件、不解析 JSON**：值由 <see cref="GoalRules"/> 一起读出来，
/// 这里只负责**怎么解释**。一份文件一个解析器——v3 的 §15.4 就是同一份文件两条读取
/// 路径，咬了两次，症状都是半个文件安静地失效。
///
/// ⚠️ **别搬进设置表**：那是程序写、用户只看的地方（I12），而这两个值用户要手改。
///
/// ⚠️ **只在启动时读一次，运行中改了不生效**——这是用户要的语义，也顺带免掉了
/// 「运行中换档要重新夹回屏幕、提示条正显示着怎么办」的一整类边界情况。
/// </summary>
public static class WindowLayout
{
    /// <summary>
    /// 作废的那个文件名。留着只为**提醒**：它还在的话说明用户以为它还管用，
    /// 而「改了没反应」正是这个项目最恨的那类失败——所以启动时要吭一声。
    /// </summary>
    public const string RetiredFileName = "layout.json";

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

    private static readonly LayoutMetrics Standard = new(
        WindowWidth: 380, DialHeight: 330, DominoHeight: 76,
        BannerMaxLines: 2, BannerMaxWidth: 280,
        DominoMargin: new Thickness(0, 2, 0, -4));

    /// <summary>
    /// 紧凑档。**292 减掉左右各 18 的留白正好是 256**，所以钟面的
    /// <c>box = Math.Min(宽, 高)</c> 两边相等，刚好填满那一行不留空隙。
    /// 钟面一切都从 <c>Bounds</c> 推导，所以改这一个数就等比缩放，**绘制代码一行不用动**。
    /// </summary>
    private static readonly LayoutMetrics Compact = new(
        WindowWidth: 292, DialHeight: 256, DominoHeight: 56,
        BannerMaxLines: 1, BannerMaxWidth: 220,
        DominoMargin: new Thickness(0, 8, 0, -2));

    public sealed record LayoutSettings(LayoutMode Mode, double Opacity);

    /// <summary>
    /// 启动时装一次，之后全程不变。
    ///
    /// ⚠️ **不用 `Lazy` 也不用字段初始化器**：那两种写法会在**类型初始化**那一刻就去碰
    /// 文件和日志，单元测试碰一下任何静态成员都被拖下水。现在它就是个普通的值，
    /// 谁装谁负责。
    /// </summary>
    private static LayoutSettings _current = new(LayoutMode.Standard, DefaultOpacity);

    /// <summary>规则读出来之后装上去。**一次启动只该调一次。**</summary>
    public static void Bind(GoalRules rules)
        => _current = new LayoutSettings(ModeOf(rules.LayoutName), OpacityOf(rules.OpacityPercent));

    public static LayoutMode Mode => _current.Mode;

    /// <summary>这一次启动的不透明度（0~1）。</summary>
    public static double Opacity => _current.Opacity;

    public static LayoutMetrics Current => Mode == LayoutMode.Compact ? Compact : Standard;

    /// <summary>认不出的一律标准档。</summary>
    public static LayoutMode ModeOf(string? name)
        => string.Equals(name, "compact", StringComparison.OrdinalIgnoreCase)
            ? LayoutMode.Compact : LayoutMode.Standard;

    /// <summary>
    /// 认那个百分数。**不在 10~100 里、没写、写成别的类型——一律
    /// <see cref="DefaultOpacityPercent"/>**（不夹到边界，理由见那里）。
    /// </summary>
    public static double OpacityOf(double? percent)
        => percent is { } p && p >= MinOpacityPercent && p <= MaxOpacityPercent
            ? p / 100.0 : DefaultOpacity;
}

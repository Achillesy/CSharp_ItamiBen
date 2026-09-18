using System.Text.Json;

namespace ItamiBen.Core;

/// <summary>
/// `layout.json`：窗口档位和不透明度。**两个键，只在启动时读一次**。
///
/// ⚠️ 这两个值**没有任何界面能改**，所以它们是配置（智能体写）而不是程序状态。
/// 其余十九个 `setting` 键都有界面，留在库里。
///
/// ⚠️ 这是唯一一份**按机器**的配置：`rules.md` / `commands.md` 是设计成
/// 在两台机器之间原样搬的，而档位和透明度在笔记本上合适、在台式机上未必。
///
/// ⚠️ 这里只负责**读出来**，不负责解释。合法范围、越界怎么办（一律换成 90，
/// 不是夹到边界）归 `WindowLayout`——**一个值只有一处解释**。
/// </summary>
public sealed record LayoutFile(string? Layout, double? OpacityPercent)
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>什么都没写。文件不存在时用它，后面每个值各自走默认。</summary>
    public static LayoutFile Empty { get; } = new(null, null);

    public static LayoutFile Parse(string json)
        => JsonSerializer.Deserialize<LayoutFile>(json, Opts) ?? Empty;
}

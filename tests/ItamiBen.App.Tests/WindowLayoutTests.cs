using ItamiBen.App;

namespace ItamiBen.App.Tests;

/// <summary>
/// 档位和透明度**怎么解释**。
///
/// ⚠️ 解析已经不在这一层了：这两个值跟规则一起写在 `rules.json` 里，由
/// `GoalRules.Parse` 一次读出来（DECISIONS I14）。这里只剩两个纯函数，不碰磁盘。
/// 「手写文件的三件套」「一个字段写错不牵连另一个」那些现在归 `GoalRulesTests` 管。
/// </summary>
public class WindowLayoutTests
{
    [Theory]
    [InlineData("compact", LayoutMode.Compact)]
    [InlineData("COMPACT", LayoutMode.Compact)]
    [InlineData("standard", LayoutMode.Standard)]
    [InlineData("莫须有", LayoutMode.Standard)]
    [InlineData(null, LayoutMode.Standard)]
    public void 认不出的档位一律退回标准档(string? name, LayoutMode expected)
        => Assert.Equal(expected, WindowLayout.ModeOf(name));

    [Theory]
    [InlineData(10)]
    [InlineData(35)]
    [InlineData(90)]
    [InlineData(100)]
    public void 范围内的百分数原样生效(double pct)
        => Assert.Equal(pct / 100.0, WindowLayout.OpacityOf(pct), 3);

    [Theory]
    [InlineData(5.0)]    // 低于下限
    [InlineData(120.0)]  // 高于上限
    [InlineData(null)]   // 没写，或者写了个认不出的类型
    public void 写错的透明度强制回默认值而不是夹到边界(double? pct)
    {
        // ⚠️ 夹到边界会让「我写了 5」和「我写了 10」看起来一样——
        //    用户以为生效了，其实是被悄悄改掉的
        Assert.Equal(WindowLayout.DefaultOpacityPercent / 100.0, WindowLayout.OpacityOf(pct), 3);
    }
}

using Avalonia.Controls;
using Avalonia.Media;

// ImplicitUsings 会把 System.IO 带进来，那里面也有个 Path。这个文件里的 Path 永远是
// 图形那个，起个别名钉死，省得到处写全名。
using Path = Avalonia.Controls.Shapes.Path;

namespace ItamiBen.App;

/// <summary>
/// 窗口右上角那两个即点即切的小图标（图钉 = 置顶，主题 = 日/夜），以及右键菜单里的关闭。
/// 从 v3 原样搬过来，跟 AW 无关。
///
/// **为什么不是字体字形**：v3 原来用的是 Segoe Fluent Icons，那套字体只在 Windows 上
/// 预装——macOS 上就是两个豆腐块。改成矢量画不只是「为了跨平台将就一下」，它本来就更
/// 合这个项目：钟面、指针、图标全是代码画出来的，**仓库里一张位图都没有**。
///
/// 状态靠**形状 + 不透明度双重表达**，从不用文字：
///
/// | | 关 | 开 |
/// |---|---|---|
/// | 图钉 | 空心（只有轮廓） | 填实 |
/// | 主题 | 太阳（日面） | 月亮（夜面） |
///
/// ⚠️ 图钉的「关」态**刻意不用 X**：满不透明度下 X 会读成「置顶被禁用了」，
/// 正好跟「当前没置顶」相反。
///
/// ⚠️ 主题图标画的是**当前所处的状态，不是「点了会变成什么」**——跟图钉一致，
/// 图标即状态，这一排读法统一。
/// </summary>
public static class ChromeIcons
{
    /// <summary>
    /// 墨色 + 光晕，两支笔都从当前主题的钟面调色板里取。
    ///
    /// 不新增色号：墨 = <c>Palette.Ink</c>（钟面上数字和指针用的同一支笔），
    /// 光晕 = <c>Palette.Face</c>（盘面色，日面素白就是白光晕，夜面深灰自然是深光晕）。
    /// </summary>
    private static (IBrush Ink, IBrush Halo) Pens(DialPalette? p)
    {
        p ??= DialPalette.Light;
        return (new SolidColorBrush(p.Ink), new SolidColorBrush(p.Face));
    }

    // ⚠️ **描边光晕这一层不能省**：主窗口无边框透明之后，这几个图标可能直接叠在任意
    //    桌面壁纸上——只有墨色一个颜色时，遇到明暗接近的壁纸区域就看不清了。
    //    每个形状先拿光晕色描一圈更粗的轮廓垫底，再叠正常的墨色。
    //    这是「双层描边」，不是加背景色块（v3 的用户 2026-08-08 明确要求过方向）。

    /// <summary>图标画在 16×16 的格子里，缩放和不透明度由外面那个 Button 负责。</summary>
    private const double Box = 16;

    /// <summary>
    /// 喇叭（滴答声）。**开着是两道声波，关着是一道斜杠**——所有人都把划掉的喇叭读成静音，
    /// 没有歧义。
    /// </summary>
    public static Control Speaker(bool on, DialPalette? palette = null)
    {
        var (ink, halo) = Pens(palette);

        // 箱体（左边那个小方块）+ 号角（向右张开的梯形），一笔画完
        var geo = Geometry.Parse("M 2,6 L 5,6 L 9,2.5 L 9,13.5 L 5,10 L 2,10 Z");
        var canvas = new Canvas { Width = Box, Height = Box };

        // ⚠️ 光晕和填充必须是**两个分开的 Path**，不能在同一个 Path 上同时设 Fill 和
        //    Stroke——那样描边会描在填充**之上**，啃掉半圈填充的边缘。
        canvas.Children.Add(new Path { Data = geo, Stroke = halo, StrokeThickness = 1.6, StrokeJoin = PenLineJoin.Round });
        canvas.Children.Add(new Path { Data = geo, Fill = ink });

        if (on)
        {
            // 两道声波，半径拉开一档才读得出是「正在响」而不是「一个圈」
            AddStroke(canvas, "M 11,5.5 A 4,4 0 0 1 11,10.5", 1.4, ink, halo);
            AddStroke(canvas, "M 12.8,3.4 A 7,7 0 0 1 12.8,12.6", 1.4, ink, halo);
        }
        else
        {
            AddStroke(canvas, "M 11,4 L 15,12", 1.6, ink, halo);
        }

        return canvas;
    }

    /// <summary>
    /// 图钉。竖着扎进去的那种：圆头 + 杆，**开着填实、关着只留轮廓**。
    /// </summary>
    public static Control Pin(bool on, DialPalette? palette = null)
    {
        var (ink, halo) = Pens(palette);

        const string Data = "M 5.5,2 L 10.5,2 L 9.5,7 L 11.5,9 L 8.6,9 L 8,14.5 " +
                            "L 7.4,9 L 4.5,9 L 6.5,7 Z";
        var geo = Geometry.Parse(Data);
        var canvas = new Canvas { Width = Box, Height = Box };

        // 光晕垫底：填实时描边够粗就能从深色填充边缘露出来；空心时光晕是更粗的一圈
        // 浅色描边，深色描边细一圈叠在正上方
        canvas.Children.Add(new Path
        {
            Data = geo, Stroke = halo, StrokeThickness = on ? 2.2 : 3.0, StrokeJoin = PenLineJoin.Round,
        });
        canvas.Children.Add(new Path
        {
            Data = geo,
            Fill = on ? ink : null,
            Stroke = on ? null : ink,
            StrokeThickness = 1.2,
            StrokeJoin = PenLineJoin.Round,
        });
        return canvas;
    }

    /// <summary>
    /// 齿轮（设置）。八颗齿 + 中间一个孔，用 EvenOdd 填充规则把孔挖出来。
    ///
    /// 跟喇叭、图钉一样是**算出来的**：齿廓那 32 个点由三个数推出来（外半径、内半径、
    /// 齿宽），不是字体字形。
    ///
    /// 它没有开关态，所以不收 bool：设置窗口开着的时候这个按钮本来就被模态挡住了，
    /// 不存在「现在是不是开着」这个需要表达的状态。
    /// </summary>
    public static Control Gear(DialPalette? palette = null)
    {
        var (ink, halo) = Pens(palette);

        const string Outer = "M 14.91,6.85 L 14.91,9.15 L 12.98,9.49 L 12.58,10.47 L 13.70,12.07 L 12.07,13.70 L 10.47,12.58 L 9.49,12.98 L 9.15,14.91 L 6.85,14.91 L 6.51,12.98 L 5.53,12.58 L 3.93,13.70 L 2.30,12.07 L 3.42,10.47 L 3.02,9.49 L 1.09,9.15 L 1.09,6.85 L 3.02,6.51 L 3.42,5.53 L 2.30,3.93 L 3.93,2.30 L 5.53,3.42 L 6.51,3.02 L 6.85,1.09 L 9.15,1.09 L 9.49,3.02 L 10.47,3.42 L 12.07,2.30 L 13.70,3.93 L 12.58,5.53 L 12.98,6.51 Z";
        const string Hole = "M 5.70,8.00 A 2.30,2.30 0 1 0 10.30,8.00 A 2.30,2.30 0 1 0 5.70,8.00 Z";

        var geo = Geometry.Parse(Outer + " " + Hole);
        if (geo is PathGeometry pg) pg.FillRule = FillRule.EvenOdd;

        var canvas = new Canvas { Width = Box, Height = Box };
        // 这份 EvenOdd 几何体的描边会把外沿和中间那个孔的边缘一起描到，两处都有光晕
        canvas.Children.Add(new Path { Data = geo, Stroke = halo, StrokeThickness = 1.4, StrokeJoin = PenLineJoin.Round });
        canvas.Children.Add(new Path { Data = geo, Fill = ink });
        return canvas;
    }

    /// <summary>
    /// 主题。日间是太阳（实心日轮 + 八根光芒），夜间是月亮——**一块饼干被咬掉一口**
    /// 的形状，斜着躺。
    /// </summary>
    public static Control Theme(bool dark, DialPalette? palette = null)
    {
        var (ink, halo) = Pens(palette);
        var canvas = new Canvas { Width = Box, Height = Box };

        if (dark)
        {
            // ⚠️ **咬口跨过外沿，所以不能用「两个圆 + EvenOdd」那一手**（齿轮挖中间那个
            //    孔可以，是因为孔整个在里面）：小圆露在大圆外面的那半边，EvenOdd 数下来
            //    交叉次数是奇数，**照样会被填上**——屏幕上就是两个圆叠在一起。
            //    这里改成一条闭合路径描一圈轮廓：
            //      ① 从咬口上角出发，沿饼干外沿绕远路（左、下、右）走到咬口下角；
            //      ② 再沿「嘴」那条弧切回来收口。
            //    **嘴比饼干大得多**（半径 10 : 5.7）——弧越大越接近直线，咬痕就越像
            //    「切」而不是「啃」。
            var geo = Geometry.Parse(
                "M 6.40,2.53 A 5.7,5.7 0 1 0 13.47,9.60 A 10,10 0 0 1 6.40,2.53 Z");
            canvas.Children.Add(new Path { Data = geo, Stroke = halo, StrokeThickness = 1.4, StrokeJoin = PenLineJoin.Round });
            canvas.Children.Add(new Path { Data = geo, Fill = ink });
        }
        else
        {
            var disc = Geometry.Parse("M 4.4,8 A 3.6,3.6 0 1 0 11.6,8 A 3.6,3.6 0 1 0 4.4,8 Z");
            canvas.Children.Add(new Path { Data = disc, Stroke = halo, StrokeThickness = 1.5, StrokeJoin = PenLineJoin.Round });
            canvas.Children.Add(new Path { Data = disc, Fill = ink });

            const double In = 5.3, Out = 7.1;
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                double cos = Math.Cos(a), sin = Math.Sin(a);
                // ⚠️ 不变文化格式化：`Geometry.Parse` 只认小数点，跟着系统区域设置走
                //    会在用逗号做小数点的地区**安静画错**
                AddStroke(canvas, FormattableString.Invariant(
                        $"M {8 + In * cos:0.##},{8 + In * sin:0.##} L {8 + Out * cos:0.##},{8 + Out * sin:0.##}"),
                    1.3, ink, halo);
            }
        }

        return canvas;
    }

    /// <summary>
    /// 关闭（钟面右键菜单里那一项）。两笔交叉的斜线。
    ///
    /// **不加光晕描边**，跟上面两个不一样：它只出现在右键菜单里，菜单自己有不透明背景，
    /// 不存在直接叠在桌面壁纸上的问题。
    ///
    /// ⚠️ 这里的 X 跟 <see cref="Pin"/> 那条「刻意不用 X」是两回事：那条说的是别拿 X
    /// 表示「图钉关着」（会读成「禁用置顶」，正好相反）；这里 X 就是它字面的意思。
    /// </summary>
    public static Control Close(DialPalette? palette = null)
    {
        var (ink, _) = Pens(palette);
        var canvas = new Canvas { Width = Box, Height = Box };
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 4,4 L 12,12 M 12,4 L 4,12"),
            Stroke = ink,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
        });
        return canvas;
    }

    /// <summary>光晕垫底 + 正常描边：光晕更粗垫在下面，正常粗细的墨色叠在正上方。</summary>
    private static void AddStroke(Canvas canvas, string data, double thickness, IBrush ink, IBrush halo)
    {
        var geo = Geometry.Parse(data);
        canvas.Children.Add(new Path { Data = geo, Stroke = halo, StrokeThickness = thickness + 1.6, StrokeLineCap = PenLineCap.Round });
        canvas.Children.Add(new Path { Data = geo, Stroke = ink, StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round });
    }
}

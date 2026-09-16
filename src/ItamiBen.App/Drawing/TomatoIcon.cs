using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ItamiBen.App;

/// <summary>
/// Tomato -- the app icon.
///
/// A pomodoro timer using a tomato as its icon is fitting. **This is the only icon there
/// is**: it goes into the exe's resources (csproj `ApplicationIcon`) and the .app's .icns,
/// both by way of <see cref="IconExport"/>. There used to be a `RingIcon` that swapped in a
/// live progress ring while a task ran; it was deleted on 2026-08-10 because nothing
/// displayed it any more (DECISIONS D11).
///
/// Pure vector, like the dial and the dominoes: a handful of polygons and Beziers, no
/// bitmap asset, crisp at any size.
///
/// **Colour constraint (set by the user): at most 8 colours.** Actually 7. **No outline** -- adding a black line would turn it into
/// an illustration. Depth comes from the brightness difference between faces, not
/// gradients: a gradient smears into a blur at a 16px taskbar icon size, while flat colour
/// blocks still read as a tomato even scaled down further.
///
/// Recoloured green on 2026-09-16: ItamiTimer (v3) wears the red one, and the two programs
/// run side by side on the same machine, so the icon has to tell them apart in the Dock.
/// A halved cut-face version was drawn first and lives in git at 2b46059 -- the user chose
/// the whole fruit instead: the shape was already right, and red vs green carries the
/// difference on its own at every size.
///
/// Redrawn on 2026-07-27: the first version modeled a "paper-tomato" look with a big green
/// cap sitting over the whole body, which was a plain factual error -- a real tomato's
/// sepals are **a few small, pointed leaves**, plus a short stem, never large enough to
/// cover the shoulders of the fruit.
/// </summary>
public static class TomatoIcon
{
    // ---- 7 colours, not one more
    //
    // ⚠️ 果身要**闷一点的橄榄绿，不能用亮柠檬绿**（用户 2026-09-16：「太透了，像一颗葡萄」）。
    //    亮而饱和的黄绿读起来是**通透的浆果**；真正的青番茄是压着土气的、偏暗的绿。
    //    这里**真实感压过分辨度**——用户原话：「糊也不怕，现实中绿番茄和叶子本来
    //    颜色就相近，我要的是一眼看出这是个番茄」。所以果身和萼片只拉开明度，不硬掰色相。
    //
    // ⚠️ 高光也要跟着压：一块又亮又大的高光正是「葡萄感」的另一半来源，
    //    它现在只比果身亮一档，够交代圆，不够抢戏。
    private static readonly Color Body_ = Color.FromRgb(0x6F, 0x96, 0x33);       // 果身
    private static readonly Color BodyDark = Color.FromRgb(0x54, 0x75, 0x22);    // 右下那弯暗面
    private static readonly Color BodyLit = Color.FromRgb(0x8C, 0xB0, 0x4E);     // 左上的高光
    private static readonly Color Stripe = Color.FromRgb(0x5D, 0x80, 0x2A);      // 深绿条纹（青番茄的品种特征）
    private static readonly Color GreenDark = Color.FromRgb(0x1C, 0x46, 0x1A);   // 萼片
    private static readonly Color Green = Color.FromRgb(0x27, 0x5E, 0x23);       // 压在上面那两片
    private static readonly Color GreenLit = Color.FromRgb(0x35, 0x77, 0x2C);    // 果梗的亮面

    public static RenderTargetBitmap Render(int size = 128)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            Point P(double x, double y) => new(x * size, y * size);
            void Fill(Color c, Geometry g) => ctx.DrawGeometry(new SolidColorBrush(c), null, g);

            // ---- The fruit body. Draws the dark version whole first, then offsets the
            //      bright version slightly toward the upper-left on top of it, so the
            //      lower-right naturally shows a crescent of shadow -- two shapes create
            //      volume without any gradient.
            Fill(BodyDark, Body(P, 0.512, 0.572));
            Fill(Body_, Body(P, 0.492, 0.556));

            // ---- 深绿条纹（用户 2026-09-16 要的，青番茄的品种特征）。
            //      ⚠️ **靠裁剪贴合轮廓，不要去算每条到边缘的交点**：条纹画成一路长到
            //      果子外面的整条，再用果身自己的形状裁掉——轮廓和条纹从此不可能对不齐。
            using (ctx.PushGeometryClip(Body(P, 0.492, 0.556)))
                foreach (var (mid, w, top, bottom) in new[]
                         {
                             // ⚠️ **宽窄、长短、起止高度全都要不齐**，而且**左右不对称**：
                             //    整齐的一圈竖条就是南瓜。真果子的斑纹是零散的。
                             (0.250, 0.026, 0.36, 0.86),
                             (0.372, 0.017, 0.30, 0.78),
                             (0.560, 0.030, 0.27, 0.90),
                             (0.690, 0.021, 0.34, 0.82),
                             (0.828, 0.013, 0.44, 0.74),
                         })
                    Fill(Stripe, Meridian(P, mid, w, top, bottom));

            // The upper-left highlight: a crescent of bright red
            //      ⚠️ **画在条纹之后**：高光是表面的反光，它本来就该把底下的花纹压住；
            //      反过来先高光后条纹，条纹会像印在玻璃上一样浮起来。
            Fill(BodyLit, Highlight(P));

            // ---- Sepals: a few small, pointed leaves fanning outward from the top of the
            //      fruit, drooping slightly. Not quite symmetric left to right, which is
            //      what makes it look real.
            var hub = P(0.50, 0.250);
            foreach (var (tx, ty, w) in new[]
                     {
                         (0.150, 0.248, 0.026),   // Leftmost, nearly horizontal
                         (0.252, 0.146, 0.024),
                         (0.388, 0.108, 0.022),
                         (0.618, 0.102, 0.022),
                         (0.762, 0.150, 0.024),
                         (0.858, 0.262, 0.026),   // Rightmost
                     })
                Fill(GreenDark, Leaf(hub, P(tx, ty), w * size));

            // The middle two overlap on top, one shade brighter, giving the leaves a sense of layering
            foreach (var (tx, ty, w) in new[] { (0.318, 0.186, 0.028), (0.692, 0.182, 0.028) })
                Fill(Green, Leaf(hub, P(tx, ty), w * size));

            // ---- Stem: a short piece, tilted slightly to the right
            Fill(Green, Quad(P(0.470, 0.268), P(0.524, 0.268), P(0.558, 0.078), P(0.512, 0.072)));
            Fill(GreenLit, Quad(P(0.512, 0.072), P(0.558, 0.078), P(0.564, 0.048), P(0.518, 0.042)));
        }
        return rtb;
    }

    /// <summary>The fruit body: rounded, slightly flattened, fuller toward the bottom, close to the real thing's "rounded square" silhouette.</summary>
    private static Geometry Body(Func<double, double, Point> P, double cx, double cy)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(P(cx, cy - 0.300), true);
        g.CubicBezierTo(P(cx - 0.290, cy - 0.300), P(cx - 0.440, cy - 0.140), P(cx - 0.440, cy + 0.040));
        g.CubicBezierTo(P(cx - 0.440, cy + 0.250), P(cx - 0.265, cy + 0.378), P(cx, cy + 0.378));
        g.CubicBezierTo(P(cx + 0.265, cy + 0.378), P(cx + 0.440, cy + 0.250), P(cx + 0.440, cy + 0.040));
        g.CubicBezierTo(P(cx + 0.440, cy - 0.140), P(cx + 0.290, cy - 0.300), P(cx, cy - 0.300));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// 一道条纹：顺着果子的经线走，两头都收成尖。
    ///
    /// ⚠️ **两头都要停在果肉里**，别一路通到果蒂和果脐：一路通到底的等距竖条会把番茄
    /// 画成**南瓜**——那种「一瓣一瓣」的读法全来自条纹在两极汇聚。停在中间，
    /// 它才是长在皮上的花纹而不是瓣与瓣的分界。（2026-09-16 为这个改了两轮。）
    ///
    /// ⚠️ 离中轴的距离随高度按抛物线收（赤道最远、两极最近）：球面上的经线就是这样，
    /// 画成上下一样宽的直条，果子立刻变成一个圆筒。
    /// </summary>
    private static Geometry Meridian(Func<double, double, Point> P, double mid, double halfWidth,
                                     double topY, double bottomY)
    {
        const double axis = 0.492;
        double Spread(double y)
        {
            var d = (y - 0.60) / 0.44;
            return Math.Clamp(1 - d * d * 0.8, 0.2, 1);
        }

        var top = axis + (mid - axis) * Spread(topY);
        var bottom = axis + (mid - axis) * Spread(bottomY);
        var a = topY + (bottomY - topY) * 0.30;
        var b = topY + (bottomY - topY) * 0.70;

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(P(top, topY), true);
        g.CubicBezierTo(P(mid + halfWidth, a), P(mid + halfWidth, b), P(bottom, bottomY));
        g.CubicBezierTo(P(mid - halfWidth, b), P(mid - halfWidth, a), P(top, topY));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>The crescent highlight in the upper-left.</summary>
    private static Geometry Highlight(Func<double, double, Point> P)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(P(0.208, 0.650), true);
        g.CubicBezierTo(P(0.132, 0.505), P(0.180, 0.372), P(0.292, 0.306));
        g.CubicBezierTo(P(0.336, 0.348), P(0.326, 0.362), P(0.306, 0.382));
        g.CubicBezierTo(P(0.230, 0.444), P(0.210, 0.540), P(0.258, 0.648));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>One leaf: a slender triangle from the hub toward the tip, slightly bulged at the waist, tapering to a point.</summary>
    private static Geometry Leaf(Point hub, Point tip, double halfWidth)
    {
        var dx = tip.X - hub.X;
        var dy = tip.Y - hub.Y;
        var len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
        var nx = -dy / len * halfWidth;
        var ny = dx / len * halfWidth;

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(new Point(hub.X + nx, hub.Y + ny), true);
        g.QuadraticBezierTo(new Point(hub.X + dx * 0.55 + nx * 0.85, hub.Y + dy * 0.55 + ny * 0.85), tip);
        g.QuadraticBezierTo(new Point(hub.X + dx * 0.55 - nx * 0.85, hub.Y + dy * 0.55 - ny * 0.85),
                            new Point(hub.X - nx, hub.Y - ny));
        g.EndFigure(true);
        return geo;
    }

    private static Geometry Quad(Point a, Point b, Point c, Point d)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(a, true);
        g.LineTo(b); g.LineTo(c); g.LineTo(d);
        g.EndFigure(true);
        return geo;
    }
}

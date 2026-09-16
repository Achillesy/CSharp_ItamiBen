using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ItamiBen.App;

/// <summary>
/// Tomato -- the app icon. A **green tomato, cut in half**, seen on the cut face.
///
/// **This is the only icon there is**: it goes into the exe's resources (csproj
/// `ApplicationIcon`) and the .app's .icns, both by way of <see cref="IconExport"/>.
///
/// Redrawn on 2026-09-16 (user's request, from a photograph): it used to be a whole red
/// tomato, which is what ItamiTimer (v3) wears. The two programs run side by side on the
/// same machine, so **the icon's first job is telling them apart in the Dock** -- and
/// red-whole vs green-halved differs in hue, in silhouette, and in interior structure, so
/// it still reads apart at 16px where a subtler difference would not.
///
/// **The outline and the leaves are the old icon's, unchanged** -- only the colour and the
/// interior are new. The silhouette was right the first time and there was nothing to gain
/// by redrawing it.
///
/// Anatomy of the cut face (user's correction, 2026-09-16): the fruit is **flesh all the
/// way out -- no skin band**, and the two **dark green cavities** -- equal-width bands on a
/// common circle, sitting low in the fruit -- are the juice. The pale apple-core shape in
/// the middle is not drawn at all: it is simply the flesh left between and above them.
///
/// Pure vector, like the dial and the dominoes: a handful of Beziers and ellipses, no
/// bitmap asset, crisp at any size.
///
/// **Colour constraint (set by the user): at most 8 colours, no outline.** Six here.
/// Depth comes from the brightness difference between flat blocks, never a gradient: a
/// gradient smears into a blur at 16px, while flat blocks still read.
/// </summary>
public static class TomatoIcon
{
    // ---- 6 colours, not one more
    private static readonly Color Flesh     = Color.FromRgb(0xB7, 0xD1, 0x73);  // 果肉，青番茄偏黄的绿
    private static readonly Color FleshDark = Color.FromRgb(0x98, 0xB8, 0x56);  // 右下那弯暗面
    private static readonly Color Juice     = Color.FromRgb(0x44, 0x83, 0x22);  // 果汁，深绿
    private static readonly Color Seed      = Color.FromRgb(0xF4, 0xF8, 0xE4);  // 籽
    private static readonly Color Sepal     = Color.FromRgb(0x24, 0x60, 0x1C);  // 萼片
    private static readonly Color SepalLit  = Color.FromRgb(0x3E, 0x8A, 0x2B);  // 压在上面那两片 + 果梗

    public static RenderTargetBitmap Render(int size = 128)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            Point P(double x, double y) => new(x * size, y * size);
            void Fill(Color c, Geometry g) => ctx.DrawGeometry(new SolidColorBrush(c), null, g);

            // ---- 果肉。⚠️ **没有果皮那一圈**（用户 2026-09-16 指出）：番茄本身就是果肉
            //      组成的，切面上画一道深色边就成了西瓜。
            //      暗的整块先画，亮的朝左上挪一点点盖上去，右下自然露出一弯暗面——
            //      两个形状造出体积，不用任何渐变，也不用描边。
            Fill(FleshDark, Body(P, 0.512, 0.572, 1.00));
            Fill(Flesh, Body(P, 0.492, 0.556, 1.00));

            // ---- 果腔：**左右对称两条等宽的弯带**，整体偏在果子的下半。
            //      中间和上面剩下的浅色就是果肉，不用另画——它自然长成照片里那个
            //      「苹果剖面 / 蝴蝶翅膀」的形状：上面一整片圆顶，中间最宽，往下收窄。
            foreach (var mirror in new[] { false, true })
            {
                Fill(Juice, Cavity(P, mirror, false));
                foreach (var c in SeedCentres(P, mirror))
                    Fill(Seed, new EllipseGeometry(new Rect(
                        c.X - 0.026 * size, c.Y - 0.020 * size, 0.052 * size, 0.040 * size)));
            }

            // ---- 萼片和果梗：**跟旧图标一字不差**，只是换了个绿。
            //      几片小而尖的叶子从果蒂往外张，左右不完全对称才像真的。
            var hub = P(0.50, 0.250);
            foreach (var (tx, ty, w) in new[]
                     {
                         (0.150, 0.248, 0.026),   // 最左，几乎平伸
                         (0.252, 0.146, 0.024),
                         (0.388, 0.108, 0.022),
                         (0.618, 0.102, 0.022),
                         (0.762, 0.150, 0.024),
                         (0.858, 0.262, 0.026),   // 最右
                     })
                Fill(Sepal, Leaf(hub, P(tx, ty), w * size));

            // 中间两片压在上面，亮一号，叶子就有了层次
            foreach (var (tx, ty, w) in new[] { (0.318, 0.186, 0.028), (0.692, 0.182, 0.028) })
                Fill(SepalLit, Leaf(hub, P(tx, ty), w * size));

            // 果梗：短短一截，略向右倾
            Fill(SepalLit, Quad(P(0.470, 0.268), P(0.524, 0.268), P(0.558, 0.078), P(0.512, 0.072)));
            Fill(Sepal, Quad(P(0.512, 0.072), P(0.558, 0.078), P(0.564, 0.048), P(0.518, 0.042)));
        }
        return rtb;
    }

    /// <summary>
    /// 果实轮廓：圆、略扁、下半更饱满，接近真果子那个「圆角方」的剪影。
    /// **旧图标原样搬过来的曲线**，只多了一个 <paramref name="k"/>——
    /// 果汁那一块用的是同一条曲线缩小一号，不是另画一个椭圆，这样谁改了剪影两层都跟着动。
    /// </summary>
    private static Geometry Body(Func<double, double, Point> P, double cx, double cy, double k)
    {
        Point Q(double dx, double dy) => P(cx + dx * k, cy + dy * k);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(Q(0, -0.300), true);
        g.CubicBezierTo(Q(-0.290, -0.300), Q(-0.440, -0.140), Q(-0.440, 0.040));
        g.CubicBezierTo(Q(-0.440, 0.250), Q(-0.265, 0.378), Q(0, 0.378));
        g.CubicBezierTo(Q(0.265, 0.378), Q(0.440, 0.250), Q(0.440, 0.040));
        g.CubicBezierTo(Q(0.440, -0.140), Q(0.290, -0.300), Q(0, -0.300));
        g.EndFigure(true);
        return geo;
    }

    // ── 果腔的几何。**一条中线 + 一个固定半宽**，轮廓是把中线朝两侧各偏移半宽得到的。
    //
    //    ⚠️ 这么写，是因为「**从上到下宽度不变**」（用户 2026-09-16）：手画内外两条曲线
    //    的话，粗细是两条线之差，调任何一条都会把它调歪，而且看不出来。偏移法让宽度
    //    成为**一个常数**，形状怎么改都不会影响它。
    //
    //    ⚠️ 中线**起在果肩、顺着果壁鼓出去、收在底部中央**：两头的间距因此上宽下窄
    //    （顶上 0.26，底下 0.05），加上果腔上方那一整片果肉，中间留出来的正是
    //    用户要的「苹果 / 蝴蝶翅膀」那个形状——**那片形状不画，它是剩出来的**。
    //
    //    ⚠️ 中线整条**偏在下半部**（起点 y=0.45，果子顶在 0.256）：往上挪一点，
    //    头上的半圆帽子就会顶出果肉外面去——果肩那里果肉本来就窄。
    private static readonly (double X, double Y)[] Spine =
        [(0.640, 0.452), (0.735, 0.540), (0.740, 0.716), (0.592, 0.822)];

    private const double CavityHalf = 0.078;   // 半宽。果腔的粗细**只由它决定**

    /// <summary>中线上 t 处的点。</summary>
    private static (double X, double Y) SpineAt(double t)
    {
        var u = 1 - t;
        double B(int i) => u * u * u * Comp(0, i) + 3 * u * u * t * Comp(1, i)
                         + 3 * u * t * t * Comp(2, i) + t * t * t * Comp(3, i);
        return (B(0), B(1));

        static double Comp(int k, int i) => i == 0 ? Spine[k].X : Spine[k].Y;
    }

    /// <summary>中线上 t 处的单位法线（指向果壁那一侧）。</summary>
    private static (double X, double Y) SpineNormal(double t)
    {
        const double h = 1e-3;
        var a = SpineAt(Math.Max(0, t - h));
        var b = SpineAt(Math.Min(1, t + h));
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
        return (dy / len, -dx / len);
    }

    /// <summary>
    /// 一个果腔：从果蒂侧方起，顺着果壁弯下来，收在底部中央附近，**整条偏在果子的下半**。
    ///
    /// ⚠️ 两头是**半圆的帽子**（用户 2026-09-16：头尾不是尖的）：把中线端点当圆心，
    /// 绕半圈就行——帽子的半径跟半宽是同一个数，所以它永远跟带身一样粗。
    ///
    /// ⚠️ 轮廓采样成折线而不是再拟合一条贝塞尔：偏移曲线本来就不是贝塞尔，
    /// 硬拟合会在弯得急的地方**悄悄变细**，而那正是最看得出来的地方。
    /// 32 段在 1024px 下看不出折角。
    /// </summary>
    private static Geometry Cavity(Func<double, double, Point> P, bool mirror, bool _)
    {
        const int steps = 32, cap = 12;
        Point At(double x, double y) => P(mirror ? 1 - x : x, y);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        // ⚠️ **必须 NonZero**：偏移曲线在弯得急的地方会自己叠自己，EvenOdd 会把叠上的
        //    那一小块**挖成洞**——表现是果腔头上无端多出一道浅色的尖楔，看着像画错了边。
        g.SetFillRule(FillRule.NonZero);

        var p0 = SpineAt(0);
        var n0 = SpineNormal(0);
        g.BeginFigure(At(p0.X + n0.X * CavityHalf, p0.Y + n0.Y * CavityHalf), true);

        for (var i = 1; i <= steps; i++)   // 外缘，顺着中线走到底
        {
            var p = SpineAt((double)i / steps);
            var n = SpineNormal((double)i / steps);
            g.LineTo(At(p.X + n.X * CavityHalf, p.Y + n.Y * CavityHalf));
        }
        AddCap(g, At, SpineAt(1), SpineNormal(1), forward: true, cap);
        for (var i = steps - 1; i >= 0; i--)   // 内缘，原路返回
        {
            var p = SpineAt((double)i / steps);
            var n = SpineNormal((double)i / steps);
            g.LineTo(At(p.X - n.X * CavityHalf, p.Y - n.Y * CavityHalf));
        }
        AddCap(g, At, p0, n0, forward: false, cap);

        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// 端点上那个半圆帽子：从「法线一侧」绕到「法线另一侧」，途中经过中线方向。
    ///
    /// ⚠️ **尾帽和头帽的绕行方向相反**：轮廓是一笔画的——外缘走到底、绕尾帽、内缘走回来、
    /// 绕头帽回到起点。尾帽是外→内，头帽必须是内→外。两个都写成外→内的话，头帽会在
    /// 起点处**打个结**，表现是果腔头上凭空多出一道浅色尖楔；改填充规则没用，
    /// 因为轮廓本身就是错的。（2026-09-16 为这个楔子查了一轮。）
    /// </summary>
    private static void AddCap(StreamGeometryContext g, Func<double, double, Point> At,
                               (double X, double Y) p, (double X, double Y) n, bool forward, int steps)
    {
        // 切线 = 法线转 90 度；forward 决定帽子朝前还是朝后鼓，也决定绕行方向
        var tx = -n.Y * (forward ? 1 : -1);
        var ty = n.X * (forward ? 1 : -1);
        for (var k = 1; k <= steps; k++)
        {
            var phi = Math.PI * (forward ? k : steps - k) / steps;
            var c = Math.Cos(phi) * CavityHalf;
            var s = Math.Sin(phi) * CavityHalf;
            g.LineTo(At(p.X + n.X * c + tx * s, p.Y + n.Y * c + ty * s));
        }
    }

    /// <summary>籽的位置：就排在果腔的中线上，两头各留出一个帽子的余量。</summary>
    private static IEnumerable<Point> SeedCentres(Func<double, double, Point> P, bool mirror)
    {
        foreach (var t in new[] { 0.10, 0.30, 0.50, 0.70, 0.90 })
        {
            var p = SpineAt(t);
            yield return P(mirror ? 1 - p.X : p.X, p.Y);
        }
    }

    /// <summary>一片萼：从果蒂往尖端收的细长三角，腰部略鼓。**旧图标原样搬过来**。</summary>
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

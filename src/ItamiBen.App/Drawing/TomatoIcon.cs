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
/// Pure vector, like the dial and the dominoes: a handful of Beziers and ellipses, no
/// bitmap asset, crisp at any size.
///
/// **Colour constraint (set by the user): at most 8 colours, no outline.** Seven here.
/// Depth comes from the brightness difference between flat blocks, never a gradient: a
/// gradient smears into a blur at 16px, while flat blocks still read.
///
/// ⚠️ **Everything is built out of one silhouette** (<see cref="Body"/>, scaled toward the
/// fruit's centre): skin, flesh wall and the locule cavity are the same curve at 1.00 /
/// 0.93 / 0.70. Nesting three hand-drawn outlines instead would let the bands drift apart
/// as soon as anyone tweaks the shape.
/// </summary>
public static class TomatoIcon
{
    // ---- 7 colours, not one more
    private static readonly Color Skin     = Color.FromRgb(0x2F, 0x6B, 0x1F);  // 外皮那一圈深绿
    private static readonly Color Wall     = Color.FromRgb(0xD5, 0xE6, 0xA4);  // 果肉壁，泛白的浅绿
    private static readonly Color Gel      = Color.FromRgb(0x8C, 0xC4, 0x46);  // 籽腔的胶质
    private static readonly Color GelDeep  = Color.FromRgb(0x5F, 0x9B, 0x2E);  // 籽腔下缘的暗面
    private static readonly Color Seed     = Color.FromRgb(0xF4, 0xF1, 0xCF);  // 籽
    private static readonly Color Sepal    = Color.FromRgb(0x24, 0x60, 0x1C);  // 萼片
    private static readonly Color SepalLit = Color.FromRgb(0x3E, 0x8A, 0x2B);  // 压在上面那两片 + 果梗

    /// <summary>果实的重心。<see cref="Body"/> 收缩时朝它收，三圈才会同心。</summary>
    private static readonly Point Heart = new(0.500, 0.628);

    public static RenderTargetBitmap Render(int size = 128)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            Point P(double x, double y) => new(x * size, y * size);
            void Fill(Color c, Geometry g) => ctx.DrawGeometry(new SolidColorBrush(c), null, g);

            // ---- 剖面：皮 → 果肉壁，两圈同心
            Fill(Skin, Body(P, 1.00, 0, 0));
            Fill(Wall, Body(P, 0.93, 0, 0));

            // ---- 两个籽腔。⚠️ **不要画成四瓣的米字**：对称的四条亮芒在小尺寸下
            //      会读成「闪光」而不是「切开的果子」。照片里就是左右两室，
            //      中间那条浅色竖芯是**两室之间露出来的果肉壁**，不是另画的形状。
            foreach (var mirror in new[] { false, true })
            {
                Fill(GelDeep, Locule(P, mirror, 0.008, 0.012));
                Fill(Gel, Locule(P, mirror, 0, 0));
            }

            // ⚠️ **中轴不另画一道更亮的**：真果子的胎座跟果壁本来就是连着的同一块组织，
            //    在中间压一条更白的竖条会读成「叶脉」，整个图标变成一片叶子。
            //    两室之间露出来的果壁就是中轴，颜色相同才对。

            // ---- 籽：贴着胎座排成一道弧，每室五粒
            foreach (var (x, y) in new[]
                     {
                         (0.600, 0.428), (0.646, 0.500), (0.664, 0.586), (0.648, 0.670), (0.604, 0.744),
                         (0.400, 0.428), (0.354, 0.500), (0.336, 0.586), (0.352, 0.670), (0.396, 0.744),
                     })
                Fill(Seed, new EllipseGeometry(new Rect(
                    (x - 0.026) * size, (y - 0.020) * size, 0.052 * size, 0.040 * size)));

            // ---- 萼片：几片小而尖的叶子从果蒂往外张，左右不完全对称才像真的。
            //      ⚠️ **别放长**：伸到果子外沿那么长就成了一圈尖刺，读起来是爆炸不是叶子
            var hub = P(0.500, 0.322);
            foreach (var (tx, ty, w, bend) in new[]
                     {
                         (0.316, 0.204, 0.056, -0.10),   // 最左，几乎平伸
                         (0.378, 0.120, 0.052, -0.13),
                         (0.500, 0.084, 0.048,  0.00),
                         (0.622, 0.118, 0.052,  0.13),
                         (0.684, 0.202, 0.056,  0.10),   // 最右
                     })
                Fill(Sepal, Leaf(hub, P(tx, ty), w * size, bend));

            // 中间两片压在上面，亮一号，叶子就有了层次
            foreach (var (tx, ty, w, bend) in new[] { (0.416, 0.152, 0.058, -0.11), (0.588, 0.150, 0.058, 0.11) })
                Fill(SepalLit, Leaf(hub, P(tx, ty), w * size, bend));

            // ---- 果梗：短短一截，略向右倾
            Fill(Sepal, Quad(P(0.478, 0.322), P(0.522, 0.322), P(0.538, 0.086), P(0.498, 0.080)));
            Fill(SepalLit, Quad(P(0.498, 0.080), P(0.538, 0.086), P(0.544, 0.048), P(0.504, 0.042)));
        }
        return rtb;
    }

    /// <summary>
    /// 切面的轮廓：圆而略扁，顶上因为果蒂有个浅浅的凹，越往下越收，底部一个圆钝的尖。
    ///
    /// <paramref name="k"/> 是朝 <see cref="Heart"/> 收缩的比例，皮 / 果肉 / 籽腔
    /// 用的是**同一条曲线的三个尺寸**；<paramref name="dx"/> / <paramref name="dy"/>
    /// 只给籽腔那一层做明暗错位用。
    /// </summary>
    private static Geometry Body(Func<double, double, Point> P, double k, double dx, double dy)
    {
        Point Q(double x, double y) => P(Heart.X + (x - Heart.X) * k + dx,
                                         Heart.Y + (y - Heart.Y) * k + dy);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(Q(0.500, 0.300), true);
        g.CubicBezierTo(Q(0.660, 0.238), Q(0.845, 0.300), Q(0.905, 0.455));
        g.CubicBezierTo(Q(0.960, 0.600), Q(0.900, 0.810), Q(0.720, 0.905));
        g.CubicBezierTo(Q(0.640, 0.948), Q(0.560, 0.958), Q(0.500, 0.958));
        g.CubicBezierTo(Q(0.440, 0.958), Q(0.360, 0.948), Q(0.280, 0.905));
        g.CubicBezierTo(Q(0.100, 0.810), Q(0.040, 0.600), Q(0.095, 0.455));
        g.CubicBezierTo(Q(0.155, 0.300), Q(0.340, 0.238), Q(0.500, 0.300));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// 一个籽腔：外缘顺着果壁鼓出去，内缘贴着中轴微微内凹，上下收成钝尖。
    /// <paramref name="mirror"/> 把它翻到另一边——**一份形状画两室**，
    /// 左右各写一遍迟早会调歪一边。
    /// </summary>
    private static Geometry Locule(Func<double, double, Point> P, bool mirror, double dx, double dy)
    {
        Point Q(double x, double y) => P((mirror ? 1 - x : x) + (mirror ? -dx : dx), y + dy);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(Q(0.548, 0.306), true);
        g.CubicBezierTo(Q(0.706, 0.340), Q(0.822, 0.458), Q(0.822, 0.606));
        g.CubicBezierTo(Q(0.818, 0.746), Q(0.700, 0.848), Q(0.572, 0.858));
        g.CubicBezierTo(Q(0.594, 0.750), Q(0.590, 0.430), Q(0.548, 0.306));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>两头尖、中间鼓的梭形：中轴和隔膜都是它，只是方向不同。</summary>
    private static Geometry Spindle(Point a, Point b, double width)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
        var nx = -dy / len * width;
        var ny = dx / len * width;
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(a, true);
        g.QuadraticBezierTo(new Point(mid.X + nx, mid.Y + ny), b);
        g.QuadraticBezierTo(new Point(mid.X - nx, mid.Y - ny), a);
        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// 一片萼：从果蒂往尖端收的三角，腰部略鼓。
    ///
    /// <paramref name="bend"/> 把腰部整体推向一侧，叶子就**弯**了。⚠️ 这个参数不是
    /// 装饰：一圈笔直的尖三角从一个点射出去，读起来是**爆炸**不是叶子——弯一下才像
    /// 长出来的东西。
    /// </summary>
    private static Geometry Leaf(Point hub, Point tip, double halfWidth, double bend = 0)
    {
        var dx = tip.X - hub.X;
        var dy = tip.Y - hub.Y;
        var len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
        var nx = -dy / len * halfWidth;
        var ny = dx / len * halfWidth;
        var bx = -dy / len * bend * len;
        var by = dx / len * bend * len;

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(new Point(hub.X + nx, hub.Y + ny), true);
        g.QuadraticBezierTo(new Point(hub.X + dx * 0.55 + nx * 0.85 + bx, hub.Y + dy * 0.55 + ny * 0.85 + by), tip);
        g.QuadraticBezierTo(new Point(hub.X + dx * 0.55 - nx * 0.85 + bx, hub.Y + dy * 0.55 - ny * 0.85 + by),
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

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
/// way out -- no skin band**, and the two **dark green worms**, curving out along the wall
/// and back in, are the juice. The pale column down the middle is not drawn at all: it is
/// simply the flesh left between the two worms.
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

            // ---- 果汁：**左右对称的两条弯虫**，从果蒂下方起、贴着果壁鼓出去、
            //      再收回底部中央。中间和四周剩下的浅色就是果肉，不用另画。
            //      ⚠️ 别再画成「一整块深色 + 白色分隔件」——那样中间那片白色会抢戏，
            //      整个图标读成星形闪光。深色本身就是形状。
            Fill(Juice, Worm(P, false));
            Fill(Juice, Worm(P, true));

            // ---- 籽：顺着虫身排一列
            foreach (var (x, y) in new[]
                     {
                         (0.672, 0.404), (0.716, 0.490), (0.726, 0.584), (0.706, 0.678), (0.652, 0.762),
                         (0.328, 0.404), (0.284, 0.490), (0.274, 0.584), (0.294, 0.678), (0.348, 0.762),
                     })
                Fill(Seed, new EllipseGeometry(new Rect(
                    (x - 0.026) * size, (y - 0.020) * size, 0.052 * size, 0.040 * size)));

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

    /// <summary>
    /// 一条果汁：从果蒂下方起，贴着果壁鼓出去，再收回底部中央——一条两头尖、
    /// 中间宽的弯虫。<paramref name="mirror"/> 翻到另一边，**一份形状画两条**。
    ///
    /// ⚠️ **两头是钝的，不是尖的**（用户 2026-09-16 指出）：收成尖就成了新月，
    /// 照片里果腔的头尾都是圆钝的。
    ///
    /// ⚠️ **中间那条浅色上宽下窄**（我第一版画反了）：胎座在果蒂那头最宽，
    /// 一路收到花萼那头。所以内缘的 x **越往上离中线越远**。
    /// ⚠️ **外缘贴着果壁走、内缘留出中轴**：中间那条浅色不是画出来的，是两条虫之间
    /// 剩下的果肉——这样它永远跟果壁同色，也永远不会抢戏。
    /// </summary>
    private static Geometry Worm(Func<double, double, Point> P, bool mirror)
    {
        Point Q(double x, double y) => P(mirror ? 1 - x : x, y);

        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(Q(0.596, 0.344), true);
        g.CubicBezierTo(Q(0.628, 0.308), Q(0.678, 0.302), Q(0.716, 0.330));   // 头：钝的，不是尖的
        g.CubicBezierTo(Q(0.812, 0.398), Q(0.834, 0.570), Q(0.810, 0.680));   // 外缘，贴着果壁
        g.CubicBezierTo(Q(0.782, 0.780), Q(0.698, 0.828), Q(0.594, 0.842));   // 外缘，收向底部
        g.CubicBezierTo(Q(0.554, 0.848), Q(0.528, 0.826), Q(0.528, 0.790));   // 尾：也是钝的
        g.CubicBezierTo(Q(0.548, 0.726), Q(0.560, 0.648), Q(0.572, 0.560));   // 内缘，往回上
        g.CubicBezierTo(Q(0.586, 0.488), Q(0.596, 0.418), Q(0.596, 0.344));   // 内缘，越往上离中线越远
        g.EndFigure(true);
        return geo;
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

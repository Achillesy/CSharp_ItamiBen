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
/// Anatomy of the cut face, in the order it gets painted (user's correction, 2026-09-16):
/// the fruit is **flesh all the way out -- no skin band**; the **white wings** spreading
/// from the centre are the placenta; the **dark green** pockets they separate are juice.
///
/// Pure vector, like the dial and the dominoes: a handful of Beziers and ellipses, no
/// bitmap asset, crisp at any size.
///
/// **Colour constraint (set by the user): at most 8 colours, no outline.** Seven here.
/// Depth comes from the brightness difference between flat blocks, never a gradient: a
/// gradient smears into a blur at 16px, while flat blocks still read.
/// </summary>
public static class TomatoIcon
{
    // ---- 7 colours, not one more
    private static readonly Color Flesh     = Color.FromRgb(0xCF, 0xE3, 0x94);  // 果肉，青番茄偏黄的浅绿
    private static readonly Color FleshDark = Color.FromRgb(0xA9, 0xC4, 0x70);  // 右下那弯暗面
    private static readonly Color Juice     = Color.FromRgb(0x4E, 0x8E, 0x2A);  // 果汁，深绿
    private static readonly Color JuiceDeep = Color.FromRgb(0x3A, 0x6E, 0x1E);  // 果汁的暗面
    private static readonly Color Pith      = Color.FromRgb(0xF4, 0xF8, 0xE4);  // 中间那副白翅膀（胎座），籽也是它
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

            // ---- 果汁：中间一整块深绿，等下被白翅膀切成四腔。
            //      ⚠️ 这里**不要去画四个独立的腔**：那样四块的边界得手工对齐，
            //      翅膀一改就全歪。一块底 + 一个分隔件，边界永远自洽。
            Fill(JuiceDeep, Body(P, 0.500, 0.564, 0.80));
            Fill(Juice, Body(P, 0.488, 0.552, 0.785));

            // ---- 白翅膀：从果蒂下面一路向下的中轴，中途朝左右张开两片，
            //      这是整个图标的主角（用户原话：「中间是白色像翅膀一样张开的结构」）
            Fill(Pith, Wings(P));

            // ---- 籽：嵌在果汁里，贴着翅膀排
            foreach (var (x, y) in new[]
                     {
                         (0.354, 0.370), (0.268, 0.400),                   // 左上腔
                         (0.646, 0.370), (0.732, 0.400),                   // 右上腔
                         (0.300, 0.648), (0.342, 0.726), (0.406, 0.784),   // 左下腔
                         (0.700, 0.648), (0.658, 0.726), (0.594, 0.784),   // 右下腔
                     })
                Fill(Pith, new EllipseGeometry(new Rect(
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
    /// 中间那副白翅膀：一条中轴从果蒂下面一直到底，中途朝左右各张开一片，
    /// 于是果汁被分成上下左右四腔。
    ///
    /// ⚠️ **翅尖停在果汁的边上就够了**（x 到 0.185 / 0.815，正好压在果汁的边界上）：再往外扎进浅色果肉里，
    /// 白尖对着浅底成了两根长刺，整个图标读成「星形闪光」而不是切开的果子。
    /// 白色只在深色上出现，形状才立得住。
    ///
    /// ⚠️ **翅膀要向上兜**，不能左右拉平：拉平的那版四条白芒等长等角，就是个米字。
    ///
    /// ⚠️ 左右严格对称，中轴不歪：这是唯一一处**对称比生动重要**的地方——歪了看着
    /// 不像切开的果子，像被咬了一口。
    /// </summary>
    private static Geometry Wings(Func<double, double, Point> P)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(P(0.500, 0.262), true);
        // 左半边，从上往下：中轴 → 那片向上兜起来的翅膀（上缘出去、下缘回来）→ 中轴到底
        g.CubicBezierTo(P(0.478, 0.312), P(0.470, 0.382), P(0.464, 0.462));
        g.CubicBezierTo(P(0.400, 0.416), P(0.276, 0.404), P(0.185, 0.468));   // 翅膀上缘，兜上去再到翅尖
        g.CubicBezierTo(P(0.286, 0.502), P(0.398, 0.546), P(0.458, 0.576));   // 翅膀下缘，收回中轴
        g.CubicBezierTo(P(0.466, 0.664), P(0.476, 0.782), P(0.500, 0.884));   // 中轴一路到底
        // 右半边，从下往上（跟左边镜像）
        g.CubicBezierTo(P(0.524, 0.782), P(0.534, 0.664), P(0.542, 0.576));
        g.CubicBezierTo(P(0.602, 0.546), P(0.714, 0.502), P(0.815, 0.468));
        g.CubicBezierTo(P(0.724, 0.404), P(0.600, 0.416), P(0.536, 0.462));
        g.CubicBezierTo(P(0.530, 0.382), P(0.522, 0.312), P(0.500, 0.262));
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

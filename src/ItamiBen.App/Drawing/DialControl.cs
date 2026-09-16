using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 钟面。
///
/// **纯渲染层**：只吃一个 <see cref="MinuteCell"/> 列表和几个标量，自己不做任何判定、
/// 不持有任何累计值。三段色块的位置**直接读 <see cref="Core.Round.Project"/>**，
/// 不在这里重推一遍——那条规则只能存在一份（DECISIONS D1）。
///
/// 几何全部归一化到 rFace = 1.0，12 点是 0°、顺时针、每分钟 6°，跟 v3 逐字相同
/// （DESIGN §6：钟面渲染规格可以直接搬）。「材质与光」那四层也一并搬过来了：
/// 钟在墙上的投影、边框渐变、边框投在盘面上的内阴影、指针自己的投影。**光源在左上。**
/// </summary>
public class DialControl : Control
{
    // ── 各层半径（归一化到 rFace = 1.0）
    private const double RBezelOut = 1.075;
    private const double RNumerals = 0.745;
    private const double RTickMinor = 0.918, RTickMajor = 0.893, RTickOuter = 0.955;
    private const double RHour = 0.55, RMinute = 0.775, RSecond = 0.88;

    /// <summary>闹钟黄针：比分针短、比时针略长（v3 的比例，原样搬）。</summary>
    private const double RAlarm = 0.62;

    /// <summary>
    /// alarms.cron 的小红圈：圆心落在表盘边缘（1.0），**故意允许盖到木框上**——
    /// 跟色环（0.31~0.68）、闹钟黄针（0.62）都不在同一层，不会被看成同一件事。
    /// </summary>
    private const double RAlarmsDot = 1.0, RAlarmsDotRadius = 0.05, RAlarmsDotStroke = 0.022;

    /// <summary>
    /// 点中小红圈的额外容差，**固定像素、不跟着表盘缩放**：红圈在常见尺寸下画出来
    /// 半径只有 7px 左右，纯几何精确点太挑手感。这圈容差是隐形的——不改画出来的样子，
    /// 只放宽「算不算点中」。
    /// </summary>
    private const double AlarmsDotHitPaddingPx = 6.0;

    /// <summary>
    /// 同一分钟不止一条时，圈**里面**再画一个**实心点**。
    /// ⚠️ 半径 0.020 是量出来的：外圈内缘只在 ≈5.5px 处，想在里面塞「环 + 可见间隙」
    /// 两者各只能分到 1px 出头，非 Retina 屏上是一团糊。实心点离外圈内缘还有 2.7px 空白。
    /// **两态靠两个独立信号**（颜色红/橙 + 中心那点），任缺一个另一个仍然读得出来。
    /// </summary>
    private const double RAlarmsDotInner = 0.020;
    private const double RHub = 0.035;

    /// <summary>
    /// 螺旋**只有两圈**——因为环就是 120 分钟，一分钟不多（DECISIONS C4：不环绕、
    /// 不归档、不塌缩）。外圈画第 0~59 分钟，内圈画第 60~119 分钟。
    /// </summary>
    private static readonly (double In, double Out)[] Lanes =
    [
        (0.50, 0.68),   // 第 0~59 分钟
        (0.31, 0.46),   // 第 60~119 分钟
    ];

    /// <summary>
    /// 桶板的最矮高度（占色带径向宽度的比例）。**一半**。
    ///
    /// 真实尺寸下一格的径向跨度只有约 25px，太矮的红短板会看不见——但
    /// ⚠️ **绝不能取 0**：「没记录」画的就是「什么都不画」，零高度会跟它撞上，
    /// 而这正是最不能混的一对：一个不怪你，一个全怪你。
    /// </summary>
    private const double StaveFloor = 0.5;

    public static readonly StyledProperty<DialPalette> PaletteProperty =
        AvaloniaProperty.Register<DialControl, DialPalette>(nameof(Palette), DialPalette.Light);

    /// <summary>环的内容。空列表 = 空盘 = 下一轮的邀请。</summary>
    public static readonly StyledProperty<IReadOnlyList<MinuteCell>> CellsProperty =
        AvaloniaProperty.Register<DialControl, IReadOnlyList<MinuteCell>>(nameof(Cells), []);

    /// <summary>
    /// 本轮起点，决定环从钟面哪个分钟刻度开始画。
    /// 它已经抹到整分（DECISIONS C11），所以**分针就是写头**：环的起点角度
    /// 跟墙上时钟的分针天然重合，不需要任何对齐代码。
    /// </summary>
    public static readonly StyledProperty<DateTimeOffset?> StartedAtProperty =
        AvaloniaProperty.Register<DialControl, DateTimeOffset?>(nameof(StartedAt));

    /// <summary>三段色块的位置。null = 没有正在显示的一轮，只画钟。</summary>
    public static readonly StyledProperty<RingProjection?> ProjectionProperty =
        AvaloniaProperty.Register<DialControl, RingProjection?>(nameof(Projection));

    /// <summary>
    /// 闹钟黄针的位置，从 12 点起算的分钟数（0~719）——直接传
    /// <see cref="Core.AlarmClock.Position"/>。
    ///
    /// ⚠️ **从没拨过针时它是 0，黄针停在 12 点上，照样画**（v3 的 E7：过期或没设的
    /// 闹钟留下的是「黄针残影」）。真实的闹钟本来就长这样：针一直在，只是没上弦。
    /// </summary>
    public static readonly StyledProperty<double> AlarmMinutesProperty =
        AvaloniaProperty.Register<DialControl, double>(nameof(AlarmMinutes));

    /// <summary>
    /// alarms.cron 下一条的角度位置（0~719 分钟），null = 不画。
    /// **由调用方每拍整个重算**——跟盘面上其它一切一样，条件不满足这一拍的结果直接就是
    /// 「不画」，不存在「清除上一次画的圆」这回事。
    /// </summary>
    public static readonly StyledProperty<double?> AlarmsDotMinutesProperty =
        AvaloniaProperty.Register<DialControl, double?>(nameof(AlarmsDotMinutes));

    /// <summary>下一条那一分钟上是不是**不止一条**。</summary>
    public static readonly StyledProperty<bool> AlarmsDotMultipleProperty =
        AvaloniaProperty.Register<DialControl, bool>(nameof(AlarmsDotMultiple));

    public double? AlarmsDotMinutes { get => GetValue(AlarmsDotMinutesProperty); set => SetValue(AlarmsDotMinutesProperty, value); }
    public bool AlarmsDotMultiple { get => GetValue(AlarmsDotMultipleProperty); set => SetValue(AlarmsDotMultipleProperty, value); }

    public DialPalette Palette { get => GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }
    public double AlarmMinutes { get => GetValue(AlarmMinutesProperty); set => SetValue(AlarmMinutesProperty, value); }
    public IReadOnlyList<MinuteCell> Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }
    public DateTimeOffset? StartedAt { get => GetValue(StartedAtProperty); set => SetValue(StartedAtProperty, value); }
    public RingProjection? Projection { get => GetValue(ProjectionProperty); set => SetValue(ProjectionProperty, value); }

    static DialControl()
        => AffectsRender<DialControl>(PaletteProperty, CellsProperty, StartedAtProperty,
                                      ProjectionProperty, AlarmMinutesProperty,
                                      AlarmsDotMinutesProperty, AlarmsDotMultipleProperty);

    // 12 点是 0°，顺时针，每分钟 6°
    private static Point At(Point c, double r, double deg)
    {
        var rad = (deg - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    private static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    private static readonly Color Shadow = Color.FromArgb(0xFF, 0, 0, 0);

    private (Point Center, double RFace)? FaceGeometry()
    {
        var box = Math.Min(Bounds.Width, Bounds.Height);
        if (box <= 0) return null;

        // 给投影留余量，否则会被控件边界裁掉
        var rFace = box / 2 / (RBezelOut + 0.10);
        return (new Point(Bounds.Width / 2, Bounds.Height / 2 - rFace * 0.03), rFace);
    }

    /// <summary>
    /// 这一下按下有没有落在小红圈上。
    ///
    /// ⚠️ **不做成 Button**：Button 内部会把 `PointerPressed` 标成已处理，挂在钟面上的
    /// 普通订阅就收不到了（拖窗口正是那么挂的）。改成纯几何判断，跟拖拽**共用同一次
    /// 按下事件**，命中就分岔、不命中照旧拖。
    ///
    /// ⚠️ **必须在按下那一刻判，不能等松开**：`BeginMoveDrag` 只能在按下时调用，
    /// 等「松开算不算点击」那套判完，拖拽的时机早就过了。
    ///
    /// ⚠️ 没画红圈时恒为 false——**没画出来的东西不该点得中**。
    /// </summary>
    public bool HitTestAlarmsDot(Point point)
    {
        if (AlarmsDotMinutes is not { } minutes) return false;
        if (FaceGeometry() is not { } geometry) return false;
        var (c, rFace) = geometry;

        var at = At(c, rFace * RAlarmsDot, minutes % 720 / 2.0);   // 跟 DrawAlarmsDot 同一套公式
        var hit = rFace * RAlarmsDotRadius + AlarmsDotHitPaddingPx;

        var dx = point.X - at.X;
        var dy = point.Y - at.Y;
        return dx * dx + dy * dy <= hit * hit;
    }

    public override void Render(DrawingContext ctx)
    {
        if (FaceGeometry() is not { } geometry) return;
        var (c, rFace) = geometry;
        double R(double n) => n * rFace;

        DrawDropShadow(ctx, c, R, rFace);
        DrawBezel(ctx, c, R);
        DrawFace(ctx, c, R);
        DrawRing(ctx, c, R);
        DrawTicks(ctx, c, R, rFace);
        DrawNumerals(ctx, c, R, rFace);
        DrawHands(ctx, c, R, rFace);
        DrawAlarmsDot(ctx, c, R);
    }

    /// <summary>
    /// alarms.cron 下一条的小红圈。空心圆环而不是实心点，圆心移到表盘边缘、故意盖到
    /// 木框上，好让它在一堆细刻度和数字旁边一眼就能看见。
    /// </summary>
    private void DrawAlarmsDot(DrawingContext ctx, Point c, Func<double, double> R)
    {
        if (AlarmsDotMinutes is not { } minutes) return;
        var at = At(c, R(RAlarmsDot), minutes % 720 / 2.0);   // 720 分钟 = 360°，跟黄针同一个换算

        var ring = AlarmsDotMultiple ? Palette.AlarmsDotOuter : Palette.AlarmsDot;
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(ring), R(RAlarmsDotStroke)),
                        at, R(RAlarmsDotRadius), R(RAlarmsDotRadius));

        if (AlarmsDotMultiple)
            ctx.DrawEllipse(new SolidColorBrush(Palette.AlarmsDot), null,
                            at, R(RAlarmsDotInner), R(RAlarmsDotInner));
    }

    /// <summary>钟投在墙上的影子。压扁、下移、由黑渐隐。</summary>
    private static void DrawDropShadow(DrawingContext ctx, Point c, Func<double, double> R, double rFace)
    {
        var center = new Point(c.X, c.Y + rFace * 0.06);
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(A(Shadow, 0x55), 0.72),
                new GradientStop(A(Shadow, 0x30), 0.88),
                new GradientStop(A(Shadow, 0x00), 1.0),
            }
        };
        ctx.DrawEllipse(brush, null, center, R(RBezelOut) * 1.06, R(RBezelOut) * 0.98);
    }

    /// <summary>木边框。左上受光、右下背光的线性渐变，才读得出「一圈有厚度的木头」。</summary>
    private void DrawBezel(DrawingContext ctx, Point c, Func<double, double> R)
    {
        var p = Palette;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.15, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.85, 1.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(p.BezelLit, 0.0),
                new GradientStop(p.BezelMid, 0.55),
                new GradientStop(p.BezelDark, 1.0),
            }
        };
        ctx.DrawEllipse(brush, null, c, R(RBezelOut), R(RBezelOut));

        // 最外缘那圈亮边：弧面转过去的地方仍然吃得到光。没有它整圈会读成「一块扁平的棕色饼」
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(A(p.BezelLit, 0x70)), R(0.012)),
            c, R(RBezelOut) - R(0.008), R(RBezelOut) - R(0.008));

        // 边框和盘面之间那道暗线
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(A(Shadow, 0x44)), R(0.014)), c, R(1.008), R(1.008));
    }

    /// <summary>盘面：极淡的左上高光（玻璃感）+ 边框投在盘面上的内阴影。</summary>
    private void DrawFace(DrawingContext ctx, Point c, Func<double, double> R)
    {
        var p = Palette;

        ctx.DrawEllipse(new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.36, 0.30, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(p.Face, 0.0),
                new GradientStop(p.Face, 0.62),
                new GradientStop(p.FaceRim, 1.0),
            }
        }, null, c, R(1.0), R(1.0));

        ctx.DrawEllipse(new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(A(Shadow, 0x00), 0.80),
                new GradientStop(A(Shadow, 0x0C), 0.95),
                new GradientStop(A(Shadow, 0x22), 1.0),
            }
        }, null, c, R(1.0), R(1.0));
    }

    /// <summary>
    /// 环：**过去**是格子（绿 / 红 / 空白），**未来**是三段色块（灰承诺弧 / 淡蓝休息 / 空白余量）。
    ///
    /// 这两半在同一条环上首尾相接，分针就是交界处——这正是 DESIGN §5 那张图。
    /// </summary>
    private void DrawRing(DrawingContext ctx, Point c, Func<double, double> R)
    {
        if (StartedAt is not { } start || Projection is not { } proj) return;
        var p = Palette;

        // 起点在钟面上的角度。start 已经抹到整分（C11），Second 恒为 0；这一项留着，
        // 万一将来起点不再对齐，画出来会是错位而不是**安静地错**
        var m0 = start.Minute + start.Second / 60.0;

        // ── 过去：一分钟一格。**颜色和高度编码同一个量**（DESIGN §4.4）
        //
        // 「这一格该读成什么」由 `cell.Tier` 决定（判定层，规则只写一份），这里只管
        // 「某种读法画成什么样」。颜色走绿→黄→红的三段色阶，高度跟着一起变——
        // 一个给正常视觉，一个给所有人（红绿是最常见的色盲混淆对）。
        foreach (var cell in Cells)
        {
            var lane = Math.Min(cell.Index / 60, Lanes.Length - 1);
            var (rIn, rOut) = Lanes[lane];
            var d0 = (m0 + cell.Index) * 6;
            var d1 = d0 + 6;

            switch (cell.Tier)
            {
                case CellTier.FocusFull: Stave(ctx, c, R, rIn, rOut, d0, d1, p.Focus, 1.00); break;
                case CellTier.FocusMid: Stave(ctx, c, R, rIn, rOut, d0, d1, p.Ramp(0.5), 0.80); break;
                case CellTier.FocusLow: Stave(ctx, c, R, rIn, rOut, d0, d1, p.Ramp(0.8), 0.60); break;
                case CellTier.OffTask: Stave(ctx, c, R, rIn, rOut, d0, d1, p.OffTask, 0.50); break;

                // 人不在：**空心虚线框，满高**。一个没有填充的形状，读起来正是
                // 「这段时间存在，但不属于任何一边」——跟红格（全怪你）和什么都不画
                // （没记录）都区分得开
                case CellTier.Away:
                    ctx.DrawGeometry(null,
                        new Pen(new SolidColorBrush(p.Absent), R(0.012))
                        { DashStyle = new DashStyle([2, 2], 0) },
                        Annulus(c, R(rIn), R(rOut), d0 + 0.4, d1 - 0.4));
                    break;

                    // CellTier.NotDrawn：什么都不画。落到这里的只有漏拍留下的洞，
                    // 它不配占任何视觉面积
            }
        }

        // ── 未来：灰色承诺弧 → 淡蓝休息块 → 空白余量
        //
        // ⚠️ 灰色那段**不能省**（DECISIONS D3）：省了就是空白，跟余量那段的空白长得
        // 一模一样，两个完全不同的含义撞色，三段读数当场塌成两段。

        // 把「从 Start 起第 a 秒到第 b 秒」画到环上，**跨圈自动拆开**：
        // 外圈是 [0, 3600)、内圈是 [3600, 7200)，一段承诺弧完全可能横跨两圈，
        // 不拆的话内外圈会连成一条不存在的弧。
        void Span(double a, double b, Action<double, double, double, double> draw)
        {
            a = Math.Clamp(a, 0, Round.RingSeconds);
            b = Math.Clamp(b, 0, Round.RingSeconds);
            if (b <= a) return;

            for (var lane = 0; lane < Lanes.Length; lane++)
            {
                var from = Math.Max(a, lane * 3600.0);
                var to = Math.Min(b, (lane + 1) * 3600.0);
                if (to <= from) continue;

                var (rIn, rOut) = Lanes[lane];
                var d0 = (m0 + from / 60.0) * 6;
                var d1 = (m0 + to / 60.0) * 6;
                // 整整一圈时起点和终点重合，圆弧会画成空——退那么一点点
                if (d1 - d0 >= 359.99) d1 = d0 + 359.99;
                draw(rIn, rOut, d0, d1);
            }
        }

        Span(proj.HandSeconds, proj.CommitEndSeconds, (rIn, rOut, d0, d1) =>
        {
            using (ctx.PushOpacity(0.32))
                ctx.DrawGeometry(new SolidColorBrush(p.Commit), null, Annulus(c, R(rIn), R(rOut), d0, d1));
        });

        // 淡蓝块：**预告**和**已发生**两种画法（DECISIONS D2）——不分开的话，分针走进
        // 休息区时画面毫无变化，用户无法确认休息到底开始没有。
        // 分界就是分针：已经走过的那一段填实，还没走到的那一段描边 + 淡填。
        var breakSeen = Math.Clamp(proj.HandSeconds, proj.BreakStartSeconds, proj.BreakEndSeconds);

        Span(proj.BreakStartSeconds, breakSeen, (rIn, rOut, d0, d1) =>
            ctx.DrawGeometry(new SolidColorBrush(p.Break), null, Annulus(c, R(rIn), R(rOut), d0, d1)));

        Span(breakSeen, proj.BreakEndSeconds, (rIn, rOut, d0, d1) =>
            ctx.DrawGeometry(new SolidColorBrush(A(p.Break, 0x38)),
                             new Pen(new SolidColorBrush(p.Break), R(0.008)),
                             Annulus(c, R(rIn), R(rOut), d0, d1)));

        // 余量那段什么都不画——DESIGN §5：「淡蓝块尾 → 环终点 = 你还剩多少余量」，
        // 它是被前面三段挤剩下的那一截，**不是一个要画的东西**。
    }

    /// <summary>
    /// 一块桶板：**从内缘往外长**，高度是 <paramref name="height"/>。
    /// 内圈保持一个干净的圆，参差不齐那一边冲着刻度。
    /// </summary>
    private void Stave(DrawingContext ctx, Point c, Func<double, double> R,
                       double rIn, double rOut, double d0, double d1, Color tint, double height)
    {
        var top = rIn + (rOut - rIn) * Math.Max(StaveFloor, height);
        ctx.DrawGeometry(new SolidColorBrush(tint),
            new Pen(new SolidColorBrush(A(Palette.Face, 0xCC)), R(0.005)),
            Annulus(c, R(rIn), R(top), d0, d1));
    }

    private void DrawTicks(DrawingContext ctx, Point c, Func<double, double> R, double rFace)
    {
        for (var i = 0; i < 60; i++)
        {
            var major = i % 5 == 0;
            var pen = new Pen(new SolidColorBrush(major ? Palette.Ink : Palette.Tick),
                              rFace * (major ? 0.026 : 0.0105))
            { LineCap = PenLineCap.Flat };
            ctx.DrawLine(pen, At(c, R(major ? RTickMajor : RTickMinor), i * 6), At(c, R(RTickOuter), i * 6));
        }
    }

    private void DrawNumerals(DrawingContext ctx, Point c, Func<double, double> R, double rFace)
    {
        foreach (var (n, deg) in Enumerable.Range(1, 12).Select(n => (n, n * 30)))
        {
            var ft = new FormattedText(n.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.Bold),
                rFace * 0.185, new SolidColorBrush(Palette.Ink));
            var at = At(c, R(RNumerals), deg);
            ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
        }
    }

    /// <summary>
    /// 指针角度是 <c>纯函数(now)</c>，**没有累加器**——于是丢帧和系统睡眠之后的漂移
    /// 结构上就不存在。
    ///
    /// **秒针一秒一跳，不扫**：扫针来自连续驱动的机芯，跳针来自擒纵机构，现实里这两者
    /// 互斥。这只钟要的是「跳」。
    ///
    /// 每根针先用半透明黑偏移画一遍，那是它投在盘面上的影子（光源在左上）。
    /// </summary>
    private void DrawHands(DrawingContext ctx, Point c, Func<double, double> R, double rFace)
    {
        var now = DateTime.Now;
        var sec = (double)now.Second;
        var min = now.Minute + sec / 60.0;
        var hour = now.Hour % 12 + min / 60.0;

        var shift = Matrix.CreateTranslation(rFace * 0.014, rFace * 0.018);
        var shadowBrush = new SolidColorBrush(A(Shadow, 0x38));

        // 闹钟黄针：720 分钟 = 360°，所以位置除以 2 就是角度
        var alarmGeo = Taper(c, AlarmMinutes % 720 / 2.0, R(RAlarm), rFace * 0.024, rFace * 0.006, rFace * 0.06);
        var hourGeo = Taper(c, hour * 30, R(RHour), rFace * 0.030, rFace * 0.013, rFace * 0.10);
        var minGeo = Taper(c, min * 6, R(RMinute), rFace * 0.022, rFace * 0.008, rFace * 0.10);
        var secPen = new Pen(new SolidColorBrush(Palette.Sweep), rFace * 0.008) { LineCap = PenLineCap.Round };
        var secShadowPen = new Pen(shadowBrush, rFace * 0.008) { LineCap = PenLineCap.Round };
        var tail = At(c, -rFace * 0.16, sec * 6);
        var tip = At(c, R(RSecond), sec * 6);

        using (ctx.PushTransform(shift))
        {
            ctx.DrawGeometry(shadowBrush, null, alarmGeo);
            ctx.DrawGeometry(shadowBrush, null, hourGeo);
            ctx.DrawGeometry(shadowBrush, null, minGeo);
            ctx.DrawLine(secShadowPen, tail, tip);
        }

        // ⚠️ 黄针画在时针**前面**，所以「被时针盖住」= 到点了——不需要任何额外状态
        ctx.DrawGeometry(new SolidColorBrush(Palette.Alarm), null, alarmGeo);
        ctx.DrawGeometry(new SolidColorBrush(Palette.Ink), null, hourGeo);
        ctx.DrawGeometry(new SolidColorBrush(Palette.Ink), null, minGeo);
        ctx.DrawLine(secPen, tail, tip);

        ctx.DrawEllipse(new SolidColorBrush(Palette.Ink), null, c, R(RHub), R(RHub));
        ctx.DrawEllipse(new SolidColorBrush(A(Palette.Face, 0x99)), null, c, R(RHub * 0.34), R(RHub * 0.34));
    }

    /// <summary>一根针：根部宽、针尖细，带一小截配重尾。</summary>
    private static StreamGeometry Taper(Point c, double deg, double len, double wBase, double wTip, double tail)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        var perp = deg + 90;
        Point P(double r, double w, int sign) => At(At(c, r, deg), w * sign, perp);

        g.BeginFigure(P(-tail, wBase * 0.7, 1), true);
        g.LineTo(P(len, wTip, 1));
        g.LineTo(P(len, wTip, -1));
        g.LineTo(P(-tail, wBase * 0.7, -1));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>环形扇区 [d0, d1)。</summary>
    private static StreamGeometry Annulus(Point c, double rIn, double rOut, double d0, double d1)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        var large = d1 - d0 > 180;
        g.BeginFigure(At(c, rOut, d0), true);
        g.ArcTo(At(c, rOut, d1), new Size(rOut, rOut), 0, large, SweepDirection.Clockwise);
        g.LineTo(At(c, rIn, d1));
        g.ArcTo(At(c, rIn, d0), new Size(rIn, rIn), 0, large, SweepDirection.CounterClockwise);
        g.EndFigure(true);
        return geo;
    }
}

using Avalonia.Media;

namespace ItamiBen.App;

/// <summary>
/// 钟面的两套配色。**换主题换的是表盘的照明，不是把这只钟换成另一只钟**——
/// 所以木边框两套逐字相同（v3 的 M10，用户 2026-08-29：钟的木制边框不要变）。
///
/// 光源统一假定在**左上**。
/// </summary>
public sealed record DialPalette(
    Color Face, Color FaceRim, Color Ink, Color Tick,
    Color BezelLit, Color BezelMid, Color BezelDark,
    Color Focus, Color Amber, Color OffTask, Color Absent, Color Commit, Color Break, Color Sweep, Color Alarm, Color AlarmsDot, Color AlarmsDotOuter, Color Card,
    Color DominoTop, Color DominoFace, Color DominoSide)
{
    /// <summary>日面：白表盘 + 木边框（照着用户给的那张实物挂钟照片调的）。</summary>
    public static readonly DialPalette Light = new(
        Face: Color.FromRgb(0xFF, 0xFF, 0xFF),
        FaceRim: Color.FromRgb(0xF2, 0xF3, 0xF5),     // 表盘边缘，边框把光挡住的地方
        Ink: Color.FromRgb(0x1B, 0x22, 0x2A),
        Tick: Color.FromRgb(0x5A, 0x63, 0x6D),
        BezelLit: Color.FromRgb(0xB5, 0x7C, 0x4C),
        BezelMid: Color.FromRgb(0x8C, 0x58, 0x30),
        BezelDark: Color.FromRgb(0x5A, 0x35, 0x1C),
        Focus: Color.FromRgb(0x2F, 0xA3, 0x6B),
        Amber: Color.FromRgb(0xE0, 0xA0, 0x3A),
        OffTask: Color.FromRgb(0xD6, 0x45, 0x3F),
        // 「人不在」那个空心虚线框的描边色
        Absent: Color.FromRgb(0x8A, 0x94, 0xA0),
        Commit: Color.FromRgb(0x8A, 0x94, 0xA0),      // 灰色承诺弧。⚠️ 不能省（DECISIONS D3）
        Break: Color.FromRgb(0x7F, 0xB2, 0xDD),       // 淡蓝休息块
        Sweep: Color.FromRgb(0x33, 0x40, 0x4B),
        // 闹钟黄针：老式闹钟那种暖黄。比分针短、比时针粗
        Alarm: Color.FromRgb(0xF0, 0xC0, 0x40),
        // schedule.md 的小红圈。⚠️ 独立色号，**别复用 OffTask 的红**——数值相近也不共用，
        // 以后想单独调其中一个不会牵动另一个（v3 的 J 组同款理由）
        AlarmsDot: Color.FromRgb(0xD6, 0x45, 0x3F),
        // 同一分钟不止一条时外圈换橙。⚠️ 取值要偏艳橙：这个标记的圆心压在木框上
        // （#B57C4C），淡橙会糊进木色里，而红色本来就是靠对比度选的
        AlarmsDotOuter: Color.FromRgb(0xE8, 0x6A, 0x16),
        // 控件区那块实心底色。窗口透明之后没有它，按钮和文字就直接浮在桌面上
        Card: Color.FromRgb(0xF4, 0xF5, 0xF7),
        // 骨牌：**木头**，比钟面的木边框浅一档，免得抢戏。镜像之后侧面落在左边、
        // 正对左上的光，所以它是受光面——但只需要**稍微**亮一点，差太多就不像同一块木头了。
        // DominoTop 留着但用不到：相机高度跟骨牌顶面齐平，顶面永远看不见
        DominoTop: Color.FromRgb(0xE2, 0xC6, 0xA4),
        DominoFace: Color.FromRgb(0xC4, 0x9E, 0x74),
        DominoSide: Color.FromRgb(0xE6, 0xC8, 0xA4));

    /// <summary>
    /// 夜面。⚠️ <see cref="Break"/> **单独调亮一档**（DECISIONS D4）：
    /// 淡蓝在深底上容易发灰发脏，两个模式共用一个值必然有一边难看。
    /// 木边框（<c>Bezel*</c>）跟日面**逐字相同**，别「顺手」调暗。
    /// </summary>
    public static readonly DialPalette Dark = new(
        Face: Color.FromRgb(0x20, 0x27, 0x2F),
        FaceRim: Color.FromRgb(0x14, 0x19, 0x1F),
        Ink: Color.FromRgb(0xE4, 0xE9, 0xEF),
        Tick: Color.FromRgb(0x8A, 0x97, 0xA4),
        BezelLit: Color.FromRgb(0xB5, 0x7C, 0x4C),
        BezelMid: Color.FromRgb(0x8C, 0x58, 0x30),
        BezelDark: Color.FromRgb(0x5A, 0x35, 0x1C),
        Focus: Color.FromRgb(0x46, 0xBE, 0x84),
        Amber: Color.FromRgb(0xED, 0xB2, 0x55),
        OffTask: Color.FromRgb(0xE9, 0x63, 0x5C),
        Absent: Color.FromRgb(0x6E, 0x7A, 0x87),
        Commit: Color.FromRgb(0x6E, 0x7A, 0x87),
        Break: Color.FromRgb(0x8F, 0xC4, 0xEE),       // 比日面亮一档，见 D4
        Sweep: Color.FromRgb(0xB8, 0xC4, 0xD0),
        Alarm: Color.FromRgb(0xF5, 0xD0, 0x50),
        AlarmsDot: Color.FromRgb(0xE9, 0x63, 0x5C),
        AlarmsDotOuter: Color.FromRgb(0xFA, 0x8A, 0x3C),
        Card: Color.FromRgb(0x25, 0x2B, 0x33),
        DominoTop: Color.FromRgb(0x8A, 0x6E, 0x50),
        DominoFace: Color.FromRgb(0x6E, 0x56, 0x3C),
        DominoSide: Color.FromRgb(0x93, 0x77, 0x57));

    /// <summary>
    /// 绿 → 黄 → 红 三段过渡（<paramref name="impurity"/> 0 = 纯专注，1 = 全跑偏）。
    ///
    /// ⚠️ **为什么不直接插值 RGB**：绿红直接插值在 50% 处会落到一坨发暗的橄榄绿，
    /// 看着像画错了。绕道黄色干净，而且**白捡一个亮度变化**——红绿是最常见的色盲
    /// 混淆对（约 8% 的男性），亮度差是颜色之外的第二个信号。
    /// </summary>
    public Color Ramp(double impurity)
    {
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));

        impurity = Math.Clamp(impurity, 0, 1);
        return impurity <= 0.5
            ? Lerp(Focus, Amber, impurity / 0.5)
            : Lerp(Amber, OffTask, (impurity - 0.5) / 0.5);
    }

    /// <summary>
    /// 跑偏闪烁用的**半反色调色板**：只把**钟面、刻度、指针**换成另一档，其余原样保留。
    ///
    /// 翻的五个：<see cref="Face"/>、<see cref="FaceRim"/>、<see cref="Ink"/>
    /// （数字 + 时针分针 + 轴心）、<see cref="Tick"/>、<see cref="Sweep"/>（秒针）。
    ///
    /// **不翻的，以及为什么**：
    /// <list type="bullet">
    ///   <item>木边框——换的是表盘的照明，不是换一只钟；</item>
    ///   <item>色环的绿 / 红、淡蓝休息块、闹钟黄针、提醒小红圈——**那是账本本身**，
    ///         反了就把「绿 = 专注、红 = 跑偏」这套语义拆了；</item>
    ///   <item>骨牌——它压根不在钟面上。</item>
    /// </list>
    /// </summary>
    public DialPalette WithFaceFrom(DialPalette other) => this with
    {
        Face = other.Face,
        FaceRim = other.FaceRim,
        Ink = other.Ink,
        Tick = other.Tick,
        Sweep = other.Sweep,
    };
}

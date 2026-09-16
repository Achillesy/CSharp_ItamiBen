using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 把钟面在几个关键状态下**离屏渲染成 PNG**，供人眼检查几何。
///
/// ⚠️ **为什么需要它**：钟面活在 App 层，Core 的测试够不到。v3 那个「承诺弧跨整点时
/// 跳一整圈」的 bug 正是这么漏到线上的，最后靠用户在真实使用中发现。
/// **有些几何问题（半径、角度、图层顺序）真的只能靠看图抓。**
///
/// ⚠️ **这里不手搓 `MinuteCell`，而是喂一个真的 <see cref="Round"/>**（v3 那边是手搓的）：
/// 手搓等于把「秒数怎么变成档位」这套规则在测试里**又写了一遍**，两边迟早分家——
/// 而分家之后样张会**让错误的渲染看起来是对的**。走真 `Round`，样张里的每一格
/// 都是判定引擎自己算出来的。
///
/// 用法：<c>ItamiBen --dial-specimens &lt;输出目录&gt;</c>，渲染完立刻退出，不开窗口。
/// **这是调试出口，不是产品功能**，正常启动路径一点都不经过它。
/// </summary>
internal static class DialSpecimens
{
    private const int Size = 480;

    /// <summary>样张里用的规则：`Code` 算专注，别的都不算。</summary>
    private static readonly GoalRules Rules = GoalRules.Parse(
        """{ "Groups": { "Focus": { "Rules": [ { "App": "^Code$" } ] } } }""");

    private static readonly string[] Goals = ["Focus"];

    public static void Render(string outDir)
    {
        Directory.CreateDirectory(outDir);

        // v3 那个 bug 的现场：23:59 开始，跨过午夜
        var midnight = new DateTimeOffset(2026, 9, 16, 23, 59, 0, TimeSpan.FromHours(8));
        var morning = new DateTimeOffset(2026, 9, 16, 10, 10, 0, TimeSpan.FromHours(8));

        // 01 刚按下 Start：一格都还没有，休息块在最远处
        Save(outDir, "01-just-started", Fresh(morning, 25), morning);

        // 02 五种画法各来一格——**最要紧的一张**：绿有三档、红、空心虚线框、什么都不画，
        //    六种读数必须一眼分得开
        var tiers = Fresh(morning, 25);
        Minute(tiers, morning, 0, focused: 60, offTask: 0, away: 0);    // 满格绿
        Minute(tiers, morning, 1, focused: 35, offTask: 25, away: 0);   // 中档
        Minute(tiers, morning, 2, focused: 15, offTask: 45, away: 0);   // 低档
        Minute(tiers, morning, 3, focused: 0, offTask: 60, away: 0);    // 全红
        Minute(tiers, morning, 4, focused: 0, offTask: 0, away: 60);    // 人不在：空心虚线框
        Minute(tiers, morning, 5, focused: 0, offTask: 0, away: 0);     // 一秒没采：什么都不画
        tiers.Advance(morning.AddMinutes(6));
        Save(outDir, "02-one-of-each-tier", tiers, morning);

        // 03 同一批格子换暗色主题：明暗两套色要各自成立，不是把亮色调暗
        Save(outDir, "03-one-of-each-tier-dark", tiers, morning, DialPalette.Dark);

        // 04 跨整点：v3 栽过的那一幕
        var cross = Fresh(midnight, 25);
        for (var i = 0; i < 3; i++) Minute(cross, midnight, i, 60, 0, 0);
        cross.Advance(midnight.AddMinutes(3));
        Save(outDir, "04-crosses-midnight", cross, midnight);

        // 05 真实一点的样子：大部分满格，中间零星几块矮桶板
        var real = Fresh(morning, 50);
        int[] mix = [60, 60, 60, 31, 60, 60, 60, 60, 12, 60, 60, 47, 60, 60, 60, 0, 60, 60];
        for (var i = 0; i < mix.Length; i++) Minute(real, morning, i, mix[i], 60 - mix[i], 0);
        real.Advance(morning.AddMinutes(mix.Length));
        Save(outDir, "05-mostly-full-with-a-few-short-staves", real, morning);

        // 06 桶板从满到零连续变化：检查「越短越红」这套读数在整条色阶上都立得住
        var barrel = Fresh(morning, 50);
        for (var i = 0; i < 20; i++)
        {
            var f = (int)Math.Round(60.0 * (1 - i / 19.0));
            Minute(barrel, morning, i, f, 60 - f, 0);
        }
        barrel.Advance(morning.AddMinutes(20));
        Save(outDir, "06-barrel-from-full-to-zero", barrel, morning);

        // 07 浪费了很多之后：**休息块被推得很远**——这就是全部的惩罚，它必须一眼可见
        var wasted = Fresh(morning, 25);
        for (var i = 0; i < 40; i++) Minute(wasted, morning, i, i % 4 == 0 ? 60 : 0, i % 4 == 0 ? 0 : 60, 0);
        wasted.Advance(morning.AddMinutes(40));
        Save(outDir, "07-rest-block-pushed-far-out", wasted, morning);

        // 08 达成之后进入休息：环还在，休息块就在指针前面
        var done = Fresh(morning, 10);
        for (var i = 0; i < 10; i++) Minute(done, morning, i, 60, 0, 0);
        done.Advance(morning.AddMinutes(12));
        Save(outDir, "08-achieved-now-resting", done, morning);

        // 09 黄色闹钟针 + 小红圈（12 小时内的下一条提醒），而且是多条
        Save(outDir, "09-alarm-hand-and-alarms-dot", tiers, morning,
             alarmMinutes: 38, alarmsDotMinutes: 17, alarmsDotMultiple: true);

        // 10 半透明：layout.json 的 opacity 走的是 OpacityMask，**整层合成后再降透**，
        //    不是逐笔半透——逐笔会让木色透过白钟面混上来，读成「表盘泛黄」
        Save(outDir, "10-translucent-60-percent", tiers, morning, mask: 0.60);

        Console.WriteLine($"dial specimens written to {outDir}");
    }

    private static Round Fresh(DateTimeOffset start, int focusMinutes)
        => new(start, focusMinutes, Goals, Rules);

    /// <summary>
    /// 把第 <paramref name="index"/> 分钟喂成指定的构成。
    ///
    /// ⚠️ 三个数加起来**可以小于 60**：差额就是「一秒都没采到」，那一格什么都不画。
    /// 这一档跟「人不在」长得完全不同，样张的存在意义之一就是盯住这个区别。
    /// </summary>
    private static void Minute(Round round, DateTimeOffset start, int index, int focused, int offTask, int away)
    {
        var from = start.AddMinutes(index);
        var s = 0;
        for (var i = 0; i < focused; i++, s++) round.Observe(from.AddSeconds(s), "Code", "Round.cs");
        for (var i = 0; i < offTask; i++, s++) round.Observe(from.AddSeconds(s), "Google Chrome", "bilibili");
        for (var i = 0; i < away; i++, s++) round.Observe(from.AddSeconds(s), "loginwindow", "Login", away: true);
    }

    private static void Save(string dir, string name, Round round, DateTimeOffset startedAt,
                             DialPalette? palette = null,
                             double alarmMinutes = -1, double? alarmsDotMinutes = null,
                             bool alarmsDotMultiple = false, double mask = 1.0)
    {
        var p = palette ?? DialPalette.Light;
        var dial = new DialControl
        {
            Width = Size,
            Height = Size,
            Palette = p,
            StartedAt = startedAt,
            Cells = round.Cells,
            Projection = round.Project(),
            AlarmMinutes = alarmMinutes,
            AlarmsDotMinutes = alarmsDotMinutes,
            AlarmsDotMultiple = alarmsDotMultiple,
            // ⚠️ 用 OpacityMask 不是 Opacity：Avalonia 的 `Visual.Opacity` 是**逐个绘制操作**
            //    各自半透，木色会透过白钟面混上来（v3 的 K29，实测出来的）
            OpacityMask = mask < 1.0 ? new SolidColorBrush(Colors.White, mask) : null,
        };

        // ⚠️ 垫一层背景再渲染：钟面画在透明底上，直接存 PNG 的话半透明那张在看图器里
        //    是叠在棋盘格上的，**根本看不出它到底透了多少**
        var frame = new Border
        {
            Width = Size,
            Height = Size,
            Background = new SolidColorBrush(p.Face == DialPalette.Dark.Face
                ? Color.FromRgb(0x10, 0x12, 0x16)
                : Color.FromRgb(0xE8, 0xEA, 0xEE)),
            Child = dial,
        };
        frame.Measure(new Size(Size, Size));
        frame.Arrange(new Rect(0, 0, Size, Size));

        using var bmp = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        bmp.Render(frame);
#pragma warning disable CS0618 // Avalonia 12 把无参 Save 标了过时；这是调试出口，默认 PNG 够用
        bmp.Save(Path.Combine(dir, name + ".png"));
#pragma warning restore CS0618
    }
}

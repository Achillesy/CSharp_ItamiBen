using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ItamiBen.App;

/// <summary>程序自己的东西放在哪。</summary>
public static class AppData
{
    /// <summary>
    /// 运行时目录。
    ///
    /// <code>
    /// Windows   %LOCALAPPDATA%\ItamiBen
    /// macOS     ~/Library/Application Support/ItamiBen
    /// </code>
    ///
    /// ⚠️ **跟 v3 完全隔离，不共用任何文件**（DECISIONS A6）：两个程序的规则语义会各自
    /// 演进，共用一份文件 = 一个文件两条读取路径。
    ///
    /// ⚠️ **macOS 上不能直接用 <c>SpecialFolder.LocalApplicationData</c>**：.NET 在
    /// Unix 上把它映射到 XDG 的 <c>~/.local/share</c>，Finder 根本不显示。rules.json
    /// 是**给用户手写的**，藏起来等于把那条路堵死。
    /// </summary>
    public static string Dir { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                       "Library", "Application Support", "ItamiBen")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "ItamiBen");

    /// <summary>
    /// 累计账本的老文件。**只剩迁移在用**——账本 2026-09-16 搬进库了（DECISIONS I13）。
    /// </summary>
    public static string TotalsPath() => Path.Combine(Dir, "during.json");

    /// <summary>
    /// 窗口标题。**只有这一处定义**（2026-09-16），XAML 那边用 `{x:Static}` 引过去。
    ///
    /// 「痛みを知らせる」= **告知痛苦**。名字里的 Itami 本来就是「痛み」，v4 的核心
    /// 想法比 v3 多一层：不只是感到痛，是**把拖延的痛摆到你眼前**。
    ///
    /// ⚠️ 前面不再挂 `ItamiBen — `：程序名在 Dock / 任务栏 / 关于窗口里各出现一次，
    /// 标题里再写一遍是重复。而且主窗口是 `WindowDecorations="None"`，
    /// **macOS 上这行字根本看不见**——它只出现在 Windows 的任务栏，
    /// 和「ItamiBen 自己在前台」那一秒记进库里的 title 列。
    ///
    /// ⚠️ **它是文案，不是标识符。** 2026-09-16 之前 Windows 的单实例靠
    /// `FindWindow` 按这串精确匹配找窗口，等于「改文案 = 静默改坏功能」。
    /// 那条依赖已经拆了（<see cref="SingleInstance"/> 改按进程名找），
    /// 所以这里可以随便改。**别再把它接回任何逻辑上。**
    /// </summary>
    public const string WindowTitle = "痛みを知らせる";

    /// <summary>
    /// 四份配置文件。**智能体写、程序只读**（2026-09-18 起，配置从库里搬回文件）。
    ///
    /// ⚠️ 分界线是**谁写**，不是「配置 vs 账本」：这四份由人或智能体写，
    /// 库里剩下的全部由程序自己写。切口干净之后，智能体**连库的写权限都不需要**。
    /// </summary>
    public static string RulesPath()    => Path.Combine(Dir, "rules.md");
    public static string CommandsPath() => Path.Combine(Dir, "commands.md");
    public static string LayoutPath()   => Path.Combine(Dir, "layout.md");
    public static string SchedulePath() => Path.Combine(Dir, "schedule.md");

    /// <summary>
    /// 每次启动都摆好配置文件。**两种文件，两种待遇**：
    ///
    /// <list type="bullet">
    ///   <item><c>*.sample.md</c> —— 随程序发的**参考件**，每次启动覆盖刷新。
    ///   程序自己从不读它；它存在只为一件事：用户那份被 AI 改坏了，旁边有个完好的对照。
    ///   因为它跟二进制同源，**永远不会跟代码漂开**。</item>
    ///   <item><c>*.md</c> —— **用户自己那份**，只在不存在时从参考件播一次，
    ///   之后永不覆盖。人和智能体写的东西永远优先。</item>
    /// </list>
    ///
    /// ⚠️ 配置文件是三段式的 Markdown（给人的说明 → 给 AI 的规矩 → 标记好的配置块），
    /// 所以**播种就是原样拷贝**：用户拿到的第一份就已经自带完整说明，
    /// 把它整个扔给任何 AI 就够了——不需要再配一份 `AGENT.md`。
    ///
    /// ⚠️ 拷不动一声不吭：少一份配置不该让程序起不来，界面上本来就会说「没有目标」。
    /// </summary>
    public static void SeedDefaults()
    {
        try
        {
            var src = Path.Combine(AppContext.BaseDirectory, "defaults");
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(Dir);

            foreach (var from in Directory.EnumerateFiles(src, "*.sample.md"))
            {
                var sample = Path.GetFileName(from);
                File.Copy(from, Path.Combine(Dir, sample), overwrite: true);

                var mine = Path.Combine(Dir, sample.Replace(".sample.md", ".md"));
                if (!File.Exists(mine)) File.Copy(from, mine);
            }
        }
        catch { }
    }

    /// <summary>
    /// 那**一个**数据库：观测、轮次、事件、设置、账本、配置全在里面
    /// （2026-09-16 起，DECISIONS I15）。
    ///
    /// ⚠️ 改名前叫 `samples.db`——那个名字现在是错的，里面早就不只有采样了。
    /// 旧文件还在就顺手改个名，**不动内容**：SQLite 认的是文件不是名字。
    /// </summary>
    public static string DbPath()
    {
        var path = Path.Combine(Dir, "ItamiBen.sqlite3");
        var old = Path.Combine(Dir, "samples.db");
        if (!File.Exists(path) && File.Exists(old))
            try
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                    if (File.Exists(old + suffix)) File.Move(old + suffix, path + suffix);
            }
            catch { /* 改不动名就用旧的，下面照样开得起来 */ return File.Exists(path) ? path : old; }
        return path;
    }

    /// <summary>
    /// 程序**自己那些文件**的写法。不转义非 ASCII：目标名可能是任何语言，这个文件是
    /// 给人看的（想把某个目标清零就是手动改它）。
    ///
    /// ⚠️ **不许拿它去读写那四份配置 `.md`**——它们的解析各自收口在 Core 里
    /// （<see cref="Core.GoalRules"/> / <see cref="Core.CommandTable"/> / …），
    /// 一个文件只有一条读取路径。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>在访达 / 资源管理器里打开运行时目录——四份配置 `.md` 和两份日志都在那儿。</summary>
    public static void OpenInFileManager()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "explorer.exe");
            psi.ArgumentList.Add(Dir);
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Events.Error("ui", "Failed to open the config folder", e);
        }
    }
}

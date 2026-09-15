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
    /// rules.json 按三级查找，**绝不只看当前工作目录**——桌面快捷方式的「起始位置」
    /// 可以是任何地方，按工作目录找会变成「有时能跑有时不能」。
    ///
    /// <code>
    /// 1. &lt;运行时目录&gt;/rules.json   ← 用户自己那份，重装不会被覆盖
    /// 2. &lt;可执行文件旁边&gt;/rules.json ← 随程序发的默认规则
    /// 3. ./rules.json                  ← 开发时从仓库根目录跑
    /// </code>
    ///
    /// **这条链是只读的**：程序永远不写 rules.json（写一次用户的注释就全没了）。
    /// </summary>
    public static string RulesPath()
    {
        var mine = Path.Combine(Dir, "rules.json");
        if (File.Exists(mine)) return mine;

        var beside = Path.Combine(AppContext.BaseDirectory, "rules.json");
        return File.Exists(beside) ? beside : "rules.json";
    }

    /// <summary>累计账本。**程序自己写的文件**，跟 rules.json 不是一条链。</summary>
    public static string TotalsPath() => Path.Combine(Dir, "during.json");

    /// <summary>
    /// alarms.cron —— **用户手写，程序只读不写**，跟 rules.json 同一类契约。
    /// 每分钟重读一次，所以改完不用重启。
    /// </summary>
    public static string AlarmsPath() => Path.Combine(Dir, "alarms.cron");

    /// <summary>
    /// 观测库（DESIGN §9）：一秒一行的 app / title / 空闲。
    /// **跨轮持久**——v3 的 `During` 是 checkpoint 模型，第 N 轮的秒在第 N+1 轮 Start 时
    /// 才入账，库一清这条链就断（DECISIONS F4）。
    /// </summary>
    public static string SamplesPath() => Path.Combine(Dir, "samples.db");

    /// <summary>
    /// 程序**自己那些文件**的写法。不转义非 ASCII：目标名可能是任何语言，这个文件是
    /// 给人看的（想把某个目标清零就是手动改它）。
    ///
    /// ⚠️ **不许拿它去读写 rules.json**——那份是用户手写的，解析选项（注释、尾逗号、
    /// 大小写不敏感）在 <see cref="Core.GoalRules"/> 里，一个文件只有一条读取路径。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>在访达 / 资源管理器里打开运行时目录——rules.json 就在那儿等着被手写。</summary>
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
            Log.Error("Failed to open the config folder", e);
        }
    }
}

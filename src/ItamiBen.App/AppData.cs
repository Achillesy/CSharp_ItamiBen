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
    /// 观测库（DESIGN §9）：一秒一行的 app / title / 空闲。
    /// **跨轮持久**——v3 的 `During` 是 checkpoint 模型，第 N 轮的秒在第 N+1 轮 Start 时
    /// 才入账，库一清这条链就断（DECISIONS F4）。
    /// </summary>
    /// <summary>
    /// 把随程序发的 `AGENT.md` 刷进运行时目录，**跟数据库放在同一个文件夹**。
    ///
    /// ⚠️ 配置住在库里、由智能体改（DECISIONS I15），而智能体得先找得到说明。
    /// 拿到那个目录 = 同时拿到库和用法，不用再去翻仓库。
    ///
    /// ⚠️ **每次启动都覆盖**：它是随二进制发的，不该被人改——也就不会跟代码漂开。
    /// 复制失败一声不吭：少一份说明不该让程序起不来。
    /// </summary>
    public static void RefreshAgentDoc()
    {
        try
        {
            var src = Path.Combine(AppContext.BaseDirectory, "AGENT.md");
            if (File.Exists(src)) File.Copy(src, Path.Combine(Dir, "AGENT.md"), overwrite: true);
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
            Events.Error("ui", "Failed to open the config folder", e);
        }
    }
}

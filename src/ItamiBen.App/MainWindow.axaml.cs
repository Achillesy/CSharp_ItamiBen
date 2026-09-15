using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ItamiBen.App.Platform;

namespace ItamiBen.App;

/// <summary>
/// **骨架阶段的第一个探针**（不是产品界面）：置顶小窗，每秒读一次前台窗口，
/// 标题命中关键词就整窗变绿。它要回答的问题只有一个——**本机自己到底能不能读到标题**，
/// 以及 macOS 的授权流程实际是什么体验（DESIGN §4 的 D1、§5 的第 1/2 条）。
///
/// 自身豁免在这里是眼见为实的：点一下这个窗口，app 就变成 ItamiBen——
/// 产品里这一秒必须被排除，否则「提醒 → 用户去看提醒 → 又判跑偏」会死循环。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush Hit = new SolidColorBrush(Color.FromRgb(0x2F, 0xA3, 0x6B));
    private static readonly IBrush Miss = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x68));
    private static readonly IBrush Warn = new SolidColorBrush(Color.FromRgb(0xC4, 0x5A, 0x28));
    private static readonly IBrush Calm = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x48));

    private int _hitSeconds;

    /// <summary>
    /// 每拍一行的诊断日志。**探针必须能被别人读到**——不然调试就退化成「你看看窗口上写的啥」，
    /// 一来一回比改代码还慢（2026-09-15 就这么浪费了好几轮）。
    /// </summary>
    private static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ItamiBen", "probe.log");

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        InitLog();

        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            // ⚠️ 平台层整层都包在 try 里：2026-09-15 这个按钮把整个 app 搞崩过一次
            // （CFDictionary 的 key 用了自造的 CFString，AX 查不到 → CFGetTypeID(NULL)
            // → SIGSEGV）。根因已修，但**探针不该因为读不到权限就死掉**。
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception ex) { Note($"请求授权失败: {ex.Message}"); }
            RefreshPermission();
        };

        RefreshPermission();

        // 探针阶段：没授权就**开机自己注册一次**。
        // ⚠️ 这一步不只是"弹个框方便用户"——ad-hoc 签名的授权绑在 cdhash 上，
        //    每次重编都会作废，而系统设置里那条旧记录**看着还在**、其实认的是旧指纹。
        //    启动时主动注册，能保证列表里那一条对应的就是当前这个二进制。
        //    ⚠️ 产品化之前要改掉：每次启动都弹框 + 开系统设置太吵。
        if (!ForegroundWindow.TitlePermissionGranted)
        {
            try { ForegroundWindow.RequestTitlePermission(); }
            catch (Exception ex) { LogLine($"自动注册授权失败: {ex.Message}"); }
        }

        // 一秒一拍。产品里这会是唯一那口钟，探针阶段先这么跑。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Sample();
        timer.Start();
        Sample();
    }

    private void Sample()
    {
        Foreground fg;
        try { fg = ForegroundWindow.Read(); }
        catch (Exception ex) { Note($"读前台窗口失败: {ex.Message}"); return; }

        var keyword = this.FindControl<TextBox>("KeywordBox")!.Text ?? "";

        this.FindControl<TextBlock>("AppText")!.Text = string.IsNullOrEmpty(fg.App) ? "—" : fg.App;
        this.FindControl<TextBlock>("TitleText")!.Text =
            fg.TitleReadable ? (string.IsNullOrEmpty(fg.Title) ? "（这个窗口没有标题）" : fg.Title)
                             : "（读不到）";
        this.FindControl<TextBlock>("NoteText")!.Text =
            $"{DateTime.Now:HH:mm:ss}   {fg.Note}";

        var hit = keyword.Length > 0 && fg.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        if (hit) _hitSeconds++;

        RefreshPermission();   // 用户在系统设置里勾上之后，这里自己就变过来了，不用重启

        LogLine($"trusted={ForegroundWindow.TitlePermissionGranted,-5} hit={hit,-5} "
              + $"app={fg.App,-18} note={fg.Note,-46} title={fg.Title}");

        this.FindControl<Border>("HitBanner")!.Background = hit ? Hit : Miss;
        this.FindControl<TextBlock>("HitText")!.Text = hit ? $"命中「{keyword}」" : "没命中";
        this.FindControl<TextBlock>("HitCount")!.Text = $"命中 {_hitSeconds} 秒";
    }

    private void Note(string text)
        => this.FindControl<TextBlock>("NoteText")!.Text = $"{DateTime.Now:HH:mm:ss}   {text}";

    private static void InitLog()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            System.IO.File.WriteAllText(LogPath,
                $"# ItamiBen 探针 pid={Environment.ProcessId} 启动于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        }
        catch { /* 写不了就算了，窗口照跑 */ }
    }

    private static void LogLine(string line)
    {
        try { System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss}  {line}\n"); }
        catch { }
    }

    private void RefreshPermission()
    {
        bool ok;
        try { ok = ForegroundWindow.TitlePermissionGranted; }
        catch (Exception ex) { Note($"查授权状态失败: {ex.Message}"); return; }

        this.FindControl<Border>("PermBar")!.Background = ok ? Calm : Warn;
        this.FindControl<TextBlock>("PermText")!.Text = ok
            ? "辅助功能已授权，标题读得到"
            : "⚠️ 没有辅助功能授权 → 只有 app 名，读不到标题。点右边请求授权，然后在"
              + "「系统设置 → 隐私与安全性 → 辅助功能」里把 ItamiBen 打开";
        this.FindControl<Button>("GrantBtn")!.IsVisible = !ok;
    }
}

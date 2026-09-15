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

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<Button>("GrantBtn")!.Click += (_, _) =>
        {
            ForegroundWindow.RequestTitlePermission();
            RefreshPermission();
        };

        RefreshPermission();

        // 一秒一拍。产品里这会是唯一那口钟，探针阶段先这么跑。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Sample();
        timer.Start();
        Sample();
    }

    private void Sample()
    {
        var fg = ForegroundWindow.Read();
        var keyword = this.FindControl<TextBox>("KeywordBox")!.Text ?? "";

        this.FindControl<TextBlock>("AppText")!.Text = string.IsNullOrEmpty(fg.App) ? "—" : fg.App;
        this.FindControl<TextBlock>("TitleText")!.Text =
            fg.TitleReadable ? (string.IsNullOrEmpty(fg.Title) ? "（这个窗口没有标题）" : fg.Title)
                             : "（读不到）";
        this.FindControl<TextBlock>("NoteText")!.Text =
            $"{DateTime.Now:HH:mm:ss}   {fg.Note}";

        var hit = keyword.Length > 0 && fg.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        if (hit) _hitSeconds++;

        this.FindControl<Border>("HitBanner")!.Background = hit ? Hit : Miss;
        this.FindControl<TextBlock>("HitText")!.Text = hit ? $"命中「{keyword}」" : "没命中";
        this.FindControl<TextBlock>("HitCount")!.Text = $"命中 {_hitSeconds} 秒";
    }

    private void RefreshPermission()
    {
        var ok = ForegroundWindow.TitlePermissionGranted;
        this.FindControl<Border>("PermBar")!.Background = ok ? Calm : Warn;
        this.FindControl<TextBlock>("PermText")!.Text = ok
            ? "辅助功能已授权，标题读得到"
            : "⚠️ 没有辅助功能授权 → 只有 app 名，读不到标题。点右边请求授权，然后在"
              + "「系统设置 → 隐私与安全性 → 辅助功能」里把 ItamiBen 打开";
        this.FindControl<Button>("GrantBtn")!.IsVisible = !ok;
    }
}

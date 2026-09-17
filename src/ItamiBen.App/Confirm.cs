using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ItamiBen.App;

/// <summary>
/// A minimal "yes / no" confirmation dialog.
///
/// Avalonia has no built-in MessageBox, and all that's needed here is asking one
/// question, not worth pulling in a library or adding another axaml file for it. The
/// whole window is built in code, twenty lines.
///
/// 两处用它，都是**会作废整轮**的动作：点 Give up，和**专注途中关窗口**。
/// 后者尤其要问——「收起来」应该是最小化，可这两个动作长得像而后果差很远。
/// </summary>
public static class Confirm
{
    public static async Task<bool> AskAsync(Window owner, string message)
    {
        var result = false;

        // ⚠️ Padding=0 and VerticalContentAlignment=Center must be given together,
        // otherwise the text sits pinned to the top edge -- the same trap as
        // MainWindow.axaml's Button.start, see the reasoning there.
        var yes = MakeButton("Yes");
        var no = MakeButton("No");
        no.IsDefault = true;

        var dlg = new Window
        {
            // ⚠️ 跟主窗口同一个常量，不再各写一份（AppData.WindowTitle）。
            Title = AppData.WindowTitle,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            // The main window might be pinned (§8.3.7), and an ordinary modal window would
            // sink beneath it, showing up as "I clicked X and nothing happened". A modal
            // should always sit above everything else.
            Topmost = true,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20, 18, 20, 16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { yes, no },
                    },
                },
            },
        };

        // 底色取钟面调色板那一档，不新增色号；跟设置窗口的卡片同源。
        //
        // ⚠️ **看的是系统主题，不是 owner 的**（2026-09-18 修）。原来这里读
        //    `owner.ActualThemeVariant`，而 owner 就是主窗口——唯一一扇被那个主题按钮
        //    改过的窗。于是在 Windows 浅色下把 ItamiBen 切成深色，会得到一个
        //    **深色底 + 浅色按钮**的确认框：底色跟了按钮，而 Fluent 画的按钮和文字
        //    跟的是系统。半深半浅，不报错。
        //
        //    主题按钮**只管主窗口那只钟**（表盘 / 骨牌 / 卡片），这是设计意图；
        //    其余每一扇窗都跟系统走。`Application` 是 `RequestedThemeVariant="Default"`，
        //    所以它的 ActualThemeVariant 就是系统那一档——跟设置窗口读到的是同一个值，
        //    两扇窗因此永远一致。
        dlg.Background = new SolidColorBrush(
            (Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                ? DialPalette.Dark : DialPalette.Light).Card);

        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => { result = false; dlg.Close(); };

        // A minimize button on a confirmation dialog is meaningless (it's modal, and
        // minimizing it would just make it impossible to find), so it's disabled. Avalonia
        // has no property for this, so clearing WS_MINIMIZEBOX does it -- the system draws
        // the button greyed-out rather than removing it entirely, which is exactly "disable".
        //
        // **Not needed on macOS**: a modal window there doesn't get a minimize button to
        // begin with -- the system already handles it, nothing to disable.
        dlg.Opened += (_, _) => { if (OperatingSystem.IsWindows()) DisableMinimize(dlg); };

        await dlg.ShowDialog(owner);
        return result;
    }

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 96,
        Height = 34,
        Padding = new Avalonia.Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        CornerRadius = new Avalonia.CornerRadius(6),
    };

    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    [SupportedOSPlatform("windows")]
    private static void DisableMinimize(Window w)
    {
        if (w.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero) return;
        SetWindowLong(h, GWL_STYLE, GetWindowLong(h, GWL_STYLE) & ~WS_MINIMIZEBOX);
    }
}

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ItamiBen.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 版本号只有 Directory.Build.props 一个来源，这里从程序集读回来
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text =
            v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }
}

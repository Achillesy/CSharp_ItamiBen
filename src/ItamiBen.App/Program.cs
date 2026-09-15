using Avalonia;

namespace ItamiBen.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Avalonia 需要它保持这个签名和可见性（设计器和 XAML 编译器会找它）
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect();
}

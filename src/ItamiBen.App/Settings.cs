using System.Text.Json;
using System.Text.Json.Serialization;
using ItamiBen.App.Platform;

namespace ItamiBen.App;

/// <summary>
/// 程序自己的设置，运行时目录下的 <c>settings.json</c>。
///
/// <code>
/// { "alarmSound": "Sosumi", "alarmAt": "2026-09-16T07:30:00" }
/// </code>
///
/// ⚠️ **这个文件是程序整份重写的**，跟 `rules.json` 不是一类东西
/// （v3 的 K25 记着这条）：手改要在程序**没跑**的时候改，而且写不了注释。
/// 用户手写的配置一律另起文件、程序只读不写。
///
/// ⚠️ 闹钟只存**一个值**：响铃时刻（v3 的 E7）。黄针位置是它对 12 小时取余的推导值，
/// 存两份会漂。**退出时写一次**，不是每拨一格就写盘。
/// </summary>
public sealed class Settings
{
    /// <summary>闹钟音色（系统音的名字，不带扩展名）。null = 启动时自动挑一个。</summary>
    [JsonPropertyName("alarmSound")]
    public string? AlarmSound { get; set; }

    /// <summary>窗口是不是一直压在最上面。默认**开**——它是一只挂钟，挡住了就没用了。</summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; } = true;

    /// <summary>
    /// 上次窗口停在哪。⚠️ **读回来的坐标不能直接信**：显示器拔掉、分辨率改了、
    /// 外接屏断开之后，上次那个位置可能整个落在屏幕外，无边框窗口就再也找不着也够不着了。
    /// 所以恢复之后必须过一遍夹取（`MainWindow.ClampIntoScreen`）。
    /// </summary>
    [JsonPropertyName("windowX")] public int? WindowX { get; set; }
    [JsonPropertyName("windowY")] public int? WindowY { get; set; }

    /// <summary>alarms.cron 到点的音色。跟闹钟分开挑，好让两者听起来不一样。</summary>
    [JsonPropertyName("alarmsSound")]
    public string? AlarmsSound { get; set; }

    /// <summary>
    /// 闹钟的响铃时刻。读回来**只为了显示**（黄针残影），不激活——
    /// 关着程序时错过的闹钟不补响。
    /// </summary>
    [JsonPropertyName("alarmAt")]
    public DateTime? AlarmAt { get; set; }

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static Settings Load()
    {
        var settings = new Settings();
        try
        {
            var path = Path.Combine(AppData.Dir, "settings.json");
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), ReadOpts) ?? new Settings();
        }
        catch (Exception e)
        {
            Log.Error("Failed to read settings.json", e);
        }

        // 没挑过音色就自动挑一个：两个平台的候选写在一张表里，反正只有一边的文件存在。
        // 都没有就退回音库里的第一个——一台机器一个系统音都没有的情况下本来也没得响。
        settings.AlarmSound ??= Sound.PreferredOrFirst(
            "Sosumi", "Ping", "Glass", "Submarine",        // macOS
            "Alarm01", "Ring01", "Windows Notify", "chimes");  // Windows

        // 故意挑跟闹钟不一样的一个：两件事该听得出区别
        settings.AlarmsSound ??= Sound.PreferredOrFirst(
            "Ping", "Glass", "Purr", "Submarine",
            "Windows Notify", "chimes", "Alarm02");

        return settings;
    }

    /// <summary>⚠️ 整份重写。手改过的内容会被这一次写盘覆盖掉。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppData.Dir);
            File.WriteAllText(Path.Combine(AppData.Dir, "settings.json"),
                              JsonSerializer.Serialize(this, AppData.JsonOptions));
        }
        catch (Exception e)
        {
            Log.Error("Failed to write settings.json", e);
        }
    }
}

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

    /// <summary>
    /// 上次选的专注时长和目标。**纯粹是省事**：开程序不用每次重新挑一遍。
    /// 目标名对不上了（rules.json 改过）就自然勾不上，不猜也不报错。
    /// </summary>
    [JsonPropertyName("focusMinutes")] public int? FocusMinutes { get; set; }
    [JsonPropertyName("selectedGoal")] public string? SelectedGoal { get; set; }

    /// <summary>
    /// 日面还是夜面。**null = 跟着系统走**（第一次启动就是这个），
    /// 点过主题图标之后才会钉死成 true / false。
    /// </summary>
    [JsonPropertyName("darkTheme")]
    public bool? DarkTheme { get; set; }

    /// <summary>
    /// 滴答声开着没有。
    /// ⚠️ **v4 只有「一直响」这一种**（环境音）；v3 那种「跑偏才响」是提示音，
    /// 跟 D1 / E4 冲突，没搬。
    /// </summary>
    [JsonPropertyName("tickEnabled")] public bool TickEnabled { get; set; }

    /// <summary>滴答音量 0~100。音色是合成的，没有可挑的（见 <see cref="Platform.Tick"/>）。</summary>
    [JsonPropertyName("tickVolume")] public int TickVolume { get; set; } = 35;

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

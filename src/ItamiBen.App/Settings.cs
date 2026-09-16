using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ItamiBen.App.Platform;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 程序自己的设置，**存在 `samples.db` 的 `setting` 表里**（2026-09-16 从 `settings.json`
/// 搬进来，DECISIONS I12）。
///
/// ⚠️ **这些从来不是用户手写的**，跟 `rules.json` / `alarms.cron` / `layout.json`
/// 不是一类东西：那三份**用户写、程序只读**；这些**程序写、用户只看**
/// （v3 的 K25 说的就是这个区别）。既然用户不用手改，那就没理由为它单开一个文件——
/// 数据库随时都在，顺手就查了。
///
/// ⚠️ 搬过来还顺手解决了一个真问题：JSON 那套是**整份重写**，而「一次写全部」正是两个
/// 实例互相覆盖的根源（I1）。现在是逐键 upsert，一个事务。
///
/// ⚠️ 读写**复用同一个类型模型和同一个解析器**——先序列化成 `JsonObject`，再把顶层
/// 逐键摊平成行；读回来反着拼。所以 `value` 存的是 **JSON 片段**（字符串带引号、
/// 数字不带）。看着略怪，但**一个文件一条读取路径**是这个项目反复吃亏换来的规矩
/// （v3 的 §15.4：同一份数据两条解析路径，咬了两次，症状都是半个文件安静地失效）。
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
    /// **跑偏时滴答**（桌面上那个喇叭图标管的就是它）。
    ///
    /// ⚠️ 跟 <see cref="ForceTicking"/> 是两个开关，合起来是：
    /// <c>ticking = ForceTicking || (TickEnabled &amp;&amp; 正在跑偏)</c>。
    /// 喇叭挂跑偏，菜单里那个「Force ticking」挂设置——两者不是一回事。
    /// </summary>
    [JsonPropertyName("tickEnabled")] public bool TickEnabled { get; set; }

    /// <summary>专注达成时响一声。</summary>
    [JsonPropertyName("focusDoneEnabled")] public bool FocusDoneEnabled { get; set; } = true;
    [JsonPropertyName("focusDoneSound")] public string? FocusDoneSound { get; set; }

    /// <summary>休息走完时响一声。</summary>
    [JsonPropertyName("restDoneEnabled")] public bool RestDoneEnabled { get; set; } = true;
    [JsonPropertyName("restDoneSound")] public string? RestDoneSound { get; set; }

    /// <summary>
    /// 键鼠空闲 60~180 秒时提醒一下——**还没到「离开」但人已经飘了**，
    /// 这一声是把你捞回来，不是事后报账。
    /// </summary>
    [JsonPropertyName("idleEnabled")] public bool IdleEnabled { get; set; } = true;
    [JsonPropertyName("idleSound")] public string? IdleSound { get; set; }

    /// <summary>
    /// **无条件滴答**：不看任何判据，一直响。那才是 force 的字面意思。
    /// 跟 <see cref="TickEnabled"/> 是两个开关：那个只在**跑偏**时响。
    /// </summary>
    [JsonPropertyName("forceTicking")] public bool ForceTicking { get; set; }

    /// <summary>alarms.cron 到点响不响铃。⚠️ 只管**响不响**——检查清单那条主链路无条件每分钟都做。</summary>
    [JsonPropertyName("alarmsEnabled")] public bool AlarmsEnabled { get; set; } = true;

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
    /// <summary>
    /// 窗口档位和不透明度。**智能体可以改这两个**（AGENT.md 里点了名），
    /// 其余的键是程序自己记的，改了也会被下一次写盘覆盖。
    /// </summary>
    [JsonPropertyName("layout")] public string? Layout { get; set; }

    [JsonPropertyName("opacityPercent")] public double? OpacityPercent { get; set; }

    /// <summary>
    /// 闹钟到点要跑的那条命令**的名字**（`command` 表的主键）。
    ///
    /// ⚠️ 只存名字，不存命令原文：**可执行的文本只准住在 `command` 表里**，
    /// 这样「这台机器上有哪些命令能被自动跑」永远只要看一个地方。
    /// </summary>
    [JsonPropertyName("alarmCommand")] public string? AlarmCommand { get; set; }

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

    /// <summary>
    /// 从哪儿读来的就写回哪儿去。
    ///
    /// ⚠️ 记在实例上、而不是让每个调用方自己传：`Save()` 有五个调用点（设置窗口四处、
    /// 退出一处），**只要有一处忘了传，那一处的改动就安静地不落盘**——而「设置没保存」
    /// 恰恰是最容易被当成「我记错了」的那类 bug。
    /// </summary>
    private SampleStore? _store;

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 从库里读设置；库没开（或者根本打不开）就全用默认值——**程序照样能跑**。
    ///
    /// 第一次跑会把旧的 `settings.json` 搬进来，然后把那个文件改名成
    /// `settings.json.migrated`：留着是为了万一要回看，改名是为了**它不再看起来像
    /// 还在生效的配置**。
    /// </summary>
    public static Settings Load(SampleStore? store)
    {
        var settings = new Settings();
        try
        {
            var rows = store?.Settings() ?? [];
            if (rows.Count == 0 && store is not null) rows = MigrateFromJson(store);
            if (rows.Count > 0)
            {
                var obj = new JsonObject();
                foreach (var (k, v) in rows)
                {
                    // 单个值坏了就跳过这一个键，别让整份设置陪葬
                    try { obj[k] = JsonNode.Parse(v); } catch { }
                }
                settings = obj.Deserialize<Settings>(ReadOpts) ?? new Settings();
            }
        }
        catch (Exception e)
        {
            Events.Error("settings", "Failed to read settings", e);
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
        settings.FocusDoneSound ??= Sound.PreferredOrFirst("Glass", "Hero", "Blow", "chimes", "notify");
        settings.RestDoneSound ??= Sound.PreferredOrFirst("Submarine", "Purr", "Bottle", "chord", "tada");
        settings.IdleSound ??= Sound.PreferredOrFirst("Tink", "Pop", "Morse", "ding", "Speech On");

        settings._store = store;
        return settings;
    }

    /// <summary>
    /// 写回库里。库没开就什么都不做——**丢的是「下次启动记得上次的选择」，
    /// 不是账本**，不值得为它多一条容错路径。
    /// </summary>
    public void Save()
    {
        if (_store is null) return;
        try
        {
            _store.PutSettings(Flatten());
        }
        catch (Exception e)
        {
            Events.Error("settings", "Failed to write settings", e);
        }
    }

    /// <summary>把自己摊平成「键 → JSON 片段」。null 的键不写，读回来时自然走默认值。</summary>
    private Dictionary<string, string> Flatten()
    {
        var map = new Dictionary<string, string>();
        if (JsonSerializer.SerializeToNode(this, AppData.JsonOptions) is not JsonObject obj) return map;
        foreach (var (k, v) in obj)
            if (v is not null)
                // ⚠️ **必须把编码选项传给 `ToJsonString`**：它不继承序列化时那一份，
                //    默认编码器会把中文和 `+` 转义成 \uXXXX。存进去照样读得回来，
                //    但在 DB Browser 里**「自学数理化」会变成一串 \u81EA…**，
                //    而「出了问题直接查数据库」的前提就是那一眼能看懂。
                map[k] = v.ToJsonString(AppData.JsonOptions);
        return map;
    }

    /// <summary>
    /// 把旧的 `settings.json` 搬进库，然后改名。只在库里一条设置都没有时才走这一遭。
    /// </summary>
    private static Dictionary<string, string> MigrateFromJson(SampleStore store)
    {
        var path = Path.Combine(AppData.Dir, "settings.json");
        if (!File.Exists(path)) return [];

        var map = new Dictionary<string, string>();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }) is JsonObject obj)
            {
                foreach (var (k, v) in obj)
                    if (v is not null)
                        map[k] = v.ToJsonString(AppData.JsonOptions);
            }

            if (map.Count > 0) store.PutSettings(map);
            File.Move(path, path + ".migrated", overwrite: true);
            Events.Info("settings", $"migrated {map.Count} settings from settings.json");
        }
        catch (Exception e)
        {
            Events.Error("settings", "Failed to migrate settings.json", e);
        }
        return map;
    }
}

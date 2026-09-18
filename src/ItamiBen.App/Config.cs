using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 配置的装配。**配置住在文件里，由智能体写**（2026-09-18 起）。
///
/// 一路走过来是：五个手写文件 → 一个数据库（I15）→ 四个文件 + 一个纯观测的库。
/// 绕回来不是白绕——两次的分界线不一样：
///
/// <list type="bullet">
///   <item>I15 按「配置 vs 账本」分，于是把**程序自己写**的设置和累计也一并塞进库，
///   那一半是对的（`settings.json` 整份重写正是两个实例互相覆盖的根源，I1）；</item>
///   <item>现在按「**谁写**」分：人和智能体写的进文件，程序自己写的留在库里。
///   切口干净之后，智能体**连库的写权限都不需要**——一整类「AI 一句 SQL 抹掉
///   十六小时累计」的事故在结构上消失了，不是靠护栏拦住的。</item>
/// </list>
///
/// ⚠️ **文件优先，库是后备。** 过渡期两条路并存：运行时目录里有那份文件就用文件，
/// 没有就退回库里的老配置——这样从 SQLite 那一版升上来的用户不会一下子失去配置。
/// 库那条路在第二步（迁移 + 拆除）里删掉。
/// </summary>
internal static class Config
{
    /// <summary>
    /// 读一份配置文件，**并把其中标记过的那个配置块抠出来**。
    ///
    /// 配置文件是三段式的 Markdown：给人的说明 → 给 AI 的规矩 → 标记好的配置块。
    /// 这一层只负责把最后那一段交给对应的解析器；说明部分程序一个字都不看，
    /// 它是写给**下一个打开这个文件的人或 AI** 的。
    ///
    /// 不存在返回 null（调用方退回库那条路）；**读不动、或者块找不到也返回 null，
    /// 但要记一笔**——「文件在那儿却没生效」是最难查的一类，不能一声不吭。
    /// </summary>
    private static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return MarkdownConfig.Extract(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Events.Error("config", $"Cannot read {Path.GetFileName(path)}", e);
            return null;
        }
    }

    /// <summary>
    /// 出错时那句话里的文件名**从路径推，不硬编码**。
    /// 2026-09-18 实测抓到：文件改名成 `.md` 之后，日志里还在说 `rules.json`
    /// ——又一次「同一个名字写了两份」。
    /// </summary>
    private static string Unusable(string path) => $"{Path.GetFileName(path)} is not usable";

    /// <summary>
    /// 装配规则。
    ///
    /// ⚠️ **解析失败返回空规则，不是放行一切**（DECISIONS C 组）：一个目标都没有时
    /// Start 按不下去，界面会直说「没有目标」。宁可什么都做不了。
    /// </summary>
    public static GoalRules LoadRules(SampleStore? store)
    {
        var path = AppData.RulesPath();
        if (Read(path) is { } json)
        {
            try { return GoalRules.Parse(json); }
            catch (Exception e)
            {
                Events.Error("config", Unusable(path), e);
                return GoalRules.Empty;
            }
        }
        return store is null ? GoalRules.Empty : GoalRules.Of(store.Goals(), store.Commands());
    }

    /// <summary>装配命令清单。</summary>
    public static CommandTable LoadCommands(SampleStore? store, Settings settings)
    {
        var path = AppData.CommandsPath();
        if (Read(path) is { } json)
        {
            try { return CommandTable.Parse(json); }
            catch (Exception e)
            {
                Events.Error("config", Unusable(path), e);
                return CommandTable.Empty;
            }
        }
        return store is null
            ? CommandTable.Empty
            : CommandTable.Of(store.Commands(), settings.AlarmCommand);
    }

    /// <summary>
    /// 装配外观。**只在启动时读一次**，运行中改了不生效——这是用户要的语义，
    /// 也顺带免掉「运行中换档要重新夹回屏幕、提示条正显示着怎么办」那一整类边界情况。
    /// </summary>
    public static LayoutFile LoadLayout(Settings settings)
    {
        var path = AppData.LayoutPath();
        if (Read(path) is { } json)
        {
            try { return LayoutFile.Parse(json); }
            catch (Exception e)
            {
                Events.Error("config", Unusable(path), e);
                return LayoutFile.Empty;
            }
        }
        return new LayoutFile(settings.Layout, settings.OpacityPercent);
    }

    /// <summary>
    /// 装配计划表。
    ///
    /// ⚠️ **读不懂的行要记一笔**（DECISIONS I29，对 v3 的 J16「安静跳过」的修订）：
    /// 新格式里最容易犯的错是「写了 `!命令` 却忘了写提醒文字」，那行不合法被跳过，
    /// 而症状是「这条提醒从此再也不响」——原本屏幕上和日志里零反馈。
    /// </summary>
    public static IReadOnlyList<CronEntry> LoadSchedule(SampleStore? store)
    {
        if (Read(AppData.SchedulePath()) is { } text)
        {
            var file = AlarmsList.Read(text);
            foreach (var why in file.Skipped) Events.Warn("schedule", why);
            return file.Entries;
        }

        if (store is null) return [];
        var entries = new List<CronEntry>();
        foreach (var row in store.Schedule())
            if (AlarmsList.ParseExpression(row.Cron) is { } schedule)
                entries.Add(new CronEntry(schedule, row.Text ?? "", row.Run));
        return entries;
    }

    /// <summary>
    /// 配置的「版本」：**四个文件的写入时刻 + 库的版本号**，拼成一个串。
    /// 变了就重装，每分钟比一次。
    ///
    /// ⚠️ 文件这条路**不需要智能体记得 bump 任何东西**——那是库那一版的头号坑
    /// （改完忘了加版本号 = 用户以为你没改成）。文件的 mtime 就是版本号，
    /// 操作系统替我们维护。
    /// </summary>
    public static string Stamp(SampleStore? store)
    {
        var parts = new List<string> { (store?.ConfigVersion ?? 0).ToString() };
        foreach (var p in new[] { AppData.RulesPath(), AppData.CommandsPath(), AppData.SchedulePath() })
        {
            try { parts.Add(File.Exists(p) ? File.GetLastWriteTimeUtc(p).Ticks.ToString() : "-"); }
            catch { parts.Add("?"); }
        }
        // ⚠️ layout.json **故意不在这里**：它只在启动时读一次，把它算进来只会让
        //    程序每分钟「重装」一次却什么都不变，白白掩盖「改了要重启」这个事实
        return string.Join('|', parts);
    }
}

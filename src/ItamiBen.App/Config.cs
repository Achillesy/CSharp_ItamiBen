using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 配置的装配、迁移和播种——**配置住在库里**（2026-09-16 用户定，DECISIONS I15）。
///
/// 原来是两个手写文件（`rules.json` / `alarms.cron`）。它们之所以能变成表，是因为
/// **用户不手写它们，智能体写**：用户说「我想要什么」，智能体去改库。那么「程序永远
/// 不写 rules.json」这条硬规矩（它存在的唯一理由是保护手写的注释）连同它带来的一堆
/// 限制，就全部消失了。
///
/// ⚠️ **库里那几行默认值同时是模板。** 给智能体看的 `AGENT.md` 是手写的散文，
/// **散文一定会漂**——这个项目一天之内被这件事咬过三次（rules.json 里那句「ItamiBen
/// 有豁免」在豁免删掉之后还挂了半天；v3 的 F1 比它引用的 A5 多活两个月；README 里
/// 写着 macOS 上用 `dotnet run` 而那条路拿不到授权）。**库里一行真实的数据不会漂**，
/// 它是代码建出来的，形状永远跟代码一致。
/// </summary>
internal static class Config
{
    /// <summary>
    /// 默认那条计划**是关着的**（`enabled = 0`）。
    ///
    /// ⚠️ 它存在只为当模板：让智能体看得见计划表长什么样。开着的话就成了
    /// **用户没要求过、却每小时打扰一次**的东西——样例不该有副作用。
    /// </summary>
    private const string GreetingCron = "0 * * * *";

    /// <summary>库里一条配置都没有时，装点什么进去。</summary>
    public static void EnsureSeeded(SampleStore store)
    {
        if (!store.ConfigIsEmpty) return;

        // 老文件还在就搬它，否则播种
        if (TryMigrate(store)) return;

        var dir = AppData.Dir;
        store.PutConfig(
            goals:
            [
                // ⚠️ **必须有一条目标，否则全新安装是一台什么都不做的钟**：
                //    目标列表空了 Start 就按不下去。默认这条认 ItamiBen 自己，
                //    所以装完就能按 Start 看见绿色——它同时是「规则长什么样」的样例。
                new SampleStore.GoalRow("Pomodoro", true,
                    [new SampleStore.RuleRow("^ItamiBen(\\.exe)?$", null)]),
            ],
            commands:
            [
                // 打开数据库所在的文件夹。既是命令表的样例，本身也有用——
                // 想拿 DB 工具看这个库，第一步就是把这个文件夹打开
                new SampleStore.CommandRow("show-files",
                    MacOS: $"open \"{dir}\"",
                    Windows: $"explorer \"{dir}\""),
            ],
            schedule:
            [
                (new SampleStore.ScheduleRow(GreetingCron, "Nice work. Keep it up.", null),
                 Enabled: false,
                 Note: "Example: an hourly nudge. Off by default — set enabled = 1 to turn it on."),
            ],
            note: "seeded defaults");

        Events.Info("config", "seeded defaults (1 goal, 1 command, 1 example schedule)");
    }

    /// <summary>
    /// 把老的 `rules.json` / `alarms.cron` 搬进库，然后改名成 `.migrated`。
    /// 一个都不在就返回 false（该播种了）。
    ///
    /// ⚠️ **先落库、确认没抛，再改名**：顺序反了中途出错就两头都没了。
    /// </summary>
    private static bool TryMigrate(SampleStore store)
    {
        var rulesPath = Path.Combine(AppData.Dir, "rules.json");
        var cronPath = Path.Combine(AppData.Dir, "alarms.cron");
        if (!File.Exists(rulesPath) && !File.Exists(cronPath)) return false;

        try
        {
            List<SampleStore.GoalRow> goals = [];
            List<SampleStore.CommandRow> commands = [];
            string? layout = null;
            double? opacity = null;

            if (File.Exists(rulesPath))
            {
                var rules = GoalRules.Parse(File.ReadAllText(rulesPath));
                (goals, commands) = rules.ToRows();
                layout = rules.LayoutName;
                opacity = rules.OpacityPercent;
            }

            var schedule = new List<(SampleStore.ScheduleRow, bool, string?)>();
            if (File.Exists(cronPath))
                foreach (var e in AlarmsList.Parse(File.ReadAllText(cronPath)))
                    schedule.Add((new SampleStore.ScheduleRow(e.Schedule.Expression, e.Text, null), true, null));

            store.PutConfig(goals, commands, schedule, "migrated from rules.json / alarms.cron");

            // 外观那两个键归设置表——它们是「怎么显示」，不是「判定什么」
            var extra = new Dictionary<string, string>();
            if (layout is not null) extra["layout"] = $"\"{layout}\"";
            if (opacity is { } p) extra["opacityPercent"] = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (extra.Count > 0) store.PutSettings(extra);

            if (File.Exists(rulesPath)) File.Move(rulesPath, rulesPath + ".migrated", overwrite: true);
            if (File.Exists(cronPath)) File.Move(cronPath, cronPath + ".migrated", overwrite: true);

            Events.Info("config", $"migrated {goals.Count} goals, {commands.Count} commands, "
                                + $"{schedule.Count} schedule entries into the database");
            return true;
        }
        catch (Exception e)
        {
            Events.Error("config", "Failed to migrate rules.json / alarms.cron", e);
            return false;
        }
    }

    /// <summary>装配规则。库没开就是空规则——**宁可什么都做不了，也不能放行一切**。</summary>
    public static GoalRules LoadRules(SampleStore? store, Settings settings)
        => store is null
            ? GoalRules.Empty
            : GoalRules.Of(store.Goals(), store.Commands(), settings.Layout, settings.OpacityPercent);

    /// <summary>装配计划表。cron 表达式解析不了的那条**安静跳过**（v3 的 J16）。</summary>
    public static IReadOnlyList<CronEntry> LoadSchedule(SampleStore? store)
    {
        if (store is null) return [];
        var entries = new List<CronEntry>();
        foreach (var row in store.Schedule())
            if (AlarmsList.ParseExpression(row.Cron) is { } schedule)
                entries.Add(new CronEntry(schedule, row.Text ?? "", row.Run));
        return entries;
    }
}

using System.Text.Json;
using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 把 2026-09-18 之前住在库里的配置搬进四份 `.md`。**一次性**：搬完就把那几张表删掉，
/// 新建的库里根本不会有它们。
///
/// ⚠️ **必须排在 <see cref="AppData.SeedDefaults"/> 之前**：播种只补缺的文件，
/// 所以先让迁移把用户真实的配置写下去，剩下真的没有的才用出厂默认填。
/// 顺序反了的症状是「升级之后配置变回默认，而且不报错」。
///
/// ⚠️ 只补**不存在**的文件。用户已经有 `rules.md` 就说明他已经在用新方案了，
/// 库里那份是历史，不该回头覆盖现在的。
///
/// ⚠️ 用出厂参考件当模板、只换配置块，所以迁移出来的文件**一上来就自带完整说明**，
/// 跟播种出来的没有区别——用户不会因为「我是升级上来的」就少一份说明。
///
/// ⚠️ **这整个文件是过渡期的东西**，等确认没有还在用老库的机器之后整块删掉，
/// 连同 <see cref="SampleStore.HasLegacyConfig"/> 和 <see cref="SampleStore.DropLegacyConfig"/>。
/// </summary>
/// ⚠️ 序列化那几个方法是 `internal` 而不是 `private`：`ConfigMigrationTests` 拿它们做
/// **往返测试**（老库 → 文本 → 再解析回来，必须一模一样）。这是这段代码唯一的验证机会
/// ——它在用户机器上只跑一次，写错了就是配置静默丢失。
internal static class ConfigMigration
{
    public static void Run(SampleStore? store, Settings settings)
    {
        if (store is null || !store.HasLegacyConfig) return;

        try
        {
            Put(AppData.RulesPath(),    "rules.sample.md",    Rules(store));
            Put(AppData.CommandsPath(), "commands.sample.md", Commands(store, settings));
            Put(AppData.SchedulePath(), "schedule.sample.md", Schedule(store));
            Put(AppData.LayoutPath(),   "layout.sample.md",   Layout(settings));

            store.DropLegacyConfig();
            Events.Info("config", "migrated the configuration out of the database into *.md");
        }
        catch (Exception e)
        {
            // ⚠️ **搬不动就别删表**：这一遍失败了，下次启动还能再试一次
            Events.Error("config", "Could not migrate the configuration out of the database", e);
        }
    }

    /// <summary>
    /// 老库里那张 `event` 表倒进 `event.log`，然后删掉。
    ///
    /// ⚠️ **倒完再删，不是直接删**：那是用户的历史。事件换了住处不该让历史凭空消失，
    /// 而「以前的事去哪了」这种问题事后没人答得上来。
    ///
    /// ⚠️ 倒过去的行**原样带着当时的时间戳**，所以 `event.log` 不是按写入顺序而是
    /// 按事件顺序——这正是读它的人想要的。
    ///
    /// ⚠️ **必须排在本次启动那条 `start` 之前**（所以它在 `OpenStore` 里紧跟着开库，
    /// 而不是跟别的迁移一起）。排在后面的话，历史会被追加到本次启动的下面，
    /// 文件里就出现了「写入序 ≠ 时间序」——只发生一次，但读它的人会困惑。
    /// </summary>
    public static void MoveEvents(SampleStore store)
    {
        if (!store.HasLegacyEvents) return;
        try
        {
            var rows = store.LegacyEvents();
            foreach (var (at, level, kind, text) in rows)
                Log.WriteAt(at, level, kind, text);
            store.DropLegacyEvents();
            Events.Info("config", $"moved {rows.Count} event(s) out of the database into event.log");
        }
        catch (Exception e)
        {
            Events.Error("config", "Could not move the old events into event.log", e);
        }
    }

    /// <summary>拿出厂参考件当模板，只换配置块。文件已经在就什么都不做。</summary>
    private static void Put(string path, string sample, string? body)
    {
        if (body is null || File.Exists(path)) return;

        var template = Path.Combine(AppContext.BaseDirectory, "defaults", sample);
        if (!File.Exists(template)) return;

        Directory.CreateDirectory(AppData.Dir);
        File.WriteAllText(path, MarkdownConfig.Replace(File.ReadAllText(template), body));
    }

    /// <summary>
    /// 一个 JSON 字符串字面量。
    ///
    /// ⚠️ **必须换掉默认的 encoder**：<c>JsonSerializer</c> 默认把所有非 ASCII 转成
    /// <c>\uXXXX</c>，于是目标名「学习经济学」搬出来会变成
    /// <c>"\u5B66\u4E60\u7ECF\u6D4E\u5B66"</c>——**读得进去，但人看不懂**。
    /// 而这四份 `.md` 存在的全部理由就是「整个文件扔给谁都能读」：用户要手改它，
    /// AI 要照着抄名字。一串转义把这个理由整个抵消掉。
    ///
    /// ⚠️ 2026-09-19 在 Windows 上搬一份中文配置时发现的。`schedule.md` 没事，
    /// 因为 cron 那半边是原样拼字符串、根本不过 JSON——**同一份配置里两种语言待遇不同**，
    /// 正是这种不一致最容易被当成「本来就这样」。
    ///
    /// ⚠️ 不复用 <c>AppData.JsonOptions</c>：那个对象上写着「不许拿它去读写这四份 `.md`」。
    /// 这里只借同一个 encoder，理由也是同一个（见它的注释）。
    /// </summary>
    private static readonly JsonSerializerOptions QuoteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Q(string? s) => JsonSerializer.Serialize(s ?? "", QuoteOptions);

    internal static string? Rules(SampleStore db)
    {
        var goals = db.Goals();
        if (goals.Count == 0) return null;

        var L = new List<string> { "{", "  \"Groups\": {" };
        for (var i = 0; i < goals.Count; i++)
        {
            var g = goals[i];
            L.Add($"    {Q(g.Name)}: {{");
            if (!g.Enabled) L.Add("      \"Disabled\": true,");
            L.Add("      \"Rules\": [");
            for (var j = 0; j < g.Rules.Count; j++)
            {
                var r = g.Rules[j];
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(r.App)) parts.Add($"\"App\": {Q(r.App)}");
                if (!string.IsNullOrEmpty(r.Title)) parts.Add($"\"Title\": {Q(r.Title)}");
                L.Add($"        {{ {string.Join(", ", parts)} }}" + (j < g.Rules.Count - 1 ? "," : ""));
            }
            L.Add("      ]");
            L.Add("    }" + (i < goals.Count - 1 ? "," : ""));
        }
        L.Add("  }");
        L.Add("}");
        return string.Join('\n', L);
    }

    internal static string? Commands(SampleStore db, Settings settings)
    {
        var rows = db.Commands();
        if (rows.Count == 0) return null;

        // ⚠️ 老的 `alarmCommand` 是**一个名字**，新格式按系统分开绑。
        //    两边填同一个名字，是最忠实的搬法——行为一个字都不变。
        var alarm = Q(settings.AlarmCommand);
        var L = new List<string>
        {
            "{", "  \"Alarm\": {", $"    \"MacOS\": {alarm},", $"    \"Windows\": {alarm}", "  },",
            "  \"Commands\": {",
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var c = rows[i];
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(c.MacOS)) parts.Add($"      \"MacOS\": {Q(c.MacOS)}");
            if (!string.IsNullOrEmpty(c.Windows)) parts.Add($"      \"Windows\": {Q(c.Windows)}");
            L.Add($"    {Q(c.Name)}: {{");
            L.Add(string.Join(",\n", parts));
            L.Add("    }" + (i < rows.Count - 1 ? "," : ""));
        }
        L.Add("  }");
        L.Add("}");
        return string.Join('\n', L);
    }

    internal static string? Schedule(SampleStore db)
    {
        var rows = db.LegacySchedule();
        if (rows.Count == 0) return null;

        var L = new List<string>();
        foreach (var (cron, text, run, enabled) in rows)
        {
            // ⚠️ 老的 `enabled = 0` 在 crontab 里的对应物就是**注释掉**——
            //    不是丢掉。用户停用一条是有意的。
            var line = $"{cron,-12} {text}" + (string.IsNullOrEmpty(run) ? "" : $" !{run}");
            L.Add(enabled ? line : $"# {line}");
        }
        return string.Join('\n', L);
    }

    internal static string Layout(Settings settings)
        => "{\n"
         + $"  \"Layout\": {Q(settings.Layout ?? "standard")},\n"
         + $"  \"OpacityPercent\": {settings.OpacityPercent ?? 90}\n"
         + "}";
}

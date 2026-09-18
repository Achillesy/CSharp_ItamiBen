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

    /// <summary>拿出厂参考件当模板，只换配置块。文件已经在就什么都不做。</summary>
    private static void Put(string path, string sample, string? body)
    {
        if (body is null || File.Exists(path)) return;

        var template = Path.Combine(AppContext.BaseDirectory, "defaults", sample);
        if (!File.Exists(template)) return;

        Directory.CreateDirectory(AppData.Dir);
        File.WriteAllText(path, MarkdownConfig.Replace(File.ReadAllText(template), body));
    }

    private static string Q(string? s) => JsonSerializer.Serialize(s ?? "");

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

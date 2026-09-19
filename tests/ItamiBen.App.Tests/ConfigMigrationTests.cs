using ItamiBen.App;
using ItamiBen.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ItamiBen.App.Tests;

/// <summary>
/// 老库里的配置搬进 `.md` 的**往返测试**：造一个 2026-09-18 之前形状的库 →
/// 序列化 → 用真解析器读回来 → 必须跟原样一致。
///
/// ⚠️ 这段代码在每台机器上**只跑一次**，写错了就是配置静默丢失，事后还查不出来。
/// 往返是唯一能证明它对的方式——光「不抛异常」说明不了任何事。
/// </summary>
public class ConfigMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"itami-{Guid.NewGuid():N}.db");

    /// <summary>造一个老库：那五张配置表现在的 <c>Open</c> 已经不建了，测试自己建。</summary>
    private SampleStore OldStore()
    {
        var db = SampleStore.Open(_path);
        using var con = new SqliteConnection($"Data Source={_path}");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE goal (name TEXT PRIMARY KEY, enabled INTEGER NOT NULL DEFAULT 1,
                               position INTEGER NOT NULL DEFAULT 0, note TEXT);
            CREATE TABLE rule (id INTEGER PRIMARY KEY, goal TEXT NOT NULL, app TEXT, title TEXT, note TEXT);
            CREATE TABLE command (name TEXT PRIMARY KEY, macos TEXT, windows TEXT, note TEXT);
            CREATE TABLE schedule (id INTEGER PRIMARY KEY, cron TEXT NOT NULL, text TEXT, run TEXT,
                                   enabled INTEGER NOT NULL DEFAULT 1, note TEXT);
            CREATE TABLE config (id INTEGER PRIMARY KEY CHECK (id = 1), version INTEGER NOT NULL,
                                 changed_at INTEGER NOT NULL, note TEXT);

            INSERT INTO goal (name, enabled, position) VALUES ('编程', 1, 0), ('去年的', 0, 1);
            INSERT INTO rule (goal, app, title) VALUES
                ('编程', '^(Code|Code\.exe)$', NULL),
                ('编程', '^Google Chrome$', 'GitHub'),
                ('去年的', '^Xcode$', NULL);
            INSERT INTO command (name, macos, windows) VALUES
                ('sleep', 'pmset sleepnow', 'rundll32.exe powrprof.dll,SetSuspendState 0,1,0'),
                ('mac-only', 'say hi', NULL);
            INSERT INTO schedule (cron, text, run, enabled) VALUES
                ('0 9 * * 1', '海贼王', NULL, 1),
                ('0 23 * * *', '该睡了', 'sleep', 1),
                ('0 * * * *', '停用的那条', NULL, 0);
            INSERT INTO config (id, version, changed_at) VALUES (1, 7, 0);
            """;
        cmd.ExecuteNonQuery();
        return db;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            try { File.Delete(f); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 规则往返_中文名和正则里的反斜杠都活着()
    {
        using var db = OldStore();

        var rules = GoalRules.Parse(ConfigMigration.Rules(db)!);

        // 停用的目标不出现在可选列表里，但它没被丢掉
        Assert.Equal(["编程"], rules.SelectableGoals);
        Assert.True(rules.Matches("编程", "Code", ""));
        Assert.True(rules.Matches("编程", "Code.exe", ""));       // 反斜杠没被吃掉
        Assert.True(rules.Matches("编程", "Google Chrome", "GitHub"));
        Assert.False(rules.Matches("编程", "Google Chrome", "别的"));
    }

    /// <summary>
    /// 搬出来的文件里，中文必须是**中文本身**，不是 <c>\uXXXX</c>。
    ///
    /// ⚠️ **往返测试看不见这个 bug**：`JsonSerializer` 默认把非 ASCII 转义，而解析器
    /// 又原样解回来——两边对称，`规则往返_…` 那条无论转义与否都是绿的。可这四份 `.md`
    /// 的全部意义就是「整个文件扔给谁都能读」：用户要手改，AI 要照着抄名字。
    /// 一个叫 `"\u5B66\u4E60\u7ECF\u6D4E\u5B66"` 的目标机器读得懂，人读不懂。
    ///
    /// ⚠️ 所以这一条**断言的是文本本身**，不是往返结果。2026-09-19 真出过：
    /// 库里搬出来的 `rules.md` 里每个中文目标名都是转义串，而 `schedule.md` 好好的
    /// ——因为 cron 那半边压根不过 JSON。同一份配置里两种语言待遇不同。
    /// </summary>
    [Fact]
    public void 搬出来的文件是给人读的_中文不许变成转义串()
    {
        using var db = OldStore();

        var rules = ConfigMigration.Rules(db)!;
        Assert.Contains("\"编程\"", rules);
        Assert.Contains("\"去年的\"", rules);
        Assert.DoesNotContain(@"\u", rules);

        var schedule = ConfigMigration.Schedule(db)!;
        Assert.Contains("海贼王", schedule);
        Assert.DoesNotContain(@"\u", schedule);
    }

    [Fact]
    public void 命令往返_闹钟绑定两个系统都填上了()
    {
        using var db = OldStore();
        var settings = new Settings { AlarmCommand = "sleep" };

        var t = CommandTable.Parse(ConfigMigration.Commands(db, settings)!);

        Assert.Equal(["mac-only", "sleep"], t.Names);
        Assert.Equal("sleep", t.AlarmName);                       // 老的单个名字 → 两边同名
        Assert.NotNull(t.TextFor("sleep"));
        // 只给了 macOS 的那条：在 Windows 上就是「没有」，不退而求其次
        if (OperatingSystem.IsWindows()) Assert.Null(t.TextFor("mac-only"));
        else Assert.Equal("say hi", t.TextFor("mac-only"));
    }

    [Fact]
    public void 计划往返_带命令的行变成叹号写法_停用的行变成注释()
    {
        using var db = OldStore();

        var text = ConfigMigration.Schedule(db)!;
        var f = AlarmsList.Read(text);

        Assert.Empty(f.Skipped);
        Assert.Equal(2, f.Entries.Count);                          // 停用那条被注释掉了
        Assert.Equal("海贼王", f.Entries[0].Text);
        Assert.Null(f.Entries[0].Run);
        Assert.Equal("该睡了", f.Entries[1].Text);
        Assert.Equal("sleep", f.Entries[1].Run);                   // ⚠️ 老的 run 列 → 行尾 !sleep
        Assert.Contains("# 0 * * * *", text);                      // 停用 = 注释掉，不是丢掉
    }

    [Fact]
    public void 外观往返()
    {
        var f = LayoutFile.Parse(ConfigMigration.Layout(
            new Settings { Layout = "compact", OpacityPercent = 60 }));

        Assert.Equal("compact", f.Layout);
        Assert.Equal(60, f.OpacityPercent);
    }

    [Fact]
    public void 新库不会被当成老库()
    {
        // ⚠️ 这一条守的是「Open 不再建那五张表」——不然全新安装每次都会跑一遍迁移
        using var db = SampleStore.Open(":memory:");
        Assert.False(db.HasLegacyConfig);
    }
}

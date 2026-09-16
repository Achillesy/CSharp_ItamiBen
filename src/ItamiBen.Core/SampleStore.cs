using Microsoft.Data.Sqlite;

namespace ItamiBen.Core;

/// <summary>
/// **一秒一行的观测库。** 这一层唯一碰磁盘的地方（对应 v3 的 `AwClient` 之于网络）。
///
/// 它取代了 ActivityWatch：不再有 aw-server、不再有两个 watcher、不再有 HTTP。
/// 前台窗口是本程序自己读的（实测跟 AW 记的逐字节相同），一秒写一行，判定的时候
/// 再整段读回来重建。
///
/// ## 为什么整层「镜像 + 预测」消失了
///
/// v3 有 `AwMirror` / `MirrorFeed` / 预测三规则 / 环形缓冲 / `carryForward` 一整层，
/// **它们存在的唯一理由是 AW 的数据会事后改写**（窗口事件滞后 6~12 秒、afk 回填
/// 3 分钟），所以每一秒的判定在几分钟内都可能被推翻，必须留一个可重画的窗口。
///
/// **SQLite 不会改写自己写过的行。** 于是那一层整个不需要——想知道环长什么样，
/// 把区间读回来重放一遍就是了，重放多少次结果都一样。
///
/// ## 表结构
///
/// <code>
/// app    (id, name)              去重：app 名短但极重复
/// title  (id, text)              去重：标题长（实测 67 字节）且一坐十分钟不变
/// sample (at, app_id, title_id, idle)
/// </code>
///
/// <c>at</c> 用 <c>INTEGER PRIMARY KEY</c> 有三重好处，都是承重的：
/// <list type="number">
///   <item>在 SQLite 里它**就是 rowid**，表本身即索引，区间查询是一次顺序扫描——
///         引擎能做的最快的事，不用再建索引；</item>
///   <item>它把「一秒只记一行」变成**存储引擎的约束**，`INSERT OR IGNORE` 让重复
///         写入成为空操作——去重哨兵不用自己维护；</item>
///   <item>天然有序，读出来就是时间序，不用 ORDER BY 排序。</item>
/// </list>
///
/// 容量：一轮两小时 7200 行，去重之后整库约 150KB。
///
/// ⚠️ **测试一律传 <c>":memory:"</c>**，这样测试碰不到用户真实的库。v3 栽过一次
/// （它的 I5：单测把用户的账本冲成了测试数据，而且悄无声息）。
/// </summary>
public sealed class SampleStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Dictionary<string, long> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _titles = new(StringComparer.Ordinal);

    private SampleStore(SqliteConnection db) => _db = db;

    /// <summary>打开（不存在就建）。<paramref name="path"/> 传 <c>":memory:"</c> 就是一个用完即弃的库。</summary>
    public static SampleStore Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();

        // WAL + synchronous=NORMAL：一秒一次写入，逐次 fsync 既慢又没必要。
        // ⚠️ 知情代价：断电时可能丢最后几秒——那几秒本来就只是环上的几格，
        //    而账本（during.json）是整轮结束时才写的，不受影响。
        // ⚠️ 内存库不支持 WAL，失败就算了，反正内存库也没有崩溃一说。
        Execute(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", ignoreErrors: true);

        Execute(db, """
            CREATE TABLE IF NOT EXISTS app   (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS title (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS sample (
              at       INTEGER PRIMARY KEY,
              app_id   INTEGER REFERENCES app(id),
              title_id INTEGER REFERENCES title(id),
              idle     INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS round (
              started_at    INTEGER PRIMARY KEY,   -- UTC 秒，已抹到整分
              focus_minutes INTEGER NOT NULL,
              goals         TEXT NOT NULL,         -- 换行分隔（目标名可能含逗号）
              ended_at      INTEGER,               -- NULL = 还在跑
              end_reason    TEXT
            );
            -- 值得记一笔的事。⚠️ **凡是能从 sample / round 推出来的，这里一律不记**
            -- （DECISIONS I11）：那就成了第二份副本，而副本迟早跟正本对不上。
            -- 这张表只放**别处留不下痕迹**的东西：闹钟响了、提醒到点了、命令跑了、
            -- 出错了。正常跑一轮，它一行都不该长。
            -- ⚠️ `at` 不是主键：同一秒可以有好几件事。
            CREATE TABLE IF NOT EXISTS event (
              at    INTEGER NOT NULL,
              level TEXT NOT NULL,                 -- info / warn / error
              kind  TEXT NOT NULL,                 -- alarm / cron / command / stop / ...
              text  TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS event_at ON event(at);
            -- 程序自己的设置（音色、置顶、窗口位置、闹钟时刻……）。
            -- ⚠️ **这些从来不是用户手写的**，跟 rules.json / alarms.cron / layout.json
            -- 不是一类东西：那三份用户写、程序只读；这些程序写、用户只看。
            -- 放这儿是为了少一个文件，也为了**不用再整份重写**——JSON 那套「一次写全部」
            -- 正是两个实例互相覆盖的根源（I1）。
            -- ⚠️ `value` 存的是**JSON 片段**（字符串带引号、数字不带），因为读写复用的是
            -- 同一个类型模型和同一个解析器——**一个文件一条读取路径**，不另开一套。
            CREATE TABLE IF NOT EXISTS setting (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            -- 每个目标的终身累计秒数（原来的 during.json）。
            -- ⚠️ 这是**账本**，跟上面几张表不是一个量级：观测丢了还能重新采，
            -- 这里丢了就是几十上百个小时凭空消失。所以加法走
            -- `UPDATE ... seconds + $n` 的 upsert，**在库里原子地加**，
            -- 而不是「读出来、加一加、整份写回去」——后者中途没了就全丢。
            CREATE TABLE IF NOT EXISTS total (
              goal    TEXT PRIMARY KEY,
              seconds INTEGER NOT NULL
            );

            -- ── 下面四张是**配置**表：智能体改这些，程序读这些 ─────────────────
            --
            -- ⚠️ 上面那些是**账本**（sample / app / title / round / event / total）：
            --    程序写、谁都别改。分界写在 AGENT.md 里，那是给智能体看的那份。

            -- 配置版本号。**只有一行。** 改完配置把 version 加一，
            -- 运行中的程序下一分钟就会重读——否则改了要等重启才生效，
            -- 而「改了没反应」正是这个项目最恨的那类失败。
            CREATE TABLE IF NOT EXISTS config (
              id         INTEGER PRIMARY KEY CHECK (id = 1),
              version    INTEGER NOT NULL,
              changed_at INTEGER NOT NULL,
              note       TEXT
            );

            -- 小目标。position 决定界面上的顺序；enabled=0 = 不再用但留着
            -- （**删了以后想找回来还得重写**）。
            CREATE TABLE IF NOT EXISTS goal (
              name     TEXT PRIMARY KEY,
              enabled  INTEGER NOT NULL DEFAULT 1,
              position INTEGER NOT NULL DEFAULT 0,
              note     TEXT
            );

            -- 匹配规则。**组内任意一条命中就算命中**；一条里 app 和 title 都写就都要中。
            -- ⚠️ 两个都是正则，而且**区分大小写**：macOS 报 `Code`、Windows 报 `Code.exe`，
            --    两边都要覆盖就写 `^Code(\.exe)?$`。写成只对一边的，另一边**一条都不中
            --    而且不报错**——症状是整轮全红，跟「今天确实没干活」长得一模一样。
            CREATE TABLE IF NOT EXISTS rule (
              id    INTEGER PRIMARY KEY,
              goal  TEXT NOT NULL REFERENCES goal(name) ON DELETE CASCADE,
              app   TEXT,
              title TEXT,
              note  TEXT,
              CHECK (app IS NOT NULL OR title IS NOT NULL)
            );

            -- 命令清单。**按名字引用**，闹钟和计划表都从这儿挑。
            -- ⚠️ 可执行的文本**只准出现在这一张表里**：别处（计划表）只存名字。
            --    这样「这台机器上有哪些命令能被自动跑」永远只要看一个地方。
            CREATE TABLE IF NOT EXISTS command (
              name    TEXT PRIMARY KEY,
              macos   TEXT,
              windows TEXT,
              note    TEXT,
              CHECK (macos IS NOT NULL OR windows IS NOT NULL)
            );

            -- 计划表（原来的 alarms.cron）。cron 是标准 crontab 的前五列。
            -- text 是提醒文字，run 是要跑的命令名，**至少写一个**。
            -- ⚠️ 带 run 的条目**错过了就不补**：合盖两小时再打开，一条 22:00 的关机
            --    会当场执行。只提醒的条目照旧补放（那正是提醒该有的行为）。
            CREATE TABLE IF NOT EXISTS schedule (
              id      INTEGER PRIMARY KEY,
              cron    TEXT NOT NULL,
              text    TEXT,
              run     TEXT REFERENCES command(name),
              enabled INTEGER NOT NULL DEFAULT 1,
              note    TEXT,
              CHECK (text IS NOT NULL OR run IS NOT NULL)
            );
            """);

        return new SampleStore(db);
    }

    private static void Execute(SqliteConnection db, string sql, bool ignoreErrors = false)
    {
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) when (ignoreErrors) { }
    }

    /// <summary>
    /// 记下这一秒。**同一秒重复写是空操作**（主键约束 + `INSERT OR IGNORE`），
    /// 所以调用方多调无害——这正是「当场定死、永不改写」的落地。
    /// </summary>
    /// <returns>真的写进去了没有。同一秒第二次调用返回 false。</returns>
    public bool Write(DateTimeOffset at, string app, string title, int idleSeconds)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO sample (at, app_id, title_id, idle) VALUES ($at, $app, $title, $idle);";
        cmd.Parameters.AddWithValue("$at", at.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$app", (object?)Intern(_apps, "app", "name", app) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)Intern(_titles, "title", "text", title) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$idle", Math.Max(idleSeconds, 0));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 字符串 → id，内存里缓存一份。字符串重复率极高（一坐十分钟就是 600 行同一个标题），
    /// 所以稳态下这里全是缓存命中，一秒一次写入连一次查表都不用。
    ///
    /// 空串返回 null ⇒ 库里存 NULL ⇒ 「当时读不到」。
    /// </summary>
    private long? Intern(Dictionary<string, long> cache, string table, string column, string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (cache.TryGetValue(value, out var id)) return id;

        using var cmd = _db.CreateCommand();
        // 表名和列名是本文件里的常量，不来自外部；值走参数
        cmd.CommandText = $"INSERT INTO {table} ({column}) VALUES ($v) ON CONFLICT({column}) DO UPDATE SET {column}={column} RETURNING id;";
        cmd.Parameters.AddWithValue("$v", value);
        id = (long)cmd.ExecuteScalar()!;
        cache[value] = id;
        return id;
    }

    /// <summary>
    /// 读回 <c>[from, to)</c> 里**真的有记录**的那些秒，按时间序。
    ///
    /// ⚠️ **缺的秒就是缺着，这里不补**。调用方拿到的是「看见了什么」，
    /// 「没看见」由行的缺席表达（见 <see cref="Observation"/>）。
    /// </summary>
    public List<Observation> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var list = new List<Observation>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT s.at, a.name, t.text, s.idle
              FROM sample s
              LEFT JOIN app   a ON a.id = s.app_id
              LEFT JOIN title t ON t.id = s.title_id
             WHERE s.at >= $from AND s.at < $to;
            """;   // at 是 rowid，本来就有序，不用 ORDER BY
        cmd.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$to", to.ToUnixTimeSeconds());

        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Observation(
                // ⚠️ 在边界上归一到本地时区。v3 栽过：AW 的 UTC 偏移一路流到显示层，
                //    日志里打出「16:37:35 达成」而本地其实是 00:37:35
                At: DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)).ToLocalTime(),
                App: r.IsDBNull(1) ? "" : r.GetString(1),
                Title: r.IsDBNull(2) ? "" : r.GetString(2),
                IdleSeconds: r.GetInt32(3)));

        return list;
    }

    // ── 轮次：本轮状态进库 ⇒ 崩溃不丢本轮 ────────────────────────────────

    /// <summary>
    /// 库里记着的一轮。**只有重建需要的三个参数**——其余（专注了多少秒、余量还剩多少、
    /// 现在是哪一阶段）全都是从 `sample` 重放出来的，一个都不存。
    ///
    /// ⚠️ 别往这里加「已专注秒数」之类的字段：那就成了第二个真相源，跟重放的结果会漂。
    /// </summary>
    public readonly record struct RoundRecord(DateTimeOffset StartedAt, int FocusMinutes, IReadOnlyList<string> Goals);

    /// <summary>`round` 表的一行，含结束信息。`EndedAt` 为 null = 还在跑。</summary>
    public readonly record struct FinishedRound(DateTimeOffset StartedAt, int FocusMinutes,
                                                IReadOnlyList<string> Goals,
                                                DateTimeOffset? EndedAt, string? EndReason);

    /// <summary>`event` 表的一行。时刻已在边界上归一成本地时间。</summary>
    public readonly record struct EventRow(DateTimeOffset At, string Level, string Kind, string Text);

    /// <summary>
    /// 开一轮。
    ///
    /// ⚠️ 用 REPLACE 而不是 IGNORE：`started_at` 抹到了整分，所以同一分钟里
    /// Give up 再开一轮会撞主键，后开的那轮应该赢——它俩的环起点本来就是同一个。
    /// </summary>
    public void BeginRound(DateTimeOffset startedAt, int focusMinutes, IReadOnlyList<string> goals)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO round (started_at, focus_minutes, goals) VALUES ($at, $f, $g);";
        cmd.Parameters.AddWithValue("$at", startedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$f", focusMinutes);
        cmd.Parameters.AddWithValue("$g", string.Join('\n', goals));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 终结一轮。<paramref name="reason"/> **只是记录**，跟 <c>EndReason</c> 一样
    /// 不许拿来分叉任何逻辑（DECISIONS C5）。
    /// </summary>
    public void EndRound(DateTimeOffset startedAt, DateTimeOffset endedAt, string reason)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE round SET ended_at = $end, end_reason = $why WHERE started_at = $at;";
        cmd.Parameters.AddWithValue("$at", startedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$end", endedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$why", reason);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 还没终结的那一轮（最近的一条）。**崩溃恢复的入口**：有就把它从 `sample`
    /// 重放出来接着跑，没有就是干净启动。
    /// </summary>
    public RoundRecord? OpenRound()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT started_at, focus_minutes, goals FROM round WHERE ended_at IS NULL ORDER BY started_at DESC LIMIT 1;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new RoundRecord(
            DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)).ToLocalTime(),
            r.GetInt32(1),
            r.GetString(2).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// 记一笔事。⚠️ **能从 `sample` / `round` 推出来的东西一律别往这儿写**
    /// （DECISIONS I11）——那是第二份副本，副本迟早跟正本对不上。
    ///
    /// 写失败一律吞掉：**记不上账绝不能把程序搞崩**，跟日志同一条原则。
    /// </summary>
    public void Note(DateTimeOffset at, string level, string kind, string text)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO event (at, level, kind, text) VALUES ($at, $l, $k, $t);";
            cmd.Parameters.AddWithValue("$at", at.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$l", level);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$t", text);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    // ── 配置 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 配置版本号。**运行中的程序靠它知道该重读了**——每分钟看一眼，变了就重新装配置。
    /// 一行都还没有就是 0。
    /// </summary>
    public long ConfigVersion
    {
        get
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT version FROM config WHERE id = 1;";
            return cmd.ExecuteScalar() is long v ? v : 0;
        }
    }

    /// <summary>版本号加一。改完配置**必须调一次**，否则要等重启才生效。</summary>
    public void BumpConfig(string note)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config (id, version, changed_at, note) VALUES (1, 1, $at, $note)
            ON CONFLICT(id) DO UPDATE SET version = version + 1, changed_at = $at, note = $note;
            """;
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$note", note);
        cmd.ExecuteNonQuery();
    }

    /// <summary>一个小目标连同它的规则。</summary>
    public readonly record struct GoalRow(string Name, bool Enabled, IReadOnlyList<RuleRow> Rules);

    /// <summary>一条匹配规则。两个都可以是 null，但不能都是。</summary>
    public readonly record struct RuleRow(string? App, string? Title);

    /// <summary>一条命令，按系统分。</summary>
    public readonly record struct CommandRow(string Name, string? MacOS, string? Windows);

    /// <summary>一条计划。<paramref name="Text"/> 和 <paramref name="Run"/> 至少有一个。</summary>
    public readonly record struct ScheduleRow(string Cron, string? Text, string? Run);

    /// <summary>所有目标连同规则，按 position 排。</summary>
    public List<GoalRow> Goals()
    {
        var rules = new Dictionary<string, List<RuleRow>>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT goal, app, title FROM rule ORDER BY id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var goal = r.GetString(0);
                if (!rules.TryGetValue(goal, out var list)) rules[goal] = list = [];
                list.Add(new RuleRow(r.IsDBNull(1) ? null : r.GetString(1),
                                     r.IsDBNull(2) ? null : r.GetString(2)));
            }
        }

        using var g = _db.CreateCommand();
        g.CommandText = "SELECT name, enabled FROM goal ORDER BY position, name;";
        var goals = new List<GoalRow>();
        using var gr = g.ExecuteReader();
        while (gr.Read())
        {
            var name = gr.GetString(0);
            goals.Add(new GoalRow(name, gr.GetInt64(1) != 0,
                                  rules.GetValueOrDefault(name) ?? []));
        }
        return goals;
    }

    public List<CommandRow> Commands()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT name, macos, windows FROM command ORDER BY name;";
        var list = new List<CommandRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CommandRow(r.GetString(0),
                                    r.IsDBNull(1) ? null : r.GetString(1),
                                    r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>启用着的计划，按 id 排。</summary>
    public List<ScheduleRow> Schedule()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT cron, text, run FROM schedule WHERE enabled != 0 ORDER BY id;";
        var list = new List<ScheduleRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ScheduleRow(r.GetString(0),
                                     r.IsDBNull(1) ? null : r.GetString(1),
                                     r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>配置表是不是一条都还没有——决定要不要播种 / 迁移。</summary>
    public bool ConfigIsEmpty
    {
        get
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM goal) + (SELECT COUNT(*) FROM command) "
                            + "+ (SELECT COUNT(*) FROM schedule);";
            return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
        }
    }

    /// <summary>整批写配置。一个事务——**要么全落，要么一条都不落**。</summary>
    public void PutConfig(IReadOnlyList<GoalRow> goals, IReadOnlyList<CommandRow> commands,
                          IReadOnlyList<(ScheduleRow Row, bool Enabled, string? Note)> schedule,
                          string note)
    {
        using var tx = _db.BeginTransaction();

        void Run(string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        for (var i = 0; i < goals.Count; i++)
        {
            var g = goals[i];
            Run("INSERT INTO goal (name, enabled, position) VALUES ($n, $e, $p) "
              + "ON CONFLICT(name) DO UPDATE SET enabled = excluded.enabled, position = excluded.position;",
                ("$n", g.Name), ("$e", g.Enabled ? 1 : 0), ("$p", i));
            foreach (var r in g.Rules)
                Run("INSERT INTO rule (goal, app, title) VALUES ($g, $a, $t);",
                    ("$g", g.Name), ("$a", r.App), ("$t", r.Title));
        }
        foreach (var c in commands)
            Run("INSERT INTO command (name, macos, windows) VALUES ($n, $m, $w) "
              + "ON CONFLICT(name) DO UPDATE SET macos = excluded.macos, windows = excluded.windows;",
                ("$n", c.Name), ("$m", c.MacOS), ("$w", c.Windows));
        foreach (var (row, enabled, n) in schedule)
            Run("INSERT INTO schedule (cron, text, run, enabled, note) VALUES ($c, $t, $r, $e, $n);",
                ("$c", row.Cron), ("$t", row.Text), ("$r", row.Run), ("$e", enabled ? 1 : 0), ("$n", n));

        Run("INSERT INTO config (id, version, changed_at, note) VALUES (1, 1, $at, $note) "
          + "ON CONFLICT(id) DO UPDATE SET version = version + 1, changed_at = $at, note = $note;",
            ("$at", DateTimeOffset.Now.ToUnixTimeSeconds()), ("$note", note));

        tx.Commit();
    }

    // ── 在线修改配置：导出、执行、账本护栏 ─────────────────────────────────

    /// <summary>一次 SQL 应用的结果。</summary>
    public readonly record struct SqlResult(bool Ok, int RowsChanged, string Message);

    /// <summary>
    /// 账本的指纹。**执行任何外来 SQL 的前后各取一次，变了就整个回滚。**
    ///
    /// ⚠️ 用事后核对，**不用事前审查 SQL**：想靠解析 SQL 判断「它会不会动账本」是必输的
    /// （子查询、触发器、`DROP TABLE`、`ATTACH`）。指纹对得上才提交，这一条不依赖
    /// 任何对 SQL 的理解。
    /// </summary>
    private string LedgerFingerprint()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT COUNT(*) FROM sample) || '/' || (SELECT COUNT(*) FROM round)
                || '/' || (SELECT COUNT(*) FROM total)
                || '/' || (SELECT COALESCE(SUM(seconds), 0) FROM total)
                || '/' || (SELECT COUNT(*) FROM event);
            """;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    /// <summary>把整个库复制一份出去。<c>VACUUM INTO</c> 要求目标不存在。</summary>
    public void BackupTo(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "VACUUM INTO $p;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 执行一段外来 SQL（多半是网页 AI 写的），**全程在一个事务里**。
    ///
    /// 顺序是有讲究的：
    /// <list type="number">
    ///   <item>取账本指纹；</item>
    ///   <item>开事务、整段执行；</item>
    ///   <item>再取一次指纹，**对不上就整个回滚**——一行都不落；</item>
    ///   <item>顺手把 `config.version` 加一（AI 很可能忘了写那句，而忘了的症状是
    ///   「改了没反应」，正是这个项目最恨的那类失败）；</item>
    ///   <item>提交。</item>
    /// </list>
    ///
    /// ⚠️ **整段当一条命令跑，不按分号切**：字符串里的分号会把切分器骗过去，
    /// 为了好看的逐条报错引入一个会切错的解析器不划算。出错时 SQLite 的异常里
    /// 本来就带着出错的位置。
    /// </summary>
    public SqlResult ApplySql(string sql)
    {
        var before = LedgerFingerprint();
        var changesBefore = TotalChanges();

        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            if (LedgerFingerprint() != before)
            {
                tx.Rollback();
                return new SqlResult(false, 0,
                    "Refused: that SQL changed the ledger (sample / round / total / event). "
                    + "Nothing was written — the whole thing was rolled back.");
            }

            using (var bump = _db.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "INSERT INTO config (id, version, changed_at, note) VALUES (1, 1, $at, 'applied SQL') "
                                 + "ON CONFLICT(id) DO UPDATE SET version = version + 1, changed_at = $at, note = 'applied SQL';";
                bump.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToUnixTimeSeconds());
                bump.ExecuteNonQuery();
            }

            tx.Commit();
            return new SqlResult(true, (int)(TotalChanges() - changesBefore), "");
        }
        catch (Exception e)
        {
            try { tx.Rollback(); } catch { }
            return new SqlResult(false, 0, e.Message);
        }
    }

    private long TotalChanges()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT total_changes();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 把当前配置导成一段给网页 AI 看的 SQL。
    ///
    /// ⚠️ **`sample` 和 `title` 一行都不进来**，这条由程序钉死、不靠用户记得删：
    /// `title` 是用户开过的每一个窗口标题（看了什么、刷了谁、哪个文件名），
    /// **那是要被贴进网页对话框的东西**。程序名单另给一段，因为 AI 写 `app` 正则时
    /// 确实需要它——**程序名泄露的是「装了什么」，窗口标题泄露的是「在干什么」，
    /// 这两者不是一个量级。**
    ///
    /// ⚠️ 程序名单写成**注释**而不是 `INSERT`：`app` 是账本表，写成 INSERT 会诱导
    /// AI 往里插东西，而那会被账本护栏挡下、白跑一趟。
    /// </summary>
    public string DumpConfig(IReadOnlyList<string> agentEditableSettings)
    {
        var b = new System.Text.StringBuilder();
        b.AppendLine($"-- ItamiBen configuration as of {DateTime.Now:yyyy-MM-dd HH:mm}, config version {ConfigVersion}.");
        b.AppendLine("-- This is the CURRENT state, shown for reference. Do not repeat it back.");
        b.AppendLine("-- The ledger (sample / title / round / event / total) is deliberately not included.");
        b.AppendLine();

        foreach (var g in Goals())
        {
            b.AppendLine($"INSERT INTO goal (name, enabled) VALUES ({Q(g.Name)}, {(g.Enabled ? 1 : 0)});");
            foreach (var r in g.Rules)
                b.AppendLine($"INSERT INTO rule (goal, app, title) VALUES ({Q(g.Name)}, {Q(r.App)}, {Q(r.Title)});");
        }
        b.AppendLine();
        foreach (var c in Commands())
            b.AppendLine($"INSERT INTO command (name, macos, windows) VALUES ({Q(c.Name)}, {Q(c.MacOS)}, {Q(c.Windows)});");
        b.AppendLine();

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT cron, text, run, enabled FROM schedule ORDER BY id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                b.AppendLine($"INSERT INTO schedule (cron, text, run, enabled) VALUES ("
                           + $"{Q(r.GetString(0))}, {Q(r.IsDBNull(1) ? null : r.GetString(1))}, "
                           + $"{Q(r.IsDBNull(2) ? null : r.GetString(2))}, {r.GetInt64(3)});");
        }
        b.AppendLine();

        var settings = Settings();
        foreach (var key in agentEditableSettings)
            if (settings.TryGetValue(key, out var v))
                b.AppendLine($"INSERT INTO setting (key, value) VALUES ({Q(key)}, {Q(v)});");

        b.AppendLine();
        b.AppendLine("-- Application names this machine has actually been seen running.");
        b.AppendLine("-- Use these to write `app` rules that really match. (Window titles are NOT listed.)");
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM app ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) b.AppendLine($"--   {r.GetString(0)}");
        }

        return b.ToString();

        static string Q(string? v) => v is null ? "NULL" : "'" + v.Replace("'", "''") + "'";
    }

    /// <summary>每个目标的终身累计秒数。</summary>
    public Dictionary<string, long> Totals()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT goal, seconds FROM total;";
        var map = new Dictionary<string, long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetInt64(1);
        return map;
    }

    /// <summary>
    /// 把这一轮的秒数**加**到各目标头上，一个事务。
    ///
    /// ⚠️ **是加，不是写**：读出来在内存里加完再整份写回，中途进程没了就把之前所有的
    /// 累计一起带走。这里让数据库自己加，最坏情况是这一轮没加上，**已有的账一分不少**。
    /// </summary>
    public void AddTotals(IReadOnlyDictionary<string, int> byGoal)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO total (goal, seconds) VALUES ($g, $s) "
                        + "ON CONFLICT(goal) DO UPDATE SET seconds = seconds + excluded.seconds;";
        var g = cmd.Parameters.Add("$g", Microsoft.Data.Sqlite.SqliteType.Text);
        var sec = cmd.Parameters.Add("$s", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var (goal, seconds) in byGoal)
        {
            if (seconds <= 0) continue;   // 0 秒不开新账（C5：不写空记录）
            g.Value = goal;
            sec.Value = seconds;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>整批**覆盖**累计值。只给迁移用——正常路径一律走 <see cref="AddTotals"/>。</summary>
    public void PutTotals(IReadOnlyDictionary<string, long> byGoal)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO total (goal, seconds) VALUES ($g, $s) "
                        + "ON CONFLICT(goal) DO UPDATE SET seconds = excluded.seconds;";
        var g = cmd.Parameters.Add("$g", Microsoft.Data.Sqlite.SqliteType.Text);
        var sec = cmd.Parameters.Add("$s", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var (goal, seconds) in byGoal)
        {
            g.Value = goal;
            sec.Value = seconds;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>所有设置。没写过就是空的。</summary>
    public Dictionary<string, string> Settings()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM setting;";
        var map = new Dictionary<string, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    /// <summary>
    /// 整批写设置。**一个事务**——要么全落，要么一条都不落。
    ///
    /// ⚠️ 逐个 upsert 而不是「先清空再插入」：清空那一瞬间要是进程没了，设置就全丢了。
    /// </summary>
    public void PutSettings(IReadOnlyDictionary<string, string> values)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO setting (key, value) VALUES ($k, $v) "
                        + "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        var k = cmd.Parameters.Add("$k", Microsoft.Data.Sqlite.SqliteType.Text);
        var v = cmd.Parameters.Add("$v", Microsoft.Data.Sqlite.SqliteType.Text);
        foreach (var (key, value) in values)
        {
            k.Value = key;
            v.Value = value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>一段时间里开过的轮次（按起点算落不落在区间里），按时间先后。</summary>
    public List<FinishedRound> Rounds(DateTimeOffset from, DateTimeOffset to)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT started_at, focus_minutes, goals, ended_at, end_reason FROM round "
                        + "WHERE started_at >= $from AND started_at < $to ORDER BY started_at;";
        cmd.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$to", to.ToUnixTimeSeconds());

        var list = new List<FinishedRound>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FinishedRound(
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)).ToLocalTime(),
                r.GetInt32(1),
                r.GetString(2).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                r.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)).ToLocalTime(),
                r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    /// <summary>一段时间里记过的事，按时间先后。</summary>
    public List<EventRow> Events(DateTimeOffset from, DateTimeOffset to)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT at, level, kind, text FROM event WHERE at >= $from AND at < $to ORDER BY at;";
        cmd.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$to", to.ToUnixTimeSeconds());

        var list = new List<EventRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EventRow(
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)).ToLocalTime(),
                r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// <summary>
    /// 三张表各有多少行。**给日志和测试用**——去重是这个设计的要点
    /// （盯着一个标题十分钟 = 600 行 sample 但只有 1 行 title），
    /// 没有这个读数就只能靠相信它。
    /// </summary>
    public (long Apps, long Titles, long Samples) Counts
    {
        get
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM app), (SELECT COUNT(*) FROM title), (SELECT COUNT(*) FROM sample);";
            using var r = cmd.ExecuteReader();
            r.Read();
            return (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
        }
    }

    /// <summary>库里最新那一秒；空库是 null。</summary>
    public DateTimeOffset? Newest => Edge("MAX");

    /// <summary>库里最早那一秒；空库是 null。</summary>
    public DateTimeOffset? Oldest => Edge("MIN");

    private DateTimeOffset? Edge(string fn)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {fn}(at) FROM sample;";   // at 是 rowid，取端点是 O(log n)
        var v = cmd.ExecuteScalar();
        return v is long at ? DateTimeOffset.FromUnixTimeSeconds(at).ToLocalTime() : null;
    }

    public void Dispose() => _db.Dispose();
}

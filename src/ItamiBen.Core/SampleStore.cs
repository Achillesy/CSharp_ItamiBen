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

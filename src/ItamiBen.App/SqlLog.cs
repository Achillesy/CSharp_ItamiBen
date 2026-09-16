using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// 手动执行过的 SQL，记在运行时目录的 <c>applied-sql.log</c> 里。
///
/// <para>
/// 2026-09-16 从库里那张 `applied_sql` 表挪出来（用户：把 SQL 记在 SQL 表里让人不舒服）。
/// **挪出来之后反而更硬**：SQLite 够不着普通文件——`ATTACH` 只能挂另一个数据库——
/// 所以这份记录**根本不在任何一句外来 SQL 的射程之内**。原来靠账本指纹拦
/// `DELETE FROM applied_sql`，那是「拦得住已知的那一招」；现在是**压根没有那一招**。
/// </para>
///
/// <para>
/// ⚠️ 这个文件跟 <see cref="Log"/> 那份「最后的求救信」**不是一回事，别合并**：
/// 那份的全部价值在于它是二值的（里面有东西 = 出事了），今天刚靠这个抓到退出路径的
/// bug；这份是**正常且必要**的流水账，天天都该长。合在一起，两个性质一起没。
/// </para>
///
/// <para>
/// ⚠️ **不滚存、不截断。** 别的日志滚存是因为它们可能被高频事件灌爆；这份一次几百字节、
/// 一个月几次，一年也就几 KB。而**把审计记录滚掉，正好滚掉的是最早、也最难回忆的那些**。
/// </para>
/// </summary>
internal static class SqlLog
{
    private static readonly Lock Gate = new();

    public static string Path_ => System.IO.Path.Combine(AppData.Dir, "applied-sql.log");

    /// <summary>
    /// 记一笔。<paramref name="request"/> 是用户当时写的那句需求。
    ///
    /// ⚠️ **意图和产物要配在一起**：光有 SQL 看不出智能体有没有理解错那句话。
    /// </summary>
    public static void Append(string? request, string statement, bool ok, int rows, string? message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                var head = ok ? $"OK  {rows} row(s)" : $"FAILED  {message}";
                var text = $"""

                    ────────────────────────────────────────────────────────
                    {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {head}
                    {(string.IsNullOrWhiteSpace(request) ? "asked: (not recorded)" : "asked: " + request.Trim())}

                    {statement.Trim()}

                    """;
                File.AppendAllText(Path_, text);
            }
            catch (Exception e)
            {
                // 记不上账不该把这次执行本身搞砸——但这条要留个声音，
                // 因为「改了却没留痕」正是这份文件存在要防的事
                Events.Error("sql", "Could not write applied-sql.log", e);
            }
        }
    }

    /// <summary>整份读出来给 <c>--query sql</c>。没有就是没改过。</summary>
    public static string Read()
    {
        try { return File.Exists(Path_) ? File.ReadAllText(Path_) : ""; }
        catch (Exception e) { return $"(could not read {Path_}: {e.Message})"; }
    }

    /// <summary>
    /// 把库里那张老的 `applied_sql` 表搬进文件，然后把表删掉。只在表还在时干活。
    ///
    /// ⚠️ **先写文件、确认没抛，再删表**：顺序反了中途出错就两头都没了。
    /// </summary>
    public static void MigrateFromTable(SampleStore store)
    {
        try
        {
            if (store.TakeAppliedSqlRows() is not { Count: > 0 } rows) return;
            foreach (var a in rows.OrderBy(a => a.At))
                Append(a.Request, a.Statement, a.Ok, a.RowsChanged, a.Message);
            store.DropAppliedSqlTable();
            Events.Info("sql", $"moved {rows.Count} applied-SQL record(s) out of the database into applied-sql.log");
        }
        catch (Exception e)
        {
            Events.Error("sql", "Failed to move applied_sql out of the database", e);
        }
    }
}

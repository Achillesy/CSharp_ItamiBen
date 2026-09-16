using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// <see cref="GoalTotals"/> 的**落盘那一半**——现在落在 `samples.db` 的 `total` 表里
/// （2026-09-16 从 `during.json` 搬进来，DECISIONS I13）。
///
/// ⚠️ **纯逻辑和写盘必须分在两层**，这是从 v3 买回来的教训（它的 I5）：两者写在一个
/// 方法里，单元测试调一次就把用户真实的账本冲成了测试数据，而且悄无声息。
/// 现在 Core 里根本没有 `File.` 也没有 SQL，测试想碰用户的账本都碰不到。
/// </summary>
public static class Totals
{
    /// <summary>
    /// 读账本。库没开就是空账本——**程序照样跑**，只是目标右边那个数显示成 0。
    ///
    /// 第一次跑会把老的 `during.json` 搬进来，然后把文件改名成 `during.json.migrated`：
    /// 留着是为了万一要回看，改名是为了**它不再看起来像还在生效的账本**。
    /// </summary>
    public static GoalTotals Load(SampleStore? store)
    {
        if (store is null) return new GoalTotals();
        try
        {
            var rows = store.Totals();
            if (rows.Count == 0) rows = MigrateFromJson(store);
            return GoalTotals.Of(rows);
        }
        catch (Exception e)
        {
            Events.Error("totals", "Failed to read totals", e);
            return new GoalTotals();
        }
    }

    /// <summary>
    /// 把这一轮的成绩记上。**受控退出时只调这一次**（DECISIONS C5：达成 / Give up /
    /// 触底 / 关窗走的是同一个动作）。
    ///
    /// ⚠️ **是「加」不是「写」**：老的 during.json 每次整份重写，读出来在内存里加完再
    /// 写回去——中途进程没了就把**之前所有的累计**一起带走。现在让数据库自己加，
    /// 最坏情况是这一轮没记上，**已有的账一分不少**。
    /// </summary>
    public static void Add(SampleStore? store, IReadOnlyDictionary<string, int> byGoal)
    {
        if (store is null) return;
        try
        {
            store.AddTotals(byGoal);
        }
        catch (Exception e)
        {
            // 记不上也不能崩——那些秒已经丢了，再把程序带走是雪上加霜
            Events.Error("totals", "Failed to write totals", e);
        }
    }

    /// <summary>把老的 `during.json` 搬进库，然后改名。只在库里一条累计都没有时才走这一遭。</summary>
    private static Dictionary<string, long> MigrateFromJson(SampleStore store)
    {
        var path = AppData.TotalsPath();
        if (!File.Exists(path)) return [];

        var map = new Dictionary<string, long>();
        try
        {
            var totals = GoalTotals.Parse(File.ReadAllText(path));
            foreach (var goal in totals.Goals) map[goal] = totals[goal];

            // ⚠️ **先落库、确认没抛，再改名**：反过来的话中途出错就两头都没了，
            //    而这是账本，丢的是几十上百个小时
            if (map.Count > 0) store.PutTotals(map);
            File.Move(path, path + ".migrated", overwrite: true);
            Events.Info("totals", $"migrated {map.Count} goals from during.json");
        }
        catch (Exception e)
        {
            Events.Error("totals", "Failed to migrate during.json", e);
        }
        return map;
    }
}

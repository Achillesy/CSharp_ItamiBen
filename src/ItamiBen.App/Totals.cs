using ItamiBen.Core;

namespace ItamiBen.App;

/// <summary>
/// <see cref="GoalTotals"/> 的**落盘那一半**——`during.json` 的读和写。
///
/// ⚠️ **纯逻辑和写盘必须分在两层**，这是从 v3 买回来的教训（它的 I5）：两者写在一个
/// 方法里，单元测试调一次就把用户真实的账本冲成了测试数据，而且悄无声息。
/// 现在 Core 里根本没有 `File.`，测试想碰用户的文件都碰不到。
/// </summary>
public static class Totals
{
    public static GoalTotals Load()
    {
        try
        {
            var path = AppData.TotalsPath();
            return GoalTotals.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception e)
        {
            Log.Error("Failed to read during.json", e);
            return GoalTotals.Parse(null);
        }
    }

    /// <summary>
    /// 写账本。**受控退出时只调这一次**（DECISIONS C5：达成 / Give up / 触底 / 关窗
    /// 走的是同一个动作）。
    /// </summary>
    public static void Save(GoalTotals totals)
    {
        try
        {
            Directory.CreateDirectory(AppData.Dir);
            File.WriteAllText(AppData.TotalsPath(), totals.ToJson());
        }
        catch (Exception e)
        {
            // 写不进去也不能崩——那些秒已经丢了，再把程序带走是雪上加霜
            Log.Error("Failed to write during.json", e);
        }
    }
}

namespace ItamiBen.Core;

/// <summary>
/// **一秒的原始观测**——数据库里的一行，判定之前的东西。
///
/// 它跟 <see cref="SecondJudgment"/> 的分工是死的：**这里是「看见了什么」，那里是
/// 「算不算数」**。规则可以改、目标可以换，观测不变——所以整个环任何时候都能从库里
/// 重建出同一个结果。
///
/// ⚠️ <b>库里缺一行 ≠ 这一秒空闲</b>。缺行是**第三种状态**：那一秒没人在看
/// （程序没跑、电脑睡了、漏拍）。它既不计入也不算跑偏，但照样从环上过去
/// （DESIGN §4.1）。**绝不要为了「补全」往库里插假行**——那会把「没观测到」和
/// 「观测到空闲」搅成一件事，而这两者待遇完全不同。
/// </summary>
/// <param name="At">这一秒。秒级对齐，本地时区（在 <see cref="SampleStore"/> 的边界上归一）。</param>
/// <param name="App">前台 app 名。空 = 当时读不到前台窗口。</param>
/// <param name="Title">前台窗口标题。空 = 读不到，或者那个窗口本来就没标题。</param>
/// <param name="IdleSeconds">
/// 距上次键鼠输入多少秒。**存原始值，不存算好的 afk 标志**——AW 的门槛是 180 秒，
/// 意味着「人不在」永远是事后才知道的：跨过门槛那一刻，前面 180 行早就写完了。
/// 写入时就编码成 afk 的话那 180 秒永远找不回来，回头 UPDATE 又破了
/// 「每一秒当场定死、永不改写」。所以离开区间在**读取时**算，起点回溯到 <c>At − IdleSeconds</c>。
/// </param>
public readonly record struct Observation(DateTimeOffset At, string App, string Title, int IdleSeconds);

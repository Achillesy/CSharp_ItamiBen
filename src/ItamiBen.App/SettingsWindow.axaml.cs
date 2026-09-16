using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using ItamiBen.App.Platform;

namespace ItamiBen.App;

/// <summary>
/// 设置窗口：**只放需要挑值的东西**（音色、音量）。
///
/// ⚠️ 开关（滴答 / 到点跑命令 / 置顶）全在钟面右键菜单上，**不在这里重复一份**——
/// 同一个状态摆两个地方迟早分家，而分家之后界面还是好好的、只是说的不是同一件事。
///
/// 改动**当场生效当场落盘**：没有「确定 / 取消」。这是三个外观类的值，改错了再改回来
/// 就是，为它维护一份「还没提交的草稿」不值当。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    /// <summary>装填下拉框时置起来，免得初始化那几下 SelectionChanged 直接开始试听。</summary>
    private bool _loading;

    // Avalonia 的设计器要一个无参构造
    public SettingsWindow() : this(new Settings()) { }

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        AvaloniaXamlLoader.Load(this);

        var available = Sound.Available();

        _loading = true;
        Fill("AlarmSound", available, _settings.AlarmSound);
        Fill("AlarmsSound", available, _settings.AlarmsSound);
        this.FindControl<Slider>("TickVolume")!.Value = _settings.TickVolume;
        _loading = false;

        Wire("AlarmSound", name => _settings.AlarmSound = name);
        Wire("AlarmsSound", name => _settings.AlarmsSound = name);

        // ⚠️ 音量改了要**当场试听**：合成是按音量烘焙的，不听一下根本不知道调到哪儿了。
        //    而且 Tick.Play 内部音量一变就会重新合成并 MacAudio.Forget——不 Forget 的话
        //    拖滑块什么都不会发生（SystemSoundID 在创建那一刻就把音频吃进去了）
        var volume = this.FindControl<Slider>("TickVolume")!;
        volume.PropertyChanged += (_, e) =>
        {
            if (_loading || e.Property != RangeBase.ValueProperty) return;
            _settings.TickVolume = (int)Math.Round(volume.Value);
            Tick.Play(DateTime.Now.Second, _settings.TickVolume);
            _settings.Save();
        };

        this.FindControl<Button>("OpenFolder")!.Click += (_, _) => AppData.OpenInFileManager();

        Closed += (_, _) => _settings.Save();
    }

    private void Fill(string name, IReadOnlyList<string> available, string? current)
    {
        var box = this.FindControl<ComboBox>(name)!;
        box.ItemsSource = available;
        box.SelectedItem = current is not null && available.Contains(current) ? current : null;
    }

    /// <summary>挑一个就**当场放一遍**——挑音色不听一下没有意义。</summary>
    private void Wire(string name, Action<string> assign)
    {
        var box = this.FindControl<ComboBox>(name)!;
        box.SelectionChanged += (_, _) =>
        {
            if (_loading || box.SelectedItem is not string picked) return;
            assign(picked);

            // ⚠️ 试听是 `Play` 一遍，不是 `Repeat` 四遍：这是在挑音色，不是在排练打扰
            Sound.Play(picked);
            _settings.Save();
        };
    }
}

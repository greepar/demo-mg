using Avalonia.Controls;
namespace Magpie.Desktop.Controls;
public record ScalingTypeChoice(string Name, string Description);
public partial class ScaleFlyoutContent : UserControl
{
    public IReadOnlyList<ScalingTypeChoice> Choices { get; } = new[] { "Factor", "Fit", "Absolute", "Fill" }
        .Select(type => new ScalingTypeChoice(LocExtension.Get($"ScalingModes_ScaleFlyout_Type_{type}"), LocExtension.Get($"ScalingModes_ScaleFlyout_Type_{type}_Description"))).ToArray();
    private bool _initialized;
    private bool _connected;
    internal void ConnectLanczosSettings()
    {
        if (_connected) return;
        var settings = Core.RuntimeSettings.Current;
        _initialized = false;
        TypeSelector.SelectedIndex = settings.ScaleType;
        WidthFactor.Minimum=Math.Min(.1m,(decimal)settings.ScaleX);WidthFactor.Maximum=Math.Max(100m,(decimal)settings.ScaleX);
        HeightFactor.Minimum=Math.Min(.1m,(decimal)settings.ScaleY);HeightFactor.Maximum=Math.Max(100m,(decimal)settings.ScaleY);
        WidthFactor.Value = (decimal)settings.ScaleX; HeightFactor.Value = (decimal)settings.ScaleY;
        WidthPixels.Value = settings.AbsoluteWidth; HeightPixels.Value = settings.AbsoluteHeight;
        _initialized = true; _connected = true;
        FactorPanel.IsVisible = settings.ScaleType is 0 or 1; PixelPanel.IsVisible = settings.ScaleType == 2;
        foreach (var number in new[] { WidthFactor, HeightFactor, WidthPixels, HeightPixels }) number.ValueChanged += (_, _) => SaveSettings();
    }
    private void SaveSettings()
    {
        if (!_connected) return;
        var settings = Core.RuntimeSettings.Current;
        settings.ScaleType = TypeSelector.SelectedIndex;
        settings.ScaleX = (double)(WidthFactor.Value ?? 1); settings.ScaleY = (double)(HeightFactor.Value ?? 1);
        settings.AbsoluteWidth = (int)(WidthPixels.Value ?? 1920); settings.AbsoluteHeight = (int)(HeightPixels.Value ?? 1080);
        settings.Save();
    }
    public ScaleFlyoutContent() { InitializeComponent(); _initialized = true; DataContext = this; }
    private void Type_Changed(object? sender, SelectionChangedEventArgs args)
    {
        if (!_initialized) return;
        if (this.FindControl<StackPanel>("FactorPanel") is not { } factor || this.FindControl<StackPanel>("PixelPanel") is not { } pixels || sender is not ComboBox combo) return;
        factor.IsVisible = combo.SelectedIndex is 0 or 1;
        pixels.IsVisible = combo.SelectedIndex == 2;
        SaveSettings();
    }
}

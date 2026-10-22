using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Controls;

// Layout equivalents of the original SettingsCard wrap visual states (476 DIP).
public class SettingsCardLayout : Panel
{
    public static readonly StyledProperty<bool> IsWrapEnabledProperty = AvaloniaProperty.Register<SettingsCardLayout, bool>(nameof(IsWrapEnabled));
    public bool IsWrapEnabled { get => GetValue(IsWrapEnabledProperty); set => SetValue(IsWrapEnabledProperty, value); }
    private bool _wrap;
    private double _iconWidth, _actionWidth, _headerHeight;
    static SettingsCardLayout() => AffectsMeasure<SettingsCardLayout>(IsWrapEnabledProperty);
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return default;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 10000;
        Children[0].Measure(new Size(width, double.PositiveInfinity));
        _iconWidth = Children[0].DesiredSize.Width;
        Children[2].Measure(new Size(Math.Max(0, width - _iconWidth), double.PositiveInfinity));
        _actionWidth = Children[2].DesiredSize.Width;
        _wrap = IsWrapEnabled && width + 34 < 476;
        Children[1].Measure(new Size(Math.Max(0, width - _iconWidth - (_wrap ? 0 : _actionWidth)), double.PositiveInfinity));
        _headerHeight = Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height);
        return new Size(Math.Min(width, _iconWidth + Children[1].DesiredSize.Width + (_wrap ? 0 : _actionWidth)),
            _wrap ? _headerHeight + 8 + Children[2].DesiredSize.Height : Math.Max(_headerHeight, Children[2].DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        var height = _wrap ? _headerHeight : finalSize.Height;
        Children[0].Arrange(new Rect(0, 0, _iconWidth, height));
        Children[1].Arrange(new Rect(_iconWidth, 0, Math.Max(0, finalSize.Width - _iconWidth - (_wrap ? 0 : _actionWidth)), height));
        Children[2].Arrange(_wrap
            ? new Rect(_iconWidth, _headerHeight + 8, Math.Max(0, finalSize.Width - _iconWidth), Children[2].DesiredSize.Height)
            : new Rect(Math.Max(0, finalSize.Width - _actionWidth), 0, _actionWidth, height));
        return finalSize;
    }
}

public class SettingsExpanderLayout : Panel
{
    public static readonly StyledProperty<bool> IsWrapEnabledProperty = AvaloniaProperty.Register<SettingsExpanderLayout, bool>(nameof(IsWrapEnabled));
    public bool IsWrapEnabled { get => GetValue(IsWrapEnabledProperty); set => SetValue(IsWrapEnabledProperty, value); }
    public static readonly StyledProperty<bool> HasIconProperty = AvaloniaProperty.Register<SettingsExpanderLayout, bool>(nameof(HasIcon));
    public bool HasIcon { get => GetValue(HasIconProperty); set => SetValue(HasIconProperty, value); }
    private bool _wrap;
    private double _actionWidth, _headerHeight;
    static SettingsExpanderLayout() => AffectsMeasure<SettingsExpanderLayout>(IsWrapEnabledProperty, HasIconProperty);
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return default;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 10000;
        _wrap = IsWrapEnabled && width + 2 < 476;
        Children[1].Measure(new Size(Math.Max(0, width - 44), double.PositiveInfinity));
        _actionWidth = Children[1].DesiredSize.Width;
        Children[0].Measure(new Size(Math.Max(0, width - 44 - (_wrap ? 0 : _actionWidth)), double.PositiveInfinity));
        Children[2].Measure(new Size(44, double.PositiveInfinity));
        _headerHeight = Math.Max(44, Children[0].DesiredSize.Height);
        var height = _wrap ? _headerHeight + Children[1].DesiredSize.Height + 8 : Math.Max(69, Math.Max(_headerHeight, Children[1].DesiredSize.Height));
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        var headerHeight = _wrap ? _headerHeight : finalSize.Height;
        Children[0].Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - 44 - (_wrap ? 0 : _actionWidth)), headerHeight));
        Children[2].Arrange(new Rect(Math.Max(0, finalSize.Width - 44), 0, 44, headerHeight));
        var left = HasIcon ? 58 : 16;
        Children[1].Arrange(_wrap
            ? new Rect(left, _headerHeight - 8, Math.Max(0, finalSize.Width - left - 16), Children[1].DesiredSize.Height)
            : new Rect(Math.Max(0, finalSize.Width - 44 - _actionWidth), 0, _actionWidth, headerHeight));
        return finalSize;
    }
}

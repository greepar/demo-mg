using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

namespace Magpie.Desktop.Controls;

public class KeyVisual : ContentControl { }

public class InfoBar : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<InfoBar, string?>(nameof(Title));
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<InfoBar, bool>(nameof(IsOpen), true);
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public static readonly StyledProperty<bool> IsClosableProperty = AvaloniaProperty.Register<InfoBar, bool>(nameof(IsClosable), true);
    public static readonly StyledProperty<string> SeverityProperty = AvaloniaProperty.Register<InfoBar, string>(nameof(Severity), "Informational");
    public bool IsClosable { get => GetValue(IsClosableProperty); set => SetValue(IsClosableProperty, value); }
    public string Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    static InfoBar()
    {
        IsOpenProperty.Changed.AddClassHandler<InfoBar>((bar, _) => bar.IsVisible = bar.IsOpen);
        SeverityProperty.Changed.AddClassHandler<InfoBar>((bar, _) => bar.UpdateSeverity());
    }
    private SymbolIcon? _severityIcon;
    public InfoBar() => UpdateSeverity();
    private void UpdateSeverity()
    {
        if (_severityIcon is not null) _severityIcon.Kind = Severity switch { "Success" => "Success", "Warning" => "Warning", "Error" => "Error", _ => "Info" };
        foreach (var severity in new[] { "informational", "success", "warning", "error" })
            Classes.Set(severity, Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _severityIcon = e.NameScope.Find<SymbolIcon>("PART_SeverityIcon");
        UpdateSeverity();
        if (e.NameScope.Find<Button>("PART_Close") is { } close) close.Click += (_, _) => IsOpen = false;
    }
}

// Property and template boundaries follow the original WinUI SettingsControls.
public class SettingsCard : ContentControl
{
    public static readonly StyledProperty<bool> IsWrapEnabledProperty = AvaloniaProperty.Register<SettingsCard, bool>(nameof(IsWrapEnabled));
    public bool IsWrapEnabled { get => GetValue(IsWrapEnabledProperty); set => SetValue(IsWrapEnabledProperty, value); }
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingsCard, string?>(nameof(Header));
    public static readonly StyledProperty<object?> DescriptionProperty = AvaloniaProperty.Register<SettingsCard, object?>(nameof(Description));
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<SettingsCard, string?>(nameof(Icon));
    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

public class SettingsGroup : HeaderedItemsControl
{
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Description));
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}

public class PageFrame : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PageFrame, string?>(nameof(Title));
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public static readonly StyledProperty<Control?> HeaderActionProperty = AvaloniaProperty.Register<PageFrame, Control?>(nameof(HeaderAction));
    public Control? HeaderAction { get => GetValue(HeaderActionProperty); set => SetValue(HeaderActionProperty, value); }
    public static readonly StyledProperty<Control?> IconProperty = AvaloniaProperty.Register<PageFrame, Control?>(nameof(Icon));
    public Control? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public PageFrame() => SizeChanged += (_, _) => PseudoClasses.Set(":narrow", Bounds.Width <= 590);
}

public class SettingsExpander : Expander
{
    public static readonly StyledProperty<bool> IsWrapEnabledProperty = AvaloniaProperty.Register<SettingsExpander, bool>(nameof(IsWrapEnabled));
    public bool IsWrapEnabled { get => GetValue(IsWrapEnabledProperty); set => SetValue(IsWrapEnabledProperty, value); }
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsExpander, string?>(nameof(Description));
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<SettingsExpander, string?>(nameof(Icon));
    public static readonly StyledProperty<Control?> ActionProperty = AvaloniaProperty.Register<SettingsExpander, Control?>(nameof(Action));
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Control? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
}

public class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Get(key);
    public static string Get(string key) => Core.Localization.Get(key);

}

public class ContentIsVisibleConverter : Avalonia.Data.Converters.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is Control || value is string { Length: > 0 };
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

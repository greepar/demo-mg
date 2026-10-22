using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
namespace Magpie.Desktop.Controls;
public partial class ShortcutControl : UserControl
{
    public static readonly StyledProperty<string> KeyProperty = AvaloniaProperty.Register<ShortcutControl, string>(nameof(Key), "A");
    public static readonly StyledProperty<string> ModifiersProperty = AvaloniaProperty.Register<ShortcutControl, string>(nameof(Modifiers), "Alt,Shift");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ShortcutControl, string>(nameof(Title), "Shortcut");
    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public string Modifiers { get => GetValue(ModifiersProperty); set => SetValue(ModifiersProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public ObservableCollection<string> Keys { get; } = new();
    static ShortcutControl()
    {
        KeyProperty.Changed.AddClassHandler<ShortcutControl>((control, _) => control.RefreshKeys());
        ModifiersProperty.Changed.AddClassHandler<ShortcutControl>((control, _) => control.RefreshKeys());
    }
    public ShortcutControl() { RefreshKeys(); InitializeComponent(); }
    private void RefreshKeys()
    {
        Keys.Clear();
        foreach (var modifier in Modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries)) Keys.Add(modifier);
        if (!string.IsNullOrEmpty(Key)) Keys.Add(Key);
    }
    private void Edit_Click(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) owner.ShowShortcutDialog(this);
    }
}

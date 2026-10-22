using Avalonia.Controls;
using Avalonia.Input;
using System.Collections.ObjectModel;
namespace Magpie.Desktop.Controls;
public partial class ShortcutDialogContent : UserControl
{
    public ObservableCollection<string> Keys { get; } = new();
    public string PreviewKey { get; private set; } = "A";
    public string PreviewModifiers { get; private set; } = "Alt,Shift";
    public bool IsValid { get; private set; } = true;
    public ShortcutDialogContent() => InitializeComponent();
    internal void ShowError(string message) { WarningBanner.IsVisible=true;((TextBlock)WarningBanner.Child!).Text=message; }
    public void SetShortcut(ShortcutControl shortcut)
    {
        PreviewKey = shortcut.Key; PreviewModifiers = shortcut.Modifiers;
        Keys.Clear(); foreach (var key in shortcut.Keys) Keys.Add(key);
    }
    public void Capture(KeyEventArgs args)
    {
        Keys.Clear();
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) Keys.Add("Win");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) Keys.Add("Ctrl");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) Keys.Add("Alt");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) Keys.Add("Shift");
        PreviewModifiers = string.Join(',', Keys);
        PreviewKey = args.Key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin ? "" : args.Key.ToString();
        IsValid = Keys.Count > 0 && !string.IsNullOrEmpty(PreviewKey);
        if (!string.IsNullOrEmpty(PreviewKey)) Keys.Add(PreviewKey);
        WarningBanner.IsVisible = !IsValid;
        args.Handled = true;
    }
}

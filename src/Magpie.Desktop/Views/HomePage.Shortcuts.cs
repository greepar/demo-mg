using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class HomePage
{
    private MainWindow? _shortcutWindow;
    private void RefreshShortcuts()
    {
        if(TopLevel.GetTopLevel(this) is MainWindow main&&!ReferenceEquals(main,_shortcutWindow))
        {
            if(_shortcutWindow is not null)_shortcutWindow.GlobalShortcutsChanged-=RefreshShortcuts;
            _shortcutWindow=main;main.GlobalShortcutsChanged+=RefreshShortcuts;
        }
        foreach(var (control,id) in new[]{(FullscreenShortcut,"fullscreen"),(WindowedShortcut,"windowed"),(ToolbarShortcut,"toolbar"),(ScreenshotShortcut,"screenshot")})
        {
            var binding=GlobalShortcutSettings.Binding(RuntimeSettings.Current,id);control.Key=binding.Key;control.Modifiers=binding.Modifiers;
        }
        bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
        ShortcutSetupButton.Content=chinese ? "启用全局快捷键" : "Enable global shortcuts";
        ShortcutStopButton.Content=chinese ? "停用 / 取消" : "Disable / Cancel";
        ShortcutSetupButton.IsEnabled=_shortcutWindow is not null&&!_shortcutWindow.GlobalShortcutsBusy;
        ShortcutStopButton.IsEnabled=_shortcutWindow is not null&&(_shortcutWindow.GlobalShortcutsBusy||_shortcutWindow.GlobalShortcutsActive);
        ShortcutSystemStatus.Text=!string.IsNullOrEmpty(_shortcutWindow?.GlobalShortcutStatus) ? _shortcutWindow.GlobalShortcutStatus :
            chinese ? "尚未向系统注册；按键编辑只保存偏好，启用时由桌面确认实际绑定。" : "Not registered. Editing saves preferences; enabling requests the actual bindings from your desktop.";
    }
    private async void ShortcutSetup_Click(object? sender,RoutedEventArgs args)
    {
        if(_shortcutWindow is null)return;
        try { await _shortcutWindow.EnableGlobalShortcutsAsync(); }
        catch(Exception error) { ShortcutSystemStatus.Text=error.Message; }
    }
    private async void ShortcutStop_Click(object? sender,RoutedEventArgs args)
    {
        if(_shortcutWindow is null)return;
        try { await _shortcutWindow.DisableGlobalShortcutsAsync(); }
        catch(Exception error) { ShortcutSystemStatus.Text=error.Message; }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Styling;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;
public partial class SettingsPage : UserControl
{
    private bool _initialized,_restarting;
    private readonly string[] _languageTags=["",..Localization.Languages];
    public IReadOnlyList<string> Languages { get; } = new[] { Controls.LocExtension.Get("Settings_General_Language_System") }
        .Concat(Localization.Languages.Select(tag=>System.Globalization.CultureInfo.GetCultureInfo(tag).NativeName)).ToArray();
    public SettingsPage()
    {
        InitializeComponent();DataContext=this;RefreshLanguage();RefreshTheme();RefreshConfiguration();RefreshStartup();_initialized=true;
        AttachedToVisualTree+=(_,_)=> { RefreshLanguage();RefreshTheme();RefreshConfiguration();RefreshStartup();RefreshTray(); };
    }
    private void RefreshLanguage()
    {
        bool initialized=_initialized;_initialized=false;
        LanguageSelector.SelectedIndex=Array.FindIndex(_languageTags,tag=>tag.Equals(RuntimeSettings.Current.Language,StringComparison.OrdinalIgnoreCase));
        RestartNotice.IsOpen=Localization.RequiresRestart(RuntimeSettings.Current.Language);_initialized=initialized;
    }
    private void Language_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if(!_initialized || LanguageSelector.SelectedIndex<0)return;
        var settings=RuntimeSettings.Current;string previous=settings.Language;
        settings.Language=_languageTags[LanguageSelector.SelectedIndex];
        try { settings.Save();LanguageError.IsVisible=false; }
        catch(Exception failure)
        {
            settings.Language=previous;LanguageError.Text="Could not save language: "+failure.Message;LanguageError.IsVisible=true;RefreshLanguage();return;
        }
        RestartNotice.IsOpen=Localization.RequiresRestart(settings.Language);
    }
    private void Restart_Click(object? sender,RoutedEventArgs args)
    {
        if(_restarting)return;
        try
        {
            if(!RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Restart is disabled in preview mode.");
            if(TopLevel.GetTopLevel(this) is not Window window)throw new InvalidOperationException("The application window is unavailable.");
            RuntimeSettings.Current.Save();_restarting=true;RestartLanguageButton.IsEnabled=false;ApplicationRestart.Restart();if(window is MainWindow main && main.DesktopIntegration is { } integration)integration.Exit();else window.Close();
        }
        catch(Exception failure) { _restarting=false;RestartLanguageButton.IsEnabled=true;LanguageError.Text="Could not restart: "+failure.Message;LanguageError.IsVisible=true; }
    }
    private void Theme_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_initialized || ThemeSelector.SelectedIndex<0 || Application.Current is not { } application) return;
        var settings=RuntimeSettings.Current;int previous=settings.Theme;
        try
        {
            settings.Theme=ThemeSelector.SelectedIndex;settings.Save();ThemePreference.Apply(application,settings.Theme);
        }
        catch(Exception failure)
        {
            settings.Theme=previous;RefreshTheme();
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(Localization.Get("Settings_General_Theme.Header"),failure.Message);
        }
    }
    private void RefreshTheme()
    {
        bool initialized=_initialized;_initialized=false;ThemeSelector.SelectedIndex=RuntimeSettings.Current.Theme;_initialized=initialized;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Views;
public partial class HomePage : UserControl
{
    private void FullscreenStart_Click(object? sender, RoutedEventArgs args) => Start(true);
    private void WindowedStart_Click(object? sender, RoutedEventArgs args) => Start(false);
    private void Start(bool fullscreen)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) owner.StartScaling(fullscreen, (int)DelaySeconds.Value);
    }
    internal void SetRunning(bool running)
    {
        _scalingRunning=running;
        ActiveProfileSelector.IsEnabled=!running;
        foreach (var button in new[] { FullscreenStart, WindowedStart })
            if (button.Content is StackPanel panel && panel.Children.LastOrDefault() is TextBlock label) label.Text = running ? Core.Localization.Text("Stop") : LocExtension.Get("Home_Activation_Timer_Start");
    }
    private bool _scalingRunning;
    internal void SetCountdown(bool? fullscreen,int seconds,double progress)
    {
        CountdownDisplay.IsVisible=fullscreen.HasValue;
        CountdownProgress.Value=progress;CountdownLabel.Text=seconds.ToString(System.Globalization.CultureInfo.CurrentCulture);
        foreach(var (button,mode) in new[]{(FullscreenStart,true),(WindowedStart,false)})
            if(button.Content is StackPanel panel && panel.Children.LastOrDefault() is TextBlock label)
                label.Text=_scalingRunning?Core.Localization.Text("Stop"):LocExtension.Get(fullscreen==mode?"Home_Activation_Timer_Cancel":"Home_Activation_Timer_Start");
    }
    private async void ScreenshotDirectory_Click(object? sender,RoutedEventArgs args)
    {
        if(TopLevel.GetTopLevel(this) is not { } owner || sender is not Button button)return;
        button.IsEnabled=false;
        try { if(await ScreenshotDirectoryPicker.ChooseAsync(owner,Core.RuntimeSettings.Current.ActiveSettings))RefreshScreenshotDirectory(); }
        catch(Exception error) { if(owner is MainWindow main)main.ShowToast("Screenshot directory",error.Message); }
        finally { button.IsEnabled=true; }
    }
    private async void OpenScreenshotDirectory_Click(object? sender,RoutedEventArgs args)
    {
        if(TopLevel.GetTopLevel(this) is not { } owner)return;
        try
        {
            var directory=Directory.CreateDirectory(Core.RuntimeSettings.Current.ActiveSettings.ResolveScreenshotDirectory());
            if(!await owner.Launcher.LaunchDirectoryInfoAsync(directory))throw new InvalidOperationException("The screenshot directory could not be opened.");
        }
        catch(Exception error) { if(owner is MainWindow main)main.ShowToast("Screenshot directory",error.Message); }
    }
    private bool _refreshingProfiles,_refreshingDelay;
    internal void RefreshProfiles()
    {
        _refreshingProfiles=true;
        var settings=Core.RuntimeSettings.Current;
        ActiveProfileSelector.Items.Clear();ActiveProfileSelector.Items.Add(new ComboBoxItem { Content=Core.Localization.Text("Defaults") });
        foreach(var profile in settings.Profiles)ActiveProfileSelector.Items.Add(new ComboBoxItem { Content=profile.Name,Tag=profile.Id });
        ActiveProfileSelector.SelectedIndex=settings.ActiveProfileId is { } id ? settings.Profiles.FindIndex(p=>p.Id==id)+1 : 0;
        ActiveProfileCard.IsVisible=settings.Profiles.Count>0;
        _refreshingProfiles=false;RefreshScreenshotDirectory();RefreshToolbar();
    }
    private void ActiveProfile_Changed(object? sender,SelectionChangedEventArgs args)
    {
        if(_refreshingProfiles || ActiveProfileSelector.SelectedItem is not ComboBoxItem item)return;
        try { Core.RuntimeSettings.Current.UseProfile(item.Tag as string);RefreshScreenshotDirectory();RefreshToolbar(); }
        catch(Exception error) { RefreshProfiles();if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Profile",error.Message); }
    }
    private void RefreshScreenshotDirectory() => ScreenshotDirectoryCard.Description=Core.RuntimeSettings.Current.ActiveSettings.ResolveScreenshotDirectory();
    public HomePage()
    {
        InitializeComponent();RefreshShortcuts();DelaySeconds.Value=Core.RuntimeSettings.Current.CountdownSeconds;RefreshScreenshotDirectory();RefreshPerformanceSettings();RefreshDeveloperMode();
        void RefreshDelay()=>DelayedScaling.Description=LocExtension.Get("Home_Activation_Timer_Description").Replace("{}",((int)DelaySeconds.Value).ToString(System.Globalization.CultureInfo.CurrentCulture));
        RefreshDelay();DelaySeconds.PropertyChanged+=(_,args)=> { if(args.Property==Slider.ValueProperty&&!_refreshingDelay)
        {
            int previous=Core.RuntimeSettings.Current.CountdownSeconds;
            Core.RuntimeSettings.Current.CountdownSeconds=(int)DelaySeconds.Value;
            try { Core.RuntimeSettings.Current.Save();(TopLevel.GetTopLevel(this) as MainWindow)?.DesktopIntegration?.RefreshMenu(); }
            catch(Exception error) { Core.RuntimeSettings.Current.CountdownSeconds=previous;_refreshingDelay=true;DelaySeconds.Value=previous;_refreshingDelay=false;(TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast("Countdown",error.Message); }
            RefreshDelay();
        } };
        AttachedToVisualTree+=(_,_)=> { RefreshScreenshotDirectory();RefreshProfiles();RefreshPerformanceSettings();RefreshDeveloperMode();RefreshShortcuts(); };
    }
}

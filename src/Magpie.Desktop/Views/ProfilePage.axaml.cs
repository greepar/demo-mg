using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Views;
public partial class ProfilePage : UserControl
{
    internal static readonly uint[] BuiltinEffectIds=[1,6,9,11,8,12,0,10,13,14,15,16,17,18,19,20,21,22,23,24,25,..Core.GraphCatalog.All.Where(e=>e.Registered&&e.Id>25).Select(e=>e.Id)];
    internal static int BuiltinModeCount=>BuiltinEffectIds.Length;
    private bool _catalogItemsAdded;
    public Control? ActiveFlyoutContent { get; private set; }
    private Core.ApplicationProfile? _profile;
    private Core.RuntimeSettings Settings => _profile?.Settings ?? Core.RuntimeSettings.Current;
    internal void ConfigureProfile(Core.ApplicationProfile profile)
    {
        _profile=profile;
        ProfileFrame.Icon = new SymbolIcon { Kind = "Window", Width = 24, Height = 24 };
        LaunchParametersCard.IsVisible = true;
        ProfileFrame.Title = profile.Name; ProfileActions.IsVisible = true; AutoScaleCard.IsVisible = false;RefreshSettings();
    }
    private void ProfileAction_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: string action }) ShowProfileAction(action);
    }
    private void CrtSettings_Click(object? sender,RoutedEventArgs args)
    {
        var editor=Core.GraphEffects.HasParameters(Settings.Effect) ? GraphParameterEditor.Create(Settings.Effect,i=>Core.GraphEffects.Get(Settings,Settings.Effect,i),(i,v)=>Settings.GraphParameters[Core.GraphEffects.Key(Settings.Effect,Core.GraphEffects.Definitions(Settings.Effect)[i])]=v,()=>Core.RuntimeSettings.Current.Save()) : CrtParameterEditor.Create(Settings.Crt,()=>Core.RuntimeSettings.Current.Save());
        ActiveFlyoutContent=editor;var flyout=new Flyout { Content=editor };
        PopupAlignment.Track(CrtSettingsButton,flyout);flyout.ShowAt(CrtSettingsButton);
    }
    private Flyout? _profileActionFlyout;
    public void ShowProfileAction(string action)
    {
        _profileActionFlyout?.Hide();
        Control content = action switch
        {
            "Rename" => new RenameFlyoutContent { Value = ProfileFrame.Title ?? "" },
            "Reorder" => new ReorderFlyoutContent(),
            _ => new DeleteConfirmationFlyoutContent()
        };
        ActiveFlyoutContent = content;
        var flyout = new Flyout { Content = content };
        _profileActionFlyout=flyout;
        PopupAlignment.Track(ProfileMore, flyout);
        if (content is RenameFlyoutContent rename) rename.Confirmed += (_, _) => ProfileMutation(() =>
        {
            if(_profile is null)return;
            Core.RuntimeSettings.Current.RenameProfile(_profile.Id,rename.Value);ProfileFrame.Title=_profile.Name;flyout.Hide();
        });
        if(content is ReorderFlyoutContent reorder && _profile is not null)
        {
            void RefreshPosition()=>reorder.SetPosition(Core.RuntimeSettings.Current.Profiles.IndexOf(_profile),Core.RuntimeSettings.Current.Profiles.Count);
            RefreshPosition();reorder.MoveRequested+=direction=>ProfileMutation(()=> { Core.RuntimeSettings.Current.MoveProfile(_profile.Id,direction);RefreshPosition(); });
        }
        if (content is DeleteConfirmationFlyoutContent delete)
        {
            delete.SetMessage(LocExtension.Get("Profile_MoreOptions_DeleteFlyout.Text"));
            delete.Confirmed += (_, _) => ProfileMutation(()=>
            {
                if(_profile is null)return;
                Core.RuntimeSettings.Current.DeleteProfile(_profile.Id);flyout.Hide();
                if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.Navigate("Defaults");
            });
        }
        Dispatcher.UIThread.Post(() => flyout.ShowAt(ProfileMore));
    }
    private void CaptureMethod_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_initialized) return;
        CaptureWarning.IsOpen = CaptureMethod.SelectedIndex == 1;
        CaptureTitleBarCard.IsEnabled = CaptureMethod.SelectedIndex < 2;
    }
    private void ScalingMode_SelectionChanged(object? sender,SelectionChangedEventArgs args)
    {
        if(!_initialized)return;
        var settings=Settings;
        var previous=(settings.SelectedModeId,settings.Effect,settings.ScaleType,settings.ScaleX,settings.ScaleY);
        try
        {
            if(ScalingModeSelector.SelectedIndex>=BuiltinModeCount && ScalingModeSelector.SelectedItem is ComboBoxItem { Tag: string modeId }) { Core.RuntimeSettings.Current.Modes.Single(m=>m.Id==modeId).EnsureAvailable();settings.SelectedModeId=modeId; }
            else if(ScalingModeSelector.SelectedIndex>=0&&ScalingModeSelector.SelectedIndex<BuiltinModeCount)
            {
                settings.SelectedModeId=null;
                settings.Effect=BuiltinEffectIds[ScalingModeSelector.SelectedIndex];
                CrtSettingsButton.IsVisible=(settings.Effect==12 || Core.GraphEffects.HasParameters(settings.Effect));
                settings.ScaleType=ScalingModeSelector.SelectedIndex==6 ? 0 : 1;
                settings.ScaleX=settings.ScaleY=ScalingModeSelector.SelectedIndex==6 ? 2 : 1;
            }
            else
            {
                RefreshSettings();
                if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Effect not available yet","This mode's shaders have not been ported yet.");
                return;
            }
            CrtSettingsButton.IsVisible=(settings.Effect==12 || Core.GraphEffects.HasParameters(settings.Effect)) && settings.SelectedModeId is null;
            SsimParametersCard.IsVisible=settings.Effect==14 && settings.SelectedModeId is null;
            Core.RuntimeSettings.Current.Save();
        }
        catch(Exception error)
        {
            (settings.SelectedModeId,settings.Effect,settings.ScaleType,settings.ScaleX,settings.ScaleY)=previous;
            RefreshSettings();
            if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Scaling mode",error.Message);
        }
    }
    internal void ApplyScreenshotDirectory(string path)
    {
        ScreenshotDirectoryPicker.ApplyDirectory(path,Settings);
        var settings=Settings;
        ScreenshotDirectoryCard.Description=settings.ResolveScreenshotDirectory();
    }
    private async void ScreenshotDirectory_Click(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return;
        ScreenshotDirectoryButton.IsEnabled=false;
        try
        {
            if(await ScreenshotDirectoryPicker.ChooseAsync(owner,Settings))ScreenshotDirectoryCard.Description=Settings.ResolveScreenshotDirectory();
        }
        catch(Exception error) { if(owner is MainWindow main)main.ShowToast("Screenshot directory",error.Message); }
        finally { ScreenshotDirectoryButton.IsEnabled=true; }
    }
    private void ScreenshotTemplate_Changed(object? sender, TextChangedEventArgs args)
    {
        if(!_initialized)return;
        string template=ScreenshotTemplateInput.Text ?? "";
        ScreenshotTemplateError.Text="Invalid filename template";
        ScreenshotTemplateError.IsVisible=!Core.ScreenshotNames.IsValidTemplate(template);
        if(ScreenshotTemplateError.IsVisible)return;
        var settings=Settings;string previous=settings.ScreenshotTemplate;
        try { settings.ScreenshotTemplate=template;Core.RuntimeSettings.Current.Save(); }
        catch(Exception error)
        {
            settings.ScreenshotTemplate=previous;
            ScreenshotTemplateError.Text=error.Message;ScreenshotTemplateError.IsVisible=true;
        }
    }
    private void SaveFrameLimiter()
    {
        var settings=Settings;
        bool previousEnabled=settings.FrameRateLimiterEnabled;double previousRate=settings.MaximumFrameRate;
        try
        {
            settings.FrameRateLimiterEnabled=FrameLimiterToggle.IsChecked==true;
            settings.MaximumFrameRate=(double)(MaximumFrameRateInput.Value ?? 60);
            Core.RuntimeSettings.Current.Save();
        }
        catch(Exception error)
        {
            settings.FrameRateLimiterEnabled=previousEnabled;settings.MaximumFrameRate=previousRate;
            _initialized=false;
            FrameLimiterToggle.IsChecked=previousEnabled;MaximumFrameRateInput.Value=(decimal)previousRate;
            _initialized=true;
            if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Frame rate limiter",error.Message);
        }
    }
    private void FrameLimiter_Changed(object? sender,RoutedEventArgs args) { if(_initialized)SaveFrameLimiter(); }
    private void MaximumFrameRate_Changed(object? sender,NumericUpDownValueChangedEventArgs args) { if(_initialized)SaveFrameLimiter(); }
    private bool _initialized;
    public ProfilePage()
    {
        InitializeComponent();RefreshSettings();
        AttachedToVisualTree+=(_,_)=> { RefreshSettings();if(!_devicesLoaded && !_loadingDevices)_=ReloadGraphicsDevicesAsync(); };
    }
    private void RefreshSettings()
    {
        CrtSettingsButton.IsVisible=(Settings.Effect==12 || Core.GraphEffects.HasParameters(Settings.Effect)) && Settings.SelectedModeId is null;
        _initialized=false;
        RefreshDeviceChoices();
        var settings=Settings;
        WindowScale.SelectedIndex=settings.InitialWindowedScaleFactor;
        WindowCustomScale.Minimum=Math.Min(1.1m,(decimal)settings.CustomInitialWindowedScaleFactor);
        WindowCustomScale.Value=(decimal)settings.CustomInitialWindowedScaleFactor;
        WindowCustomScale.IsVisible=settings.InitialWindowedScaleFactor==6;
        SsimParametersCard.IsVisible=settings.Effect==14 && settings.SelectedModeId is null;ProfileSsimOversharp.Value=(decimal)settings.SsimOversharp;
        OutputAlignmentSelector.SelectedIndex=settings.OutputAlignment;
        CroppingExpander.IsExpanded=settings.IsCroppingEnabled;CroppingToggle.IsChecked=settings.IsCroppingEnabled;
        CroppingLeftInput.Value=(decimal)settings.CroppingLeft;CroppingRightInput.Value=(decimal)settings.CroppingRight;
        CroppingTopInput.Value=(decimal)settings.CroppingTop;CroppingBottomInput.Value=(decimal)settings.CroppingBottom;
        FrameLimiterExpander.IsExpanded=settings.FrameRateLimiterEnabled;
        FrameLimiterToggle.IsChecked=settings.FrameRateLimiterEnabled;
        MaximumFrameRateInput.Value=(decimal)settings.MaximumFrameRate;
        ScreenshotDirectoryCard.Description=settings.ResolveScreenshotDirectory();
        ScreenshotTemplateInput.Text=settings.ScreenshotTemplate;
        ScreenshotTemplateError.IsVisible=false;
        if(!_catalogItemsAdded)
        {
            foreach(uint id in BuiltinEffectIds.Skip(21))ScalingModeSelector.Items.Add(new ComboBoxItem { Content=Core.EffectStageSettings.Name(id) });
            _catalogItemsAdded=true;
        }
        while(ScalingModeSelector.Items.Count>BuiltinModeCount)ScalingModeSelector.Items.RemoveAt(ScalingModeSelector.Items.Count-1);
        var modes=Core.RuntimeSettings.Current.Modes;
        foreach(var mode in modes)ScalingModeSelector.Items.Add(new ComboBoxItem { Content=mode.IsAvailable?mode.Name:mode.Name+" (unavailable)",Tag=mode.Id,IsEnabled=mode.IsAvailable });
        int selected=modes.FindIndex(m=>m.Id==settings.SelectedModeId);
        ScalingModeSelector.SelectedIndex=selected>=0 ? selected+BuiltinModeCount : Math.Max(0,Array.IndexOf(BuiltinEffectIds,settings.Effect));
        LaunchArgumentsInput.Text=_profile is null ? "" : string.Join('\n',_profile.Arguments);
        _initialized=true;
    }
    private void ProfileMutation(Action action)
    {
        var owner=TopLevel.GetTopLevel(this) as MainWindow;
        try { action();owner?.RefreshProfilesNavigation(); }
        catch(Exception error) { owner?.ShowToast("Profile",error.Message); }
    }
    private void LaunchArguments_Changed(object? sender,TextChangedEventArgs args)
    {
        if(!_initialized || _profile is null)return;
        var next=(LaunchArgumentsInput.Text ?? "").Split('\n').Select(a=>a.TrimEnd('\r')).Where(a=>a.Length>0).ToList();
        if(_profile.Arguments.SequenceEqual(next))return;
        var previous=_profile.Arguments;_profile.Arguments=next;
        try { Core.RuntimeSettings.Current.Save(); }
        catch(Exception error)
        {
            _profile.Arguments=previous;
            if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Profile",error.Message);
        }
    }
    internal void LaunchProfile() => ProfileMutation(()=>
    {
        if(_profile is null)return;
        if(!File.Exists(_profile.ExecutablePath))throw new FileNotFoundException("The configured executable does not exist.",_profile.ExecutablePath);
        System.Diagnostics.Process.Start(_profile.CreateLaunchInfo())?.Dispose();
    });
    private void Launch_Click(object? sender,RoutedEventArgs args)=>LaunchProfile();
    internal void OpenProgramLocation()=>ProgramAction_Click(null,new RoutedEventArgs());
    private async void ProgramAction_Click(object? sender,RoutedEventArgs args)
    {
        if(_profile is null || TopLevel.GetTopLevel(this) is not { } owner)return;
        try
        {
            if(sender is MenuItem { Tag: "ChangeExecutable" })
            {
                var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title="Select executable",AllowMultiple=false });
                if(files.Count==0)return;
                using var file=files[0];string previous=_profile.ExecutablePath;
                try { _profile.ExecutablePath=file.TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local executable.");Core.RuntimeSettings.Current.Save(); }
                catch { _profile.ExecutablePath=previous;throw; }
            }
            else
            {
                var directory=new DirectoryInfo(Path.GetDirectoryName(_profile.ExecutablePath)!);
                if(!await owner.Launcher.LaunchDirectoryInfoAsync(directory))throw new InvalidOperationException("Program location could not be opened.");
            }
        }
        catch(Exception error) { if(owner is MainWindow main)main.ShowToast("Profile",error.Message); }
    }
    private void Scale_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_initialized) return;
        if (sender is not ComboBox combo) return;
        var custom = this.FindControl<NumericUpDown>(combo.Name == "WindowScale" ? "WindowCustomScale" : "CursorCustomScale");
        if (custom is not null) custom.IsVisible = combo.SelectedIndex == combo.ItemCount - 1;
        if(combo.Name=="WindowScale")SaveWindowPresentationSettings();
    }
}

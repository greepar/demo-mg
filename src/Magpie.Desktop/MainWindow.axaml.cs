using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Magpie.Desktop.Views;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop;
public partial class MainWindow : Window
{
    private Core.ScalingSession? _scalingSession;
    private Task? _scalingRun;
    internal Core.DesktopIntegration? DesktopIntegration { get; set; }
    internal bool CanImportConfiguration=>_scalingSession is null&&!GlobalShortcutsBusy;
    internal void StopScaling() { CancelAutomaticRequest();CancelCountdown();_scalingSession?.Stop(); }
    private bool _paneOpen;
    private bool _compact;
    private string _page = "Home";
    private readonly Dictionary<string, Control> _pages = new();
    private ShortcutControl? _shortcutOwner;
    private ShortcutDialogContent? _shortcutContent;
    internal Button? PreviewProfileNavigation { get; private set; }
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, args) => { if(DesktopIntegration?.TryHide(args)!=true)StopScaling(); };
        Closed += async (_,_)=> { _automaticClosed=true;_automaticTimer?.Stop();CancelAutomaticRequest();DesktopIntegration?.Dispose();try { await DisableGlobalShortcutsAsync(); }catch(Exception error) { Console.Error.WriteLine(error.Message); } };
        RootLayout.SizeChanged += (_, _) => UpdateNavigationLayout();
        _pages["Home"] = (Control)ContentFrame.Content!;
        InitializeProfiles();
        Opened += (_, _) => { RefreshNavigation();StartAutomaticScaling(); };
        DialogShell.Confirmed += async (_, _) =>
        {
            if (_shortcutOwner is not null && _shortcutContent is { IsValid: true } content)
            {
                try { await SaveShortcutAsync(_shortcutOwner,content.PreviewKey,content.PreviewModifiers);CloseDialog(); }
                catch(Exception error) { content.ShowError(error.Message); }
            }
        };
        DialogShell.Cancelled += (_, _) => CloseDialog();
        AddHandler(KeyDownEvent, Dialog_KeyDown, RoutingStrategies.Tunnel);
    }
    internal void StartScaling(bool fullscreen, int delaySeconds)
    {
        if(delaySeconds is <0 or >5)throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        CancelAutomaticRequest();
        if (_scalingSession is not null) { _scalingSession.Stop(); return; }
        if(delaySeconds>0){StartCountdown(fullscreen,delaySeconds);return;}
        CancelCountdown();
        StartScalingSession(CreateScalingSession(),fullscreen);
    }
    private void StartScalingSession(Core.ScalingSession session,bool fullscreen)
    {
        session.StopRequested+=()=>
        {
            void Cancel(){if(ReferenceEquals(_scalingSession,session))CancelAutomaticRequest();}
            if(Avalonia.Threading.Dispatcher.UIThread.CheckAccess())Cancel();else Avalonia.Threading.Dispatcher.UIThread.Post(Cancel);
        };
        session.GlobalToolbarShortcutActive=()=>_globalShortcuts?.Bound.Any(binding=>binding.Id=="toolbar")==true;
        session.GlobalScreenshotShortcutActive=()=>_globalShortcuts?.Bound.Any(binding=>binding.Id=="screenshot")==true;
        _scalingSession = session;
        ((HomePage)_pages["Home"]).SetRunning(true);
        session.Status += text => Avalonia.Threading.Dispatcher.UIThread.Post(() => Console.WriteLine(text));
        session.Finished += failure =>
        {
            if(!ReferenceEquals(_scalingSession,session))return;
            _scalingSession = null;
            _scalingRun = null;
            ((HomePage)_pages["Home"]).SetRunning(false);
            if (failure is not null) ShowToast("Scaling could not start", failure);
            AutomaticSessionStopped(session);
        };
        _scalingRun=session.RunAsync(this, fullscreen, 0);
    }
    public void ShowShortcutDialog(ShortcutControl shortcut)
    {
        _shortcutOwner = shortcut;
        _shortcutContent = new ShortcutDialogContent { MinWidth = Math.Min(498, Math.Max(0, RootLayout.Bounds.Width - 80)) };
        _shortcutContent.SetShortcut(shortcut);
        DialogShell.SetContent(shortcut.Title, _shortcutContent);
        DialogShell.EnableSave(true);
        DialogLayer.IsVisible = true;
        DialogShell.Focus();
    }
    public void ShowDeveloperOptions()
    {
        var settings=Core.RuntimeSettings.Current;bool previous=settings.DeveloperMode;
        try { settings.DeveloperMode=true;settings.Save();if(_pages["Home"] is HomePage home)home.RefreshDeveloperMode(); }
        catch(Exception failure) { settings.DeveloperMode=previous;ShowToast("Developer mode",failure.Message); }
    }
    internal void EnsurePreviewProfile()
    {
        if (_pages.ContainsKey("Profile")) return;
        var model=Core.RuntimeSettings.Current.AddProfile("Example",Path.Combine(Path.GetTempPath(),"example-app"));
        RefreshProfilesNavigation();
        _pages["Profile"]=_pages["Profile:"+model.Id];
        PreviewProfileNavigation=ProfilesNavItems.Children.OfType<Button>().Single(b=>b.Tag as string=="Profile:"+model.Id);
    }

    internal Control ShowToast(string title, string message)
    {
        var content = new ToastPage();
        content.SetMessage(title, message);
        var flyout = new Flyout { Content = content, Placement = Avalonia.Controls.PlacementMode.Center };
        PopupAlignment.Track(WindowBorder, flyout);
        flyout.ShowAt(WindowBorder);
        return content;
    }
    private void CloseDialog()
    {
        DialogLayer.IsVisible = false;
        _shortcutOwner?.Focus();
        _shortcutContent = null;
        _shortcutOwner = null;
    }
    private void Dialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!DialogLayer.IsVisible) return;
        if (e.Key == Key.Escape) { CloseDialog(); e.Handled = true; return; }
        _shortcutContent?.Capture(e);
        DialogShell.EnableSave(_shortcutContent?.IsValid == true);
    }
    public void Navigate(string page)
    {
        _paneOpen = false;
        UpdateNavigationLayout();
        _page = page;
        if (!_pages.TryGetValue(page, out var content))
        {
            content = page switch
            {
                "Settings" => new SettingsPage(), "About" => new AboutPage(),
                "Defaults" => new ProfilePage(), "ScalingModes" => new ScalingModesPage(), _ => new HomePage()
            };
            _pages[page] = content;
        }
        ContentFrame.Content = content;
        RefreshNavigation();
    }
    private void UpdateNavigationLayout()
    {
        _compact = RootLayout.Bounds.Width > 0 && RootLayout.Bounds.Width < 920;
        if (!_compact) _paneOpen = false;
        Classes.Set("compact", _compact);
        Classes.Set("pane-open", _paneOpen);
        RootLayout.ColumnDefinitions[0].Width = new GridLength(_compact ? 48 : 321.333);
        NavSurface.Width = _compact && !_paneOpen ? 48 : 321.333;
        Grid.SetColumnSpan(NavSurface, _compact && _paneOpen ? 2 : 1);
        NavSurface.Classes.Set("pane-overlay", _compact && _paneOpen);
        PaneButton.IsVisible = _compact;
        PaneBackdrop.IsVisible = _compact && _paneOpen;
    }
    private void PaneButton_Click(object? sender, RoutedEventArgs e) { _paneOpen = !_paneOpen; UpdateNavigationLayout(); }
    private void PaneBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e) { _paneOpen = false; UpdateNavigationLayout(); }
    private void RefreshNavigation()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("nav")))
            button.Classes.Set("selected", button.Tag as string == _page);
    }
    private void Navigate_Click(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) Navigate(page); }
}

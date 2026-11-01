using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Core;
using Magpie.Desktop.Views;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private int _profileIconGeneration;
    private bool _profileIconsClosed;
    private readonly List<WriteableBitmap> _profileIconBitmaps=[];
    private readonly List<Task> _profileIconLoads=[];
    private Func<string,uint,Task<WindowsApplicationIcon?>>? _profileIconFixture;
    private void ClearProfileIcons()
    {
        ++_profileIconGeneration;
        foreach(var button in ProfilesNavItems.Children.OfType<Button>())
            if(button.Content is StackPanel {Children.Count:>0} navigation)navigation.Children[0]=new SymbolIcon {Kind="Window",Width=18,Height=18};
        foreach(var page in _pages.Values.OfType<ProfilePage>())page.SetApplicationIcon(null);
        foreach(var bitmap in _profileIconBitmaps)bitmap.Dispose();_profileIconBitmaps.Clear();
        _profileIconLoads.Clear();
    }
    private static string ProfileIconPath(ApplicationProfile profile)
        =>WindowsProfileMatching.Rule(profile) is {} rule?rule.Packaged?"":rule.PathOrAumid:profile.ExecutablePath;
    private async Task LoadProfileIconAsync(ApplicationProfile profile,StackPanel navigation,ProfilePage page)
    {
        int generation=_profileIconGeneration;string path=ProfileIconPath(profile);
        if(_profileIconsClosed||string.IsNullOrWhiteSpace(path))return;
        try
        {
            uint size=(uint)Math.Clamp(Math.Round(24*RenderScaling,MidpointRounding.AwayFromZero),1,512);
            WindowsApplicationIcon? data;
            if(_profileIconFixture is {} fixture)
            {
                if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Profile icon fixtures require isolated settings.");
                data=await fixture(path,size);
            }
            else
            {
                if(!OperatingSystem.IsWindows()||!RuntimeSettings.PersistenceEnabled)return;
                data=await Task.Run(()=>WindowsApplicationIcon.ReadExecutable(path,size));
            }
            // Completion stays on the UI dispatcher. A stale result owns only
            // managed bytes, never a native HICON or a detached image control.
            if(data is null||_profileIconsClosed||generation!=_profileIconGeneration||
                !RuntimeSettings.Current.Profiles.Contains(profile)||ProfileIconPath(profile)!=path||
                !ProfilesNavItems.Children.OfType<Button>().Any(button=>ReferenceEquals(button.Content,navigation)))return;
            var bitmap=ApplicationIconImage.Create(data);_profileIconBitmaps.Add(bitmap);
            navigation.Children[0]=new Image {Source=bitmap,Width=18,Height=18};
            page.SetApplicationIcon(bitmap);
        }
        catch(Exception error) {Console.Error.WriteLine("Application profile icon: "+error.Message);}
    }
}

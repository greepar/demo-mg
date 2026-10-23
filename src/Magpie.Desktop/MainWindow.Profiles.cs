using Avalonia.Controls;
using Avalonia.Layout;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Core;
using Magpie.Desktop.Views;

namespace Magpie.Desktop;
public partial class MainWindow
{
    private void InitializeProfiles()
    {
        var flyout=(Flyout)NewProfileNav.Flyout!;
        var form=(NewProfileFlyoutContent)flyout.Content!;
        flyout.Opening+=(_,_)=>form.RefreshSources();
        form.Created+=profile=> { RefreshProfilesNavigation();flyout.Hide();Navigate("Profile:"+profile.Id); };
        RefreshProfilesNavigation();
    }
    internal void RefreshProfilesNavigation()
    {
        ProfilesNavItems.Children.Clear();
        var profiles=RuntimeSettings.Current.Profiles;
        foreach(string key in _pages.Keys.Where(k=>k.StartsWith("Profile:") && profiles.All(p=>k!="Profile:"+p.Id)).ToArray())_pages.Remove(key);
        foreach(var profile in profiles)
        {
            string key="Profile:"+profile.Id;
            if(!_pages.TryGetValue(key,out var existing))
            {
                var created=new ProfilePage();created.ConfigureProfile(profile);_pages[key]=created;existing=created;
            }
            var page=(ProfilePage)existing;
            var button=new Button { Tag=key,Classes={"nav"},Content=new StackPanel { Orientation=Orientation.Horizontal,Spacing=16,
                Children={new SymbolIcon { Kind="Window",Width=18,Height=18 },new TextBlock { Text=profile.Name,Classes={"nav-label"} }} } };
            button.Click+=(_,_)=>Navigate(key);
            var launch=new MenuItem { IsEnabled=profile.CanLaunch,Header=Core.Localization.Text("Launch"),Icon=new SymbolIcon { Kind="Play",Width=16,Height=16 } };
            launch.Click+=(_,_)=>page.LaunchProfile();
            var location=new MenuItem { IsEnabled=profile.CanLaunch,Header=Core.Localization.Text("Open program location"),Icon=new SymbolIcon { Kind="Folder",Width=16,Height=16 } };
            location.Click+=(_,_)=>page.OpenProgramLocation();
            var delete=new MenuItem { Header=Core.Localization.Text("Delete"),Icon=new SymbolIcon { Kind="Delete",Width=16,Height=16 } };
            delete.Click+=(_,_)=> { Navigate(key);page.ShowProfileAction("Delete"); };
            var context=new MenuFlyout { Items={launch,location,delete} };
            button.ContextFlyout=context;PopupAlignment.Track(button,context);
            ProfilesNavItems.Children.Add(button);
        }
        ProfilesNavItems.IsVisible=profiles.Count>0;
        if(_pages["Home"] is HomePage home)home.RefreshProfiles();
        RefreshNavigation();
    }
}

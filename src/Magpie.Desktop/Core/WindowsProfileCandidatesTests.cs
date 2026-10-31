using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class WindowsProfileCandidatesTests
{
    private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException("Window profile creation: "+message);}
    internal static WindowsProfileCandidate Example(ulong id=10,string path="C:\\Games\\Game.exe",string windowClass="Chrome_WidgetWin_7",string title="Game window")
        =>new(new(new(title,"Game",path,100,id,1),101,windowClass),"Game");
    internal static void Verify()
    {
        bool persistence=RuntimeSettings.PersistenceEnabled;RuntimeSettings.PersistenceEnabled=false;
        string folder=Path.Combine(Path.GetTempPath(),"magpie-window-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var root=new RuntimeSettings();var firstSource=Example();
            var profile=root.AddWindowsProfile("Same display name",firstSource.Window);
            Require(profile.OriginalProfile is null&&profile.WindowsRule is {Packaged:false,WindowClass:"Chrome_WidgetWin_1"}&&WindowsProfileMatching.SupportsDesktopRule(profile),"A native rule became an import archive or lost its normalized class.");
            Require(profile.CanLaunch==OperatingSystem.IsWindows(),"A foreign Windows identity became a local launch command.");
            profile.Settings.AutoScale=AutoScaleMode.Windowed;profile.Arguments.Add("--example");
            var otherSource=Example(20,"C:\\Games\\Other.exe","OtherClass","Other window");
            var copy=root.AddWindowsProfile("Same display name",otherSource.Window,profile.Id);
            Require(copy.Settings.AutoScale==AutoScaleMode.Windowed&&!ReferenceEquals(profile.Settings,copy.Settings)&&
                copy.Arguments.SequenceEqual(profile.Arguments)&&!ReferenceEquals(copy.Arguments,profile.Arguments)&&
                copy.WindowsRule?.PathOrAumid==otherSource.Window.Source.ExecutablePath,"Copy inherited a different app's identity or shared mutable preferences.");
            copy.Settings.AutoScale=AutoScaleMode.Disabled;
            Require(ReferenceEquals(WindowsProfileMatching.Find(root.Profiles,firstSource.Window.Identity,true),profile)&&
                ReferenceEquals(WindowsProfileMatching.Find(root.Profiles,otherSource.Window.Identity,false),copy),"Same display names interfered with independent rule matching.");
            int before=root.Profiles.Count;
            try {root.AddWindowsProfile("Duplicate rule",Example(30,windowClass:"Chrome_WidgetWin_88").Window);throw new InvalidOperationException("Same normalized window rule was accepted twice.");}catch(InvalidDataException) { }
            Require(root.Profiles.Count==before,"Duplicate rule changed the profile list.");
            var available=WindowsProfileCandidates.Available([firstSource,otherSource,Example(40,"C:\\Games\\New.exe")],root);
            Require(available.Count==1&&available[0].Window.Source.WindowId==40,"Existing typed rules did not filter new candidates.");
            try {root.AddWindowsProfile("",Example(50,"C:\\Games\\Invalid.exe").Window);throw new InvalidOperationException("Invalid new profile name was accepted.");}catch(InvalidDataException) { }
            Require(root.Profiles.Count==before,"Failed profile creation was not rolled back.");
            string path=Path.Combine(folder,"profiles.json");root.WriteFile(path);var restored=RuntimeSettings.ReadFile(path);
            Require(restored.Profiles.Count==2&&restored.Profiles.All(p=>p.Name=="Same display name")&&
                restored.Profiles[0].WindowsRule?.WindowClass=="Chrome_WidgetWin_1"&&restored.Profiles[0].OriginalProfile is null&&
                restored.Profiles[0].Settings.AutoScale==AutoScaleMode.Windowed&&restored.Profiles[1].Settings.AutoScale==AutoScaleMode.Disabled,"AOT roundtrip lost native identities, duplicate names or independent modes.");
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            var rule=document.RootElement.GetProperty("Profiles")[0].GetProperty("WindowsRule");
            Require(rule.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(new[]{"Packaged","PathOrAumid","WindowClass"}.Order()),"Transient HWND/PID/TID leaked into a permanent matching rule.");
            profile.WindowsRule!.WindowClass="bad\0class";
            try {root.Validate();throw new InvalidOperationException("Invalid typed window rule was accepted.");}catch(InvalidDataException) { }
            Console.WriteLine("WINDOW PROFILE CREATION PASS: native rule normalization, duplicate rule filtering, independent same-name profiles, copy/rollback, AOT serialization without HWND/PID/TID and foreign launch guards; all source snapshots synthetic.");
        }
        finally {Directory.Delete(folder,true);RuntimeSettings.PersistenceEnabled=persistence;}
    }
}

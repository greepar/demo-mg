using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class WindowsProfileMatchingTests
{
    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException("Windows profile identity: "+message);}
    internal static void Verify()
    {
        // Independent vectors copied from the three class-name families in
        // original ProfileService; malformed names must keep their identity.
        foreach(var (raw,expected) in new (string,string)[]
        {
            ("HwndWrapper[Game;;01234567-89ab-cdef-0123-456789abcdef]","Game"),
            ("HwndWrapper[A;;B;;01234567-89ab-cdef-0123-456789abcdef]","A;;B"),
            ("HwndWrapper[;;01234567-89ab-cdef-0123-456789abcdef]",""),
            ("HwndWrapper[G;;,,,,,,,,-,,,,-,,,,-,,,,-,,,,,,,,,,,,]","G"),
            ("HwndWrapper[G;;01234567-89AB-cdef-0123-456789abcdef]","HwndWrapper[G;;01234567-89AB-cdef-0123-456789abcdef]"),
            ("HwndWrapper[G\nB;;01234567-89ab-cdef-0123-456789abcdef]","HwndWrapper[G\nB;;01234567-89ab-cdef-0123-456789abcdef]"),
            ("HwndWrapper[G;;0123456789ab-cdef-0123-456789abcdef]","HwndWrapper[G;;0123456789ab-cdef-0123-456789abcdef]"),
            ("HwndWrapper[G;;01234567-89ab-cdef-0123-456789abcdef]suffix","HwndWrapper[G;;01234567-89ab-cdef-0123-456789abcdef]suffix"),
            ("Chrome_WidgetWin_42","Chrome_WidgetWin_1"),("Chrome_WidgetWin_","Chrome_WidgetWin_1"),
            ("Chrome_WidgetWin_-1","Chrome_WidgetWin_-1"),("Chrome_WidgetWin_１","Chrome_WidgetWin_１"),
            ("XWindow_98765","XWindow_0"),("XWindow_","XWindow_0"),("XWindow_12a","XWindow_12a"),
            ("Game*","Game*"),("game","game")
        })Require(WindowsProfileMatching.NormalizeClass(raw)==expected,"Incorrect normalized class: "+raw);
        var root=OriginalConfiguration.Parse(OriginalConfigurationTests.Example).Settings;
        var first=root.Profiles[0];var packaged=root.Profiles[1];
        var desktop=new WindowsWindowIdentity(false,"C:\\Games\\example.exe","Game*");
        Require(ReferenceEquals(WindowsProfileMatching.Find(root.Profiles,desktop,false),first),"An archived desktop rule needs no local launch path to match.");
        root.ActiveProfileId=packaged.Id;
        Require(ReferenceEquals(WindowsProfileMatching.ResolveManual(root,desktop),first.Settings)&&
            ReferenceEquals(WindowsProfileMatching.ResolveManual(root,null),root)&&
            ReferenceEquals(WindowsProfileMatching.ResolveManual(root,desktop with {ClassName="NoMatch"}),root),"Manual source resolution used an unrelated selected profile instead of the matching rule/defaults.");
        Require(WindowsProfileMatching.Find(root.Profiles,desktop with {ClassName="GameWindow"},false) is null,"A literal '*' became a wildcard.");
        Require(WindowsProfileMatching.Find(root.Profiles,desktop with {PathOrAumid="c:\\Games\\example.exe"},false) is null,"Original ordinal path equality changed.");
        Require(WindowsProfileMatching.Find(root.Profiles,desktop with {Packaged=true},false) is null,"Packaged and desktop identities were mixed.");
        Require(ReferenceEquals(WindowsProfileMatching.Find(root.Profiles,new(true,"Package!App","ApplicationFrameWindow"),false),packaged),"AUMID rule identity was lost.");
        Require(first.Settings.AutoScale==AutoScaleMode.Windowed&&packaged.Settings.AutoScale==AutoScaleMode.Disabled,"Original automatic mode did not migrate independently.");
        // Ordered duplicate candidates are intentional: automatic lookup skips
        // disabled rules BEFORE considering priority, as original does.
        var second=new ApplicationProfile {Name="Second",OriginalProfile=first.OriginalProfile,Settings=first.Settings.CopyForProfile()};
        first.Settings.AutoScale=AutoScaleMode.Disabled;second.Settings.AutoScale=AutoScaleMode.Fullscreen;
        Require(ReferenceEquals(WindowsProfileMatching.Find([first,second],desktop,false),first)&&
            ReferenceEquals(WindowsProfileMatching.Find([first,second],desktop,true),second),"Manual/automatic precedence changed.");
        Require(WindowsProfileMatching.Find([new() {ExecutablePath=Path.Combine(Path.GetTempPath(),"example.exe") }],desktop,false) is null,"A local launch path was treated as a reviewed Windows window rule.");
        foreach(var (encoded,expected) in new (string,AutoScaleMode)[] {("true",AutoScaleMode.Fullscreen),("false",AutoScaleMode.Disabled),("2",AutoScaleMode.Windowed),("3",AutoScaleMode.Disabled),("-1",AutoScaleMode.Disabled),("1.5",AutoScaleMode.Disabled),("\"1\"",AutoScaleMode.Disabled)})
        {
            var migrated=OriginalConfiguration.Parse(OriginalConfigurationTests.Example.Replace("\"autoScale\":2","\"autoScale\":"+encoded)).Settings;
            Require(migrated.Profiles[0].Settings.AutoScale==expected,"Original mode compatibility: "+encoded);
        }
        VerifyArchivedPreference();
        Console.WriteLine("WINDOWS PROFILE MATCHING PASS: original class families, literal/ordinal identities, packaged separation, ordered manual/automatic filtering, legacy mode migration and AOT preference recovery; no OS window or input was accessed.");
    }
    private static void VerifyArchivedPreference()
    {
        string folder=Path.Combine(Path.GetTempPath(),"magpie-profile-matching-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var root=OriginalConfiguration.Parse(OriginalConfigurationTests.Example).Settings;
            string path=Path.Combine(folder,"settings.json");root.WriteFile(path);
            Require(RuntimeSettings.ReadFile(path).Profiles[0].Settings.AutoScale==AutoScaleMode.Windowed,"Automatic mode did not survive AOT persistence.");
            // Create a genuine older typed file with the archived original
            // mode, but no new AutoScale property at any settings depth.
            using(var original=JsonDocument.Parse(File.ReadAllText(path)))
            {
                using var bytes=new MemoryStream();using(var writer=new Utf8JsonWriter(bytes))WriteLegacy(writer,original.RootElement);
                var saved=bytes.ToArray();File.WriteAllBytes(path,saved);
                Require(RuntimeSettings.ReadFile(path).Profiles[0].Settings.AutoScale==AutoScaleMode.Windowed,"Archived mode was not recovered.");
                Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(saved),"Reading a legacy file rewrote it.");
            }
            root.Profiles[0].Settings.AutoScale=AutoScaleMode.Disabled;root.WriteFile(path);
            Require(RuntimeSettings.ReadFile(path).Profiles[0].Settings.AutoScale==AutoScaleMode.Disabled,"Archived mode overwrote an explicit disabled preference.");
            var copied=root.Profiles[0].Settings.CopyForProfile();copied.AutoScale=AutoScaleMode.Fullscreen;
            Require(root.Profiles[0].Settings.AutoScale==AutoScaleMode.Disabled,"Copy changed the source profile's mode.");
            root.Profiles[0].Settings.AutoScale=(AutoScaleMode)3;
            try {root.Validate();throw new InvalidOperationException("Invalid typed mode was accepted.");}catch(InvalidDataException) { }
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void WriteLegacy(Utf8JsonWriter writer,JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            writer.WriteStartObject();foreach(var p in element.EnumerateObject())
                if(p.Name!=nameof(RuntimeSettings.AutoScale)) {writer.WritePropertyName(p.Name);WriteLegacy(writer,p.Value);}
            writer.WriteEndObject();
        }
        else if(element.ValueKind==JsonValueKind.Array) {writer.WriteStartArray();foreach(var item in element.EnumerateArray())WriteLegacy(writer,item);writer.WriteEndArray();}
        else element.WriteTo(writer);
    }
}

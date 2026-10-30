namespace Magpie.Desktop.Core;
internal static class CursorPresentationTests
{
    internal static void Verify()
    {
        static void Check(bool value) {if(!value)throw new InvalidOperationException("Original cursor presentation differs.");}
        var settings=new RuntimeSettings();
        var mapping=new WindowsPointerMapping(new(0,0,100,100),new(-200,-100,100,0),new(-220,-120,200,100));
        float[] scales=[.5f,.75f,1,1.25f,1.5f,2,2,1.7f];
        settings.CustomCursorScaleFactor=1.7f;
        for(int i=0;i<8;++i) {settings.CursorScaling=i;Check(CursorPresentation.Scale(settings,mapping.Source,mapping.Destination)==scales[i]);}
        var shape=new NativeCursorShape {Width=32,Height=24,HotX=3,HotY=3};
        settings.CursorScaling=3;
        Check(CursorPresentation.Bounds(settings,shape,mapping,-150,-50).Equals(new NativePointerRect(-154,-54,-114,-24)));
        settings.CursorScaling=6;
        Check(CursorPresentation.Bounds(settings,shape,mapping,-150,-50).Equals(new NativePointerRect(-156,-56,-92,-8)));
        settings.CursorScaling=0;
        Check(CursorPresentation.Bounds(settings,shape,mapping,-219,-119).Equals(new NativePointerRect(-221,-121,-205,-109)));
        string folder=Path.Combine(Path.GetTempPath(),"magpie-cursor-scale-"+Guid.NewGuid().ToString("N"));
        try
        {
            settings.CursorScaling=7;settings.CursorInterpolation=1;
            var copy=settings.CopyForProfile();copy.CursorScaling=6;copy.CustomCursorScaleFactor=2;copy.CursorInterpolation=0;
            settings.Profiles.Add(new ApplicationProfile {Name="Cursor",ExecutablePath=Path.GetFullPath("cursor-scale-test"),Settings=copy});
            var path=Path.Combine(folder,"settings.json");settings.WriteFile(path);var read=RuntimeSettings.ReadFile(path);
            Check(read.CursorScaling==7&&read.CustomCursorScaleFactor==1.7f&&read.CursorInterpolation==1&&read.Profiles[0].Settings.CursorScaling==6&&read.Profiles[0].Settings.CustomCursorScaleFactor==2&&read.Profiles[0].Settings.CursorInterpolation==0);
            foreach(var bad in new RuntimeSettings[]{new(){CursorScaling=8},new(){CursorInterpolation=2},new(){CustomCursorScaleFactor=float.NaN},new(){CustomCursorScaleFactor=.09f},new(){CustomCursorScaleFactor=10.1f}})
            {try {bad.WriteFile(path);throw new Exception("Invalid cursor preference saved.");}catch(InvalidDataException) { }}
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
        Console.WriteLine("CURSOR PRESENTATION PASS: original presets, source mean scale, float hotspot/local half rounding, independent profiles and AOT persistence.");
    }
}

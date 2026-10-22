using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class WindowSizingTests
{
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void Reject(Action action,string message)
    {
        try { action(); }catch(Exception error) when(error is InvalidDataException or InvalidOperationException) { return; }throw new Exception(message);
    }
    internal static void Verify()
    {
        var settings=new RuntimeSettings();
        var automatic=WindowSizing.Initial(settings,640,480,1920,1080,1);
        Require(automatic.Width==1080 && automatic.Height==810,"Original Auto did not use three quarters of the limiting display axis.");
        settings.InitialWindowedScaleFactor=1;
        var manual=WindowSizing.Initial(settings,800,450,1920,1080,1);
        Require(manual.Width==1000 && manual.Height==563,"Original width-first lround sizing is incorrect.");
        var framed=WindowSizing.Initial(settings,800,450,1920,1080,1,0,0,820,480);
        Require(framed.Width==1031 && framed.Height==580,"Known source frame geometry was ignored in the minimum-margin calculation.");
        var hidpi=WindowSizing.Initial(settings,800,450,3840,2160,1.5);
        Require(hidpi.Width==1067 && hidpi.Height==600,"Minimum margin was not converted from logical to physical pixels.");
        settings.InitialWindowedScaleFactor=5;
        var bounded=WindowSizing.Initial(settings,800,450,1920,1080,1,0,24);
        Require(bounded.Width==1877 && bounded.Height==1056,"Large initial window did not fit the work area minus decorations.");
        var halfPixel=WindowSizing.Initial(settings,300,200,1000,401,1);
        Require(halfPixel.Width==602 && halfPixel.Height==401,"Work-area limiting truncated the original half-pixel width.");
        settings.InitialWindowedScaleFactor=0;
        var huge=WindowSizing.Initial(settings,800,450,16000,10000,1);
        Require((ulong)huge.Width*huge.Height<=16777216 && huge.Width<=16000 && huge.Height<=10000,"Initial viewport exceeded rendering limits.");
        var large=WindowSizing.Initial(settings,1900,1000,1920,1080,1);
        Require(large.Width==1536 && large.Height==808 && large.MinimumWidth==320 && large.MinimumHeight==200,"Large source did not fit a normal, resizable window with visible desktop space.");
        foreach(var source in new[]{(1920u,1080u),(3840u,2160u),(4096u,4096u),(1u,16384u),(16384u,1u)})
        foreach(var work in new[]{(1920u,1080u),(1366u,768u),(800u,600u),(1u,1u)})
        foreach(double dpi in new[]{1d,1.5d,2d})
        {
            uint decorationWidth=work.Item1>1 ? 2u : 0u,decorationHeight=work.Item2>1 ? 40u : 0u;
            var fitted=WindowSizing.Initial(settings,source.Item1,source.Item2,work.Item1,work.Item2,dpi,decorationWidth,decorationHeight);
            Require(fitted.Width>0 && fitted.Height>0 && fitted.Width<=work.Item1-decorationWidth && fitted.Height<=work.Item2-decorationHeight && fitted.MinimumWidth<=fitted.Width && fitted.MinimumHeight<=fitted.Height && (ulong)fitted.Width*fitted.Height<=16777216,"Large source violated work area, minimum, DPI or pixel limits.");
        }
        Reject(()=>WindowSizing.Initial(settings,0,10,1920,1080,1),"Zero source geometry accepted.");
        Reject(()=>WindowSizing.Initial(settings,16385,1,1920,1080,1),"Source exceeded the capture dimension limit.");
        Reject(()=>WindowSizing.Initial(settings,10,10,1920,1080,double.NaN),"Invalid DPI accepted.");
        Require(Enumerable.Range(0,7).Select(i=>WindowSizing.Factor(i,1.7f)).SequenceEqual(new[]{0f,1.25f,1.5f,1.75f,2f,3f,1.7f}),"Original scale enum order changed.");
        (long X,long Y)[] expected=[(0,0),(11,0),(22,0),(0,8),(11,8),(22,8),(0,16),(11,16),(22,16)];
        for(int i=0;i<9;i++)Require(PresentationPlan.Destination(31,23,9,7,i)==expected[i],"Output alignment mapping differs from original enum: "+i);
        Require(PresentationPlan.Destination(10,8,5,3,4)==(2L,2L),"Centered output used fractional pixels.");
        Reject(()=>PresentationPlan.Destination(10,8,5,3,9),"Invalid output alignment accepted.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-window-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            settings.InitialWindowedScaleFactor=6;settings.CustomInitialWindowedScaleFactor=1.7f;settings.OutputAlignment=8;
            var copy=settings.CopyForProfile();copy.InitialWindowedScaleFactor=2;copy.OutputAlignment=0;
            Require(settings.InitialWindowedScaleFactor==6 && settings.OutputAlignment==8,"Application settings share window presentation values.");
            settings.Profiles.Add(new ApplicationProfile { Name="Window sizing",ExecutablePath=Path.GetFullPath(Path.Combine(directory,"application")),Settings=copy });
            string path=Path.Combine(directory,"settings.json");settings.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.InitialWindowedScaleFactor==6 && loaded.CustomInitialWindowedScaleFactor==1.7f && loaded.OutputAlignment==8 && loaded.Profiles[0].Settings.InitialWindowedScaleFactor==2 && loaded.Profiles[0].Settings.OutputAlignment==0,"Presentation settings failed source-generated disk roundtrip.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        foreach(int invalid in new[]{-1,7})Reject(()=>new RuntimeSettings { InitialWindowedScaleFactor=invalid }.Validate(),"Invalid initial scale enum accepted.");
        foreach(float invalid in new[]{.99f,10.1f,float.NaN,float.PositiveInfinity})Reject(()=>new RuntimeSettings { CustomInitialWindowedScaleFactor=invalid }.Validate(),"Invalid custom window factor accepted.");
        foreach(int invalid in new[]{-1,9})Reject(()=>new RuntimeSettings { OutputAlignment=invalid }.Validate(),"Invalid alignment settings accepted.");
        Console.WriteLine("WINDOW SETTINGS PASS: original Auto/presets/lround/margins, large-source windowed fallback, work/decor/DPI/pixel bounds, 9 alignments, independent profiles, AOT disk roundtrip and invalid settings.");
    }
}

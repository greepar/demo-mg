using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class SourceCroppingTests
{
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }catch(Exception error) when(error is ArgumentException or InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("Invalid cropping was accepted.");
    }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var settings=new RuntimeSettings { IsCroppingEnabled=true,CroppingLeft=3.5f,CroppingTop=2.5f,CroppingRight=4.5f,CroppingBottom=1.5f };
        var crop=SourceRegion.Resolve(settings,80,72);
        Require(crop==new SourceRegion(4,3,72,68),"Crop edges do not match original float/lround semantics.");
        byte[] source=new byte[80*72*4];
        for(int y=0;y<72;y++)for(int x=0;x<80;x++) { int i=(y*80+x)*4;source[i]=(byte)(x*3);source[i+1]=(byte)(y*3);source[i+2]=(byte)(x+y);source[i+3]=255; }
        byte[] Expected(SourceRegion region)
        {
            byte[] result=new byte[region.Width*region.Height*4];
            for(int y=0;y<region.Height;y++)for(int x=0;x<region.Width;x++)for(int c=0;c<4;c++)
                result[(y*(int)region.Width+x)*4+c]=source[((y+(int)region.Y)*80+x+(int)region.X)*4+c];
            return result;
        }
        var processor=new ScalingFrameProcessor(0);
        bool Run(bool fresh,long time)
        {
            NativeEffectStep[] graph=[new(){ Effect=0,Width=crop.Width,Height=crop.Height }];
            fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,80,72,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,false,crop);
        }
        Require(Run(true,0) && processor.Pixels.SequenceEqual(Expected(crop)),"Vulkan crop output has wrong pixels/stride/origin.");
        Require(!Run(false,1) && !Run(true,2),"Unchanged cropped frame rendered again.");
        source[0]^=1;
        Require(!Run(true,3),"An update outside the crop invoked Vulkan.");
        crop=new SourceRegion(5,3,72,68);
        Require(Run(false,4) && processor.Pixels.SequenceEqual(Expected(crop)),"Same-sized crop move did not reprocess a static source.");
        int inside=((int)crop.Y*80+(int)crop.X)*4;source[inside]^=127;
        Require(Run(true,5) && processor.Pixels.SequenceEqual(Expected(crop)),"Crop pixel update was lost.");
        crop=new SourceRegion(0,0,80,72);
        Require(Run(false,6) && processor.Pixels.SequenceEqual(source),"Disabling crop did not restore the complete frame.");
        Require(processor.Performance.TotalReceived==4 && processor.Performance.TotalScaled==4,"Cropping invented received frames or hid renders.");
        settings.CroppingLeft=16;settings.CroppingRight=0;settings.CroppingTop=settings.CroppingBottom=0;
        Require(SourceRegion.Resolve(settings,80,64).Width==64,"Exact minimum source crop rejected.");
        settings.CroppingLeft=17;Reject(()=>SourceRegion.Resolve(settings,80,64));
        settings.IsCroppingEnabled=false;Require(SourceRegion.Resolve(settings,80,64)==new SourceRegion(0,0,80,64),"Disabled crop still removed margins.");
        settings.IsCroppingEnabled=true;Reject(()=>SourceRegion.Resolve(settings,32,32));
        foreach(float invalid in new[]{-1f,float.NaN,float.PositiveInfinity,16385f})Reject(()=>new RuntimeSettings { CroppingLeft=invalid }.Validate());
        Reject(()=>new SourceRegion(79,0,2,64).Validate(80,72));
        Reject(()=>new SourceRegion(0,0,0,64).Validate(80,72));
        settings.CroppingLeft=3.5f;settings.CroppingRight=4.5f;settings.CroppingTop=2.5f;settings.CroppingBottom=1.5f;
        var copy=settings.CopyForProfile();copy.CroppingLeft=9;copy.IsCroppingEnabled=false;
        Require(settings.IsCroppingEnabled && settings.CroppingLeft==3.5f,"Application cropping shares scalar values.");
        settings.Profiles.Add(new ApplicationProfile { Name="Crop",ExecutablePath=Path.GetFullPath("crop-test"),Settings=copy });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-crop-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");settings.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(SourceRegion.Resolve(loaded,80,72)==new SourceRegion(4,3,72,68) && !loaded.Profiles[0].Settings.IsCroppingEnabled && loaded.Profiles[0].Settings.CroppingLeft==9,"AOT cropping persistence failed.");
            var legacy=JsonSerializer.Deserialize("{}",SettingsJson.Default.RuntimeSettings)!;legacy.Validate();
            Require(!legacy.IsCroppingEnabled && SourceRegion.Resolve(legacy,80,72).Width==80,"Legacy settings did not default to no crop.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("CROPPING PASS: original edge rounding/minimum, exact RGBA Vulkan pixels, outside/inside updates, static same-size move/disable, counters, invalid bounds, independent profiles and AOT disk roundtrip.");
    }
}

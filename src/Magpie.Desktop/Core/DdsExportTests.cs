using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class DdsExportTests
{
    internal static PassSnapshot Example(uint format)=>new() { Serial=1,Stage=0,Pass=0,Output=0,OutputCount=1,PassCount=2,Texture=0,Format=format,Width=3,Height=2,Bytes=6*DdsExport.BytesPerPixel(format) };
    internal static byte[] Pixels(PassSnapshot info)=>Enumerable.Range(0,(int)info.Bytes).Select(i=>(byte)((i*37+info.Format*11)%256)).ToArray();
    internal static void WriteProbe(string directory)
    {
        Directory.CreateDirectory(directory);
        for(uint format=0;format<6;format++)
        {
            var info=Example(format);string path=Path.Combine(directory,"format-"+format+".dds");
            using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write);DdsExport.Write(stream,info,Pixels(info));
        }
        Console.WriteLine("DDS EXPORT PROBE PASS: six original channel/float formats, raw payloads and DX10 headers; no UI/GPU.");
    }
    internal static void Verify()
    {
        if(System.Runtime.InteropServices.Marshal.SizeOf<PassSnapshot>()!=56)throw new InvalidOperationException("Pass export ABI size differs.");
        foreach(var graph in PassExportCatalog.All)
        {
            var descriptor=GraphCatalog.Find(graph.Key)??throw new InvalidOperationException("Pass catalog effect missing.");
            if(graph.Value.Length!=descriptor.Passes||graph.Value.Length is <1 or >16)throw new InvalidOperationException("Pass catalog count differs.");
            foreach(var pass in graph.Value)
            {
                if(string.IsNullOrWhiteSpace(pass.Name)||pass.Outputs.Length is <1 or >32)throw new InvalidOperationException("Pass catalog labels/outputs missing.");
                foreach(var output in pass.Outputs)
                    if(output.Format>5||output.Texture< -1||output.Texture>=descriptor.Textures.Length||string.IsNullOrEmpty(output.Name))throw new InvalidOperationException("Pass output identity exceeds graph.");
            }
        }
        for(uint format=0;format<6;format++)
        {
            var info=Example(format);byte[] pixels=Pixels(info);using var stream=new MemoryStream();DdsExport.Write(stream,info,pixels);
            byte[] result=stream.ToArray();
            if(result.Length!=148+pixels.Length||!result.AsSpan(148).SequenceEqual(pixels))throw new InvalidOperationException("DDS raw payload was converted or padded.");
        }
        var badInfo=Example(0);badInfo.Bytes--;using(var stream=new MemoryStream())
        {
            try { DdsExport.Write(stream,badInfo,Pixels(Example(0)));throw new InvalidOperationException("Invalid DDS metadata accepted."); }catch(InvalidDataException) { }
            if(stream.Length!=0)throw new InvalidOperationException("Rejected DDS wrote partial metadata.");
        }
        var settings=new RuntimeSettings { DeveloperMode=true };var copy=settings.CopyForProfile();copy.DeveloperMode=false;
        if(!settings.DeveloperMode)throw new InvalidOperationException("Developer preference copy shares mutable state.");
        var restored=JsonSerializer.Deserialize(JsonSerializer.Serialize(settings,SettingsJson.Default.RuntimeSettings),SettingsJson.Default.RuntimeSettings)!;
        if(!restored.DeveloperMode||JsonSerializer.Deserialize("{}",SettingsJson.Default.RuntimeSettings)!.DeveloperMode)throw new InvalidOperationException("Developer preference AOT/legacy restoration differs.");
        Console.WriteLine("DDS/PASS CATALOG PASS: 56-byte ABI, original graph/pass/output identities, six raw formats, invalid write protection, developer preference roundtrip; independent DDS decoder is a separate probe.");
    }
}

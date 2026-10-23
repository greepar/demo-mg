using System.Text;
using System.Text.Json;
namespace Magpie.Desktop.Core;

internal sealed record ModeImportResult(int Added,int Renamed,int DeferredEffects);
// The original ScalingModesService format, also embedded in original config.json files.
internal static class ScalingModeInterchange
{
    internal const int MaximumBytes=4*1024*1024;
    internal const int MaximumFileModes=256;
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static readonly string[] EffectPaths=Enumerable.Range(0,(int)GraphCatalog.All.Where(e=>e.Registered).Max(e=>e.Id)+1)
        .Select(i=>GraphCatalog.Find((uint)i) is { Registered:true } effect?effect.Path.Replace('/','\\'):"").ToArray();
    internal static readonly string[] CrtNames=GraphCatalog.Find(12)!.Parameters.Select(p=>p.Name).ToArray();
    internal static bool HasFixedOutput(uint effect)=>GraphCatalog.FixedScale(effect)>0;
    private static void Fail(string message)=>throw new InvalidDataException(message);
    private static void CheckName(string? value,int maximum,string description)
    {
        if(string.IsNullOrWhiteSpace(value) || value.Length>maximum || value.Any(char.IsControl))Fail("Invalid "+description+".");
    }
    internal static void CheckObjects(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject()) { if(!keys.Add(property.Name))Fail("Duplicate JSON property: "+property.Name);CheckObjects(property.Value); }
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())CheckObjects(item);
    }
    private static float Number(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Number || !value.TryGetSingle(out float number) || !float.IsFinite(number))throw new InvalidDataException("Expected a finite effect number.");return number;
    }
    private static (string Name,int Type,float X,float Y,Dictionary<string,float> Parameters,bool Extensions) ReadEffect(JsonElement element)
    {
        if(element.ValueKind!=JsonValueKind.Object || !element.TryGetProperty("name",out var nameValue) || nameValue.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Effect name is missing.");
        string name=nameValue.GetString()!;CheckName(name,256,"effect name");
        int type=0;
        if(element.TryGetProperty("scalingType",out var typeValue) && (typeValue.ValueKind!=JsonValueKind.Number || !typeValue.TryGetInt32(out type) || type is <0 or >3))Fail("Invalid scalingType.");
        float x=1,y=1;
        if(element.TryGetProperty("scale",out var scale))
        {
            if(scale.ValueKind!=JsonValueKind.Object || !scale.TryGetProperty("x",out var xv) || !scale.TryGetProperty("y",out var yv))throw new InvalidDataException("Scale requires x and y.");
            x=Number(xv);y=Number(yv);if(x<=0 || y<=0)Fail("Scale must be positive.");
        }
        var parameters=new Dictionary<string,float>(StringComparer.Ordinal);
        if(element.TryGetProperty("parameters",out var values))
        {
            if(values.ValueKind!=JsonValueKind.Object)Fail("Effect parameters must be an object.");
            foreach(var parameter in values.EnumerateObject()) { CheckName(parameter.Name,128,"parameter name");parameters.Add(parameter.Name,Number(parameter.Value)); }
            if(parameters.Count>143)Fail("Too many effect parameters.");
        }
        bool extensions=element.EnumerateObject().Any(p=>p.Name is not ("name" or "scalingType" or "scale" or "parameters"));
        if(element.TryGetProperty("scale",out var scaleObject))extensions|=scaleObject.EnumerateObject().Any(p=>p.Name is not ("x" or "y"));
        return (name,type,x,y,parameters,extensions);
    }
    internal static void ValidateDeferred(JsonElement element)
    {
        string raw=element.GetRawText();if(Utf8.GetByteCount(raw)>MaximumBytes)Fail("Deferred effect is too large.");
        using var document=JsonDocument.Parse(raw,new JsonDocumentOptions { MaxDepth=32 });
        CheckObjects(document.RootElement);_=ReadEffect(document.RootElement);
    }
    internal static int PromoteAvailable(RuntimeSettings settings)
    {
        int count=0;
        if(settings.Modes is null)return 0;
        foreach(var mode in settings.Modes)
        {
            if(mode?.Steps is null)continue;
            for(int i=0;i<mode.Steps.Count;i++)
            {
                if(mode.Steps[i]?.DeferredEffect is not {} raw)continue;
                var candidate=ParseEffect(raw);
                if(candidate.DeferredEffect.HasValue)continue;
                mode.Steps[i]=candidate;count++;
            }
        }
        return count;
    }
    private static EffectStageSettings ParseEffect(JsonElement element)
    {
        var source=ReadEffect(element);
        string canonical=source.Name.Replace('/','\\');
        int index=Array.FindIndex(EffectPaths,path=>path.Length>0 && path.Equals(canonical,StringComparison.OrdinalIgnoreCase));
        if(index<0)index=Array.FindIndex(EffectPaths,path=>path.Length>0 && path[(path.LastIndexOf('\\')+1)..].Equals(canonical,StringComparison.OrdinalIgnoreCase));
        if(index<0 || source.Extensions)return new() { DeferredEffect=element.Clone() };
        var step=new EffectStageSettings { Effect=(uint)index,ScaleType=source.Type,Parameter1=index==14?1:index==3?.33f:index==5?.87f:.5f,ScaleX=source.X,ScaleY=source.Y,OriginalScale=new(source.Type,source.X,source.Y) };
        if(GraphEffects.Contains((uint)index))GraphEffects.Reset(step);
        if(HasFixedOutput((uint)index)) { step.ScaleType=0;step.ScaleX=step.ScaleY=1; }
        else if(source.Type==2)
        {
            float width=MathF.Round(source.X,MidpointRounding.AwayFromZero),height=MathF.Round(source.Y,MidpointRounding.AwayFromZero);
            if(width is <1 or >16384 || height is <1 or >16384)return new() { DeferredEffect=element.Clone() };
            step.Width=(int)width;step.Height=(int)height;step.ScaleX=step.ScaleY=1;
        }
        else if(source.Type==3) { step.ScaleX=step.ScaleY=1; } // Fill ignores factors in the original renderer.
        foreach(var (key,value) in source.Parameters)
        {
            int graphSlot=Array.FindIndex(GraphEffects.Definitions((uint)index),p=>p.Name==key);
            if(graphSlot>=0)GraphEffects.Set(step,graphSlot,value);
            else if(index==1 && key=="ARStrength" || index==3 && key=="paramB" || index==5 && key=="sharpness" || index==14 && key=="oversharp")step.Parameter1=value;
            else if(index==3 && key=="paramC")step.Parameter2=value;
            else if(index==12 && Array.IndexOf(CrtNames,key) is var slot && slot>=0)step.Crt[slot]=CrtParameters.Definitions[slot].Integer?MathF.Round(value,MidpointRounding.AwayFromZero):value;
            else step.AdditionalParameters.Add(key,value); // Upstream ignores undeclared parameters; keep them for export.
        }
        try { step.Validate();return step; }
        catch(InvalidDataException) { return new() { DeferredEffect=element.Clone() }; }
    }
    internal static List<EffectModeSettings> Parse(string text)
    {
        if(Utf8.GetByteCount(text)>MaximumBytes)Fail("Scaling mode file exceeds 4 MiB.");
        using var document=JsonDocument.Parse(text.TrimStart('\uFEFF'),new JsonDocumentOptions { CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true,MaxDepth=32 });
        var root=document.RootElement;CheckObjects(root);
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("scalingModes",out var array) || array.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Expected the original scalingModes array.");
        if(array.GetArrayLength()>MaximumFileModes)Fail("At most 256 scaling modes are supported per file.");
        var modes=new List<EffectModeSettings>();
        foreach(var item in array.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object || !item.TryGetProperty("name",out var nameValue) || nameValue.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Scaling mode name is missing.");
            string name=nameValue.GetString()!;CheckName(name,128,"scaling mode name");
            var mode=new EffectModeSettings { Name=name,Steps=[] };
            if(item.TryGetProperty("effects",out var effects))
            {
                if(effects.ValueKind!=JsonValueKind.Array || effects.GetArrayLength()>16)Fail("A mode must contain at most 16 effects.");
                foreach(var effect in effects.EnumerateArray())mode.Steps.Add(ParseEffect(effect));
            }
            mode.Validate();modes.Add(mode);
        }
        return modes;
    }
    internal static ModeImportResult Import(RuntimeSettings settings,string text,Action<RuntimeSettings>? persist=null)
    {
        settings.Validate();var modes=Parse(text);
        if(settings.Modes.Count+modes.Count>100)Fail("Import would exceed the 100-mode limit.");
        var names=settings.Modes.Select(m=>m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);int renamed=0;
        foreach(var mode in modes)
        {
            string original=mode.Name;
            for(int suffix=2;!names.Add(mode.Name);suffix++)
            {
                string ending=$" ({suffix})",stem=original[..Math.Min(original.Length,128-ending.Length)];
                if(char.IsHighSurrogate(stem[^1]))stem=stem[..^1];mode.Name=stem+ending;
            }
            if(mode.Name!=original)renamed++;
        }
        int previous=settings.Modes.Count;
        if(modes.Count>0)
        {
            settings.Modes.AddRange(modes);
            try { if(persist is null)settings.Save();else persist(settings); }
            catch { settings.Modes.RemoveRange(previous,modes.Count);throw; }
        }
        return new(modes.Count,renamed,modes.Sum(m=>m.Steps.Count(s=>s.DeferredEffect.HasValue)));
    }
    internal static string Export(IEnumerable<EffectModeSettings> modes)
    {
        var list=modes.ToArray();if(list.Length>MaximumFileModes)Fail("At most 256 scaling modes can be exported.");
        using var memory=new MemoryStream();
        using(var writer=new Utf8JsonWriter(memory,new JsonWriterOptions { Indented=true }))
        {
            writer.WriteStartObject();writer.WriteStartArray("scalingModes");
            foreach(var mode in list)
            {
                mode.Validate();writer.WriteStartObject();writer.WriteString("name",mode.Name);writer.WriteStartArray("effects");
                foreach(var step in mode.Steps)
                {
                    if(step.DeferredEffect is {} deferred) { deferred.WriteTo(writer);continue; }
                    writer.WriteStartObject();writer.WriteString("name",EffectPaths[step.Effect]);
                    // Include scalingType even for fixed-size stages, as required by the original import reader.
                    int type=step.ScaleType;double x=type==2?step.Width:step.ScaleX,y=type==2?step.Height:step.ScaleY;
                    if(step.OriginalScale is {} original)
                    {
                        if(HasFixedOutput(step.Effect) || type==3 && original.ScalingType==3) { type=original.ScalingType;x=original.X;y=original.Y; }
                        else if(type==2 && original.ScalingType==2)
                        {
                            if(MathF.Round(original.X,MidpointRounding.AwayFromZero)==step.Width)x=original.X;
                            if(MathF.Round(original.Y,MidpointRounding.AwayFromZero)==step.Height)y=original.Y;
                        }
                    }
                    writer.WriteNumber("scalingType",type);writer.WriteStartObject("scale");
                    writer.WriteNumber("x",x);writer.WriteNumber("y",y);writer.WriteEndObject();
                    var values=new Dictionary<string,float>(step.AdditionalParameters,StringComparer.Ordinal);
                    for(int i=0;i<GraphEffects.Definitions(step.Effect).Length;i++)values[GraphEffects.Definitions(step.Effect)[i].Name]=GraphEffects.Get(step,i);
                    if(step.Effect==1)values["ARStrength"]=step.Parameter1;
                    else if(step.Effect==3) { values["paramB"]=step.Parameter1;values["paramC"]=step.Parameter2; }
                    else if(step.Effect==5)values["sharpness"]=step.Parameter1;
                    else if(step.Effect==14)values["oversharp"]=step.Parameter1;
                    else if(step.Effect==12)for(int i=0;i<CrtNames.Length;i++)values[CrtNames[i]]=step.Crt[i];
                    if(values.Count>0) { writer.WriteStartObject("parameters");foreach(var (name,value) in values.OrderBy(p=>p.Key,StringComparer.Ordinal))writer.WriteNumber(name,value);writer.WriteEndObject(); }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();writer.WriteEndObject();
            }
            writer.WriteEndArray();writer.WriteEndObject();
        }
        if(memory.Length>MaximumBytes)Fail("Scaling mode export exceeds 4 MiB.");
        return Utf8.GetString(memory.ToArray());
    }
    internal static async Task<string> ReadAsync(Stream input)
    {
        using var bytes=new MemoryStream();byte[] buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer))!=0) { if(bytes.Length+count>MaximumBytes)Fail("Scaling mode file exceeds 4 MiB.");await bytes.WriteAsync(buffer.AsMemory(0,count)); }
        return Utf8.GetString(bytes.ToArray());
    }
    internal static async Task WriteLocalAsync(string path,string text)
    {
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Export path must be absolute.");
        _=Parse(text);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllTextAsync(temporary,text,Utf8);File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal static async Task WriteAsync(Stream output,string text)
    {
        _=Parse(text);byte[] bytes=Utf8.GetBytes(text);
        if(!output.CanSeek || !output.CanWrite)throw new IOException("A seekable writable file is required.");
        output.SetLength(0);output.Position=0;await output.WriteAsync(bytes);await output.FlushAsync();
    }
    internal static EffectModeSettings[] Builtins(RuntimeSettings settings)
    {
        EffectStageSettings Sized(uint effect)=>new() { Effect=effect,ScaleType=settings.ScaleType,ScaleX=settings.ScaleX,ScaleY=settings.ScaleY,Width=settings.AbsoluteWidth,Height=settings.AbsoluteHeight,Parameter1=effect==1?settings.LanczosAntiRinging:.5f,Crt=(float[])settings.Crt.Clone() };
        return [new(){Name="Lanczos",Steps=[Sized(1)]},new(){Name="FSR",Steps=[Sized(4),new(){Effect=5,Parameter1=settings.FsrSharpness}]},new(){Name="FSRCNNX",Steps=[new(){Effect=9}]},new(){Name="CuNNy",Steps=[new(){Effect=11}]},new(){Name="Anime4K",Steps=[new(){Effect=8}]},new(){Name="CRT-Geom",Steps=[Sized(12)]},new(){Name="Integer Scale 2x",Steps=[new(){Effect=0,ScaleX=2,ScaleY=2}]}];
    }
}

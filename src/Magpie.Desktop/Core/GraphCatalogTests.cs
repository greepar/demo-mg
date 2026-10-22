using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class GraphCatalogTests
{
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static void Verify()
    {
        var all=GraphCatalog.All;
        Require(all.Length==117&&all.Select(e=>e.Id).Distinct().Count()==117&&all.Select(e=>e.Path).Distinct(StringComparer.Ordinal).Count()==117,"Generated catalog identities are incomplete or duplicated.");
        Require(GraphCatalog.Find(6) is null&&ScalingModeInterchange.EffectPaths[6]=="","Managed FSR composite was mistaken for an original HLSL effect.");
        Require(all.Count(e=>e.Registered)==117&&all.Count(e=>e.Registered&&e.Graph)==111,"Catalog registration differs from the embedded runtime.");
        foreach(var effect in all)
        {
            Require(effect.FixedScale<=6&&effect.Passes is >=1 and <=16&&effect.Parameters.Length<=18,"Catalog exceeds native ABI limits.");
            foreach(var parameter in effect.Parameters)
                Require(float.IsFinite(parameter.Default)&&parameter.Minimum<=parameter.Default&&parameter.Default<=parameter.Maximum&&parameter.Step>0&&(!parameter.Integer||MathF.Floor(parameter.Default)==parameter.Default),"Invalid generated parameter metadata.");
            foreach(var texture in effect.Textures)
                Require(texture.WidthAxis<=4&&texture.HeightAxis<=4&&texture.WidthFactor>0&&texture.HeightFactor>0&&texture.Bytes>0,"Invalid generated texture metadata.");
            if(effect.Registered)
            {
                Require(ScalingModeInterchange.EffectPaths[effect.Id]==effect.Path.Replace('/','\\'),"Original mode path no longer matches its stable ID.");
                var settings=new RuntimeSettings { Effect=effect.Id,ScaleType=0,ScaleX=1.5,ScaleY=1.5 };
                var resolved=settings.ResolveChain(7,5,19,13).Single();
                Require(resolved.Effect==effect.Id&&resolved.Width==(effect.FixedScale>0?7*effect.FixedScale:11)&&resolved.Height==(effect.FixedScale>0?5*effect.FixedScale:8),"Registered effect default/fixed output did not resolve.");
                if(GraphEffects.Contains(effect.Id))for(int i=0;i<effect.Parameters.Length;i++)
                    Require((i==0?resolved.Parameter1:i==1?resolved.Parameter2:resolved.Extra[i-2])==effect.Parameters[i].Default,"Generated graph parameter did not reach its native ABI slot.");
                using var stream=new MemoryStream();
                using(var writer=new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();writer.WriteStartArray("scalingModes");writer.WriteStartObject();writer.WriteString("name",effect.Path);
                    writer.WriteStartArray("effects");writer.WriteStartObject();writer.WriteString("name",effect.Path);
                    writer.WriteStartObject("parameters");foreach(var parameter in effect.Parameters)writer.WriteNumber(parameter.Name,parameter.Default);writer.WriteEndObject();
                    writer.WriteEndObject();writer.WriteEndArray();writer.WriteEndObject();writer.WriteEndArray();writer.WriteEndObject();
                }
                var imported=ScalingModeInterchange.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                Require(imported.Single().IsAvailable&&imported[0].Steps.Single().Effect==effect.Id,"An original default effect failed mode import.");
                string exported=ScalingModeInterchange.Export(imported);
                var roundtrip=ScalingModeInterchange.Parse(exported);
                Require(roundtrip.Single().IsAvailable&&roundtrip[0].Steps.Single().Effect==effect.Id&&ScalingModeInterchange.Export(roundtrip)==exported,"Original effect mode export is not stable.");
                if(GraphEffects.Contains(effect.Id))for(int i=0;i<effect.Parameters.Length;i++)
                    Require(GraphEffects.Get(roundtrip[0].Steps[0],i)==effect.Parameters[i].Default,"Original effect parameters changed in the mode roundtrip.");
                continue;
            }
            // Metadata visibility must never advertise an absent embedded kernel.
            Require(!GraphEffects.Contains(effect.Id)&&GraphCatalog.FixedScale(effect.Id)==0,"Unregistered catalog effect became runnable.");
            try { new EffectStageSettings { Effect=effect.Id }.Validate();throw new InvalidOperationException("Unregistered stage passed runtime validation."); }catch(InvalidDataException) { }
            try { new RuntimeSettings { Effect=effect.Id }.Validate();throw new InvalidOperationException("Unregistered default effect passed validation."); }catch(InvalidDataException) { }
            string path=JsonEncodedText.Encode(effect.Path).ToString();
            string original="{\"scalingModes\":[{\"name\":\"preserve\",\"effects\":[{\"name\":\""+path+"\",\"scalingType\":0,\"scale\":{\"x\":2,\"y\":3}}]}]}";
            var modes=ScalingModeInterchange.Parse(original);
            Require(!modes[0].IsAvailable&&modes[0].Steps[0].DeferredEffect.HasValue,"An unregistered original effect lost its deferred status.");
            var restored=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(modes));
            Require(restored[0].Steps[0].DeferredEffect!.Value.GetProperty("name").GetString()==effect.Path,"Unregistered original effect was not preserved for export.");
        }
        Require(GraphEffects.Definitions(24)[2].Step==.1m&&GraphEffects.Definitions(25)[0].Step==.01m,"Original UI increments acquired binary32 decimal tails.");
        Require(ReferenceEquals(GraphEffects.Definitions(24),GraphEffects.Definitions(24)),"Graph parameter metadata was reallocated per frame.");
        Require(GraphCatalog.ResourceUnits(12,7,5,19,13)==1&&GraphCatalog.ResourceUnits(19,7,5,14,10)==2880,"Descriptor padding or SOURCE resource budget changed.");
        Require(GraphCatalog.ResourceUnits(13,7,5,14,10)==35,"R16F scalar/doubled-height resource budget changed.");
        Require(GraphCatalog.ResourceUnits(14,7,5,19,13)==7u*13*2+19u*13*5,"Cross-input/output-axis resource budget changed.");
        const string unknown="{\"scalingModes\":[{\"name\":\"unknown\",\"effects\":[{\"name\":\"Future/NotInOriginalCorpus\"}]}]}";
        Require(!ScalingModeInterchange.Parse(unknown).Single().IsAvailable,"Unknown future effect was silently substituted.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-catalog-upgrade-"+Guid.NewGuid().ToString("N"));
        try
        {
            var original=GraphCatalog.All.First(e=>e.Id>25);string name=JsonEncodedText.Encode(original.Path).ToString();
            using var raw=JsonDocument.Parse("{\"name\":\""+name+"\",\"scalingType\":0,\"scale\":{\"x\":2,\"y\":3},\"parameters\":{\"future\":0.25}}");
            var previous=new RuntimeSettings { Modes=[new() { Name="old deferred",Steps=[new() { DeferredEffect=raw.RootElement.Clone() }] }] };
            previous.SelectedModeId=previous.Modes[0].Id;string path=Path.Combine(directory,"settings.json");previous.WriteFile(path);
            var loaded=RuntimeSettings.ReadFile(path);
            Require(loaded.Modes[0].IsAvailable&&loaded.Modes[0].Steps[0].Effect==original.Id&&loaded.Modes[0].Steps[0].AdditionalParameters["future"]==.25f,"Old deferred effect did not promote with preserved parameters.");
            Require(ScalingModeInterchange.PromoteAvailable(loaded)==0,"Deferred promotion was not idempotent.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        try { GraphCatalog.ResourceUnits(13,4096,4096,8192,8192);throw new InvalidOperationException("Oversized generated intermediate accepted."); }catch(InvalidOperationException failure) when(failure.Message!="Oversized generated intermediate accepted.") { }
        Console.WriteLine("CATALOG MANAGED PASS: 117 stable identities and registered effects, generated parameter/resource metadata, original parameter increments, cached metadata, SOURCE/mixed-size/format byte budgets and resource rejection.");
    }
}

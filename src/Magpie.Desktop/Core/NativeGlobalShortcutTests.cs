using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal sealed class FixtureShortcutApi : IShortcutNativeApi
{
    internal Dictionary<int,NativeShortcutKey> Registered { get; }=[];
    internal List<int> Removed { get; }=[];
    internal Action<int,bool>? Callback;
    internal int FailRegistration=-1;
    internal int RegistrationCount;
    internal bool Disposed,FailInitialize,Collide,FailCleanup;
    internal Action? AfterRegister;
    public bool LayoutIsCurrent { get; set; }=true;
    public void Initialize(Action<int,bool> callback) { Callback=callback;if(FailInitialize)throw new IOException("Fixture initialization failure."); }
    public NativeShortcutKey Resolve(string trigger)
    {
        var (symbol,modifiers)=NativeShortcutMapping.Parse(trigger);uint key=Collide ? 65u : NativeShortcutMapping.Character(symbol) is { } c ? char.ToUpperInvariant(c) : NativeShortcutMapping.WindowsSpecial(symbol);
        return new(key,modifiers,"Fixture "+trigger);
    }
    public void Register(int id,NativeShortcutKey key)
    {
        if(RegistrationCount++==FailRegistration)throw new IOException("Fixture shortcut conflict.");Registered.Add(id,key);AfterRegister?.Invoke();
    }
    public void Unregister(int id) { if(FailCleanup)throw new IOException("Fixture cleanup failed.");if(Registered.Remove(id))Removed.Add(id); }
    public void Dispose() { foreach(int id in Registered.Keys.ToArray())Unregister(id);Disposed=true; }
    internal void Emit(int id,bool pressed)=>Callback?.Invoke(id,pressed);
}

internal static class NativeGlobalShortcutTests
{
    internal static void Run()
    {
        VerifyAbi();
        foreach(var (symbol,win,mac) in new[]{("F1",0x70u,122u),("F10",0x79u,109u),("F20",0x83u,90u),("KP_0",0x60u,82u),("KP_9",0x69u,92u),("Left",0x25u,123u),("Delete",0x2Eu,117u),("Return",0x0Du,36u),("KP_Decimal",0x6Eu,65u)})
            Check(NativeShortcutMapping.WindowsSpecial(symbol)==win&&NativeShortcutMapping.MacSpecial(symbol)==mac,"Platform key tables: "+symbol);
        Check(NativeShortcutMapping.Parse("CTRL+ALT+SHIFT+LOGO+a")==("a",15u),"Modifier bits");
        Check(NativeShortcutMapping.Display("a",15,true)=="Control+Option+Shift+Command+A","macOS modifier names");
        foreach(string invalid in new[]{"a","CTRL+CTRL+a","CTRL++a","BAD+a","CTRL+"})
            try { NativeShortcutMapping.Parse(invalid);throw new InvalidOperationException("Invalid native trigger accepted."); }catch(InvalidDataException) { }
        var definitions=GlobalShortcutSettings.Definitions(new RuntimeSettings());
        var fixture=new FixtureShortcutApi();var session=new NativeGlobalShortcuts(fixture);var activations=new List<string>();var closed=new List<string>();
        session.Activated+=activations.Add;session.Closed+=closed.Add;
        session.BindAsync(definitions,"",default).GetAwaiter().GetResult();
        Check(session.Bound.Count==3&&fixture.Registered.Count==3&&session.Bound.All(b=>b.TriggerDescription.StartsWith("Fixture ")),"Register actual mapped results");
        int first=fixture.Registered.Keys.First();fixture.Emit(first,true);fixture.Emit(first,true);fixture.Emit(first,false);fixture.Emit(first,true);fixture.Emit(first+1000,true);
        Check(activations.SequenceEqual(new[]{"fullscreen","fullscreen"}),"Press/release dedup and foreign IDs");
        fixture.LayoutIsCurrent=false;session.CheckLayout();fixture.Emit(first,false);fixture.Emit(first,true);
        Check(session.Bound.Count==0&&fixture.Registered.Count==0&&fixture.Disposed&&closed.Count==1&&activations.Count==2,"Layout change clears registrations and disables callbacks");
        session.DisposeAsync().GetAwaiter().GetResult();Check(fixture.Removed.Count==3,"Idempotent disposal");
        for(int fail=0;fail<3;fail++)
        {
            fixture=new FixtureShortcutApi { FailRegistration=fail };session=new NativeGlobalShortcuts(fixture);
            try { session.BindAsync(definitions,"",default).GetAwaiter().GetResult();throw new InvalidOperationException("Conflict accepted."); }catch(IOException) { }
            Check(session.Bound.Count==0&&fixture.Registered.Count==0&&fixture.Removed.Count==fail&&fixture.Disposed,"Conflict transaction rollback at index "+fail);
        }
        fixture=new FixtureShortcutApi { FailInitialize=true };session=new NativeGlobalShortcuts(fixture);
        try { session.BindAsync(definitions,"",default).GetAwaiter().GetResult();throw new InvalidOperationException("Initialization failure accepted."); }catch(IOException) { }
        Check(fixture.Disposed&&session.Bound.Count==0,"Failed initialization cleanup");
        fixture=new FixtureShortcutApi { Collide=true };session=new NativeGlobalShortcuts(fixture);
        try { session.BindAsync(definitions,"",default).GetAwaiter().GetResult();throw new InvalidOperationException("Mapped collision accepted."); }catch(InvalidDataException) { }
        Check(fixture.Registered.Count==0&&fixture.Removed.Count==1,"Layout-dependent collision rollback");
        using var stop=new CancellationTokenSource();fixture=new FixtureShortcutApi { AfterRegister=()=>stop.Cancel() };session=new NativeGlobalShortcuts(fixture);
        try { session.BindAsync(definitions,"",stop.Token).GetAwaiter().GetResult();throw new InvalidOperationException("Cancellation accepted."); }catch(OperationCanceledException) { }
        Check(fixture.Registered.Count==0&&fixture.Removed.Count==1&&session.Bound.Count==0,"Mid-registration cancellation rollback");
        fixture=new FixtureShortcutApi();session=new NativeGlobalShortcuts(fixture);session.BindAsync(definitions,"",default).GetAwaiter().GetResult();session.DisposeAsync().GetAwaiter().GetResult();Check(fixture.Registered.Count==0&&fixture.Removed.Count==3,"Explicit unregister all");
        fixture=new FixtureShortcutApi { FailCleanup=true };session=new NativeGlobalShortcuts(fixture);var cleanupErrors=new List<string>();session.Closed+=cleanupErrors.Add;
        session.BindAsync(definitions,"",default).GetAwaiter().GetResult();int stale=fixture.Registered.Keys.First();
        try { session.DisposeAsync().GetAwaiter().GetResult();throw new InvalidOperationException("Cleanup failure was reported as success."); }catch(AggregateException) { }
        fixture.Emit(stale,true);Check(session.Bound.Count==0&&cleanupErrors.Count==1&&fixture.Registered.Count==3,"Cleanup failure disables dispatch and is explicit");
        fixture.FailCleanup=false;fixture.Dispose();
        Console.WriteLine("NATIVE SHORTCUT CONTRACT PASS: Windows/macOS key tables, Carbon LP64/UTF16 declarations, atomic registration/conflict/mapped-collision rollback, cancellation, activation dedup, layout invalidation, explicit/idempotent cleanup. OS calls are fixture-backed, not target-system runtime verification.");
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicMethods,typeof(MacShortcutApi))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicMethods,typeof(WindowsShortcutApi))]
    [UnconditionalSuppressMessage("Interoperability","CA1416",Justification="Inspect declarations only; no platform API is invoked.")]
    private static void VerifyAbi()
    {
        var type=typeof(MacShortcutApi);
        static MethodInfo Method([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type,string name)=>type.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!;
        var parameters=Method(type,"UCKeyTranslate").GetParameters();
        // Apple SDK MacTypes.h uses unsigned long for UniCharCount, ByteCount and ItemCount.
        Check(parameters[7].ParameterType==typeof(nuint)&&parameters[8].ParameterType==typeof(nuint).MakeByRefType(),"UCKeyTranslate LP64 counts");
        Check(parameters[9].ParameterType==typeof(char[]),"UniChar managed output type");
        // MarshalAs/DllImport are pseudo-attributes synthesized by CoreCLR. NativeAOT
        // consumes their metadata for generated stubs but need not expose it to reflection.
        // verify-native-shortcut-abi.py checks those source declarations separately.
        var array=parameters[9].GetCustomAttribute<MarshalAsAttribute>();
        if(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)Check(array?.ArraySubType==UnmanagedType.U2&&array.SizeParamIndex==7,"UniChar UTF16 output");
        parameters=Method(type,"GetEventParameter").GetParameters();Check(parameters[4].ParameterType==typeof(nuint)&&parameters[5].ParameterType==typeof(nuint).MakeByRefType(),"GetEventParameter LP64 counts");
        Check(Method(type,"InstallEventHandler").GetParameters()[2].ParameterType==typeof(nuint),"InstallEventHandler LP64 ItemCount");
        if(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)Check(Method(typeof(WindowsShortcutApi),"VkKeyScanExW").GetCustomAttribute<DllImportAttribute>()?.CharSet==CharSet.Unicode,"Windows key char is UTF16");
    }
    private static void Check(bool value,string name) { if(!value)throw new InvalidOperationException("Native shortcut contract failed: "+name); }
}

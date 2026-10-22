namespace Magpie.Desktop.Core;

internal interface IGlobalShortcutSession : IAsyncDisposable
{
    IReadOnlyList<BoundShortcut> Bound { get; }
    event Action<string>? Activated;
    event Action? Changed;
    event Action<string>? Closed;
    Task BindAsync(PortalShortcut[] definitions,string parent,CancellationToken stop);
}
internal readonly record struct NativeShortcutKey(uint Key,uint Modifiers,string Display);
internal interface IShortcutNativeApi : IDisposable
{
    void Initialize(Action<int,bool> changed);
    NativeShortcutKey Resolve(string trigger);
    void Register(int id,NativeShortcutKey key);
    void Unregister(int id);
    bool LayoutIsCurrent { get; }
}

internal sealed class NativeGlobalShortcuts(IShortcutNativeApi api) : IGlobalShortcutSession
{
    private readonly Dictionary<int,string> _ids=[];
    private readonly HashSet<int> _pressed=[];
    private bool _disposed,_initialized;
    private Exception? _cleanupFailure;
    private Avalonia.Threading.DispatcherTimer? _layoutTimer;
    private static int _nextId=0x4D00;
    public IReadOnlyList<BoundShortcut> Bound { get; private set; }=[];
    public event Action<string>? Activated;
    public event Action? Changed;
    public event Action<string>? Closed;
    public Task BindAsync(PortalShortcut[] definitions,string parent,CancellationToken stop)
    {
        try
        {
            if(_disposed)throw new ObjectDisposedException(nameof(NativeGlobalShortcuts));
            if(_initialized)throw new InvalidOperationException("Native shortcut session is already initialized.");
            if(definitions.Length==0||definitions.Length>16||definitions.Select(s=>s.Id).Distinct().Count()!=definitions.Length)throw new InvalidDataException("Invalid shortcut actions.");
            stop.ThrowIfCancellationRequested();api.Initialize(Handle);_initialized=true;
            var bindings=new List<BoundShortcut>();var mapped=new HashSet<(uint,uint)>();
            foreach(var definition in definitions)
            {
                stop.ThrowIfCancellationRequested();var key=api.Resolve(definition.PreferredTrigger);
                if(!mapped.Add((key.Key,key.Modifiers)))throw new InvalidDataException("Multiple actions map to the same native shortcut.");
                int id=Interlocked.Increment(ref _nextId);if(id>0xBFFF)throw new InvalidOperationException("The process has exhausted native shortcut identifiers. Restart Magpie.");
                api.Register(id,key);_ids.Add(id,definition.Id);bindings.Add(new(definition.Id,definition.Description,key.Display));
            }
            stop.ThrowIfCancellationRequested();Bound=bindings.ToArray();Changed?.Invoke();
            return Task.CompletedTask;
        }
        catch(Exception failure)
        {
            Rollback();return Task.FromException(_cleanupFailure is null?failure:new AggregateException(failure,_cleanupFailure));
        }
    }
    internal void MonitorLayout()
    {
        _layoutTimer=new Avalonia.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        _layoutTimer.Tick+=(_,_)=>CheckLayout();_layoutTimer.Start();
    }
    internal void CheckLayout()
    {
        if(_disposed||!_initialized||Bound.Count==0)return;
        try
        {
            if(api.LayoutIsCurrent)return;
            Rollback();if(_cleanupFailure is null)Closed?.Invoke("Keyboard layout changed. Enable global shortcuts again to register keys for the current layout.");
        }
        catch(Exception error) { Rollback();if(_cleanupFailure is null)Closed?.Invoke("Native shortcut registration is unavailable: "+error.Message); }
    }
    private void Handle(int id,bool pressed)
    {
        if(_disposed||!_ids.TryGetValue(id,out string? action)||!Bound.Any(b=>b.Id==action))return;
        try
        {
            if(!pressed)_pressed.Remove(id);
            else if(_pressed.Add(id))Activated?.Invoke(action);
        }
        catch(Exception error) { Rollback();if(_cleanupFailure is null)Closed?.Invoke("Shortcut action failed: "+error.Message); }
    }
    private void Rollback()
    {
        _layoutTimer?.Stop();_layoutTimer=null;_disposed=true;
        var failures=new List<Exception>();
        foreach(int id in _ids.Keys.Reverse())
            try { api.Unregister(id); }catch(Exception error) { failures.Add(error); }
        _ids.Clear();_pressed.Clear();Bound=[];
        try { api.Dispose(); }catch(Exception error) { failures.Add(error); }
        Changed?.Invoke();
        if(failures.Count>0)
        {
            _cleanupFailure=new AggregateException("The operating system could not release all shortcuts. Restart Magpie before registering shortcuts again.",failures);
            Closed?.Invoke(_cleanupFailure.Message);
        }
    }
    public ValueTask DisposeAsync()
    {
        if(!_disposed)Rollback();return _cleanupFailure is null?ValueTask.CompletedTask:ValueTask.FromException(_cleanupFailure);
    }

}

internal static class NativeShortcutMapping
{
    internal static (string Symbol,uint Modifiers) Parse(string trigger)
    {
        string[] tokens=trigger.Split('+');if(tokens.Length<2||tokens.Length>5)throw new InvalidDataException("Invalid shortcut trigger.");
        uint modifiers=0;
        foreach(string token in tokens[..^1])
        {
            uint bit=token switch { "ALT"=>1u,"CTRL"=>2u,"SHIFT"=>4u,"LOGO"=>8u,_=>throw new InvalidDataException("Invalid shortcut modifier.") };
            if((modifiers&bit)!=0)throw new InvalidDataException("Duplicate shortcut modifier.");modifiers|=bit;
        }
        if(tokens[^1].Length==0||tokens[^1].Length>32)throw new InvalidDataException("Invalid shortcut key.");
        return (tokens[^1],modifiers);
    }
    internal static char? Character(string symbol)=>symbol.Length==1&&char.IsAsciiLetterOrDigit(symbol[0]) ? char.ToLowerInvariant(symbol[0]) : symbol switch
    {
        "equal"=>'=',"minus"=>'-',"comma"=>',',"period"=>'.',"slash"=>'/',"semicolon"=>';',"apostrophe"=>'\'',"bracketleft"=>'[',"bracketright"=>']',"backslash"=>'\\',"grave"=>'`',_=>null
    };
    internal static uint WindowsSpecial(string symbol)
    {
        if(symbol.StartsWith('F')&&int.TryParse(symbol.AsSpan(1),out int f)&&f is >=1 and <=24)return (uint)(0x70+f-1);
        if(symbol.StartsWith("KP_",StringComparison.Ordinal)&&symbol.Length==4&&char.IsAsciiDigit(symbol[3]))return (uint)(0x60+symbol[3]-'0');
        return symbol switch { "Return"=>0x0D,"BackSpace"=>0x08,"Prior"=>0x21,"Next"=>0x22,"space"=>0x20,"Tab"=>0x09,"Escape"=>0x1B,"Insert"=>0x2D,"Delete"=>0x2E,"Home"=>0x24,"End"=>0x23,"Left"=>0x25,"Up"=>0x26,"Right"=>0x27,"Down"=>0x28,"KP_Multiply"=>0x6A,"KP_Add"=>0x6B,"KP_Subtract"=>0x6D,"KP_Divide"=>0x6F,"KP_Decimal"=>0x6E,"Pause"=>0x13,"Print"=>0x2C,"Scroll_Lock"=>0x91,"Num_Lock"=>0x90,"Caps_Lock"=>0x14,_=>throw new InvalidDataException("Unsupported Windows global shortcut key: "+symbol) };
    }
    internal static uint MacSpecial(string symbol)
    {
        if(symbol.StartsWith('F')&&int.TryParse(symbol.AsSpan(1),out int f)&&f is >=1 and <=20)return new uint[]{122,120,99,118,96,97,98,100,101,109,103,111,105,107,113,106,64,79,80,90}[f-1];
        if(symbol.StartsWith("KP_",StringComparison.Ordinal)&&symbol.Length==4&&char.IsAsciiDigit(symbol[3]))return new uint[]{82,83,84,85,86,87,88,89,91,92}[symbol[3]-'0'];
        return symbol switch { "Return"=>36,"BackSpace"=>51,"Prior"=>116,"Next"=>121,"space"=>49,"Tab"=>48,"Escape"=>53,"Insert"=>114,"Delete"=>117,"Home"=>115,"End"=>119,"Left"=>123,"Up"=>126,"Right"=>124,"Down"=>125,"KP_Multiply"=>67,"KP_Add"=>69,"KP_Subtract"=>78,"KP_Divide"=>75,"KP_Decimal"=>65,"Caps_Lock"=>57,_=>throw new InvalidDataException("This key has no macOS native hotkey mapping: "+symbol) };
    }
    internal static string Display(string symbol,uint modifiers,bool mac)=>string.Join("+",new[]{(2u,mac?"Control":"Ctrl"),(1u,mac?"Option":"Alt"),(4u,"Shift"),(8u,mac?"Command":"Win")}.Where(p=>(modifiers&p.Item1)!=0).Select(p=>p.Item2).Append(symbol.Length==1?symbol.ToUpperInvariant():symbol));
}

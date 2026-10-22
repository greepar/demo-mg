using Avalonia.Logging;
namespace Magpie.Desktop.Core;
internal sealed class TrayProbeLog : ILogSink
{
    public bool IsEnabled(LogEventLevel level,string area)=>level>=LogEventLevel.Warning;
    public void Log(LogEventLevel level,string area,object? source,string messageTemplate)=>Console.Error.WriteLine($"{level} {area}: {messageTemplate}");
    public void Log(LogEventLevel level,string area,object? source,string messageTemplate,params object?[] propertyValues)=>Console.Error.WriteLine($"{level} {area}: {messageTemplate} {string.Join(" | ",propertyValues.Select(x=>x?.ToString()))}");
}

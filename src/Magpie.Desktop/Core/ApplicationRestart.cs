using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal static class ApplicationRestart
{
    internal static ProcessStartInfo BuildStartInfo(string executable,string? managedAssembly,IEnumerable<string> arguments)
    {
        if(!Path.IsPathFullyQualified(executable))throw new InvalidOperationException("Application executable path is unavailable.");
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,WorkingDirectory=Environment.CurrentDirectory };
        if(managedAssembly is not null)
        {
            if(!Path.IsPathFullyQualified(managedAssembly))throw new InvalidOperationException("Application assembly path is unavailable.");
            start.ArgumentList.Add(managedAssembly);
        }
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        return start;
    }
    internal static ProcessStartInfo CurrentStartInfo(IEnumerable<string> arguments)
    {
        string executable=Environment.ProcessPath ?? throw new InvalidOperationException("Application executable path is unavailable.");
        string entry=Environment.GetCommandLineArgs()[0];
        string? managed=Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase) && entry.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(entry) : null;
        return BuildStartInfo(executable,managed,arguments);
    }
    internal static Process Start(IEnumerable<string> arguments)=>Process.Start(CurrentStartInfo(arguments)) ?? throw new InvalidOperationException("The replacement application could not start.");
    internal static void Restart()
    {
        // A fresh launch does not repeat capture/preview actions from the original command line.
        using var replacement=Start([]);
    }
}

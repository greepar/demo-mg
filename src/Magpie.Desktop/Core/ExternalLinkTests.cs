using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Core;

internal static class ExternalLinkTests
{
    internal static async Task<Control?> VerifyAsync(MainWindow window,bool fail)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Link fixtures require preview mode.");
        var opened=new List<Uri>();
        Control? errorContent=null;
        ExternalLinks.PreviewLauncher=uri=> { opened.Add(uri);return Task.FromResult(!fail); };
        try
        {
            window.Navigate("About");await Task.Delay(80);
            var buttons=window.GetVisualDescendants().OfType<ExternalLinkButton>().ToArray();
            if(buttons.Length!=7)throw new InvalidOperationException("About page links are missing.");
            foreach(var button in buttons)
            {
                var peer=ControlAutomationPeer.CreatePeerForElement(button);
                if(peer?.GetProvider<IInvokeProvider>() is not {} invoke)throw new InvalidOperationException("Link has no accessible button invoke action.");
                int before=opened.Count;invoke.Invoke();await Task.Delay(80);
                if(opened.Count!=before+1 || opened[^1]!=ExternalLinks.Resolve(button.Destination) || !button.IsEnabled)
                    throw new InvalidOperationException("Link activation or failure recovery is broken.");
                if(fail)
                {
                    errorContent=button.ErrorContent ?? throw new InvalidOperationException("A failed system launch did not display an error.");
                    if(!errorContent.IsEffectivelyVisible)throw new InvalidOperationException("Link failure message is not visible.");
                    break;
                }
            }
            if(!fail)
            {
                window.Navigate("Home");await Task.Delay(60);
                var help=window.GetVisualDescendants().OfType<ExternalLinkButton>().Single();
                var invoke=ControlAutomationPeer.CreatePeerForElement(help)!.GetProvider<IInvokeProvider>()!;
                int before=opened.Count;invoke.Invoke();await Task.Delay(80);
                if(opened.Count!=before+1 || opened[^1]!=ExternalLinks.Resolve("TouchSupport"))throw new InvalidOperationException("Touch help link did not activate.");
                window.Navigate("About");
            }
            Console.WriteLine("EXTERNAL LINKS UI PASS: "+opened.Count+" actual accessible button invocations, upstream destinations and "+(fail?"launcher rejection recovery":"touch help language selection")+"; no browser opened.");
            return errorContent;
        }
        finally { ExternalLinks.PreviewLauncher=null; }
    }
}

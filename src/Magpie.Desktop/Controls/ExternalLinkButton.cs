using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;

// A real button preserves keyboard activation, focus and accessibility for
// whole-card links as well as ordinary text links.
public sealed class ExternalLinkButton : Button
{
    protected override Type StyleKeyOverride=>typeof(Button);
    public static readonly StyledProperty<string> DestinationProperty=AvaloniaProperty.Register<ExternalLinkButton,string>(nameof(Destination),"");
    public string Destination { get=>GetValue(DestinationProperty);set=>SetValue(DestinationProperty,value); }
    internal Control? ErrorContent { get; private set; }
    protected override async void OnClick()
    {
        base.OnClick();
        ErrorContent=null;
        IsEnabled=false;
        try
        {
            var uri=Core.ExternalLinks.Resolve(Destination);
            bool opened;
            if(Core.ExternalLinks.PreviewLauncher is {} preview)
            {
                if(Core.RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Link fixtures are restricted to isolated preview mode.");
                opened=await preview(uri);
            }
            else
            {
                if(!Core.RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("External navigation is disabled in preview mode.");
                var owner=TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("The application window is unavailable.");
                opened=await owner.Launcher.LaunchUriAsync(uri);
            }
            if(!opened)throw new IOException(Core.Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"系统无法打开此链接。":"The system could not open this link.");
        }
        catch(Exception failure)
        {
            ErrorContent=(TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(Core.Localization.Get("About_OtherLinks.Header"),failure.Message);
        }
        finally { IsEnabled=true; }
    }
}

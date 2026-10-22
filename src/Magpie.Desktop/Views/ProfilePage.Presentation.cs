using Avalonia.Controls;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private void WindowCustomScale_Changed(object? sender,NumericUpDownValueChangedEventArgs args) { if(_initialized)SaveWindowPresentationSettings(); }
    private void OutputAlignment_Changed(object? sender,SelectionChangedEventArgs args) { if(_initialized)SaveWindowPresentationSettings(); }
    private void SaveWindowPresentationSettings()
    {
        if(!_initialized || WindowScale.SelectedIndex<0 || OutputAlignmentSelector.SelectedIndex<0)return;
        var settings=Settings;
        var previous=(settings.InitialWindowedScaleFactor,settings.CustomInitialWindowedScaleFactor,settings.OutputAlignment);
        var next=(WindowScale.SelectedIndex,(float)(WindowCustomScale.Value ?? 1.25m),OutputAlignmentSelector.SelectedIndex);
        if(previous==next)return;
        try
        {
            (settings.InitialWindowedScaleFactor,settings.CustomInitialWindowedScaleFactor,settings.OutputAlignment)=next;
            Core.RuntimeSettings.Current.Save();
        }
        catch(Exception failure)
        {
            (settings.InitialWindowedScaleFactor,settings.CustomInitialWindowedScaleFactor,settings.OutputAlignment)=previous;
            RefreshSettings();if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Window presentation",failure.Message);
        }
    }
}

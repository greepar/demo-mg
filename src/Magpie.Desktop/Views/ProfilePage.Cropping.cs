using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private void CroppingToggle_Changed(object? sender,RoutedEventArgs args) { if(_initialized)SaveCropping(); }
    private void CroppingValue_Changed(object? sender,NumericUpDownValueChangedEventArgs args) { if(_initialized)SaveCropping(); }
    private void SaveCropping()
    {
        var settings=Settings;
        var previous=(settings.IsCroppingEnabled,settings.CroppingLeft,settings.CroppingTop,settings.CroppingRight,settings.CroppingBottom);
        var next=(CroppingToggle.IsChecked==true,(float)(CroppingLeftInput.Value ?? 0),(float)(CroppingTopInput.Value ?? 0),(float)(CroppingRightInput.Value ?? 0),(float)(CroppingBottomInput.Value ?? 0));
        if(previous==next)return;
        try
        {
            (settings.IsCroppingEnabled,settings.CroppingLeft,settings.CroppingTop,settings.CroppingRight,settings.CroppingBottom)=next;
            Core.RuntimeSettings.Current.Save();CroppingExpander.IsExpanded=settings.IsCroppingEnabled;
        }
        catch(Exception failure)
        {
            (settings.IsCroppingEnabled,settings.CroppingLeft,settings.CroppingTop,settings.CroppingRight,settings.CroppingBottom)=previous;
            RefreshSettings();if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast(Core.Localization.Get("Profile_SourceWindow_CustomCropping.Header"),failure.Message);
        }
    }
}

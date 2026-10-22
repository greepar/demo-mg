using Avalonia.Controls;
namespace Magpie.Desktop.Views;
public partial class ProfilePage
{
    private void SsimOversharp_Changed(object? sender,NumericUpDownValueChangedEventArgs args)
    {
        if(!_initialized)return;
        float previous=Settings.SsimOversharp;
        try { Settings.SsimOversharp=(float)(ProfileSsimOversharp.Value ?? 1);Core.RuntimeSettings.Current.Save(); }
        catch(Exception error)
        {
            Settings.SsimOversharp=previous;RefreshSettings();
            if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("SSimDownscaler",error.Message);
        }
    }
}

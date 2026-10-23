using Avalonia;
using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    internal void VerifyThemeInteraction()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Theme UI fixtures require preview mode.");
        foreach(int theme in new[]{2,1,0,2})
        {
            ThemeSelector.SelectedIndex=theme;
            if(RuntimeSettings.Current.Theme!=theme || Application.Current!.RequestedThemeVariant!=ThemePreference.Variant(theme))
                throw new InvalidOperationException("Theme selection did not apply and save.");
            var reopened=new SettingsPage();
            if(reopened.ThemeSelector.SelectedIndex!=theme)throw new InvalidOperationException("Reopened settings lost theme selection.");
        }
        Console.WriteLine("THEME UI PASS: actual selection handlers apply System/Light/Dark immediately and reopened settings restore the saved selection.");
    }
}

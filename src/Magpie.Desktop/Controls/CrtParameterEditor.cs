using Avalonia.Controls;
using Avalonia.Media;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;

internal static class CrtParameterEditor
{
    internal static ScrollViewer Create(float[] values,Action save)
    {
        var body=new StackPanel { Width=260,Spacing=8 };
        var error=new TextBlock { Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,IsVisible=false };
        for(int i=0;i<CrtParameters.Definitions.Length;i++)
        {
            int slot=i;var definition=CrtParameters.Definitions[i];
            body.Children.Add(new TextBlock { Text=definition.Label });
            var number=new NumericUpDown { Value=(decimal)values[i],Minimum=(decimal)definition.Minimum,Maximum=(decimal)definition.Maximum,Increment=definition.Step };
            number.ValueChanged+=(_,_)=>
            {
                if(number.Value is not {} value)return;
                if(definition.Integer && value!=decimal.Round(value)) { number.Value=decimal.Round(value);return; }
                float previous=values[slot];values[slot]=(float)value;
                try { save();error.IsVisible=false; }
                catch(Exception failure) { values[slot]=previous;error.Text="Could not save: "+failure.Message;error.IsVisible=true; }
            };
            body.Children.Add(number);
        }
        body.Children.Add(error);
        return new ScrollViewer { Content=body,MaxHeight=450 };
    }
}

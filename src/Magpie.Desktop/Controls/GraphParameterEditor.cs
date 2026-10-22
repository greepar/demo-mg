using Avalonia.Controls;
using Avalonia.Media;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;

internal static class GraphParameterEditor
{
    internal static ScrollViewer Create(uint effect,Func<int,float> get,Action<int,float> set,Action save)
    {
        var body=new StackPanel { Width=260,Spacing=8 };
        var error=new TextBlock { Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,IsVisible=false };
        var definitions=GraphEffects.Definitions(effect);
        for(int i=0;i<definitions.Length;i++)
        {
            int slot=i;var definition=definitions[i];body.Children.Add(new TextBlock { Text=definition.Label });
            var number=new NumericUpDown { Name="GraphParameter"+i,Minimum=(decimal)definition.Minimum,Maximum=(decimal)definition.Maximum,Value=(decimal)get(i),Increment=definition.Step };
            number.ValueChanged+=(_,_)=>
            {
                if(number.Value is not {} value)return;
                if(definition.Integer&&value!=decimal.Round(value)) { number.Value=decimal.Round(value);return; }
                float previous=get(slot);set(slot,(float)value);
                try { save();error.IsVisible=false; }
                catch(Exception failure) { set(slot,previous);error.Text="Could not save: "+failure.Message;error.IsVisible=true; }
            };
            body.Children.Add(number);
        }
        body.Children.Add(error);return new ScrollViewer { Content=body,MaxHeight=450 };
    }
}

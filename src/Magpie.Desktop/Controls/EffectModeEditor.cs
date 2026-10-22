using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;

// Edits persistent modes backed by the actual Vulkan effect chain.
internal sealed class EffectModeEditor : UserControl
{
    private readonly EffectModeSettings _mode;
    private readonly Action _refresh;
    private readonly Button _use;
    private readonly StackPanel _stages = new() { Spacing = 2 };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    public EffectModeEditor(EffectModeSettings mode, Action refresh)
    {
        _mode = mode; _refresh = refresh;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth=280 };
        SizeChanged+=(_,_)=>actions.MaxWidth=Math.Max(140,Bounds.Width*.48);
        var use = _use = Button("Use", () => { if(!mode.IsAvailable) { Fail("This mode contains effects that cannot run in this build.");return; }RuntimeSettings.Current.SelectedModeId = mode.Id; Save(); });
        actions.Children.Add(use);
        var rename = new RenameFlyoutContent { Value = mode.Name };
        var renameButton = new Button { Content = Core.Localization.Text("Rename"), Flyout = new Flyout { Content = rename } };
        rename.Confirmed += (_, _) =>
        {
            if (RuntimeSettings.Current.Modes.Any(m => m != mode && m.Name.Equals(rename.Value, StringComparison.OrdinalIgnoreCase))) { Fail("A mode with this name already exists."); return; }
            mode.Name = rename.Value; Save(); renameButton.Flyout.Hide(); refresh();
        };
        actions.Children.Add(renameButton);
        actions.Children.Add(Button("↑", () => MoveMode(-1)));
        actions.Children.Add(Button("↓", () => MoveMode(1)));
        var delete = new DeleteConfirmationFlyoutContent();
        delete.SetMessage("Delete this scaling mode? Any active use will return to the default effect.");
        var deleteButton = new Button { Content = Core.Localization.Text("Delete"), Flyout = new Flyout { Content = delete } };
        delete.Confirmed += (_, _) =>
        {
            var settings = RuntimeSettings.Current;
            settings.Modes.Remove(mode);
            foreach(var profile in settings.Profiles)
                if(profile.Settings.SelectedModeId==mode.Id)profile.Settings.SelectedModeId=null;
            if (settings.SelectedModeId == mode.Id) settings.SelectedModeId = null;
            Save(); deleteButton.Flyout.Hide(); refresh();
        };
        actions.Children.Add(deleteButton);
        foreach(var action in actions.Children)action.Margin=new Thickness(0,0,4,4);
        var body = new StackPanel { Spacing = 6, Children = { _stages, _error } };
        body.Children.Add(Button("+ Add effect", () =>
        {
            if (mode.Steps.Count == 16) { Fail("A mode can contain up to 16 effects."); return; }
            mode.Steps.Add(new()); Save(); RebuildStages();
        }));
        Content = new SettingsExpander { Header = mode.Name, Description = string.Join(" > ", mode.Steps.Select(s => s.DisplayName)), Action = actions, Content = body };
        RebuildStages();RefreshAvailability();
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = Core.Localization.Text(text) };
        button.Click += (_, _) => action(); return button;
    }
    private void Fail(string text) { _error.Text = text; _error.IsVisible = true; }
    private void RefreshAvailability()
    {
        _use.IsEnabled=_mode.IsAvailable;
        if(!_mode.IsAvailable)Fail(_mode.Steps.Count==0 ? "Add an effect before running this mode." : "Unavailable effects are preserved for export. Replace or remove them to run this mode.");
    }
    private void Save()
    {
        try { RuntimeSettings.Current.Save(); _error.IsVisible = false; }
        catch (Exception error) { Fail("Could not save settings: " + error.Message); }
        RefreshAvailability();
        if (Content is SettingsExpander expander) expander.Description = string.Join(" > ", _mode.Steps.Select(s => s.DisplayName));
    }
    private void MoveMode(int offset)
    {
        var modes = RuntimeSettings.Current.Modes;
        int index = modes.IndexOf(_mode), target = index + offset;
        if (target < 0 || target >= modes.Count) return;
        (modes[index], modes[target]) = (modes[target], modes[index]); Save(); _refresh();
    }
    private void MoveStage(EffectStageSettings step, int offset)
    {
        int index = _mode.Steps.IndexOf(step), target = index + offset;
        if (target < 0 || target >= _mode.Steps.Count) return;
        (_mode.Steps[index], _mode.Steps[target]) = (_mode.Steps[target], _mode.Steps[index]); Save(); RebuildStages();
    }
    private void RebuildStages()
    {
        _stages.Children.Clear();
        foreach (var step in _mode.Steps)
        {
            uint[] effectIds=GraphCatalog.All.Where(e=>e.Registered).Select(e=>e.Id).ToArray();
            bool deferred=step.DeferredEffect.HasValue;
            string[] choices=effectIds.Select(EffectStageSettings.Name).ToArray();if(deferred)choices=[..choices,"Unavailable: "+step.DisplayName];
            var effect = new ComboBox { ItemsSource = choices, SelectedIndex = deferred?effectIds.Length:Array.IndexOf(effectIds,step.Effect), Width = 210 };
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Children = { effect } };
            var parameters = new StackPanel { Width = 260, Spacing = 8 };
            var parameter1 = new StackPanel { Spacing = 4 };
            var parameter2 = new StackPanel { Spacing = 4 };
            var label1 = new TextBlock();
            parameter1.Children.Add(label1);
            parameter1.Children.Add(Number(step.Parameter1, step.Effect==14?1:0, step.Effect==14?3:1, .01m, v => step.Parameter1 = (float)v));
            parameter2.Children.Add(new TextBlock { Text = "C" });
            parameter2.Children.Add(Number(step.Parameter2, 0, 1, .01m, v => step.Parameter2 = (float)v));
            parameters.Children.Add(parameter1); parameters.Children.Add(parameter2);
            var crtParameters=new StackPanel { Spacing=8 };
            for(int parameterIndex=0;parameterIndex<CrtParameters.Definitions.Length;parameterIndex++)
            {
                int slot=parameterIndex;var definition=CrtParameters.Definitions[slot];
                crtParameters.Children.Add(new TextBlock { Text=definition.Label });
                crtParameters.Children.Add(Number(step.Crt[slot],definition.Minimum,definition.Maximum,definition.Step,v=>step.Crt[slot]=(float)(definition.Integer ? decimal.Round(v) : v)));
            }
            parameters.Children.Add(crtParameters);
            var graphParameters=new StackPanel();parameters.Children.Add(graphParameters);
            void ParameterVisibility()
            {
                crtParameters.IsVisible=step.Effect==12;
                graphParameters.Children.Clear();
                if(GraphEffects.HasParameters(step.Effect))graphParameters.Children.Add(GraphParameterEditor.Create(step.Effect,i=>GraphEffects.Get(step,i),(i,v)=>GraphEffects.Set(step,i,v),Save));
                parameter1.IsVisible = step.Effect is 1 or 3 or 5 or 14;
                var scalar=(NumericUpDown)parameter1.Children[1];scalar.Minimum=step.Effect==14?1:0;scalar.Maximum=step.Effect==14?3:1; parameter2.IsVisible = step.Effect == 3;
                label1.Text = step.Effect==14 ? "Oversharp" : step.Effect == 1 ? "Anti-ringing Strength" : step.Effect == 5 ? "Sharpness" : "B";
            }
            effect.SelectionChanged += (_, _) =>
            {
                if (effect.SelectedIndex < 0 || effect.SelectedIndex>=effectIds.Length) return;
                step.DeferredEffect=null;step.AdditionalParameters.Clear();
                step.Effect = effectIds[effect.SelectedIndex];
                ((NumericUpDown)parameter1.Children[1]).Minimum=0;((NumericUpDown)parameter1.Children[1]).Maximum=3;
                step.Parameter1 = step.Effect==14?1:step.Effect == 3 ? .33f : step.Effect == 5 ? .87f : .5f;
                if(GraphEffects.Contains(step.Effect))GraphEffects.Reset(step);
                ((NumericUpDown)parameter1.Children[1]).Value = (decimal)step.Parameter1;
                ParameterVisibility(); Save();if(deferred)RebuildStages();
            };
            ParameterVisibility();
            var type = new ComboBox { ItemsSource = new[] { "Factor", "Fit", "Absolute", "Fill" }, SelectedIndex = step.ScaleType, HorizontalAlignment = HorizontalAlignment.Stretch };
            var scaleLabel = new TextBlock { Text = Core.EffectStageSettings.IsSameSize(step.Effect) ? "Scale: same as input" : "Scale" };
            parameters.Children.Add(scaleLabel); parameters.Children.Add(type);
            var factors = new StackPanel { Spacing = 4 };
            factors.Children.Add(new TextBlock { Text = "Width factor" }); factors.Children.Add(Number(step.ScaleX,Math.Min(.1,step.ScaleX),Math.Max(100,step.ScaleX),.1m,v => step.ScaleX=(double)v));
            factors.Children.Add(new TextBlock { Text = "Height factor" }); factors.Children.Add(Number(step.ScaleY,Math.Min(.1,step.ScaleY),Math.Max(100,step.ScaleY),.1m,v => step.ScaleY=(double)v));
            var absolute = new StackPanel { Spacing = 4 };
            absolute.Children.Add(new TextBlock { Text = "Width (px)" }); absolute.Children.Add(Number(step.Width,1,16384,1,v => step.Width=(int)v));
            absolute.Children.Add(new TextBlock { Text = "Height (px)" }); absolute.Children.Add(Number(step.Height,1,16384,1,v => step.Height=(int)v));
            parameters.Children.Add(factors); parameters.Children.Add(absolute);
            void ScaleVisibility() { factors.IsVisible = !Core.ScalingModeInterchange.HasFixedOutput(step.Effect) && step.ScaleType is 0 or 1; absolute.IsVisible = !Core.ScalingModeInterchange.HasFixedOutput(step.Effect) && step.ScaleType == 2; type.IsVisible = !Core.ScalingModeInterchange.HasFixedOutput(step.Effect); scaleLabel.Text = Core.EffectStageSettings.IsFixed2x(step.Effect) ? "Scale: 2x input (CNN)" : Core.EffectStageSettings.IsSameSize(step.Effect) ? "Scale: same as input" : "Scale"; }
            type.SelectionChanged += (_, _) => { if(type.SelectedIndex<0)return; step.ScaleType=type.SelectedIndex; ScaleVisibility(); Save(); };
            ScaleVisibility();
            effect.SelectionChanged += (_, _) => ScaleVisibility();
            row.Children.Add(new Button { IsEnabled=!deferred,Content = Core.Localization.Text("Settings"), Flyout = new Flyout { Content = new ScrollViewer { Content = parameters, MaxHeight = 480 } } });
            row.Children.Add(Button("↑", () => MoveStage(step,-1)));
            row.Children.Add(Button("↓", () => MoveStage(step,1)));
            var remove = Button("Delete", () => { _mode.Steps.Remove(step); Save(); RebuildStages(); });
            remove.IsEnabled = true; row.Children.Add(remove);
            foreach(var control in row.Children)control.Margin=new Thickness(0,0,4,4);
            _stages.Children.Add(new Border { Padding = new Thickness(12), Child = row });
        }
    }
    private NumericUpDown Number(float value,double min,double max,decimal increment,Action<decimal> apply) => Number((double)(decimal)value,min,max,increment,apply);
    private NumericUpDown Number(double value, double min, double max, decimal increment, Action<decimal> apply)
    {
        var number = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = increment };
        number.ValueChanged += (_, _) => { if (number.Value is {} valid) { apply(valid); Save(); } };
        return number;
    }
}

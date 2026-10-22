using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Views;
namespace Magpie.Desktop;

// Reproducible visual review states. Persistence is disabled; capture pickers are never invoked.
internal static class UiPreview
{
    public static async Task<List<Control>> PrepareAsync(MainWindow window, string scenario)
    {
        var narrow = scenario.StartsWith("Narrow", StringComparison.Ordinal);
        if (narrow) scenario = scenario[6..];
        var popups = new List<Control>();
        var page = scenario.StartsWith("Mode", StringComparison.Ordinal) ? "ScalingModes" :
            scenario.StartsWith("Profile", StringComparison.Ordinal) ? "Defaults" :
            scenario.StartsWith("Settings", StringComparison.Ordinal) ? "Settings" :
            scenario.StartsWith("About", StringComparison.Ordinal) ? "About" : "Home";
        if (scenario is "Home" or "Settings" or "Defaults" or "ScalingModes" or "About") page = scenario;
        if (scenario is "ProfileMore" or "ProfileRename" or "ProfileReorder" or "ProfileDelete" or "ProfileContext")
        {
            window.EnsurePreviewProfile();
            page = (string)window.PreviewProfileNavigation!.Tag!;
        }
        window.Navigate(page);
        await Task.Delay(150);
        T First<T>() where T : Control => window.GetVisualDescendants().OfType<T>().First();
        async Task OpenCombo(ComboBox combo)
        {
            combo.BringIntoView();
            await Task.Delay(100);
            combo.IsDropDownOpen = true;
            await Task.Delay(150);
            var popup = combo.GetVisualDescendants().OfType<Popup>().Single(p => p.IsOpen);
            popups.Add(popup.Child!);
        }
        async Task OpenFlyout(Button button)
        {
            button.BringIntoView();
            await Task.Delay(100);
            button.Flyout!.ShowAt(button);
            await Task.Delay(150);
            if (button.Flyout is Flyout { Content: Control content }) popups.Add(content);
            else if (button.Flyout is MenuFlyout menu) popups.Add(menu.Items.OfType<Control>().First());
        }
        switch (scenario)
        {
            case "AwakeOutput":case "AwakePendingOutput":case "AwakeUnavailableOutput":case "AwakeFailureOutput":
                await Core.ScalingSession.VerifyPowerLifecycleAsync(window,scenario);break;
            case "HomeKeepAwake":
            {
                var toggle=First<HomePage>().FindControl<ToggleSwitch>("KeepScreenOnToggle")!;
                foreach(bool enabled in new[]{true,false,true})
                {
                    toggle.IsChecked=enabled;
                    if(Core.RuntimeSettings.Current.KeepScreenOn!=enabled||new HomePage().FindControl<ToggleSwitch>("KeepScreenOnToggle")!.IsChecked!=enabled)throw new InvalidOperationException("Keep screen on preference did not save/restore through the home page.");
                }
                toggle.BringIntoView();await Task.Delay(100);break;
            }
            case "HomePrecision":
            {
                var toggle=First<HomePage>().FindControl<CheckBox>("DisableFP16Toggle")!;
                foreach(bool disabled in new[]{true,false})
                {
                    toggle.IsChecked=disabled;
                    if(Core.RuntimeSettings.Current.DisableFP16!=disabled || new HomePage().FindControl<CheckBox>("DisableFP16Toggle")!.IsChecked!=disabled)throw new InvalidOperationException("FP16 preference did not save/restore through the actual home control.");
                    using var engine=Core.NativeCore.CreateForSettings(Core.RuntimeSettings.Current);if(engine.IsInvalid)throw Core.NativeCore.Error();
                    bool expected=!disabled&&Core.NativeCore.FP16Supported(engine)!=0;
                    if((Core.NativeCore.EffectUsesFP16(engine,7)!=0)!=expected)throw new InvalidOperationException("FP16 toggle did not reach the real Vulkan engine.");
                }
                toggle.BringIntoView();await Task.Delay(100);break;
            }
            case "FP16Output":
            case "FP32Output":
            {
                bool previous=Core.RuntimeSettings.Current.DisableFP16;
                try { Core.RuntimeSettings.Current.DisableFP16=scenario=="FP32Output";await new Core.ScalingSession(new Core.RuntimeSettings()).VerifyPresentationAsync(window,7); }
                finally { Core.RuntimeSettings.Current.DisableFP16=previous; }
                break;
            }
            case "ShortcutNativeReady": case "ShortcutNativeConflict": case "ShortcutNativeLayoutChanged":
                await window.VerifyNativeShortcutUiAsync(scenario[14..]);break;
            case "ShortcutPortal":await window.VerifyPortalShortcutUiAsync();break;
            case "ShortcutPersistence": case "ShortcutDuplicate":
                await window.VerifyShortcutEditingAsync(scenario=="ShortcutDuplicate");break;
            case "TrayOutput":
                bool trayPreference=Core.RuntimeSettings.Current.ShowTrayIcon;
                Core.RuntimeSettings.Current.ShowTrayIcon=true;
                using(var integration=new Core.DesktopIntegration(window,()=>throw new InvalidOperationException("Tray preview unexpectedly shut down."),(menu,activate)=>new Core.PreviewTrayPresence()))
                    try { await new Core.ScalingSession().VerifyTrayOutputAsync(window,integration); }
                    finally { Core.RuntimeSettings.Current.ShowTrayIcon=trayPreference; }
                break;
            case "Pane": window.FindControl<Button>("PaneButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
            case "Home": case "Settings": case "Defaults": case "ScalingModes": case "About": break;
            case "AboutChecking": case "AboutAvailable": case "AboutDownloading": case "AboutInstalling":
            case "AboutDownloadFailed": case "AboutUpdated": case "AboutError":
                ((AboutPage)window.FindControl<ContentControl>("ContentFrame")!.Content!).ShowUpdateState(scenario[5..]); break;
            case "HomeUpdate": window.GetVisualDescendants().OfType<UpdateCard>().First().ShowState("Available", true); break;
            case "TouchInfo": window.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.Name == "TouchToggle").IsChecked = true;
                window.GetVisualDescendants().OfType<SettingsCard>().First(t => t.Name == "TouchCard").BringIntoView(); break;
            case "ExclusiveWarning": window.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.Name == "ExclusiveToggle").IsChecked = true;
                window.GetVisualDescendants().OfType<SettingsCard>().First(t => t.Name == "ExclusiveCard").BringIntoView(); break;
            case "SettingsStartup": case "SettingsStartupError":
                await First<SettingsPage>().VerifyStartupPreviewAsync(scenario=="SettingsStartupError");await Task.Delay(500);break;
            case "SettingsRestart": First<ComboBox>().SelectedIndex = 1; break;
            case "Toast": popups.Add(window.ShowToast("Magpie", "The selected window cannot be scaled.")); break;
            case "Shortcut": window.ShowShortcutDialog(First<ShortcutControl>()); break;
            case "Delayed": First<SettingsExpander>().IsExpanded = true; break;
            case "ToolbarMenu": await OpenCombo(First<ComboBox>()); break;
            case "NewProfile": case "NewProfileForm":
                var profileButton = window.FindControl<Button>("NewProfileNav")!;
                if (scenario == "NewProfileForm") ((NewProfileFlyoutContent)((Flyout)profileButton.Flyout!).Content!).ShowFormForPreview();
                await OpenFlyout(profileButton); break;
            case "SettingsLanguagePersistence":
                var languagePage=First<SettingsPage>();string currentLanguage=Core.Localization.CurrentLanguage;
                string nextLanguage=currentLanguage=="zh-Hans" ? "de" : "zh-Hans";
                var languageSelector=languagePage.FindControl<ComboBox>("LanguageSelector")!;
                languageSelector.SelectedIndex=Array.IndexOf(Core.Localization.Languages,nextLanguage)+1;
                if(Core.RuntimeSettings.Current.Language!=nextLanguage || !languagePage.FindControl<InfoBar>("RestartNotice")!.IsOpen || Core.Localization.CurrentLanguage!=currentLanguage)throw new InvalidOperationException("Language choice was not saved or restart state is inconsistent.");
                var restoredLanguagePage=new SettingsPage();
                if(restoredLanguagePage.FindControl<ComboBox>("LanguageSelector")!.SelectedIndex!=languageSelector.SelectedIndex)throw new InvalidOperationException("Settings page did not restore the selected language.");
                languageSelector.SelectedIndex=Array.IndexOf(Core.Localization.Languages,currentLanguage)+1;
                if(languagePage.FindControl<InfoBar>("RestartNotice")!.IsOpen)throw new InvalidOperationException("Returning to the current language did not clear the restart notice.");
                languageSelector.SelectedIndex=Array.IndexOf(Core.Localization.Languages,nextLanguage)+1;
                languagePage.FindControl<Button>("RestartLanguageButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!languagePage.FindControl<TextBlock>("LanguageError")!.IsVisible || !window.IsVisible)throw new InvalidOperationException("Preview restart guard failed.");
                break;
            case "SettingsLanguage": await OpenCombo(First<ComboBox>()); break;
            case "SettingsTheme": await OpenCombo(window.GetVisualDescendants().OfType<ComboBox>().ElementAt(1)); break;
            case "ProfileGraphicsQueryFailure":
                var failedDevicePage=First<ProfilePage>();await failedDevicePage.ReloadGraphicsDevicesAsync();
                if(!failedDevicePage.FindControl<TextBlock>("GraphicsDeviceStatus")!.Text!.Contains("query failed") || !failedDevicePage.FindControl<Button>("RefreshGraphicsDevices")!.IsEnabled || Core.RuntimeSettings.Current.GraphicsDeviceId!="")throw new InvalidOperationException("A failing Vulkan query did not preserve settings and show a recoverable error.");
                failedDevicePage.FindControl<SettingsCard>("GraphicsCard")!.BringIntoView();break;
            case "ProfileGraphicsDevices":
                var devicePage=First<ProfilePage>();await devicePage.ReloadGraphicsDevicesAsync();
                var deviceSelector=devicePage.FindControl<ComboBox>("GraphicsDeviceSelector")!;
                if(deviceSelector.ItemCount<2)throw new InvalidOperationException("No actual Vulkan devices are listed.");
                deviceSelector.SelectedIndex=1;
                string deviceId=Core.RuntimeSettings.Current.GraphicsDeviceId;
                if(deviceId.Length!=32)throw new InvalidOperationException("GPU selector did not store a device UUID.");
                using(var selectedDevice=Core.NativeCore.CreateForDevice(deviceId))
                    if(selectedDevice.IsInvalid || System.Runtime.InteropServices.Marshal.PtrToStringUTF8(Core.NativeCore.DeviceIdentifier(selectedDevice))!=deviceId)throw new InvalidOperationException("GPU UI selection did not reach native device creation.");
                devicePage.FindControl<SettingsCard>("GraphicsCard")!.BringIntoView();break;
            case "ProfileGraphicsDeviceMenu":
                var deviceMenuPage=First<ProfilePage>();await deviceMenuPage.ReloadGraphicsDevicesAsync();
                await OpenCombo(deviceMenuPage.FindControl<ComboBox>("GraphicsDeviceSelector")!);break;
            case "ProfileMissingGraphicsDevice":
            case "ProfileGraphicsDeviceRecovery":
                var missingDevicePage=First<ProfilePage>();await missingDevicePage.ReloadGraphicsDevicesAsync();
                var actualDevices=Core.VulkanDevices.Enumerate();string absentDeviceId=new('f',32);
                while(actualDevices.Any(d=>d.Identifier==absentDeviceId))absentDeviceId=absentDeviceId[1..]+"e";
                Core.RuntimeSettings.Current.GraphicsDeviceId=absentDeviceId;
                await missingDevicePage.ReloadGraphicsDevicesAsync();
                if(Core.RuntimeSettings.Current.GraphicsDeviceId!=absentDeviceId || !missingDevicePage.FindControl<TextBlock>("GraphicsDeviceStatus")!.Text!.Contains("unavailable"))throw new InvalidOperationException("Missing GPU preference was reset or its warning is missing.");
                if(scenario=="ProfileGraphicsDeviceRecovery")
                {
                    missingDevicePage.FindControl<ComboBox>("GraphicsDeviceSelector")!.SelectedIndex=0;
                    if(Core.RuntimeSettings.Current.GraphicsDeviceId!="")throw new InvalidOperationException("Automatic recovery did not update preferences.");
                    using var recovered=Core.NativeCore.CreateForDevice(Core.RuntimeSettings.Current.GraphicsDeviceId);
                    if(recovered.IsInvalid)throw Core.NativeCore.Error();
                }
                missingDevicePage.FindControl<SettingsCard>("GraphicsCard")!.BringIntoView();break;
            case "ProfileGraphicsDeviceLifecycle":
                var rootDevices=Core.RuntimeSettings.Current;await First<ProfilePage>().ReloadGraphicsDevicesAsync();
                var catalog=Core.VulkanDevices.Enumerate();if(catalog.Length==0)throw new InvalidOperationException("No Vulkan devices to verify application preferences.");
                rootDevices.GraphicsDeviceId=catalog[0].Identifier;
                var deviceProfile=rootDevices.AddProfile("GPU profile",Path.Combine(Path.GetTempPath(),"magpie-device-app"));
                window.RefreshProfilesNavigation();page="Profile:"+deviceProfile.Id;window.Navigate(page);await Task.Delay(100);
                var personalDevicePage=First<ProfilePage>();await personalDevicePage.ReloadGraphicsDevicesAsync();
                if(deviceProfile.Settings.GraphicsDeviceId!=rootDevices.GraphicsDeviceId)throw new InvalidOperationException("Application did not inherit GPU preference.");
                personalDevicePage.FindControl<ComboBox>("GraphicsDeviceSelector")!.SelectedIndex=0;
                if(deviceProfile.Settings.GraphicsDeviceId!="" || rootDevices.GraphicsDeviceId!=catalog[0].Identifier)throw new InvalidOperationException("Application GPU preference leaked into defaults.");
                personalDevicePage.FindControl<ComboBox>("GraphicsDeviceSelector")!.SelectedIndex=1;
                rootDevices.UseProfile(deviceProfile.Id);
                using(var personalEngine=Core.NativeCore.CreateForDevice(rootDevices.ActiveSettings.GraphicsDeviceId))
                    if(personalEngine.IsInvalid || System.Runtime.InteropServices.Marshal.PtrToStringUTF8(Core.NativeCore.DeviceIdentifier(personalEngine))!=deviceProfile.Settings.GraphicsDeviceId)throw new InvalidOperationException("Active application GPU preference was ignored.");
                personalDevicePage.FindControl<SettingsCard>("GraphicsCard")!.BringIntoView();break;
            case "ProfileHardware":
                First<ProfilePage>().FindControl<SettingsCard>("MultimonitorCard")!.IsVisible = true;
                First<ProfilePage>().FindControl<SettingsCard>("GraphicsCard")!.IsVisible = true; break;
            case "ProfileCaptureWarning": First<ProfilePage>().FindControl<ComboBox>("CaptureMethod")!.SelectedIndex = 1; break;
            case "ProfileCapture": await OpenCombo(First<ProfilePage>().FindControl<ComboBox>("CaptureMethod")!); break;
            case "ProfileMode": await OpenCombo(First<ComboBox>()); break;
            case "ProfileLimiter":
                var limiter = First<ProfilePage>().FindControl<SettingsExpander>("FrameLimiterExpander")!;
                ((ToggleSwitch)limiter.Action!).IsChecked = true;
                if (!limiter.IsExpanded || !Core.RuntimeSettings.Current.FrameRateLimiterEnabled) throw new InvalidOperationException("Limiter switch did not enable runtime pacing.");
                var maximumRate=First<ProfilePage>().FindControl<NumericUpDown>("MaximumFrameRateInput")!;
                maximumRate.Value=29.97m;
                if(Math.Abs(Core.RuntimeSettings.Current.EffectiveMaximumFrameRate-29.97)>.0001)throw new InvalidOperationException("Frame rate editor did not reach runtime pacing.");
                ((ToggleSwitch)limiter.Action!).IsChecked=false;
                if(Core.RuntimeSettings.Current.EffectiveMaximumFrameRate!=0)throw new InvalidOperationException("Frame rate limiter did not switch off.");
                ((ToggleSwitch)limiter.Action!).IsChecked=true;
                limiter.BringIntoView(); break;
            case "ProfileTip": await OpenFlyout(window.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Show available parameters")); break;
            case "ProfileMore": await OpenFlyout(window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ProfileMore")); break;
            case "ProfileRename": case "ProfileReorder": case "ProfileDelete":
                var personal = (ProfilePage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                personal.ShowProfileAction(scenario["Profile".Length..]);
                await Task.Delay(200);
                popups.Add(personal.ActiveFlyoutContent!); break;
            case "ProfileContext":
                var context = (MenuFlyout)window.PreviewProfileNavigation!.ContextFlyout!;
                context.ShowAt(window.PreviewProfileNavigation);
                await Task.Delay(150);
                popups.Add(context.Items.OfType<Control>().First()); break;
            case "OutputFrame": await new Core.ScalingSession().VerifyOutputAsync(window); break;
            case "ModeCrt":
                var crtPage=First<ScalingModesPage>();
                crtPage.FindControl<Button>("UseCrtMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var crtPlan=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(crtPlan.Length!=1 || crtPlan[0].Effect!=12)throw new InvalidOperationException("CRT-Geom preset did not reach its native effect.");
                var crtExpander=crtPage.GetVisualDescendants().OfType<SettingsExpander>().First(e=>e.Header as string=="CRT-Geom");
                crtExpander.IsExpanded=true;crtExpander.BringIntoView();
                break;
            case "ProfileCrt":
                var crtSelector=First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!;
                crtSelector.SelectedIndex=5;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=12)throw new InvalidOperationException("Profile CRT-Geom selection is not connected.");

                break;
            case "ModeCrtChain":
                var crtChainPage=First<ScalingModesPage>();
                var crtForm=crtChainPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                crtForm.FindControl<TextBox>("NameInput")!.Text="CRT-Geom chain";await Task.Delay(100);
                crtForm.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=1;crtForm.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var crtEditor=crtChainPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var crtStage=Core.RuntimeSettings.Current.Modes.Single().Steps[0];crtStage.ScaleType=2;crtStage.Width=66;crtStage.Height=50;
                var crtChainExpander=(SettingsExpander)crtEditor.Content!;crtChainExpander.IsExpanded=true;await Task.Delay(100);
                crtEditor.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex=11;
                ((StackPanel)crtChainExpander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Panel)crtChainExpander.Action!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedCrt=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(selectedCrt.Length!=2 || selectedCrt[0].Effect!=12 || selectedCrt[0].Width!=66 || selectedCrt[1].Width!=66)throw new InvalidOperationException("Custom CRT-Geom chain effect IDs or sizes are incorrect.");
                crtChainExpander.BringIntoView();
                break;
            case "ModeCrtParameters":
                var crtParametersPage=First<ScalingModesPage>();
                crtParametersPage.GetVisualDescendants().OfType<SettingsExpander>().First(e=>e.Header as string=="CRT-Geom").IsExpanded=true;await Task.Delay(100);
                crtParametersPage.FindControl<Slider>("EffectParameter2")!.Value=1.7;
                crtParametersPage.FindControl<Slider>("EffectParameter14")!.Value=.45;
                crtParametersPage.FindControl<CheckBox>("CrtCurvature")!.IsChecked=false;
                if(Core.RuntimeSettings.Current.Crt[0]!=1.7f || Core.RuntimeSettings.Current.Crt[13]!=.45f || Core.RuntimeSettings.Current.Crt[3]!=0)throw new InvalidOperationException("CRT preset parameter controls are not connected.");
                var crtParameterButton=crtParametersPage.FindControl<Button>("CrtParametersButton")!;crtParameterButton.BringIntoView();await Task.Delay(100);crtParameterButton.Flyout!.ShowAt(crtParameterButton);await Task.Delay(150);popups.Add((Control)((Flyout)crtParameterButton.Flyout).Content!);
                break;
            case "ProfileCrtParameters":
                var crtProfile=First<ProfilePage>();crtProfile.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=5;
                crtProfile.FindControl<Button>("CrtSettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                var crtNumbers=crtProfile.ActiveFlyoutContent!.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
                if(crtNumbers.Length!=15)throw new InvalidOperationException("Incomplete CRT profile parameter editor.");
                crtNumbers[7].Value=.12m;crtNumbers[12].Value=2;
                if(Core.RuntimeSettings.Current.Crt[7]!=.12f || Core.RuntimeSettings.Current.ResolveChain(7,5,19,13)[0].Extra[12]!=2)throw new InvalidOperationException("Profile CRT parameters did not reach the native stage.");
                popups.Add(crtProfile.ActiveFlyoutContent!);break;
            case "ModeCunny":
                var cunnyPage=First<ScalingModesPage>();
                cunnyPage.FindControl<Button>("UseCunnyMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var cunnyPlan=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(cunnyPlan.Length!=1 || cunnyPlan[0].Effect!=11 || cunnyPlan[0].Width!=66 || cunnyPlan[0].Height!=50)throw new InvalidOperationException("CuNNy preset did not reach the native six-pass effect.");
                var cunnyExpander=cunnyPage.GetVisualDescendants().OfType<SettingsExpander>().First(e=>e.Header as string=="CuNNy");
                cunnyExpander.IsExpanded=true;cunnyExpander.BringIntoView();
                break;
            case "ProfileCunny":
                var cunnySelector=First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!;
                cunnySelector.SelectedIndex=3;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=11)throw new InvalidOperationException("Profile CuNNy selection is not connected.");
                cunnySelector.SelectedIndex=3;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=11)throw new InvalidOperationException("Profile LineArt selection is not connected.");
                break;
            case "ModeCunnyChain":
                var cunnyChainPage=First<ScalingModesPage>();
                var cunnyForm=cunnyChainPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                cunnyForm.FindControl<TextBox>("NameInput")!.Text="CuNNy chain";await Task.Delay(100);
                cunnyForm.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=1;cunnyForm.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var cunnyEditor=cunnyChainPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var cunnyChainExpander=(SettingsExpander)cunnyEditor.Content!;cunnyChainExpander.IsExpanded=true;await Task.Delay(100);
                cunnyEditor.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex=10;
                ((StackPanel)cunnyChainExpander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Panel)cunnyChainExpander.Action!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedCunny=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(selectedCunny.Length!=2 || selectedCunny[0].Effect!=11 || selectedCunny[0].Width!=66 || selectedCunny[1].Width!=66)throw new InvalidOperationException("Custom CuNNy chain effect IDs or sizes are incorrect.");
                cunnyChainExpander.BringIntoView();
                break;
            case "ModeFsrcnnx":
                var fsrcnnxPage=First<ScalingModesPage>();
                fsrcnnxPage.FindControl<Button>("UseFsrcnnxMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var fsrcnnxPlan=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(fsrcnnxPlan.Length!=1 || fsrcnnxPlan[0].Effect!=9 || fsrcnnxPlan[0].Width!=66 || fsrcnnxPlan[0].Height!=50)throw new InvalidOperationException("FSRCNNX preset did not reach the native six-pass effect.");
                var fsrcnnxExpander=fsrcnnxPage.GetVisualDescendants().OfType<SettingsExpander>().First(e=>e.Header as string=="FSRCNNX");
                fsrcnnxExpander.IsExpanded=true;fsrcnnxExpander.BringIntoView();
                break;
            case "ProfileFsrcnnx":
                var fsrcnnxSelector=First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!;
                fsrcnnxSelector.SelectedIndex=2;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=9)throw new InvalidOperationException("Profile FSRCNNX selection is not connected.");
                fsrcnnxSelector.SelectedIndex=7;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=10)throw new InvalidOperationException("Profile LineArt selection is not connected.");
                break;
            case "ModeFsrcnnxChain":
                var fsrcnnxChainPage=First<ScalingModesPage>();
                var fsrcnnxForm=fsrcnnxChainPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                fsrcnnxForm.FindControl<TextBox>("NameInput")!.Text="FSRCNNX chain";await Task.Delay(100);
                fsrcnnxForm.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=1;fsrcnnxForm.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var fsrcnnxEditor=fsrcnnxChainPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var fsrcnnxChainExpander=(SettingsExpander)fsrcnnxEditor.Content!;fsrcnnxChainExpander.IsExpanded=true;await Task.Delay(100);
                fsrcnnxEditor.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex=9;
                ((StackPanel)fsrcnnxChainExpander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Panel)fsrcnnxChainExpander.Action!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedFsrcnnx=Core.RuntimeSettings.Current.ResolveChain(33,25,190,130);
                if(selectedFsrcnnx.Length!=2 || selectedFsrcnnx[0].Effect!=10 || selectedFsrcnnx[0].Width!=66 || selectedFsrcnnx[1].Width!=66)throw new InvalidOperationException("Custom FSRCNNX chain effect IDs or sizes are incorrect.");
                fsrcnnxChainExpander.BringIntoView();
                break;
            case "ModeAnime":
                var animePage=First<ScalingModesPage>();
                animePage.FindControl<Button>("UseAnimeMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var animePlan=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                if(animePlan.Length!=1 || animePlan[0].Effect!=8 || animePlan[0].Width!=14 || animePlan[0].Height!=10)throw new InvalidOperationException("Anime4K preset did not reach the native 2x CNN.");
                var animeExpander=animePage.GetVisualDescendants().OfType<SettingsExpander>().First(e=>e.Header as string=="Anime4K");
                animeExpander.IsExpanded=true;animeExpander.BringIntoView();
                break;
            case "ProfileSmaa":
                for(int preset=0;preset<4;preset++)
                {
                    First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=15+preset;
                    var graph=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                    if(graph[0].Effect!=20+preset||graph[0].Width!=7||graph[0].Height!=5)throw new InvalidOperationException("SMAA preset did not reach the same-size native effect.");
                    window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(40);
                    if(First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex!=15+preset)throw new InvalidOperationException("SMAA profile selection did not restore.");
                }
                break;
            case "ModeSmaaChain":
            {
                var pageSmaa=First<ScalingModesPage>();pageSmaa.ImportFromJson(Core.SmaaTests.OriginalMode);
                var editorSmaa=pageSmaa.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var boxSmaa=(SettingsExpander)editorSmaa.Content!;boxSmaa.IsExpanded=true;await Task.Delay(80);
                var choiceSmaa=editorSmaa.GetVisualDescendants().OfType<ComboBox>().First();
                if(choiceSmaa.SelectedIndex!=21)throw new InvalidOperationException("SMAA editor selection incorrect.");
                choiceSmaa.SelectedIndex=0;choiceSmaa.SelectedIndex=22;
                var graphSmaa=Core.RuntimeSettings.Current.Modes.Single().Resolve(7,5,190,130);
                if(graphSmaa[0].Effect!=23||graphSmaa[0].Width!=7||graphSmaa[0].Height!=5)throw new InvalidOperationException("SMAA editor did not save the same-size effect.");
                break;
            }
            case "SmaaOutput":await new Core.ScalingSession().VerifyPresentationAsync(window,22);break;
            case "ProfileRavu":
                First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=14;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=19)throw new InvalidOperationException("RAVU profile did not resolve to the SOURCE model.");
                window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(80);
                if(First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex!=14)throw new InvalidOperationException("RAVU profile selection did not restore.");break;
            case "ModeRavuChain":
            {
                var ravuPage=First<ScalingModesPage>();ravuPage.ImportFromJson(Core.RavuTests.OriginalMode);
                var ravuEditor=ravuPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var ravuBox=(SettingsExpander)ravuEditor.Content!;ravuBox.IsExpanded=true;await Task.Delay(80);
                var ravuChoice=ravuEditor.GetVisualDescendants().OfType<ComboBox>().First();
                if(ravuChoice.SelectedIndex!=18)throw new InvalidOperationException("RAVU imported editor selection is incorrect.");
                ravuChoice.SelectedIndex=0;ravuChoice.SelectedIndex=18;
                if(Core.RuntimeSettings.Current.Modes.Single().Resolve(7,5,190,130)[0].Effect!=19)throw new InvalidOperationException("RAVU edited graph did not persist.");
                ravuBox.BringIntoView();break;
            }
            case "RavuOutput":await new Core.ScalingSession().VerifyPresentationAsync(window,19);break;
            case "ProfileAnimeExpanded":
            {
                var expandedPage=First<ProfilePage>();
                foreach(uint expandedModel in Core.AnimeExpandedTests.Effects)
                {
                    expandedPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=(int)expandedModel-5;
                    if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=expandedModel)throw new InvalidOperationException("Expanded Anime4K profile did not resolve: "+expandedModel);
                }
                window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(80);
                if(First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex!=13)throw new InvalidOperationException("Expanded Anime4K profile did not restore.");
                break;
            }
            case "ModeAnimeExpanded":
            {
                var expandedPage=First<ScalingModesPage>();expandedPage.ImportFromJson(Core.AnimeExpandedTests.OriginalModes);
                var expandedEditors=expandedPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().ToArray();
                if(expandedEditors.Length!=4)throw new InvalidOperationException("Expanded Anime4K import did not add four modes.");
                for(int i=0;i<4;i++)
                {
                    var expandedBox=(SettingsExpander)expandedEditors[i].Content!;expandedBox.IsExpanded=true;await Task.Delay(30);
                    var choice=expandedEditors[i].GetVisualDescendants().OfType<ComboBox>().First();
                    if(choice.SelectedIndex!=i+14)throw new InvalidOperationException("Expanded Anime4K editor restored wrong ID.");
                    choice.SelectedIndex=0;choice.SelectedIndex=i+14;
                    if(Core.RuntimeSettings.Current.Modes[i].Resolve(7,5,190,130)[0].Effect!=i+15)throw new InvalidOperationException("Expanded Anime4K editor save failed.");
                }
                ((SettingsExpander)expandedEditors[^1].Content!).BringIntoView();break;
            }
            case "AnimeExpandedL":await new Core.ScalingSession().VerifyPresentationAsync(window,15);break;
            case "AnimeExpandedVL":await new Core.ScalingSession().VerifyPresentationAsync(window,16);break;
            case "AnimeExpandedDenoiseS":await new Core.ScalingSession().VerifyPresentationAsync(window,17);break;
            case "AnimeExpandedDenoiseVL":await new Core.ScalingSession().VerifyPresentationAsync(window,18);break;
            case "ProfileCatalog":
            {
                var catalogPage=First<ProfilePage>();var choice=catalogPage.FindControl<ComboBox>("ScalingModeSelector")!;
                if(choice.ItemCount!=ProfilePage.BuiltinModeCount)throw new InvalidOperationException("Catalog presets are missing from the profile selector.");
                for(int i=0;i<ProfilePage.BuiltinEffectIds.Length;i++)
                {
                    choice.SelectedIndex=i;uint id=ProfilePage.BuiltinEffectIds[i];
                    var plan=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                    if(Core.RuntimeSettings.Current.Effect!=id || plan[0].Effect!=(id==6?4u:id))throw new InvalidOperationException("Catalog profile selection did not reach the native graph: "+id);
                }
                await OpenCombo(choice);break;
            }
            case "ProfileCatalogParameters":
            {
                var catalogPage=First<ProfilePage>();uint id=Core.GraphCatalog.All.Single(e=>e.Path=="CRT/CRT_Easymode").Id;
                catalogPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=Array.IndexOf(ProfilePage.BuiltinEffectIds,id);
                var button=catalogPage.FindControl<Button>("CrtSettingsButton")!;button.BringIntoView();await Task.Delay(100);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
                var popup=catalogPage.ActiveFlyoutContent!;var controls=popup.GetVisualDescendants().OfType<NumericUpDown>().Where(n=>n.Name?.StartsWith("GraphParameter")==true).ToArray();
                var definitions=Core.GraphEffects.Definitions(id);if(controls.Length!=17)throw new InvalidOperationException("Full catalog parameter editor did not expose all 17 slots.");
                foreach(var control in controls)
                {
                    int slot=int.Parse(control.Name![14..]);var definition=definitions[slot];
                    decimal value=definition.Integer ? definition.Default==definition.Minimum?Math.Min((decimal)definition.Maximum,(decimal)definition.Minimum+1):(decimal)definition.Minimum : Math.Min((decimal)definition.Maximum,(decimal)definition.Default+definition.Step);
                    control.Value=value;
                }
                var plan=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0];
                for(int i=0;i<definitions.Length;i++)if((i==0?plan.Parameter1:i==1?plan.Parameter2:plan.Extra[i-2])!=Core.GraphEffects.Get(Core.RuntimeSettings.Current,id,i))throw new InvalidOperationException("Catalog UI parameter slot did not reach the ABI.");
                popups.Add(popup);break;
            }
            case "CatalogXbrzOutput":await new Core.ScalingSession(new Core.RuntimeSettings()).VerifyPresentationAsync(window,Core.GraphCatalog.All.Single(e=>e.Path=="xBRZ/xBRZ_6x").Id);break;
            case "LumaSharpenOutput":
            case "LcasOutput":await new Core.ScalingSession(new Core.RuntimeSettings { ScaleType=0,ScaleX=2,ScaleY=2 }).VerifyPresentationAsync(window,Core.GraphCatalog.All.Single(e=>e.Path==(scenario=="LcasOutput"?"Sharpen/LCAS":"Sharpen/LumaSharpen")).Id);break;
            case "ProfileJincParameters":
            case "ProfileBilateralParameters":
            {
                var filterPage=First<ProfilePage>();uint id=scenario=="ProfileJincParameters"?24u:25u;
                filterPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=(int)id-5;
                var button=filterPage.FindControl<Button>("CrtSettingsButton")!;button.BringIntoView();await Task.Delay(100);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
                var filterUiEditor=filterPage.ActiveFlyoutContent!;var value=filterUiEditor.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>n.Name=="GraphParameter0");value.Value=id==24?.65m:1.5m;
                if(id==24)filterUiEditor.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>n.Name=="GraphParameter2").Value=.8m;
                var plan=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                if(plan[0].Effect!=id || plan[0].Parameter1!=(id==24?.65f:1.5f) || id==24&&plan[0].Extra[0]!=.8f || id==25&&plan[0].Width!=7)throw new InvalidOperationException("Filter profile parameter UI did not reach native configuration.");
                popups.Add(filterUiEditor);break;
            }
            case "ModeFilterChain":
            {
                var filterPage=First<ScalingModesPage>();filterPage.ImportFromJson(Core.GraphFilterTests.OriginalMode);
                var filterUiEditor=filterPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var expanded=(SettingsExpander)filterUiEditor.Content!;expanded.IsExpanded=true;expanded.BringIntoView();await Task.Delay(100);
                var stage=Core.RuntimeSettings.Current.Modes.Single().Steps[1];
                var choices=filterUiEditor.GetVisualDescendants().OfType<ComboBox>().ToArray();choices[1].SelectedIndex=0;choices[1].SelectedIndex=23;
                var plan=Core.RuntimeSettings.Current.Modes.Single().Resolve(7,5,190,130);
                if(plan.Length!=2 || plan[0].Effect!=25 || plan[1].Effect!=24)throw new InvalidOperationException("Filter chain editing lost effect identities.");
                var rows=((StackPanel)((StackPanel)expanded.Content!).Children[0]).Children.OfType<Border>().Select(b=>(WrapPanel)b.Child!).ToArray();
                var button=rows[^1].Children.OfType<Button>().First();button.BringIntoView();await Task.Delay(100);button.Flyout!.ShowAt(button);await Task.Delay(150);
                var popup=(Control)((Flyout)button.Flyout).Content!;popups.Add(popup);
                var controls=popup.GetVisualDescendants().OfType<NumericUpDown>().Where(n=>n.Name=="GraphParameter2").ToArray();
                if(controls.Length!=1)throw new InvalidOperationException("Jinc three-parameter editor is missing.");controls[0].Value=.4m;
                if(Core.GraphEffects.Get(stage,2)!=.4f)throw new InvalidOperationException("Jinc third parameter edit was ignored.");break;
            }
            case "BilateralOutput":await new Core.ScalingSession(new Core.RuntimeSettings()).VerifyPresentationAsync(window,25);break;
            case "JincOutput":await new Core.ScalingSession(new Core.RuntimeSettings { ScaleType=0,ScaleX=1,ScaleY=1 }).VerifyPresentationAsync(window,24);break;
            case "ProfileSsim":
            {
                var ssimPage=First<ProfilePage>();ssimPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=9;
                ssimPage.FindControl<NumericUpDown>("ProfileSsimOversharp")!.Value=2.5m;
                var plan=Core.RuntimeSettings.Current.ResolveChain(11,9,5,4);
                if(plan[0].Effect!=14 || plan[0].Parameter1!=2.5f || !ssimPage.FindControl<SettingsCard>("SsimParametersCard")!.IsVisible)throw new InvalidOperationException("SSim profile parameter did not reach the actual graph.");
                window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(80);ssimPage=First<ProfilePage>();
                if(ssimPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex!=9 || ssimPage.FindControl<NumericUpDown>("ProfileSsimOversharp")!.Value!=2.5m)throw new InvalidOperationException("SSim profile did not restore.");
                ssimPage.FindControl<SettingsCard>("SsimParametersCard")!.BringIntoView();await Task.Delay(80);break;
            }
            case "ModeSsimChain":
            {
                var ssimPage=First<ScalingModesPage>();ssimPage.ImportFromJson(Core.SsimTests.OriginalMode);
                var ssimEditor=ssimPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var ssimExpander=(SettingsExpander)ssimEditor.Content!;ssimExpander.IsExpanded=true;await Task.Delay(100);
                var effectChoice=ssimEditor.GetVisualDescendants().OfType<ComboBox>().First();
                if(effectChoice.SelectedIndex!=13)throw new InvalidOperationException("SSim imported chain has the wrong effect.");
                effectChoice.SelectedIndex=0;effectChoice.SelectedIndex=13;
                var parametersButton=ssimEditor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==Core.Localization.Text("Settings"));
                await OpenFlyout(parametersButton);
                var scalar=((ScrollViewer)((Flyout)parametersButton.Flyout!).Content!).GetVisualDescendants().OfType<NumericUpDown>().First();
                if(scalar.Minimum!=1 || scalar.Maximum!=3 || scalar.Value!=1)throw new InvalidOperationException("SSim effect parameter ranges are incorrect.");
                scalar.Value=3;
                if(Core.RuntimeSettings.Current.Modes.Single().Resolve(11,9,100,100)[0].Parameter1!=3)throw new InvalidOperationException("SSim editor parameter did not persist.");
                break;
            }
            case "SsimOutput":await new Core.ScalingSession(new Core.RuntimeSettings()).VerifySsimOutputAsync(window);break;
            case "ProfileNnedi3":
                First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=8;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=13)throw new InvalidOperationException("NNEDI3 profile selector did not reach the real graph.");
                window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(80);
                if(First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex!=8)throw new InvalidOperationException("NNEDI3 profile selection did not restore.");
                break;
            case "Nnedi3Output":await new Core.ScalingSession().VerifyPresentationAsync(window,13);break;
            case "ModeNnedi3Chain":
            {
                var root=Core.RuntimeSettings.Current;
                var nnediPage=First<ScalingModesPage>();nnediPage.ImportFromJson(Core.Nnedi3Tests.OriginalMode);
                var imported=root.Modes.Single();root.SelectedModeId=imported.Id;
                var graph=root.ResolveChain(7,5,190,130);
                if(graph.Length!=1 || graph[0].Effect!=13 || graph[0].Width!=14 || !imported.IsAvailable)throw new InvalidOperationException("Original NNEDI3 mode did not become runnable.");
                await Task.Delay(100);
                var nnediEditor=nnediPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();var nnediExpander=(SettingsExpander)nnediEditor.Content!;nnediExpander.IsExpanded=true;await Task.Delay(80);
                var selector=nnediEditor.GetVisualDescendants().OfType<ComboBox>().First();
                if(selector.SelectedIndex!=12)throw new InvalidOperationException("NNEDI3 chain editor did not restore the effect.");
                selector.SelectedIndex=0;selector.SelectedIndex=12;
                if(root.ResolveChain(7,5,190,130)[0].Effect!=13)throw new InvalidOperationException("NNEDI3 chain edit did not persist.");
                nnediExpander.BringIntoView();break;
            }
            case "ProfileAnime":
                First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=4;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130)[0].Effect!=8)throw new InvalidOperationException("Profile Anime4K selection is not connected.");
                break;
            case "ModeAnimeChain":
                var animeChainPage=First<ScalingModesPage>();
                var animeForm=animeChainPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                animeForm.FindControl<TextBox>("NameInput")!.Text="Anime4K chain";
                await Task.Delay(100);
                animeForm.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=1;animeForm.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var animeEditor=animeChainPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var animeChainExpander=(SettingsExpander)animeEditor.Content!;animeChainExpander.IsExpanded=true;
                await Task.Delay(100);
                animeEditor.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex=7;
                ((StackPanel)animeChainExpander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Panel)animeChainExpander.Action!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedAnime=Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                if(selectedAnime.Length!=2 || selectedAnime[0].Effect!=8 || selectedAnime[0].Width!=14 || selectedAnime[1].Width!=14)throw new InvalidOperationException("Custom CNN chain dimensions or effect IDs are incorrect.");
                animeChainExpander.BringIntoView();
                break;
            case "ModeFsr":
                var fsrPage = (ScalingModesPage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                fsrPage.FindControl<Slider>("EffectParameter1")!.Value = .41;
                var fsrExpander = fsrPage.GetVisualDescendants().OfType<SettingsExpander>().First(e => e.Header as string == "FSR");
                fsrPage.FindControl<Button>("UseFsrMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var fsrChain = Core.RuntimeSettings.Current.ResolveChain(7,5,190,130);
                if (fsrChain.Length != 2 || fsrChain[0].Effect != 4 || fsrChain[1].Effect != 5 || Math.Abs(fsrChain[1].Parameter1-.41)>.0001)
                    throw new InvalidOperationException("FSR controls did not reach runtime compute chain.");
                fsrExpander.IsExpanded=true;
                break;
            case "HomeFramePerformance":
                var performancePage=First<HomePage>();
                performancePage.FindControl<ComboBox>("MinimumFrameRateSelector")!.SelectedIndex=5;
                performancePage.FindControl<SettingsExpander>("DeveloperOptions")!.IsVisible=true;
                var duplicateSelector=performancePage.FindControl<ComboBox>("DuplicateFrameSelector")!;
                duplicateSelector.SelectedIndex=0;
                if(Core.RuntimeSettings.Current.MinimumFrameRate!=30 || Core.RuntimeSettings.Current.DuplicateFrameDetection!=Core.DuplicateFrameMode.Always || performancePage.FindControl<CheckBox>("DynamicDetectionStatistics")!.IsEnabled)throw new InvalidOperationException("Frame settings controls are not connected.");
                duplicateSelector.SelectedIndex=1;performancePage.FindControl<CheckBox>("DynamicDetectionStatistics")!.IsChecked=true;
                if(!Core.RuntimeSettings.Current.StatisticsForDynamicDetection)throw new InvalidOperationException("Dynamic statistics did not persist.");
                var gpuToggle=performancePage.FindControl<CheckBox>("GpuProfilingToggle")!;gpuToggle.IsChecked=true;
                if(!Core.RuntimeSettings.Current.GpuProfilingEnabled || new HomePage().FindControl<CheckBox>("GpuProfilingToggle")!.IsChecked!=true)throw new InvalidOperationException("GPU profiler setting did not persist/restore.");
                gpuToggle.IsChecked=false;if(Core.RuntimeSettings.Current.GpuProfilingEnabled)throw new InvalidOperationException("GPU profiler setting did not disable.");
                await Task.Delay(100);gpuToggle.BringIntoView();break;
            case "HomeDuplicateMenu":
                var duplicatePage=First<HomePage>();duplicatePage.FindControl<SettingsExpander>("DeveloperOptions")!.IsVisible=true;
                await OpenCombo(duplicatePage.FindControl<ComboBox>("DuplicateFrameSelector")!);break;
            case "HomeMinimumMenu":await OpenCombo(First<HomePage>().FindControl<ComboBox>("MinimumFrameRateSelector")!);break;
            case "PresentationOutputFrame":await new Core.ScalingSession().VerifyPresentationAsync(window);break;
            case "ProcessingOutputFrame":await new Core.ScalingSession().VerifyProcessingOutputAsync(window);break;
            case "HomeScreenshot":
                window.Navigate("Defaults");
                var defaultsPage=(ProfilePage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                string sharedDirectory=Path.Combine(Path.GetTempPath(),"magpie-shared-screenshots");
                defaultsPage.ApplyScreenshotDirectory(sharedDirectory);
                window.Navigate("Home");await Task.Delay(100);
                if(First<HomePage>().FindControl<SettingsCard>("ScreenshotDirectoryCard")!.Description as string != sharedDirectory)
                    throw new InvalidOperationException("Home screenshot directory did not refresh from defaults.");
                First<HomePage>().FindControl<SettingsCard>("ScreenshotDirectoryCard")!.BringIntoView();
                break;
            case "ProfileScreenshot":
                var screenshotPage=First<ProfilePage>();
                string selectedDirectory=Path.Combine(Path.GetTempPath(),"magpie-selected-screenshots");
                screenshotPage.ApplyScreenshotDirectory(selectedDirectory);
                var templateInput=screenshotPage.FindControl<TextBox>("ScreenshotTemplateInput")!;
                templateInput.Text="测试_%WT:2%_\\%_%Y%";
                await Task.Delay(100);
                if(Core.RuntimeSettings.Current.ScreenshotTemplate!=templateInput.Text || Core.RuntimeSettings.Current.ResolveScreenshotDirectory()!=selectedDirectory)
                    throw new InvalidOperationException("Screenshot controls did not reach runtime settings.");
                templateInput.Text="bad/name";
                await Task.Delay(100);
                if(!screenshotPage.FindControl<TextBlock>("ScreenshotTemplateError")!.IsVisible || Core.RuntimeSettings.Current.ScreenshotTemplate=="bad/name")
                    throw new InvalidOperationException("Invalid screenshot template was saved.");
                templateInput.Text="测试_%Y%_%MS%";
                await Task.Delay(100);
                if(screenshotPage.FindControl<TextBlock>("ScreenshotTemplateError")!.IsVisible || Core.RuntimeSettings.Current.ScreenshotTemplate!=templateInput.Text)
                    throw new InvalidOperationException("Screenshot template did not recover after validation error.");
                templateInput.BringIntoView();
                break;
            case "ProfileCropping":
            {
                var cropPage=First<ProfilePage>();var root=Core.RuntimeSettings.Current;
                cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked=true;
                cropPage.FindControl<NumericUpDown>("CroppingLeftInput")!.Value=30.5m;
                cropPage.FindControl<NumericUpDown>("CroppingRightInput")!.Value=50.5m;
                cropPage.FindControl<NumericUpDown>("CroppingTopInput")!.Value=20;
                cropPage.FindControl<NumericUpDown>("CroppingBottomInput")!.Value=30;
                if(!root.IsCroppingEnabled || root.CroppingLeft!=30.5f || root.CroppingRight!=50.5f || root.CroppingTop!=20 || root.CroppingBottom!=30 || !cropPage.FindControl<SettingsExpander>("CroppingExpander")!.IsExpanded)throw new InvalidOperationException("Crop controls did not persist or expand.");
                cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked=false;
                if(root.IsCroppingEnabled || root.CroppingLeft!=30.5f)throw new InvalidOperationException("Disabling crop lost margins.");
                window.Navigate("Home");window.Navigate("Defaults");await Task.Delay(80);cropPage=First<ProfilePage>();
                if(cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked==true || cropPage.FindControl<NumericUpDown>("CroppingLeftInput")!.Value!=30.5m)throw new InvalidOperationException("Crop UI did not restore on navigation.");
                cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked=true;await Task.Delay(100);
                cropPage.FindControl<NumericUpDown>("CroppingTopInput")!.BringIntoView();break;
            }
            case "ProfileCroppingLifecycle":
            {
                var root=Core.RuntimeSettings.Current;root.IsCroppingEnabled=true;root.CroppingLeft=15;
                var a=root.AddProfile("Crop Alpha",Path.Combine(Path.GetTempPath(),"crop-alpha"));var b=root.AddProfile("Crop Beta",Path.Combine(Path.GetTempPath(),"crop-beta"));
                window.RefreshProfilesNavigation();window.Navigate("Profile:"+a.Id);await Task.Delay(80);
                var cropPage=First<ProfilePage>();cropPage.FindControl<NumericUpDown>("CroppingLeftInput")!.Value=42.5m;
                cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked=false;
                if(root.CroppingLeft!=15 || !root.IsCroppingEnabled || b.Settings.CroppingLeft!=15 || !b.Settings.IsCroppingEnabled || a.Settings.CroppingLeft!=42.5f || a.Settings.IsCroppingEnabled)throw new InvalidOperationException("Crop application settings leaked.");
                window.Navigate("Home");First<HomePage>().FindControl<ComboBox>("ActiveProfileSelector")!.SelectedIndex=1;
                if(!ReferenceEquals(root.ActiveSettings,a.Settings) || root.ActiveSettings.IsCroppingEnabled)throw new InvalidOperationException("Active profile crop was not selected.");
                window.Navigate("Defaults");await Task.Delay(80);cropPage=First<ProfilePage>();
                if(cropPage.FindControl<ToggleSwitch>("CroppingToggle")!.IsChecked!=true || cropPage.FindControl<NumericUpDown>("CroppingLeftInput")!.Value!=15)throw new InvalidOperationException("Default crop was not restored.");
                cropPage.FindControl<NumericUpDown>("CroppingTopInput")!.BringIntoView();break;
            }
            case "CroppingOutput":await new Core.ScalingSession(new Core.RuntimeSettings()).VerifyCroppingAsync(window);break;
            case "ProfileWindowSizing":
            {
                var sizingPage=First<ProfilePage>();sizingPage.FindControl<ComboBox>("WindowScale")!.SelectedIndex=6;
                sizingPage.FindControl<NumericUpDown>("WindowCustomScale")!.Value=1.7m;
                if(Core.RuntimeSettings.Current.InitialWindowedScaleFactor!=6 || Core.RuntimeSettings.Current.CustomInitialWindowedScaleFactor!=1.7f || !sizingPage.FindControl<NumericUpDown>("WindowCustomScale")!.IsVisible)throw new InvalidOperationException("Initial window controls did not persist.");
                sizingPage.FindControl<ComboBox>("WindowScale")!.SelectedIndex=2;
                if(Core.RuntimeSettings.Current.InitialWindowedScaleFactor!=2 || sizingPage.FindControl<NumericUpDown>("WindowCustomScale")!.IsVisible)throw new InvalidOperationException("Window preset did not hide custom value.");
                sizingPage.FindControl<ComboBox>("WindowScale")!.SelectedIndex=6;
                await Task.Delay(100);sizingPage.FindControl<ComboBox>("WindowScale")!.BringIntoView();await Task.Delay(60);
                var customEditor=sizingPage.FindControl<NumericUpDown>("WindowCustomScale")!.GetVisualDescendants().OfType<TextBox>().Single();
                if(customEditor.Bounds.Width<40)throw new InvalidOperationException("Custom window factor text is squeezed by spin buttons.");break;
            }
            case "ProfileWindowScaleMenu":await OpenCombo(First<ProfilePage>().FindControl<ComboBox>("WindowScale")!);break;
            case "ProfileOutputAlignment":
                First<ProfilePage>().FindControl<ComboBox>("OutputAlignmentSelector")!.SelectedIndex=8;
                if(Core.RuntimeSettings.Current.OutputAlignment!=8)throw new InvalidOperationException("Output alignment did not persist.");
                await OpenCombo(First<ProfilePage>().FindControl<ComboBox>("OutputAlignmentSelector")!);break;
            case "ProfilePresentationLifecycle":
            {
                var root=Core.RuntimeSettings.Current;var defaults=First<ProfilePage>();
                defaults.FindControl<ComboBox>("WindowScale")!.SelectedIndex=6;defaults.FindControl<NumericUpDown>("WindowCustomScale")!.Value=1.7m;defaults.FindControl<ComboBox>("OutputAlignmentSelector")!.SelectedIndex=8;
                var a=root.AddProfile("Window Alpha",Path.Combine(Path.GetTempPath(),"window-alpha"));var b=root.AddProfile("Window Beta",Path.Combine(Path.GetTempPath(),"window-beta"));
                window.RefreshProfilesNavigation();window.Navigate("Profile:"+a.Id);await Task.Delay(100);
                var aPage=First<ProfilePage>();aPage.FindControl<ComboBox>("WindowScale")!.SelectedIndex=4;aPage.FindControl<ComboBox>("OutputAlignmentSelector")!.SelectedIndex=0;
                if(root.InitialWindowedScaleFactor!=6 || root.OutputAlignment!=8 || b.Settings.InitialWindowedScaleFactor!=6 || b.Settings.OutputAlignment!=8)throw new InvalidOperationException("Application presentation settings leaked into defaults or sibling.");
                window.Navigate("Home");First<HomePage>().FindControl<ComboBox>("ActiveProfileSelector")!.SelectedIndex=1;
                if(!ReferenceEquals(root.ActiveSettings,a.Settings))throw new InvalidOperationException("Active application presentation settings were not selected.");
                await new Core.ScalingSession(root.ActiveSettings).VerifyWindowSettingsAsync(window,1600,900);
                window.Navigate("Defaults");await Task.Delay(100);
                if(First<ProfilePage>().FindControl<ComboBox>("WindowScale")!.SelectedIndex!=6 || First<ProfilePage>().FindControl<ComboBox>("OutputAlignmentSelector")!.SelectedIndex!=8)throw new InvalidOperationException("Defaults presentation settings were not restored.");
                First<ProfilePage>().FindControl<ComboBox>("WindowScale")!.BringIntoView();break;
            }
            case "LargeSourceWindowOutput":
                await new Core.ScalingSession(new Core.RuntimeSettings { Effect=0,ScaleType=3 }).VerifyLargeSourceWindowAsync(window);break;
            case "WindowPlacementOutput":
                Core.RuntimeSettings.Current.InitialWindowedScaleFactor=1;
                await new Core.ScalingSession().VerifyWindowSettingsAsync(window,1000,563);break;
            case "ProfileLifecycle":
                var profileRoot=Core.RuntimeSettings.Current;
                var creation=(NewProfileFlyoutContent)((Flyout)window.FindControl<Button>("NewProfileNav")!.Flyout!).Content!;
                creation.RefreshSources();
                creation.FindControl<TextBox>("ExecutableInput")!.Text=Path.Combine(Path.GetTempPath(),"alpha-app");
                creation.FindControl<TextBox>("NameInput")!.Text="Alpha";
                await Task.Delay(100);
                if(!creation.FindControl<Button>("CreateButton")!.IsEnabled)throw new InvalidOperationException("Valid profile form remained disabled.");
                creation.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
                var alpha=profileRoot.Profiles.Single();
                var alphaPage=(ProfilePage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                alphaPage.FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=1;
                alphaPage.FindControl<NumericUpDown>("MaximumFrameRateInput")!.Value=27.5m;
                alphaPage.FindControl<ToggleSwitch>("FrameLimiterToggle")!.IsChecked=true;
                alphaPage.FindControl<TextBox>("LaunchArgumentsInput")!.Text="--name\nvalue with spaces\n$(literal)";
                await Task.Delay(100);
                creation.RefreshSources();creation.FindControl<ComboBox>("CopySource")!.SelectedIndex=1;
                creation.FindControl<TextBox>("ExecutableInput")!.Text=Path.Combine(Path.GetTempPath(),"beta-app");
                creation.FindControl<TextBox>("NameInput")!.Text="Alpha";await Task.Delay(100);
                creation.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(profileRoot.Profiles.Count!=1 || !creation.FindControl<TextBlock>("ErrorText")!.IsVisible)throw new InvalidOperationException("Duplicate profile creation was not rejected visibly.");
                creation.FindControl<TextBox>("NameInput")!.Text="Beta";
                await Task.Delay(100);
                creation.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
                var beta=profileRoot.Profiles.Single(p=>p.Name=="Beta");
                var betaPage=(ProfilePage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                if(beta.Settings.Effect!=6 || beta.Settings.EffectiveMaximumFrameRate!=27.5 || profileRoot.FrameRateLimiterEnabled || profileRoot.Effect!=1 || alpha.Arguments.Count!=3)
                    throw new InvalidOperationException($"Profile controls leaked or copy failed: beta effect={beta.Settings.Effect}, limit={beta.Settings.EffectiveMaximumFrameRate}; defaults enabled={profileRoot.FrameRateLimiterEnabled}, effect={profileRoot.Effect}; alpha arguments={alpha.Arguments.Count}, effect={alpha.Settings.Effect}, limit={alpha.Settings.EffectiveMaximumFrameRate}.");
                betaPage.FindControl<NumericUpDown>("MaximumFrameRateInput")!.Value=45;
                if(alpha.Settings.MaximumFrameRate!=27.5 || beta.Settings.MaximumFrameRate!=45)throw new InvalidOperationException("Profile controls share mutable settings.");
                window.Navigate("Home");
                First<HomePage>().FindControl<ComboBox>("ActiveProfileSelector")!.SelectedIndex=2;
                if(!ReferenceEquals(profileRoot.ActiveSettings,beta.Settings))throw new InvalidOperationException("Home did not select the profile for scaling.");
                await new Core.ScalingSession(profileRoot.ActiveSettings).VerifyOutputAsync(window);
                window.Navigate("Profile:"+beta.Id);
                betaPage.ShowProfileAction("Reorder");await Task.Delay(200);
                ((ReorderFlyoutContent)betaPage.ActiveFlyoutContent!).FindControl<Button>("MoveUpButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(profileRoot.Profiles[0]!=beta)throw new InvalidOperationException("Profile reorder did not change stored order.");
                betaPage.ShowProfileAction("Rename");await Task.Delay(200);
                var profileRename=(RenameFlyoutContent)betaPage.ActiveFlyoutContent!;
                profileRename.FindControl<TextBox>("NameInput")!.Text="Renamed Beta";
                await Task.Delay(100);
                if(!profileRename.FindControl<Button>("ConfirmButton")!.IsEnabled)throw new InvalidOperationException("Valid profile rename remained disabled.");
                profileRename.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(beta.Name!="Renamed Beta")throw new InvalidOperationException("Profile rename did not reach model.");
                betaPage.ShowProfileAction("Delete");await Task.Delay(200);
                ((DeleteConfirmationFlyoutContent)betaPage.ActiveFlyoutContent!).FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(profileRoot.Profiles.Count!=1 || profileRoot.ActiveProfileId is not null || window.FindControl<StackPanel>("ProfilesNavItems")!.Children.Count!=1)
                    throw new InvalidOperationException("Deleting active profile did not remove navigation or restore defaults.");
                window.Navigate("Home");
                page="Home";
                Console.WriteLine("PROFILE UI PASS: creation/copy, independent controls, literal arguments, selected-profile output, order/rename/delete, navigation, default recovery.");
                break;
            case "ProfileFsr":
                First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!.SelectedIndex=1;
                if(Core.RuntimeSettings.Current.ResolveChain(7,5,190,130).Length!=2 || Core.RuntimeSettings.Current.Effect!=6)
                    throw new InvalidOperationException("Profile FSR selection is not connected.");
                break;
            case "ModeImport":
            case "ModeImportFailure":
            case "ModeExport":
            {
                var ioPage=First<ScalingModesPage>();
                var imported=ioPage.ImportFromJson(Core.ScalingModeInterchangeTests.Fixture());
                if(imported.Added!=7 || imported.DeferredEffects!=0 || Core.RuntimeSettings.Current.Modes.Count!=7)throw new InvalidOperationException("Original import did not reach the mode editor.");
                if(scenario=="ModeImportFailure")
                {
                    if(ioPage.TryImportFromJson("{bad json") is not null || Core.RuntimeSettings.Current.Modes.Count!=7 || !ioPage.FindControl<TextBlock>("InterchangeStatus")!.IsVisible)throw new InvalidOperationException("Import failure was not reported atomically.");
                }
                if(scenario=="ModeExport")
                {
                    var exported=Core.ScalingModeInterchange.Parse(ioPage.ExportToJson());
                    if(exported.Count!=14 || exported.Any(m=>!m.IsAvailable))throw new InvalidOperationException("Built-in/custom export is incomplete.");
                    // Both actual menu handlers must guard preview mode before opening a system picker.
                    ioPage.FindControl<MenuItem>("ExportModesAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    ioPage.FindControl<MenuItem>("ImportModesAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    if(!ioPage.FindControl<TextBlock>("InterchangeStatus")!.Text!.Contains("preview"))throw new InvalidOperationException("Picker preview guard was not applied.");
                }
                Console.WriteLine("MODE IO UI PASS: original presets, preserved failure state, built-in/custom JSON export and picker preview guards.");
                break;
            }
            case "ModeImportedUnavailable":
            case "ModeImportedReplacement":
            {
                var unavailablePage=First<ScalingModesPage>();unavailablePage.ImportFromJson(Core.ScalingModeInterchangeTests.Unknown);
                var unavailableMode=Core.RuntimeSettings.Current.Modes.Single();
                var unavailableEditor=unavailablePage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var unavailableExpander=(SettingsExpander)unavailableEditor.Content!;unavailableExpander.IsExpanded=true;await Task.Delay(100);
                var useUnavailable=((Panel)unavailableExpander.Action!).Children.OfType<Button>().First();
                var unavailableChoice=unavailableEditor.GetVisualDescendants().OfType<ComboBox>().First();
                if(unavailableEditor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==Core.Localization.Text("Settings")).IsEnabled || useUnavailable.IsEnabled || unavailableMode.IsAvailable || !unavailableChoice.SelectedItem!.ToString()!.Contains("Unported_model"))throw new InvalidOperationException("Unknown effect was replaced or made runnable.");
                if(scenario=="ModeImportedReplacement")
                {
                    unavailableChoice.SelectedIndex=0;
                    if(!useUnavailable.IsEnabled || !unavailableMode.IsAvailable || unavailableMode.Steps[0].Effect!=0 || unavailableMode.Steps[0].DeferredEffect.HasValue)throw new InvalidOperationException("Explicit replacement did not make the imported mode runnable.");
                    useUnavailable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(Core.RuntimeSettings.Current.SelectedModeId!=unavailableMode.Id || Core.RuntimeSettings.Current.ResolveChain(3,2,19,13)[0].Effect!=0)throw new InvalidOperationException("Replacement mode did not reach runtime selection.");
                }
                await Task.Delay(100);unavailableExpander.BringIntoView();
                Console.WriteLine("MODE DEFERRED UI PASS: raw unknown effect, disabled use/settings, explicit replacement and real runtime chain.");
                break;
            }
            case "ModeImportedEmpty":
            {
                var emptyPage=First<ScalingModesPage>();
                var emptyForm=emptyPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                emptyForm.FindControl<TextBox>("NameInput")!.Text="Empty original mode";emptyForm.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=0;
                emptyForm.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var emptyModel=Core.RuntimeSettings.Current.Modes.Single();
                var emptyEditor=emptyPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var emptyExpander=(SettingsExpander)emptyEditor.Content!;emptyExpander.IsExpanded=true;
                var emptyUse=((Panel)emptyExpander.Action!).Children.OfType<Button>().First();
                if(emptyModel.Steps.Count!=0 || emptyUse.IsEnabled || emptyModel.IsAvailable)throw new InvalidOperationException("Empty original mode was given an invented effect.");
                ((StackPanel)emptyExpander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(emptyModel.Steps.Count!=1 || !emptyUse.IsEnabled || !emptyModel.IsAvailable)throw new InvalidOperationException("Adding an effect did not activate the empty mode.");
                await Task.Delay(100);emptyExpander.BringIntoView();break;
            }
            case "ModeImportedParameters":
            {
                var importedPage=First<ScalingModesPage>();importedPage.ImportFromJson(Core.ScalingModeInterchangeTests.Bicubic);
                var importedEditor=importedPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                ((SettingsExpander)importedEditor.Content!).IsExpanded=true;await Task.Delay(100);
                var parameterButton=importedEditor.GetVisualDescendants().OfType<Button>().First(b=>b.Content as string==Core.Localization.Text("Settings"));
                await OpenFlyout(parameterButton);
                var numbers=((ScrollViewer)((Flyout)parameterButton.Flyout!).Content!).GetVisualDescendants().OfType<NumericUpDown>().ToArray();
                if(numbers[0].Value!=.2m || numbers[1].Value!=.6m)throw new InvalidOperationException("Imported parameters are not reflected in editable controls.");
                numbers[0].Value=.4m;
                if(Math.Abs(Core.RuntimeSettings.Current.Modes[0].Resolve(3,2,19,13)[0].Parameter1-.4f)>1e-6)throw new InvalidOperationException("Imported parameter edits did not update the native graph.");
                break;
            }
            case "ProfileImportedMode":
            {
                Core.ScalingModeInterchange.Import(Core.RuntimeSettings.Current,Core.ScalingModeInterchangeTests.Unknown);
                Core.ScalingModeInterchange.Import(Core.RuntimeSettings.Current,Core.ScalingModeInterchangeTests.Bicubic);
                window.Navigate("ScalingModes");window.Navigate("Defaults");await Task.Delay(100);
                var importedSelector=First<ProfilePage>().FindControl<ComboBox>("ScalingModeSelector")!;
                if(importedSelector.Items[ProfilePage.BuiltinModeCount] is not ComboBoxItem { IsEnabled:false } || importedSelector.Items[ProfilePage.BuiltinModeCount+1] is not ComboBoxItem { IsEnabled:true })throw new InvalidOperationException("Profile mode availability is not displayed.");
                importedSelector.SelectedIndex=ProfilePage.BuiltinModeCount+1;
                if(Core.RuntimeSettings.Current.SelectedModeId!=Core.RuntimeSettings.Current.Modes[1].Id)throw new InvalidOperationException("Imported mode cannot be selected for a profile.");
                await OpenCombo(importedSelector);break;
            }
            case "ModeChainEditor":
                var chainPage = (ScalingModesPage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                var form = chainPage.FindControl<NewScalingModeFlyoutContent>("NewModeForm")!;
                form.FindControl<TextBox>("NameInput")!.Text = "Test chain";
                form.FindControl<ComboBox>("CopyFrom")!.SelectedIndex=1;form.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var model = Core.RuntimeSettings.Current.Modes.Single();
                if (model.Name != "Test chain") throw new InvalidOperationException("New mode did not persist to runtime state.");
                var editor = chainPage.FindControl<StackPanel>("CustomModes")!.Children.OfType<EffectModeEditor>().Single();
                var expander = (SettingsExpander)editor.Content!;
                expander.IsExpanded = true;
                ((StackPanel)expander.Content!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (model.Steps.Count != 2) throw new InvalidOperationException("Add effect did not change chain.");
                await Task.Delay(100);
                editor.GetVisualDescendants().OfType<ComboBox>().First().SelectedIndex = 2;
                if (model.Steps[0].Effect != 2) throw new InvalidOperationException("Effect selector did not change chain.");
                ((Panel)expander.Action!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (Core.RuntimeSettings.Current.SelectedModeId != model.Id || Core.RuntimeSettings.Current.ResolveChain(7,5,100,80).Length != 2)
                    throw new InvalidOperationException("Mode selection did not reach executable chain.");
                break;
            case "ModeRuntimeSettings":
                var runtimePage = (ScalingModesPage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                runtimePage.FindControl<Slider>("EffectParameter0")!.Value = .73;
                if (Math.Abs(Core.RuntimeSettings.Current.LanczosAntiRinging - .73) > .0001) throw new InvalidOperationException("Lanczos parameter did not reach runtime settings.");
                var scaleEditor = runtimePage.FindControl<ScaleFlyoutContent>("LanczosScale")!;
                scaleEditor.FindControl<ComboBox>("TypeSelector")!.SelectedIndex = 2;
                scaleEditor.FindControl<NumericUpDown>("WidthPixels")!.Value = 1000;
                scaleEditor.FindControl<NumericUpDown>("HeightPixels")!.Value = 700;
                if (Core.RuntimeSettings.Current.OutputSize(640,480,1920,1080) != (1000u,700u)) throw new InvalidOperationException("Scale editor did not reach runtime settings.");
                First<SettingsExpander>().IsExpanded = true;
                break;
            case "ModeExpand": First<SettingsExpander>().IsExpanded = true; break;
            case "ModeRename": await OpenFlyout(((StackPanel)First<SettingsExpander>().Action!).Children.OfType<Button>().First()); break;
            case "ModeMore": await OpenFlyout(((StackPanel)First<SettingsExpander>().Action!).Children.OfType<Button>().Last()); break;
            case "ModeDelete":
                var more = ((StackPanel)First<SettingsExpander>().Action!).Children.OfType<Button>().Last();
                more.Flyout!.ShowAt(more);
                await Task.Delay(100);
                var delete = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().First(i => i.Tag as string == "Lanczos");
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                more.Flyout.Hide();
                await Task.Delay(200);
                var modePage = (ScalingModesPage)window.FindControl<ContentControl>("ContentFrame")!.Content!;
                popups.Add(modePage.ActiveConfirmation!);
                break;
            case "ModeScale": case "ModeTypes": case "ModeParameters":
                First<SettingsExpander>().IsExpanded = true;
                await Task.Delay(150);
                var tip = scenario == "ModeParameters" ? "Parameters" : "Scale";
                var action = window.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == tip);
                await OpenFlyout(action);
                if (scenario == "ModeTypes") await OpenCombo(((ScaleFlyoutContent)((Flyout)action.Flyout!).Content!).GetVisualDescendants().OfType<ComboBox>().Single());
                break;
            case "ModeNew": await OpenFlyout(window.FindControl<Button>("NewScalingModeButton") ?? window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "NewScalingModeButton")); break;
            default: throw new ArgumentException($"Unknown UI preview: {scenario}");
        }
        await Task.Delay(150);
        var homeLabel=window.FindControl<Button>("HomeNav")!.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Classes.Contains("nav-label"));
        if(homeLabel.Text!=Core.Localization.Get("Root_Home.Content"))throw new InvalidOperationException("Navigation did not load the startup language.");
        var selected = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav") && b.Classes.Contains("selected")).ToArray();
        if (selected.Length != 1 || selected[0].Tag as string != page) throw new InvalidOperationException("Navigation selection is inconsistent with the displayed page: " + page + "; " + string.Join(" / ", window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav")).Select(b => $"{b.Tag}: {string.Join(",", b.Classes)}")));
        return popups;
    }

    public static void Save(Control visual, MainWindow window, IEnumerable<Control> popupContents, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var roots=popupContents.Select(TopLevel.GetTopLevel).OfType<PopupRoot>().Distinct().ToArray();
        bool wayland=OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        string token=Guid.NewGuid().ToString("N");
        // The verifier reads only protocol events preceding this marker. Closing
        // the application destroys the popup roles after the captured state.
        Console.Error.WriteLine("MAGPIE_UI_CAPTURE_BEGIN:"+token);
        var size = PixelSize.FromSize(visual.Bounds.Size, 1);
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(visual);
        bitmap.Save(path+".base.png",PngBitmapEncoderOptions.Default);
        var layers=new List<(string File,PixelSize Size)>();
        using (var context = bitmap.CreateDrawingContext(false))
            foreach (var root in roots)
            {
                if (root.Bounds.Width <= 0 || root.Bounds.Height <= 0) throw new InvalidOperationException("Popup has no rendered bounds.");
                var popupSize = PixelSize.FromSize(root.Bounds.Size, 1);
                using var popupBitmap = new RenderTargetBitmap(popupSize);
                popupBitmap.Render(root);
                string layerPath=path+".popup-"+layers.Count+".png";
                popupBitmap.Save(layerPath,PngBitmapEncoderOptions.Default);
                layers.Add((Path.GetFileName(layerPath),popupSize));
                if(wayland)
                {
                    // Wayland deliberately has no global window coordinates.
                    // The isolated verifier will composite using acknowledged
                    // xdg_popup.configure positions, never a guessed offset.
                    Console.WriteLine($"POPUP: {popupSize.Width}x{popupSize.Height}, offset=requires-native-wayland-proof.");
                    continue;
                }
                var delta = root.PointToScreen(default) - window.PointToScreen(default);
                if (delta.X < -1 || delta.Y < -1 || delta.X + popupSize.Width > size.Width + 1 || delta.Y + popupSize.Height > size.Height + 1)
                    throw new InvalidOperationException($"Popup extends outside the application viewport: {delta}, {popupSize}, owner={size}.");
                context.DrawImage(popupBitmap, new Rect(delta.X, delta.Y, popupSize.Width, popupSize.Height));
                Console.WriteLine($"POPUP: {popupSize.Width}x{popupSize.Height}, offset={delta}.");
            }
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        using var metadata=File.Create(path+".layers.json");
        using var json=new System.Text.Json.Utf8JsonWriter(metadata);
        json.WriteStartObject();json.WriteString("capture_token",token);json.WriteBoolean("wayland",wayland);
        json.WriteNumber("render_scaling",window.RenderScaling);
        json.WriteString("base",Path.GetFileName(path)+".base.png");
        json.WriteNumber("width",size.Width);json.WriteNumber("height",size.Height);
        json.WriteStartArray("popups");
        foreach(var layer in layers)
        {
            json.WriteStartObject();json.WriteString("file",layer.File);json.WriteNumber("width",layer.Size.Width);json.WriteNumber("height",layer.Size.Height);json.WriteEndObject();
        }
        json.WriteEndArray();json.WriteEndObject();
    }
}

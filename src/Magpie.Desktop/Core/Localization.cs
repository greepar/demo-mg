using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
namespace Magpie.Desktop.Core;

internal static class Localization
{
    private const string Prefix="Magpie.Strings.Resources.language-";
    private const string Suffix=".xml";
    private static readonly Assembly Resources=typeof(Localization).Assembly;
    public static readonly string[] Languages=Resources.GetManifestResourceNames()
        .Where(n=>n.StartsWith(Prefix,StringComparison.Ordinal) && n.EndsWith(Suffix,StringComparison.Ordinal))
        .Select(n=>n[Prefix.Length..^Suffix.Length]).Order(StringComparer.Ordinal).ToArray();
    public static readonly CultureInfo SystemUICulture=CultureInfo.CurrentUICulture;
    private static readonly Dictionary<string,string> English=Read("en-US");
    private static Dictionary<string,string> _strings=English;
    public static string CurrentLanguage { get; private set; } = "en-US";
    public static string CurrentPreference { get; private set; } = "en-US";
    public static string NormalizePreference(string? preference)
    {
        if(preference=="")return "";
        return Languages.FirstOrDefault(tag=>tag.Equals(preference,StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Unsupported application language.");
    }
    public static string Resolve(string preference,CultureInfo systemCulture)
    {
        preference=NormalizePreference(preference);
        if(preference.Length>0)return preference;
        for(var culture=systemCulture;culture.Name.Length>0;culture=culture.Parent)
        {
            string name=culture.Name;
            var exact=Languages.FirstOrDefault(tag=>tag.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(exact is not null)return exact;
            if(name is "zh-TW" or "zh-HK" or "zh-MO")return "zh-Hant";
            if(name is "zh-CN" or "zh-SG" or "zh")return "zh-Hans";
            if(name=="en")return "en-US";
            if(name=="pt")return "pt-BR";
        }
        return "en-US";
    }
    public static bool RequiresRestart(string preference)=>Resolve(preference,SystemUICulture)!=CurrentLanguage;
    public static void Configure(string preference)
    {
        string normalized=NormalizePreference(preference),language=Resolve(normalized,SystemUICulture);
        var translated=language=="en-US" ? English : Read(language);
        Volatile.Write(ref _strings,translated);
        CurrentPreference=normalized;CurrentLanguage=language;
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(language);
    }
    public static string Get(string key)
    {
        if(key is "Settings_General_Language_System" or "Settings_General_Theme_System.Content")return DesktopValue(0);
        if(key=="Desktop.Settings")return DesktopValue(1);
        int desktopIndex=key switch { "Desktop.Use"=>2,"Desktop.Refresh"=>3,"Desktop.Automatic"=>4,"Desktop.Stop"=>5,"Desktop.Screenshot"=>6,_=>-1 };
        if(desktopIndex>=0)return DesktopValue(desktopIndex);
        if(key=="Desktop.Parameters")return Text("Parameters");
        var translated=Volatile.Read(ref _strings);
        return translated.TryGetValue(key,out var value) && !string.IsNullOrWhiteSpace(value) ? value : English.GetValueOrDefault(key,key);
    }
    public static string Text(string english)=>english switch
    {
        "Use"=>Get("Desktop.Use"),"Settings"=>Get("Desktop.Settings"),"Refresh"=>Get("Desktop.Refresh"),
        "Automatic"=>Get("Desktop.Automatic"),"Stop"=>Get("Desktop.Stop"),"Screenshot"=>Get("Desktop.Screenshot"),
        "Rename"=>Get("Profile_MoreOptions_Rename.Text"),"Delete"=>Get("DeleteConfirmationFlyoutContent_Delete.Text"),
        "None"=>Get("ScalingModes_NewScalingModeFlyout_CopyFrom_None"),"Defaults"=>Get("Root_Defaults.Content"),
        "Launch"=>Get("Root_ProfileFlyout_Launch.Text"),"Open program location"=>Get("Profile_MoreOptions_OpenProgramLocation.Text"),
        "+ Add effect"=>"+ "+Get("ScalingModes_AddEffect.Text"),
        "Parameters"=>Get("ScalingModes_Parameters.[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip"),
        _=>english
    };
    private static Dictionary<string,string> Read(string language)
    {
        using var stream=Resources.GetManifestResourceStream(Prefix+language+Suffix)
            ?? throw new InvalidDataException("Embedded language resource is missing: "+language);
        return XDocument.Load(stream).Root!.Elements("data").ToDictionary(item=>(string)item.Attribute("name")!,item=>item.Element("value")!.Value,StringComparer.Ordinal);
    }
    private static readonly Dictionary<string,string[]> Desktop=new(StringComparer.Ordinal)
    {
        ["de"]=["Systemstandard","Einstellungen","Verwenden","Aktualisieren","Automatisch","Beenden","Bildschirmfoto"], ["en-US"]=["System default","Settings","Use","Refresh","Automatic","Stop","Screenshot"],
        ["es"]=["Predeterminado del sistema","Configuración","Usar","Actualizar","Automático","Detener","Captura de pantalla"], ["fi"]=["Järjestelmän oletus","Asetukset","Käytä","Päivitä","Automaattinen","Pysäytä","Kuvakaappaus"],
        ["fr"]=["Par défaut du système","Paramètres","Utiliser","Actualiser","Automatique","Arrêter","Capture d’écran"], ["hu"]=["Rendszer alapértelmezése","Beállítások","Használat","Frissítés","Automatikus","Leállítás","Képernyőkép"],
        ["id"]=["Default sistem","Pengaturan","Gunakan","Segarkan","Otomatis","Hentikan","Tangkapan layar"], ["it"]=["Predefinito di sistema","Impostazioni","Usa","Aggiorna","Automatico","Interrompi","Schermata"],
        ["ja"]=["システムの既定","設定","使用","更新","自動","停止","スクリーンショット"], ["ka"]=["სისტემის ნაგულისხმევი","პარამეტრები","გამოყენება","განახლება","ავტომატური","შეჩერება","ეკრანის ანაბეჭდი"],
        ["ko"]=["시스템 기본값","설정","사용","새로 고침","자동","중지","스크린샷"], ["pl"]=["Domyślny systemu","Ustawienia","Użyj","Odśwież","Automatycznie","Zatrzymaj","Zrzut ekranu"],
        ["pt-BR"]=["Padrão do sistema","Configurações","Usar","Atualizar","Automático","Parar","Captura de tela"], ["ru"]=["По умолчанию в системе","Настройки","Применить","Обновить","Автоматически","Остановить","Снимок экрана"],
        ["ta"]=["கணினி இயல்புநிலை","அமைப்புகள்","பயன்படுத்து","புதுப்பி","தானியங்கு","நிறுத்து","திரைப்பிடிப்பு"], ["tr"]=["Sistem varsayılanı","Ayarlar","Kullan","Yenile","Otomatik","Durdur","Ekran görüntüsü"],
        ["uk"]=["Типове системне","Налаштування","Застосувати","Оновити","Автоматично","Зупинити","Знімок екрана"], ["vi"]=["Mặc định hệ thống","Cài đặt","Sử dụng","Làm mới","Tự động","Dừng","Ảnh chụp màn hình"],
        ["zh-Hans"]=["跟随系统","设置","使用","刷新","自动","停止","截图"], ["zh-Hant"]=["跟隨系統","設定","使用","重新整理","自動","停止","螢幕擷取"]
    };
    internal static void VerifyResources()
    {
        string previous=CurrentPreference;
        try
        {
            if(Languages.Length!=20)throw new InvalidDataException("Expected all 20 original languages.");
            foreach(string tag in Languages)
            {
                Configure(tag);
                if(CurrentLanguage!=tag || Get("Root_Home.Content")=="Root_Home.Content" || string.IsNullOrWhiteSpace(Get("Desktop.Settings")))throw new InvalidDataException("Incomplete language initialization: "+tag);
                foreach(var entry in English)
                    if(string.IsNullOrWhiteSpace(Get(entry.Key)))throw new InvalidDataException("Missing translated and fallback value: "+tag+" / "+entry.Key);
                foreach(string key in new[]{"Desktop.Use","Desktop.Refresh","Desktop.Automatic","Desktop.Stop","Desktop.Screenshot"})
                    if(string.IsNullOrWhiteSpace(Get(key)) || Get(key)==key)throw new InvalidDataException("Missing desktop language label: "+tag+" / "+key);
                var translated=Read(tag);
                foreach(var entry in English.Where(e=>!translated.TryGetValue(e.Key,out var value) || string.IsNullOrWhiteSpace(value)))
                    if(Get(entry.Key)!=entry.Value && entry.Key is not ("Settings_General_Language_System" or "Settings_General_Theme_System.Content"))throw new InvalidDataException("English fallback failed: "+tag);
            }
            foreach(var sample in new[]{("zh-CN","zh-Hans"),("zh-TW","zh-Hant"),("zh-HK","zh-Hant"),("zh-Hant-HK","zh-Hant"),("en-GB","en-US"),("de-AT","de"),("pt-PT","pt-BR"),("ka-GE","ka"),("ar-EG","en-US")})
                if(Resolve("",CultureInfo.GetCultureInfo(sample.Item1))!=sample.Item2)throw new InvalidDataException("System language resolution failed: "+sample.Item1);
            if(Resolve("",CultureInfo.InvariantCulture)!="en-US" || NormalizePreference("ZH-hANS")!="zh-Hans")throw new InvalidDataException("Culture fallback or normalization failed.");
            Configure("zh-Hans");if(Get("Root_Home.Content")!="主页" || Get("Settings_General_Language_System").Contains("Windows"))throw new InvalidDataException("Chinese or platform-neutral system label failed.");
            if(Get("missing.localization.key")!="missing.localization.key")throw new InvalidDataException("Unknown localization key fallback changed.");
        }
        finally { Configure(previous); }
    }
    private static string DesktopValue(int index)=>Desktop.GetValueOrDefault(CurrentLanguage,Desktop["en-US"])[index];
}

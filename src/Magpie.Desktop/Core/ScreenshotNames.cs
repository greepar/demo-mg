using System.Globalization;
using System.Text;
namespace Magpie.Desktop.Core;

internal static class ScreenshotNames
{
    internal static string WithSuffix(string filename,string suffix)
    {
        static string Bound(string text,int maximum)
        {
            var result=new StringBuilder();int bytes=0;
            foreach(var rune in text.EnumerateRunes()) { if(bytes+rune.Utf8SequenceLength>maximum)break;result.Append(rune.ToString());bytes+=rune.Utf8SequenceLength; }
            return result.ToString().TrimEnd(' ','.');
        }
        string safe=Path.GetFileNameWithoutExtension(Format("%WT%",suffix,"",DateTime.Now));
        safe=Bound(safe,80);
        return Bound(Path.GetFileNameWithoutExtension(filename),219-Encoding.UTF8.GetByteCount(safe))+"_"+safe+".png";
    }
    public const string DefaultTemplate = "%WT:80%_%Y%-%m%-%D%_%H%-%M%-%S%-%MS%";
    // Recognized tokens may contain a colon; literal filename characters may not.
    public static bool IsValidTemplate(string template)
    {
        for (int i=0;i<template.Length;i++)
        {
            if (template[i] == '\\' && i+1<template.Length && template[i+1]=='%') { i++; continue; }
            if (TryToken(template,i,out int length,out _,out _)) { i+=length-1; continue; }
            if (template[i]<32 || "<>:\"/\\|?*".Contains(template[i])) return false;
        }
        return true;
    }
    private static bool TryToken(string template,int start,out int length,out string token,out int count)
    {
        length=0;token="";count=0;
        if(template[start]!='%')return false;
        int end=template.IndexOf('%',start+1);
        if(end<0)return false;
        token=template[(start+1)..end];
        bool valid=token is "Y" or "y" or "m" or "D" or "H" or "M" or "S" or "MS" or "WT" or "PN";
        if(!valid && (token.StartsWith("WT:") || token.StartsWith("PN:")) && token.Length>3)
        {
            foreach(char digit in token.AsSpan(3))
            {
                if(digit<'0'||digit>'9')return false;
                count=Math.Min(127,count*10+digit-'0');
            }
            valid=count>0;
            if(valid)token=token[..2];
        }
        if(valid)length=end-start+1;
        return valid;
    }
    public static string Format(string template, string title, string process, DateTime time)
    {
        var result = new StringBuilder();
        for (int i = 0; i < template.Length;)
        {
            if (template[i] == '\\' && i + 1 < template.Length && template[i + 1] == '%') { result.Append('%'); i += 2; continue; }
            if (!TryToken(template,i,out int length,out string token,out int count)) { result.Append(template[i++]); continue; }
            string? value = token switch
            {
                "Y" => time.ToString("yyyy", CultureInfo.InvariantCulture), "y" => time.ToString("yy", CultureInfo.InvariantCulture),
                "m" => time.ToString("MM", CultureInfo.InvariantCulture), "D" => time.ToString("dd", CultureInfo.InvariantCulture),
                "H" => time.ToString("HH", CultureInfo.InvariantCulture), "M" => time.ToString("mm", CultureInfo.InvariantCulture),
                "S" => time.ToString("ss", CultureInfo.InvariantCulture), "MS" => time.ToString("fff", CultureInfo.InvariantCulture),
                "WT" => title, "PN" => process, _ => null
            };
            if (count > 0 && value is not null) value = string.Concat(value.EnumerateRunes().Take(count).Select(r => r.ToString()));
            result.Append(value); i += length;
        }
        var safe = new string(result.ToString().Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safe)) safe = "Magpie";
        string stem = safe.Split('.')[0];
        if (stem.Equals("CON",StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN",StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX",StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL",StringComparison.OrdinalIgnoreCase) ||
            (stem.Length==4 && (stem.StartsWith("COM",StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT",StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9')) safe="_"+safe;
        // Leave room for the extension and collision suffix on filesystems with a 255-byte component limit.
        var bounded = new StringBuilder(); int bytes=0;
        foreach(var rune in safe.EnumerateRunes()) { if(bytes+rune.Utf8SequenceLength>220)break;bounded.Append(rune.ToString());bytes+=rune.Utf8SequenceLength; }
        string filename=bounded.ToString().TrimEnd(' ','.');
        return (filename.Length==0 ? "Magpie" : filename) + ".png";
    }
}

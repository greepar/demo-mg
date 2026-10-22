namespace Magpie.Desktop.Core;

internal sealed class ApplicationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string ExecutablePath { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public RuntimeSettings Settings { get; set; } = new();
    public void Validate()
    {
        if(!Guid.TryParseExact(Id,"N",out _) || string.IsNullOrWhiteSpace(Name) || Name.Length>128 || Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(ExecutablePath) || ExecutablePath.Length>4096 || !Path.IsPathFullyQualified(ExecutablePath) || ExecutablePath.Contains('\0') ||
            Arguments is null || Arguments.Count>128 || Arguments.Any(a=>a is null || a.Length>4096 || a.Contains('\0')) ||
            Settings is null || Settings.Profiles is null || Settings.Profiles.Count!=0 || Settings.ActiveProfileId is not null)
            throw new InvalidDataException("Invalid application profile.");
        Settings.Validate();
    }
    public System.Diagnostics.ProcessStartInfo CreateLaunchInfo()
    {
        Validate();
        var info=new System.Diagnostics.ProcessStartInfo { FileName=ExecutablePath,UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(ExecutablePath)! };
        foreach(string argument in Arguments)info.ArgumentList.Add(argument);
        return info;
    }
    public bool MatchesExecutable(string path) => string.Equals(Path.GetFullPath(path),Path.GetFullPath(ExecutablePath),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

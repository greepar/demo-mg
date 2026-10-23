namespace Magpie.Desktop.Core;

internal sealed class ApplicationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public System.Text.Json.JsonElement? OriginalProfile { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool CanLaunch=>!string.IsNullOrEmpty(ExecutablePath);
    public string ExecutablePath { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public RuntimeSettings Settings { get; set; } = new();
    public void Validate()
    {
        bool validPath=ExecutablePath is {} path&&path.Length<=4096&&!path.Contains('\0')&&
            (path.Length==0?OriginalProfile.HasValue:!string.IsNullOrWhiteSpace(path)&&Path.IsPathFullyQualified(path));
        if(!Guid.TryParseExact(Id,"N",out _) || string.IsNullOrWhiteSpace(Name) || Name.Length>128 || Name.Any(char.IsControl) ||
            !validPath ||
            Arguments is null || Arguments.Count>128 || Arguments.Any(a=>a is null || a.Length>4096 || a.Contains('\0')) ||
            Settings is null || Settings.Profiles is null || Settings.Profiles.Count!=0 || Settings.ActiveProfileId is not null)
            throw new InvalidDataException("Invalid application profile.");
        if(OriginalProfile is {} source)
        {
            OriginalConfiguration.ValidatePreserved(source);
            if(!source.TryGetProperty("pathRule",out var sourcePath)||sourcePath.ValueKind!=System.Text.Json.JsonValueKind.String||string.IsNullOrWhiteSpace(sourcePath.GetString()))
                throw new InvalidDataException("An imported profile requires its preserved application identity.");
        }
        Settings.Validate();
    }
    public System.Diagnostics.ProcessStartInfo CreateLaunchInfo()
    {
        Validate();
        if(!CanLaunch)throw new InvalidOperationException("Select and review a local executable and launch arguments for this imported Windows profile first.");
        var info=new System.Diagnostics.ProcessStartInfo { FileName=ExecutablePath,UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(ExecutablePath)! };
        foreach(string argument in Arguments)info.ArgumentList.Add(argument);
        return info;
    }
    public bool MatchesExecutable(string path) => CanLaunch && string.Equals(Path.GetFullPath(path),Path.GetFullPath(ExecutablePath),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

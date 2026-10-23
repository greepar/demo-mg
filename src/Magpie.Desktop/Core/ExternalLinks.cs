namespace Magpie.Desktop.Core;

internal static class ExternalLinks
{
    internal static Func<Uri,Task<bool>>? PreviewLauncher { get; set; }
    internal static Uri Resolve(string destination)=>new(destination switch
    {
        "BugReport"=>"https://github.com/Blinue/Magpie/issues/new?assignees=&labels=bug&template=01_bug.yaml",
        "FeatureRequest"=>"https://github.com/Blinue/Magpie/issues/new?assignees=&labels=enhancement&template=03_request.yaml",
        "Discussion"=>"https://github.com/Blinue/Magpie/discussions",
        "Repository"=>"https://github.com/Blinue/Magpie",
        "FAQ"=>"https://github.com/Blinue/Magpie/wiki/FAQ",
        "Contribution"=>"https://github.com/Blinue/Magpie/blob/main/CONTRIBUTING.md",
        "License"=>"https://github.com/Blinue/Magpie/blob/main/LICENSE",
        "TouchSupport"=>Localization.CurrentLanguage.Equals("zh-Hans",StringComparison.OrdinalIgnoreCase)
            ? "https://github.com/Blinue/Magpie/blob/dev/docs/%E5%85%B3%E4%BA%8E%E8%A7%A6%E6%8E%A7%E6%94%AF%E6%8C%81.md"
            : "https://github.com/Blinue/Magpie/blob/dev/docs/About%20touch%20support.md",
        _=>throw new InvalidDataException("Unknown application link.")
    },UriKind.Absolute);
}

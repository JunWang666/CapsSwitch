namespace CapsSwitch.App.InputSources;

public sealed class SwitchConfig
{
    public string? PreferredChineseId { get; set; }
    public string? PreferredEnglishId { get; set; }
    public List<string> OrderedSourceIds { get; set; } = [];
}

public interface ISwitchStrategy
{
    InputSourceInfo? GetTarget(
        InputSourceInfo? current,
        int sequenceIndex,
        IReadOnlyList<InputSourceInfo> sources,
        SwitchConfig config);
}

namespace CapsSwitch.App.InputSources;

/// <summary>
/// 首选中英对 + 完整顺序循环策略。
/// sequenceIndex=0 在首选中/英之间互切(当前是其他源时回默认首选中文);
/// sequenceIndex≥1 沿 OrderedSourceIds 从上次切换目标之后继续循环。
/// 配置里的 Id 不在 sources 中时安全跳过/回退,不抛异常。
/// </summary>
public sealed class PreferredPairCycleStrategy : ISwitchStrategy
{
    private string? _lastTargetId;

    public InputSourceInfo? GetTarget(
        InputSourceInfo? current,
        int sequenceIndex,
        IReadOnlyList<InputSourceInfo> sources,
        SwitchConfig config)
    {
        if (sources.Count == 0)
            return null;

        if (sequenceIndex <= 0)
        {
            var target = ResolvePairTarget(current, sources, config);
            _lastTargetId = target?.Id;
            return target;
        }

        // 沿 OrderedSourceIds 从上一次目标(或当前源)之后继续循环
        string? anchorId = _lastTargetId ?? current?.Id;
        int pos = IndexOfId(config.OrderedSourceIds, anchorId);
        if (pos < 0 && anchorId is not null)
        {
            // anchor 不在配置顺序里(可能是默认源),改从默认首选起算
            pos = IndexOfId(config.OrderedSourceIds, config.PreferredChineseId);
        }
        for (int i = 1; i <= config.OrderedSourceIds.Count; i++)
        {
            int idx = (pos + i) % config.OrderedSourceIds.Count;
            if (idx < 0) idx = 0;
            var candidate = FindById(sources, config.OrderedSourceIds[idx]);
            if (candidate is not null)
            {
                _lastTargetId = candidate.Id;
                return candidate;
            }
        }

        // 配置列表全部失效 → 回退到第一个可用源
        var fallback = sources[0];
        _lastTargetId = fallback.Id;
        return fallback;
    }

    private InputSourceInfo? ResolvePairTarget(
        InputSourceInfo? current, IReadOnlyList<InputSourceInfo> sources, SwitchConfig config)
    {
        var chinese = FindById(sources, config.PreferredChineseId);
        var english = FindById(sources, config.PreferredEnglishId);

        if (current is not null)
        {
            if (current.Id == config.PreferredChineseId)
                return english ?? NextExistingAfter(config, sources, current.Id) ?? FirstOtherThan(sources, current.Id);
            if (current.Id == config.PreferredEnglishId)
                return chinese ?? NextExistingAfter(config, sources, current.Id) ?? FirstOtherThan(sources, current.Id);
        }

        // 当前为其他/未知 → 默认首选中文;中文不可用则英文;再退到顺序中第一个可用源
        return chinese ?? english
            ?? FindById(sources, config.OrderedSourceIds.FirstOrDefault())
            ?? sources[0];
    }

    /// <summary>沿配置顺序从 anchor 之后找第一个仍存在的源(跳过已卸载)。</summary>
    private static InputSourceInfo? NextExistingAfter(SwitchConfig config, IReadOnlyList<InputSourceInfo> sources, string anchorId)
    {
        int pos = IndexOfId(config.OrderedSourceIds, anchorId);
        if (pos < 0) return null;
        for (int i = 1; i <= config.OrderedSourceIds.Count; i++)
        {
            int idx = (pos + i) % config.OrderedSourceIds.Count;
            var candidate = FindById(sources, config.OrderedSourceIds[idx]);
            if (candidate is not null)
                return candidate;
        }
        return null;
    }

    private static InputSourceInfo? FirstOtherThan(IReadOnlyList<InputSourceInfo> sources, string id)
    {
        foreach (var s in sources)
            if (s.Id != id) return s;
        return null;
    }

    private static InputSourceInfo? FindById(IReadOnlyList<InputSourceInfo> sources, string? id)
    {
        if (id is null) return null;
        for (int i = 0; i < sources.Count; i++)
            if (sources[i].Id == id) return sources[i];
        return null;
    }

    private static int IndexOfId(List<string> orderedIds, string? id)
    {
        if (id is null) return -1;
        for (int i = 0; i < orderedIds.Count; i++)
            if (orderedIds[i] == id) return i;
        return -1;
    }
}

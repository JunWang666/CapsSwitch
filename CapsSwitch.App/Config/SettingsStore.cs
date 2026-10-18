using System.IO;
using System.Text.Json;
using CapsSwitch.App.InputSources;

namespace CapsSwitch.App.Config;

/// <summary>
/// %LocalAppData%\CapsSwitch\settings.json 的读写。
/// 首次运行按检测到的输入源写默认值;JSON 损坏/字段缺失时用默认值重建并备份坏文件为 .bad。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Action<string> _log;
    private readonly string _directory;
    private readonly string _filePath;

    public SettingsStore(Action<string> log)
    {
        _log = log;
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapsSwitch");
        _filePath = Path.Combine(_directory, "settings.json");
    }

    public string FilePath => _filePath;

    /// <summary>本次 Load 是否走了"文件缺失/损坏 → 重建默认"路径(首次运行气球提示用)。</summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>加载配置;文件缺失或损坏时按 sources 重建默认配置。</summary>
    public AppSettings Load(IReadOnlyList<InputSourceInfo> sources)
    {
        IsFirstRun = false;
        if (File.Exists(_filePath))
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    if (NormalizeInPlace(settings, sources))
                    {
                        // 归一化有变化(如污染 Id 修复)→ 立即写盘,避免悬空 Id 反复出现
                        Save(settings);
                        _log("[Settings] 归一化结果已写盘");
                    }
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _log($"[Settings] settings.json 损坏({ex.GetType().Name}: {ex.Message}),备份为 .bad 并重建");
                TryBackupBadFile();
            }
        }
        IsFirstRun = true;
        return CreateDefaults(sources);
    }

    /// <summary>剔除已卸载的源并回退,缺失字段填默认值;配置 Id 经 HKL 等价归一化(防冷启动 TIP 丢失污染)。</summary>
    private void Sanitize(AppSettings settings, IReadOnlyList<InputSourceInfo> sources)
    {
        if (settings.LongPressThresholdMs <= 0) settings.LongPressThresholdMs = 400;
        if (settings.SequenceGapMs <= 0) settings.SequenceGapMs = 300;
        if (settings.HudDurationMs <= 0) settings.HudDurationMs = 1000;

        var alive = new HashSet<string>(sources.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        // HKL 等价归一化:悬空 "hkl:XXXXXXXX" 若命中某源(TIP 优先),换成该源真实 Id
        string? NormalizeId(string? id)
        {
            if (id is null)
                return null;
            if (alive.Contains(id))
                return id;
            var normalized = SourceIdResolver.Normalize(id, sources);
            if (normalized is not null && !string.Equals(normalized, id, StringComparison.OrdinalIgnoreCase))
                _log($"[Settings] 配置 Id {id} 已归一化为 {normalized}");
            return normalized ?? id; // 归一化不了则保留原值,后续按"不存在"处理
        }

        settings.OrderedSourceIds = settings.OrderedSourceIds
            .Select(NormalizeId)
            .Where(id => id is not null && alive.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        settings.PreferredChineseId = NormalizeId(settings.PreferredChineseId);
        settings.PreferredEnglishId = NormalizeId(settings.PreferredEnglishId);

        if (settings.PreferredChineseId is not null && !alive.Contains(settings.PreferredChineseId))
            settings.PreferredChineseId = null;
        if (settings.PreferredEnglishId is not null && !alive.Contains(settings.PreferredEnglishId))
            settings.PreferredEnglishId = null;

        if (settings.OrderedSourceIds.Count == 0 && sources.Count > 0)
        {
            // 配置里的源全被卸载 → 用当前源重建顺序
            _log("[Settings] 配置中的输入源已全部不可用,按当前源重建顺序");
            settings.OrderedSourceIds = sources.Select(s => s.Id).ToList();
        }

        // 首选对被剔除或缺失时按当前源重新检测回退
        settings.PreferredChineseId ??= DetectChinese(sources)?.Id;
        settings.PreferredEnglishId ??= DetectEnglish(sources)?.Id;
        foreach (var id in new[] { settings.PreferredChineseId, settings.PreferredEnglishId })
        {
            if (id is not null && !settings.OrderedSourceIds.Contains(id))
                settings.OrderedSourceIds.Insert(0, id);
        }
    }

    /// <summary>
    /// 对现有配置重跑归一化(冷启动 TIP 恢复后由 AppHost 调用)。
    /// 归一化结果与当前不同才返回 true(调用方据此写盘并热生效),相同则不动盘。
    /// </summary>
    public bool NormalizeInPlace(AppSettings settings, IReadOnlyList<InputSourceInfo> sources)
    {
        var before = (settings.PreferredChineseId, settings.PreferredEnglishId,
            Joined: string.Join("|", settings.OrderedSourceIds));
        Sanitize(settings, sources);
        var after = (settings.PreferredChineseId, settings.PreferredEnglishId,
            Joined: string.Join("|", settings.OrderedSourceIds));
        bool changed = before != after;
        if (changed)
            _log("[Settings] 配置归一化有变化,将写盘并热生效");
        return changed;
    }

    private static InputSourceInfo? DetectChinese(IReadOnlyList<InputSourceInfo> sources) =>
        sources.FirstOrDefault(s => s.ProfileGuid == InputSourceService.MicrosoftPinyinProfileGuid)
        ?? sources.FirstOrDefault(s => s.ShortLabel == "中");

    private static InputSourceInfo? DetectEnglish(IReadOnlyList<InputSourceInfo> sources) =>
        sources.FirstOrDefault(s => s.Id == "hkl:00000409")
        ?? sources.FirstOrDefault(s => s.ShortLabel == "EN");

    public AppSettings CreateDefaults(IReadOnlyList<InputSourceInfo> sources)
    {
        var settings = new AppSettings();
        ApplyDetectedDefaults(settings, sources);
        Save(settings);
        _log($"[Settings] 已写入默认配置 {_filePath}");
        return settings;
    }

    private static void ApplyDetectedDefaults(AppSettings settings, IReadOnlyList<InputSourceInfo> sources)
    {
        var chinese = DetectChinese(sources);
        var english = DetectEnglish(sources);

        settings.PreferredChineseId = chinese?.Id;
        settings.PreferredEnglishId = english?.Id;

        var ordered = new List<string>();
        if (chinese is not null) ordered.Add(chinese.Id);
        if (english is not null && english.Id != chinese?.Id) ordered.Add(english.Id);
        foreach (var s in sources)
            if (!ordered.Contains(s.Id))
                ordered.Add(s.Id);
        settings.OrderedSourceIds = ordered;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private void TryBackupBadFile()
    {
        try
        {
            string bad = _filePath + ".bad";
            if (File.Exists(bad))
                File.Delete(bad);
            File.Move(_filePath, bad);
        }
        catch (Exception ex)
        {
            _log($"[Settings] 备份坏文件失败(忽略): {ex.Message}");
        }
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>版本历史条目，支持双语显示切换。</summary>
public partial class VersionHistoryItem : ObservableObject
{
    private string? _translatedNotes;

    public VersionHistoryItem(string version, string? releasedAt, string originalNotes)
    {
        Version = version;
        ReleasedAt = releasedAt;
        OriginalNotes = originalNotes;
        _notes = originalNotes;
    }

    public string Version { get; }

    public string? ReleasedAt { get; }

    public string OriginalNotes { get; }

    [ObservableProperty]
    private string _notes;

    public bool HasTranslatedNotes => !string.IsNullOrWhiteSpace(_translatedNotes);

    public void ApplyTranslated(string? translated = null)
    {
        if (translated != null)
        {
            _translatedNotes = translated;
        }

        if (!string.IsNullOrWhiteSpace(_translatedNotes))
        {
            Notes = _translatedNotes;
        }
    }

    public void RestoreOriginal()
    {
        Notes = OriginalNotes;
    }
}

/// <summary>详情对话框视图模型：卡片摘要 + 懒加载的版本历史 + 在线双语翻译。</summary>
public partial class ModDetailViewModel : ObservableObject
{
    private readonly ITuforumsClient? _tuf;
    private readonly ITranslationService? _translator;

    private string? _translatedDescription;

    public ModDetailViewModel(
        ModCardViewModel card,
        ITuforumsClient? tuf,
        ITranslationService? translator = null)
    {
        Card = card;
        _tuf = tuf;
        _translator = translator;
    }

    public ModCardViewModel Card { get; }

    public string Title => Card.Title;

    public string Author => string.IsNullOrEmpty(Card.Author) ? "未知作者" : Card.Author;

    public string Status => Card.StatusBadge;

    public string VersionLine => string.IsNullOrEmpty(Card.VersionText) ? "版本未知" : Card.VersionText;

    public string Description => Card.Remote?.Description is { Length: > 0 } md
        ? md
        : (Card.Local?.Info is not null ? "本地 Mod，未提供描述。" : string.Empty);

    public string DisplayDescription =>
        IsDescriptionTranslated && !string.IsNullOrWhiteSpace(_translatedDescription)
            ? _translatedDescription
            : Description;

    public bool CanTranslateDescription =>
        _translator is not null &&
        !string.IsNullOrWhiteSpace(Description) &&
        Description != "本地 Mod，未提供描述。";

    public string TranslateDescriptionButtonText =>
        IsDescriptionTranslated ? UiStrings.ActionShowOriginal : UiStrings.ActionTranslate;

    public string RequirementsText
    {
        get
        {
            var reqs = Card.Local?.Info?.RequirementIds();
            return reqs is { Count: > 0 } ? string.Join(", ", reqs) : "无";
        }
    }

    public string ManagerVersionText => Card.Local?.Info?.ManagerVersion ?? "—";

    public string EntryMethodText => Card.Local?.Info?.EntryMethod ?? "—";

    public string TufPageUrl => Card.TufPageUrl;

    public ObservableCollection<VersionHistoryItem> Versions { get; } = new();

    public bool CanTranslateVersions =>
        _translator is not null &&
        Versions.Count > 0 &&
        Versions.Any(v => !string.IsNullOrWhiteSpace(v.OriginalNotes));

    public string TranslateVersionsButtonText =>
        IsVersionsTranslated ? UiStrings.ActionShowOriginal : UiStrings.ActionTranslateChangelog;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayDescription))]
    [NotifyPropertyChangedFor(nameof(TranslateDescriptionButtonText))]
    private bool _isDescriptionTranslated;

    [ObservableProperty]
    private bool _isTranslatingDescription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranslateVersionsButtonText))]
    private bool _isVersionsTranslated;

    [ObservableProperty]
    private bool _isTranslatingVersions;

    [ObservableProperty]
    private bool _hasTranslationError;

    [ObservableProperty]
    private string _translationErrorMessage = string.Empty;

    [RelayCommand]
    public async Task LoadVersionsAsync()
    {
        if (Card.Remote is null || _tuf is null || Versions.Count > 0)
        {
            return;
        }

        IsLoadingVersions = true;
        try
        {
            var mod = await _tuf.GetModAsync(Card.Remote.Slug).ConfigureAwait(true);
            if (mod?.Versions is { Count: > 0 } versions)
            {
                foreach (var v in versions)
                {
                    Versions.Add(new VersionHistoryItem(
                        v.Version,
                        v.ReleasedAt?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        TextFormatting.Summarize(v.Notes, 300)));
                }
            }

            OnPropertyChanged(nameof(CanTranslateVersions));
        }
        catch (Exception)
        {
            // 详情获取失败不影响主流程
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    public async Task ToggleTranslateDescriptionAsync()
    {
        if (!CanTranslateDescription || _translator is null)
        {
            return;
        }

        if (IsDescriptionTranslated)
        {
            IsDescriptionTranslated = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_translatedDescription))
        {
            IsDescriptionTranslated = true;
            return;
        }

        IsTranslatingDescription = true;
        HasTranslationError = false;
        try
        {
            var res = await _translator.TranslateAsync(Description).ConfigureAwait(true);
            if (res.Success && !string.IsNullOrWhiteSpace(res.TranslatedText))
            {
                _translatedDescription = res.TranslatedText;
                IsDescriptionTranslated = true;
            }
            else
            {
                HasTranslationError = true;
                TranslationErrorMessage = res.ErrorMessage ?? UiStrings.TranslationFailed;
            }
        }
        catch (Exception ex)
        {
            HasTranslationError = true;
            TranslationErrorMessage = ex.Message;
        }
        finally
        {
            IsTranslatingDescription = false;
        }
    }

    [RelayCommand]
    public async Task ToggleTranslateVersionsAsync()
    {
        if (!CanTranslateVersions || _translator is null)
        {
            return;
        }

        if (IsVersionsTranslated)
        {
            foreach (var item in Versions)
            {
                item.RestoreOriginal();
            }

            IsVersionsTranslated = false;
            return;
        }

        if (Versions.All(v => string.IsNullOrWhiteSpace(v.OriginalNotes) || v.HasTranslatedNotes))
        {
            foreach (var item in Versions)
            {
                item.ApplyTranslated();
            }

            IsVersionsTranslated = true;
            return;
        }

        IsTranslatingVersions = true;
        HasTranslationError = false;
        try
        {
            var tasks = Versions
                .Where(v => !string.IsNullOrWhiteSpace(v.OriginalNotes) && !v.HasTranslatedNotes)
                .Select(async item =>
                {
                    try
                    {
                        var res = await _translator.TranslateAsync(item.OriginalNotes).ConfigureAwait(true);
                        if (res.Success && !string.IsNullOrWhiteSpace(res.TranslatedText))
                        {
                            item.ApplyTranslated(res.TranslatedText);
                        }
                    }
                    catch (Exception)
                    {
                        // 单条失败保持原样
                    }
                });

            await Task.WhenAll(tasks).ConfigureAwait(true);

            foreach (var item in Versions)
            {
                item.ApplyTranslated();
            }

            IsVersionsTranslated = true;
        }
        catch (Exception ex)
        {
            HasTranslationError = true;
            TranslationErrorMessage = ex.Message;
        }
        finally
        {
            IsTranslatingVersions = false;
        }
    }
}

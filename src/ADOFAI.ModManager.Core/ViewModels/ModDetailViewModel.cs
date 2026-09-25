using System.Collections.ObjectModel;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>版本历史条目。</summary>
public sealed record VersionHistoryItem(string Version, string? ReleasedAt, string Notes);

/// <summary>详情对话框视图模型：卡片摘要 + 懒加载的版本历史。</summary>
public partial class ModDetailViewModel : ObservableObject
{
    private readonly ITuforumsClient? _tuf;

    public ModDetailViewModel(ModCardViewModel card, ITuforumsClient? tuf)
    {
        Card = card;
        _tuf = tuf;
    }

    public ModCardViewModel Card { get; }

    public string Title => Card.Title;

    public string Author => string.IsNullOrEmpty(Card.Author) ? "未知作者" : Card.Author;

    public string Status => Card.StatusBadge;

    public string VersionLine => string.IsNullOrEmpty(Card.VersionText) ? "版本未知" : Card.VersionText;

    public string Description => Card.Remote?.Description is { Length: > 0 } md
        ? md
        : (Card.Local?.Info is not null ? "本地 Mod，未提供描述。" : string.Empty);

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

    [ObservableProperty]
    private bool _isLoadingVersions;

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
}

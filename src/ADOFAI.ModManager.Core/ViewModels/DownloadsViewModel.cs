using System.Collections.ObjectModel;
using ADOFAI.ModManager.Core.Models.App;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>
/// 下载/操作动态页：展示正在进行下载与安装的卡片（与浏览页共享同一批卡片实例），
/// 以及历史安装记录。
/// </summary>
public partial class DownloadsViewModel : ObservableObject
{
    private readonly ModsPageViewModel _mods;

    public DownloadsViewModel(ModsPageViewModel mods)
    {
        _mods = mods;
        foreach (var card in _mods.AllCardsInternal)
        {
            card.StateChanged += OnCardStateChanged;
        }

        RefreshActive();
    }

    public ObservableCollection<ModCardViewModel> ActiveItems { get; } = new();

    public ObservableCollection<InstallRecord> History => _mods.History;

    private void OnCardStateChanged(object? sender, EventArgs e)
    {
        if (sender is ModCardViewModel)
        {
            RefreshActive();
        }
    }

    /// <summary>重建“进行中”列表（卡片数有限，直接过滤即可）。</summary>
    public void RefreshActive()
    {
        var active = _mods.AllCardsInternal.Where(c => c.IsBusy).ToList();

        // 订阅新卡片的 StateChanged
        foreach (var card in active)
        {
            card.StateChanged -= OnCardStateChanged;
            card.StateChanged += OnCardStateChanged;
        }

        ActiveItems.Clear();
        foreach (var card in active)
        {
            ActiveItems.Add(card);
        }

        HasActive = active.Count > 0;
    }

    [ObservableProperty]
    private bool _hasActive;
}

using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class ModDetailViewModelTests
{
    private static ModCardViewModel CreateCard(string name, string description, string slug = "test-mod")
    {
        var settings = TestHost.CreateSettings();
        var pageVm = new ModsPageViewModel(
            null!, null!, null!, null!, null!, settings, null!, null!,
            NullLogger<ModsPageViewModel>.Instance, null);

        var remote = new TufMod
        {
            Slug = slug,
            Name = name,
            Description = description,
        };

        return new ModCardViewModel(pageVm, remote, null);
    }

    [Fact]
    public async Task DescriptionTranslation_ToggleFlow_WorksAndCaches()
    {
        var card = CreateCard("TUFHelper", "This is an awesome mod for ADOFAI.");
        var translator = new FakeTranslator(text => $"[译]{text}");
        var detailVm = new ModDetailViewModel(card, null, translator);

        Assert.True(detailVm.CanTranslateDescription);
        Assert.False(detailVm.IsDescriptionTranslated);
        Assert.Equal("翻译", detailVm.TranslateDescriptionButtonText);
        Assert.Equal("This is an awesome mod for ADOFAI.", detailVm.DisplayDescription);

        // 1. 首次翻译
        await detailVm.ToggleTranslateDescriptionCommand.ExecuteAsync(null);

        Assert.True(detailVm.IsDescriptionTranslated);
        Assert.Equal("查看原文", detailVm.TranslateDescriptionButtonText);
        Assert.Equal("[译]This is an awesome mod for ADOFAI.", detailVm.DisplayDescription);
        Assert.Equal(1, translator.CallCount);

        // 2. 切回原文
        await detailVm.ToggleTranslateDescriptionCommand.ExecuteAsync(null);

        Assert.False(detailVm.IsDescriptionTranslated);
        Assert.Equal("翻译", detailVm.TranslateDescriptionButtonText);
        Assert.Equal("This is an awesome mod for ADOFAI.", detailVm.DisplayDescription);
        Assert.Equal(1, translator.CallCount);

        // 3. 再次切为译文（复用内存中的译文，不重复调接口）
        await detailVm.ToggleTranslateDescriptionCommand.ExecuteAsync(null);

        Assert.True(detailVm.IsDescriptionTranslated);
        Assert.Equal("查看原文", detailVm.TranslateDescriptionButtonText);
        Assert.Equal("[译]This is an awesome mod for ADOFAI.", detailVm.DisplayDescription);
        Assert.Equal(1, translator.CallCount);
    }

    [Fact]
    public async Task VersionHistoryTranslation_ToggleFlow_TranslatesAndRestores()
    {
        var card = CreateCard("TUFHelper", "Description");
        var tuf = new FakeTufWithVersions(new[]
        {
            new TufVersion { Version = "1.1.0", Notes = "Fixed memory leak" },
            new TufVersion { Version = "1.0.0", Notes = "Initial release" },
        });
        var translator = new FakeTranslator(text => $"[译]{text}");
        var detailVm = new ModDetailViewModel(card, tuf, translator);

        await detailVm.LoadVersionsCommand.ExecuteAsync(null);
        Assert.Equal(2, detailVm.Versions.Count);
        Assert.True(detailVm.CanTranslateVersions);
        Assert.False(detailVm.IsVersionsTranslated);
        Assert.Equal("翻译更新日志", detailVm.TranslateVersionsButtonText);

        // 1. 翻译更新日志
        await detailVm.ToggleTranslateVersionsCommand.ExecuteAsync(null);

        Assert.True(detailVm.IsVersionsTranslated);
        Assert.Equal("查看原文", detailVm.TranslateVersionsButtonText);
        Assert.Equal("[译]Fixed memory leak", detailVm.Versions[0].Notes);
        Assert.Equal("[译]Initial release", detailVm.Versions[1].Notes);

        // 2. 切回原文
        await detailVm.ToggleTranslateVersionsCommand.ExecuteAsync(null);

        Assert.False(detailVm.IsVersionsTranslated);
        Assert.Equal("翻译更新日志", detailVm.TranslateVersionsButtonText);
        Assert.Equal("Fixed memory leak", detailVm.Versions[0].Notes);
        Assert.Equal("Initial release", detailVm.Versions[1].Notes);
    }

    [Fact]
    public async Task TranslationError_SetsErrorMessage_AndDoesNotCrash()
    {
        var card = CreateCard("TUFHelper", "Description");
        var translator = new FakeTranslator(_ => throw new InvalidOperationException("网络连接异常"));
        var detailVm = new ModDetailViewModel(card, null, translator);

        await detailVm.ToggleTranslateDescriptionCommand.ExecuteAsync(null);

        Assert.False(detailVm.IsDescriptionTranslated);
        Assert.True(detailVm.HasTranslationError);
        Assert.Equal("网络连接异常", detailVm.TranslationErrorMessage);
        Assert.Equal("Description", detailVm.DisplayDescription);
    }

    private sealed class FakeTranslator : ITranslationService
    {
        private readonly Func<string, string> _translateFunc;

        public FakeTranslator(Func<string, string> translateFunc) => _translateFunc = translateFunc;

        public int CallCount { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string text,
            string targetLanguage = "zh-CN",
            string sourceLanguage = "auto",
            CancellationToken ct = default)
        {
            CallCount++;
            var translated = _translateFunc(text);
            return Task.FromResult(new TranslationResult(translated, "en", "Fake", true));
        }
    }

    private sealed class FakeTufWithVersions : ITuforumsClient
    {
        private readonly IReadOnlyList<TufVersion> _versions;

        public FakeTufWithVersions(IReadOnlyList<TufVersion> versions) => _versions = versions;

        public Task<CatalogSnapshot> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(Array.Empty<TufMod>(), DateTimeOffset.UtcNow, false));

        public Task<TufMod?> GetModAsync(string slug, CancellationToken ct = default)
        {
            var mod = new TufMod
            {
                Slug = slug,
                Name = slug,
                Versions = new List<TufVersion>(_versions),
            };
            return Task.FromResult<TufMod?>(mod);
        }
    }
}

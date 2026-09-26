using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using ADOFAI.ModManager.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ADOFAI.ModManager;

/// <summary>应用入口与依赖注入组合根。</summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static Window? MainAppWindow { get; private set; }

    private readonly DispatcherQueue _dispatcherQueue;

    public App()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainAppWindow = window;
        window.Activate();

        Services.GetRequiredService<UiServiceHost>().Attach(window);
        Services.GetRequiredService<NavigationService>().Attach(window);

        // 启动后初始化主 ViewModel（需要窗口已就绪以便弹窗）
        _ = _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await Services.GetRequiredService<ModsPageViewModel>().InitializeAsync();
            }
            catch (Exception ex)
            {
                Services.GetRequiredService<ILogger<App>>().LogError(ex, "初始化失败");
            }
        });
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // ── 基础设施 ──
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider());
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDiskCacheService, DiskCacheService>();

        // ── HttpClientFactory ──
        services.AddHttpClient("tuforums", client =>
        {
            client.BaseAddress = new Uri(TuforumsClient.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ADOFAI-ModManager/1.0 (+https://github.com/ADOFAI-MODUI)");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("github-api", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ADOFAI-ModManager/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("downloads", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ADOFAI-ModManager/1.0");
            // 下载由 CancellationToken 控制，客户端不设整体超时
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // ── 领域服务 ──
        services.AddSingleton<ISteamLocatorService, SteamLocatorService>();
        services.AddSingleton<IGameProcessGuard, GameProcessGuard>();
        services.AddSingleton<ILocalModService, LocalModService>();
        services.AddSingleton<IModInstallerService, ModInstallerService>();
        services.AddSingleton<IModDownloadService, ModDownloadService>();
        services.AddSingleton<IDownloadUrlResolver, DownloadUrlResolver>();
        services.AddSingleton<ITuforumsClient, TuforumsClient>();
        services.AddSingleton<IModLifecycleService, ModLifecycleService>();

        // ── UI 服务抽象实现 ──
        services.AddSingleton<IDispatcherService, WinUiDispatcherService>();
        services.AddSingleton<UiServiceHost>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<UiServiceHost>());
        services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<UiServiceHost>());
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

        // ── ViewModel ──
        services.AddSingleton<ModsPageViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}

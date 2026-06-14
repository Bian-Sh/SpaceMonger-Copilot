using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Scanning;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Cleanup;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the application-wide service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddHttpClient("Anthropic", client =>
        {
            client.BaseAddress = AnthropicOptions.GetBaseUri();
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        // US1 services
        services.AddSingleton<FileScanner>();
        services.AddSingleton<IFileScanner, IncrementalFileScanner>();
        services.AddSingleton<ITreemapLayoutEngine, SquarifiedTreemapLayout>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<TreemapViewModel>();

        // US3 services
        services.AddSingleton<ICleanupService, CleanupService>();

        // US2 services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILlmClient, AnthropicClient>();
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RecommendationsViewModel>();

        // US4 chat services
        services.AddSingleton<IChatService, ChatService>();
        services.AddTransient<ChatViewModel>();

        Services = services.BuildServiceProvider();

        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        var treemapViewModel = Services.GetRequiredService<TreemapViewModel>();
        var recommendationsViewModel = Services.GetRequiredService<RecommendationsViewModel>();
        var settingsViewModel = Services.GetRequiredService<SettingsViewModel>();
        var chatViewModel = Services.GetRequiredService<ChatViewModel>();

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        mainWindow.TreemapView.SetViewModel(treemapViewModel);
        mainWindow.SetTreemapViewModel(treemapViewModel);
        mainWindow.SetViewModels(recommendationsViewModel, settingsViewModel);
        mainWindow.SetChatViewModel(chatViewModel);

        // When drilled into a subfolder, Scan targets that folder.
        mainViewModel.GetCurrentViewPath = () =>
        {
            if (treemapViewModel.CurrentRoot is not null
                && treemapViewModel.CurrentRoot != treemapViewModel.ScanRoot)
            {
                return treemapViewModel.CurrentRoot.Path;
            }
            return null;
        };

        mainViewModel.ScanCompleted += session =>
        {
            treemapViewModel.SetRoot(session.RootEntry!, session);
            chatViewModel.SetContext(session, session.RootEntry!);
        };

        mainWindow.Show();
    }
}



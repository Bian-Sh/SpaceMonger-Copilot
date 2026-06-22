using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.FileTree;
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
        CrashDiagnostics.Register(this);

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

        // Agent and chat services
        services.AddSingleton<IFileTreeQueryService, FileTreeQueryService>();
        services.AddSingleton<IAgentTool, FindByNameTool>();
        services.AddSingleton<IAgentTool, FindByPathTool>();
        services.AddSingleton<IAgentTool, ListChildrenTool>();
        services.AddSingleton<IAgentTool, SummarizeSubtreeTool>();
        services.AddSingleton<IAgentTool, FindLargeFilesTool>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddTransient<ChatViewModel>();

        // TreeView services
        services.AddTransient<TreeViewModel>();

        // Update services
        services.AddSingleton<UpdateService>();
        services.AddTransient<UpdateViewModel>();

        Services = services.BuildServiceProvider();

        var settingsService = Services.GetRequiredService<ISettingsService>();
        L.SetLanguage(settingsService.LoadSettings().Language);

        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        var treemapViewModel = Services.GetRequiredService<TreemapViewModel>();
        var recommendationsViewModel = Services.GetRequiredService<RecommendationsViewModel>();
        var settingsViewModel = Services.GetRequiredService<SettingsViewModel>();
        var chatViewModel = Services.GetRequiredService<ChatViewModel>();
        var treeViewModel = Services.GetRequiredService<TreeViewModel>();
        var updateViewModel = Services.GetRequiredService<UpdateViewModel>();

        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        mainWindow.TreemapView.SetViewModel(treemapViewModel);
        mainWindow.SetTreemapViewModel(treemapViewModel);
        mainWindow.SetViewModels(recommendationsViewModel, settingsViewModel);
        mainWindow.SetChatViewModel(chatViewModel);
        mainWindow.SetTreeViewModel(treeViewModel);
        mainWindow.SetUpdateViewModel(updateViewModel);

        mainViewModel.ScanCompleted += session =>
        {
            treemapViewModel.SetRoot(session.RootEntry!, session);
            chatViewModel.SetContext(session, session.RootEntry!);
            treeViewModel.SetRoot(session.RootEntry!, session);
        };

        mainWindow.Show();

        // Support --scan <path> command-line argument for auto-scanning
        if (e.Args.Length >= 2 && e.Args[0] == "--scan")
        {
            var scanPath = e.Args[1];
            mainViewModel.SelectedPath = scanPath;
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => mainViewModel.ScanCommand.Execute(null));
            });
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => updateViewModel.CheckForUpdateCommand.Execute(null));
        });
    }
}


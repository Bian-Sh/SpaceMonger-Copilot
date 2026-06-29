using System.Text.Json;
using NSubstitute;
using FluentAssertions;
using SpaceMonger.App.Services.Copilot;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.App.Tests;

public class ChatViewModelProposalTests
{
    [Fact]
    public void ApplyProposalIfAny_CreatesInteractionCard()
    {
        var message = new ChatMessage();
        var proposal = JsonSerializer.SerializeToElement(new
        {
            action = new
            {
                kind = nameof(AiActionKind.StartScan),
                path = @"D:\Downloads",
                scope_label = @"D:\Downloads",
                will_overwrite_existing_data = false
            },
            card = new
            {
                title = "扫描这个路径",
                description = "需要先扫描这个路径，才能继续分析。",
                impact = "会创建新的扫描结果。",
                confirm_text = "开始扫描",
                cancel_text = "取消"
            }
        });

        ChatViewModel.ApplyProposalIfAny(message, proposal);

        message.InteractionCard.Should().NotBeNull();
        message.InteractionCard!.Title.Should().Be("扫描这个路径");
        message.InteractionCard.Action.Kind.Should().Be(AiActionKind.StartScan);
        message.InteractionCard.Action.Path.Should().Be(@"D:\Downloads");
    }

    [Fact]
    public async Task SendCommand_ForExplicitClearRequest_ShowsClearConfirmationCard()
    {
        var viewModel = CreateViewModel();
        viewModel.InputText = "clear chat";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages.Should().ContainSingle(message => message.Sender == ChatSender.User);
        var card = viewModel.PendingInteractionCard;
        card.Should().NotBeNull();
        card!.Action.Kind.Should().Be(AiActionKind.ClearConversation);
        card.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConfirmInteraction_ForClearConversation_ClearsMessagesAndHistory()
    {
        var chatService = Substitute.For<IChatService>();
        var viewModel = CreateViewModel(chatService);
        viewModel.InputText = "context is messy";
        await viewModel.SendCommand.ExecuteAsync(null);
        var card = viewModel.PendingInteractionCard!;

        await viewModel.ConfirmInteractionCommand.ExecuteAsync(card);

        viewModel.Messages.Should().BeEmpty();
        chatService.Received(1).ClearHistory();
    }


    [Fact]
    public void InputText_WhenSlash_ShowsCommandSuggestions()
    {
        var viewModel = CreateViewModel();

        viewModel.InputText = "/";

        viewModel.IsSlashCommandMenuOpen.Should().BeTrue();
        viewModel.SlashCommandSuggestions.Select(item => item.Command).Should().Contain(["/new", "/clear"]);
        viewModel.SlashCommandSuggestions.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Description));
    }


    [Fact]
    public void InputText_WhenAt_ShowsSkillMentionSuggestions()
    {
        var viewModel = CreateViewModel();

        viewModel.InputText = "@";

        viewModel.IsSkillMentionMenuOpen.Should().BeTrue();
        viewModel.SkillMentionSuggestions.Select(item => item.Mention).Should().Contain(["@app-guide", "@disk-management", "@unity-project-cleanup"]);
        viewModel.SelectedSkillMentionSuggestion.Should().NotBeNull();
    }

    [Fact]
    public void ConfirmActiveCompletion_ForSkillMention_InsertsMention()
    {
        var viewModel = CreateViewModel();
        viewModel.InputText = "@";

        viewModel.MoveCompletionSelection(1);
        var handled = viewModel.ConfirmActiveCompletion();

        handled.Should().BeTrue();
        viewModel.InputText.Should().Be("@disk-management ");
        viewModel.IsSkillMentionMenuOpen.Should().BeFalse();
    }

    [Fact]
    public void MoveCompletionSelection_ForFilteredSkillMention_NavigatesVisibleMatches()
    {
        var viewModel = CreateViewModel();
        viewModel.InputText = "@unity";

        viewModel.FilteredSkillMentionSuggestions.Should().ContainSingle(item => item.Mention == "@unity-project-cleanup");
        viewModel.MoveCompletionSelection(1);
        viewModel.SelectedSkillMentionSuggestion.Should().NotBeNull();
        viewModel.SelectedSkillMentionSuggestion!.Mention.Should().Be("@unity-project-cleanup");
    }

    [Fact]
    public async Task SendCommand_ForUnityLibraryDiscovery_UsesExecutorProgressSteps()
    {
        var chatService = Substitute.For<IChatService>();
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        actionExecutor.HasExistingRecommendations.Returns(false);
        actionExecutor.ExecuteAsync(Arg.Any<AiActionRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<AiActionProgress>>())
            .Returns(callInfo =>
            {
                var progress = callInfo.ArgAt<IProgress<AiActionProgress>>(2);
                progress.Report(new AiActionProgress("enumerate_drives", "Enumerate ready drives", AiActionProgressStatus.Completed));
                progress.Report(new AiActionProgress("write_unity_recommendations", "Write Unity cleanup recommendations", AiActionProgressStatus.Completed));
                return Task.FromResult(AiActionResult.Ok("ok"));
            });

        var routed = new AiSkillRoutingResult(
            [AiIntent.UnityProjectCleanup],
            [],
            new AiActionRequest(AiActionKind.DiscoverUnityLibraries, ScopeLabel: "Unity Library"));
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.InputText = "clean Unity Library";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.PendingInteractionCard.Should().BeNull();
        viewModel.IsWorkflowProgressVisible.Should().BeFalse();
        viewModel.WorkflowSteps.Should().Contain(step => step.StepId == "write_unity_recommendations" && step.Status == CopilotWorkflowStepStatus.Finished);
        await actionExecutor.Received().ExecuteAsync(
            Arg.Is<AiActionRequest>(request => request.Kind == AiActionKind.DiscoverUnityLibraries),
            Arg.Any<CancellationToken>(),
            Arg.Any<IProgress<AiActionProgress>>());
    }

    [Fact]
    public async Task SendCommand_ForSlashClear_ClearsImmediatelyWithoutChatBubble()
    {
        var chatService = Substitute.For<IChatService>();
        var viewModel = CreateViewModel(chatService);
        viewModel.Messages.Add(new ChatMessage { Text = "old" });
        viewModel.InputText = "/clear";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages.Should().BeEmpty();
        viewModel.IsSlashCommandMenuOpen.Should().BeFalse();
        chatService.Received(1).ClearHistory();
    }



    [Fact]
    public async Task SendCommand_ForSafeScanCleanupIntent_AutoExecutesWithoutConfirmationCard()
    {
        var chatService = Substitute.For<IChatService>();
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        actionExecutor.HasExistingRecommendations.Returns(false);
        actionExecutor.ExecuteAsync(Arg.Any<AiActionRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<AiActionProgress>>())
            .Returns(Task.FromResult(AiActionResult.Ok("ok")));

        var routed = new AiSkillRoutingResult(
            [AiIntent.DiskScan, AiIntent.FolderCleanupAnalysis],
            [],
            new AiActionRequest(AiActionKind.StartScan, Path: @"D:\", ScopeLabel: @"D:\"));
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.SetScanTargetAvailabilityProbe(_ => true);
        viewModel.InputText = "analyze D drive cleanup";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages.Should().HaveCountGreaterThanOrEqualTo(2);
        viewModel.PendingInteractionCard.Should().BeNull();
        viewModel.IsWorkflowProgressVisible.Should().BeFalse();
        viewModel.WorkflowSteps.Should().NotBeEmpty();
        await actionExecutor.Received().ExecuteAsync(
            Arg.Is<AiActionRequest>(request => request.Kind == AiActionKind.StartScan && request.Path == @"D:\"),
            Arg.Any<CancellationToken>(),
            Arg.Any<IProgress<AiActionProgress>>());
        await actionExecutor.Received().ExecuteAsync(
            Arg.Is<AiActionRequest>(request => request.Kind == AiActionKind.AnalyzeCleanup),
            Arg.Any<CancellationToken>(),
            Arg.Any<IProgress<AiActionProgress>>());
    }



    [Fact]
    public async Task SendCommand_ForSafeScanOnlyIntent_AutoExecutesWithoutWorkflowStepPopup()
    {
        var chatService = Substitute.For<IChatService>();
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        actionExecutor.ExecuteAsync(Arg.Any<AiActionRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<AiActionProgress>>())
            .Returns(Task.FromResult(AiActionResult.Ok("ok")));

        var routed = new AiSkillRoutingResult(
            [AiIntent.DiskScan],
            [],
            new AiActionRequest(AiActionKind.StartScan, Path: @"G:", ScopeLabel: @"G:"));
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.SetScanTargetAvailabilityProbe(_ => true);
        viewModel.InputText = "scan G drive";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.PendingInteractionCard.Should().BeNull();
        viewModel.IsWorkflowProgressVisible.Should().BeFalse();
        viewModel.WorkflowSteps.Should().BeEmpty();
        await actionExecutor.Received().ExecuteAsync(
            Arg.Is<AiActionRequest>(request => request.Kind == AiActionKind.StartScan && request.Path == @"G:"),
            Arg.Any<CancellationToken>(),
            Arg.Any<IProgress<AiActionProgress>>());
    }

    [Fact]
    public async Task SendCommand_ForUnavailableScanTarget_AsksModelWithDriveContextWithoutExecutingAction()
    {
        var chatService = Substitute.For<IChatService>();
        chatService.StreamSkillMessageWithThinkingAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<AiSkill>>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var onThinkingToken = callInfo.ArgAt<Action<string>?>(6);
                var onTextToken = callInfo.ArgAt<Action<string>?>(7);
                onThinkingToken?.Invoke("Checking available drives before proposing a scan.\n");
                onTextToken?.Invoke("\u65e0\u6cd5\u626b\u63cf Z:\\\\?\u8be5\u78c1\u76d8\u6216\u6587\u4ef6\u5939\u4e0d\u5b58\u5728\u3001\u672a\u6302\u8f7d\uff0c\u6216\u5f53\u524d\u4e0d\u53ef\u8bbf\u95ee\u3002");
                return Task.FromResult(new ChatResponse("ok", string.Empty, null));
            });
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        var routed = new AiSkillRoutingResult(
            [AiIntent.DiskScan],
            [],
            new AiActionRequest(AiActionKind.StartScan, Path: @"Z:", ScopeLabel: @"Z:"));
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.SetScanTargetAvailabilityProbe(_ => false);
        viewModel.InputText = "scan Z drive";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.PendingInteractionCard.Should().BeNull();
        viewModel.IsWorkflowProgressVisible.Should().BeFalse();
        viewModel.WorkflowSteps.Should().BeEmpty();
        viewModel.Messages.Should().Contain(message =>
            message.Sender == ChatSender.Assistant &&
            !message.IsError &&
            message.Thinking.Contains("Checking available drives") &&
            message.Text.Contains(@"Z:") &&
            message.Text.Contains("\u65e0\u6cd5\u626b\u63cf"));
        await chatService.Received(1).StreamSkillMessageWithThinkingAsync(
            Arg.Is<string>(message =>
                message.Contains("Host disk context JSON") &&
                message.Contains("requested_scan_target") &&
                message.Contains(@"Z:")),
            Arg.Any<IReadOnlyList<AiSkill>>(),
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<CancellationToken>());
        await actionExecutor.DidNotReceive().ExecuteAsync(Arg.Any<AiActionRequest>(), Arg.Any<CancellationToken>());
    }

    private static ChatViewModel CreateViewModel(IChatService? chatService = null, AiSkillRoutingResult? routingResult = null)
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.LoadSettings().Returns(new AppSettings { Language = "zh-CN" });
        settingsService.GetApiKey(Arg.Any<AppSettings>()).Returns("test-key");
        settingsService.EncryptApiKey(Arg.Any<string>()).Returns([]);

        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns([
            new AiSkillCatalogItem("app-guide", "app-guide", "Guide the app"),
            new AiSkillCatalogItem("disk-management", "disk-management", "Manage disk space"),
            new AiSkillCatalogItem("unity-project-cleanup", "unity-project-cleanup", "Clean Unity projects")
        ]);
        router.Route(Arg.Any<string>(), Arg.Any<FileEntry?>(), Arg.Any<FileEntry?>(), Arg.Any<bool>(), Arg.Any<string?>())
            .Returns(routingResult ?? new AiSkillRoutingResult([AiIntent.GeneralChat], [], null));

        return new ChatViewModel(chatService ?? Substitute.For<IChatService>(), settingsService, router);
    }

}

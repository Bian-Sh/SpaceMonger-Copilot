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
                title = "Scan this path",
                description = "Scan this path before deeper analysis.",
                impact = "Creates a new scan result.",
                confirm_text = "Start scan",
                cancel_text = "鍙栨秷"
            }
        });

        ChatViewModel.ApplyProposalIfAny(message, proposal);

        message.InteractionCard.Should().NotBeNull();
        message.InteractionCard!.Title.Should().Be("Scan this path");
        message.InteractionCard.Action.Kind.Should().Be(AiActionKind.StartScan);
        message.InteractionCard.Action.Path.Should().Be(@"D:\Downloads");
    }

    [Fact]
    public async Task SendCommand_ForModelClearProposal_ShowsClearConfirmationCard()
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
            .Returns(new ChatResponse("I prepared a clear-chat card.", string.Empty, CreateClearConversationProposal()));
        var viewModel = CreateViewModel(chatService);
        viewModel.InputText = "clear chat";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages.Should().Contain(message => message.Sender == ChatSender.User);
        var card = viewModel.PendingInteractionCard;
        card.Should().NotBeNull();
        card!.Action.Kind.Should().Be(AiActionKind.ClearConversation);
        card.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConfirmInteraction_ForClearConversation_ClearsMessagesAndHistory()
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
            .Returns(new ChatResponse("I prepared a clear-chat card.", string.Empty, CreateClearConversationProposal()));
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
    public async Task SendCommand_ForUnityLibraryDiscovery_UsesModelProposalCard()
    {
        var proposal = JsonSerializer.SerializeToElement(new
        {
            action = new
            {
                kind = nameof(AiActionKind.DiscoverUnityLibraries),
                scope_label = "Unity Library",
                will_overwrite_existing_data = false
            },
            card = new
            {
                title = "扫描 Unity Library",
                description = "由模型按 skill 提议 Unity Library 发现流程。",
                confirm_text = "开始",
                cancel_text = "取消"
            }
        });
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
            .Returns(new ChatResponse("我已准备确认卡片。", string.Empty, proposal));
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        var routed = new AiSkillRoutingResult([]);
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.InputText = "clean Unity Library";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.PendingInteractionCard.Should().NotBeNull();
        viewModel.PendingInteractionCard!.Action.Kind.Should().Be(AiActionKind.DiscoverUnityLibraries);
        await actionExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<AiActionRequest>(),
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
    public async Task SendCommand_ForScanProposal_ShowsConfirmationCardWithoutAutoExecuting()
    {
        var proposal = JsonSerializer.SerializeToElement(new
        {
            action = new
            {
                kind = nameof(AiActionKind.StartScan),
                path = @"G:",
                scope_label = @"G:",
                will_overwrite_existing_data = false
            },
            card = new
            {
                title = "Scan G drive",
                description = "Scan before analysis.",
                confirm_text = "Start scan",
                cancel_text = "Cancel"
            }
        });
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
            .Returns(new ChatResponse("I prepared a scan card.", string.Empty, proposal));
        var actionExecutor = Substitute.For<IAiDiskActionExecutor>();
        var viewModel = CreateViewModel(chatService, new AiSkillRoutingResult([]));
        viewModel.SetActionExecutor(actionExecutor);
        viewModel.InputText = "scan G drive";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.PendingInteractionCard.Should().NotBeNull();
        viewModel.PendingInteractionCard!.Action.Kind.Should().Be(AiActionKind.StartScan);
        viewModel.PendingInteractionCard.Action.Path.Should().Be(@"G:");
        viewModel.IsWorkflowProgressVisible.Should().BeFalse();
        await actionExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<AiActionRequest>(),
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
        var routed = new AiSkillRoutingResult([]);
        var viewModel = CreateViewModel(chatService, routed);
        viewModel.SetActionExecutor(actionExecutor);
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
                message.Contains("available_drives") &&
                message.Contains("scan Z drive")),
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

    private static JsonElement CreateClearConversationProposal()
        => JsonSerializer.SerializeToElement(new
        {
            action = new
            {
                kind = nameof(AiActionKind.ClearConversation)
            },
            card = new
            {
                title = "Clear this chat window?",
                description = "Clear messages and Copilot context after confirmation.",
                confirm_text = "Clear Chat",
                cancel_text = "Cancel"
            }
        });

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
            .Returns(routingResult ?? new AiSkillRoutingResult([]));

        return new ChatViewModel(chatService ?? Substitute.For<IChatService>(), settingsService, router);
    }

}




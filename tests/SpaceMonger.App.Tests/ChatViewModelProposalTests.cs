using System.Text.Json;
using NSubstitute;
using FluentAssertions;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Copilot;

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

        viewModel.Messages.Should().HaveCount(2);
        var card = viewModel.Messages[1].InteractionCard;
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
        var card = viewModel.Messages[1].InteractionCard!;

        await viewModel.ConfirmInteractionCommand.ExecuteAsync(card);

        viewModel.Messages.Should().BeEmpty();
        chatService.Received(1).ClearHistory();
    }

    private static ChatViewModel CreateViewModel(IChatService? chatService = null)
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.LoadSettings().Returns(new AppSettings { Language = "zh-CN" });
        settingsService.GetApiKey(Arg.Any<AppSettings>()).Returns("test-key");
        settingsService.EncryptApiKey(Arg.Any<string>()).Returns([]);

        var router = Substitute.For<IAiSkillRouter>();
        router.Route(Arg.Any<string>(), Arg.Any<FileEntry?>(), Arg.Any<FileEntry?>(), Arg.Any<bool>(), Arg.Any<string?>())
            .Returns(new AiSkillRoutingResult([AiIntent.GeneralChat], [], null));

        return new ChatViewModel(chatService ?? Substitute.For<IChatService>(), settingsService, router);
    }

}

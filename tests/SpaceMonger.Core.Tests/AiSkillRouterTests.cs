using FluentAssertions;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Tests;

public class AiSkillRouterTests
{
    private readonly AiSkillRouter _router = new(new FileSkillPromptProvider());

    [Fact]
    public void Route_CleanupQuestion_LoadsCleanupSkillsAndCreatesAnalyzeAction()
    {
        var root = new FileEntry { Name = "Downloads", Path = @"D:\Downloads", IsDirectory = true };

        var result = _router.Route("这个文件夹有啥可清理的", null, root, hasExistingRecommendations: true);

        result.Intents.Should().Contain(AiIntent.FolderCleanupAnalysis);
        result.Skills.Select(skill => skill.Id).Should().Contain("folder_cleanup_analysis");
        result.SuggestedAction.Should().NotBeNull();
        result.SuggestedAction!.Kind.Should().Be(AiActionKind.AnalyzeCleanup);
        result.SuggestedAction.WillOverwriteExistingData.Should().BeTrue();
        result.SuggestedAction.Path.Should().Be(@"D:\Downloads");
    }

    [Fact]
    public void Route_ScanPath_LoadsOnlyScanSkillAndCreatesScanAction()
    {
        var result = _router.Route(@"扫描 D:\Downloads", null, null, hasExistingRecommendations: false);

        result.Skills.Select(skill => skill.Id).Should().ContainSingle().Which.Should().Be("disk_scan");
        result.SuggestedAction.Should().NotBeNull();
        result.SuggestedAction!.Kind.Should().Be(AiActionKind.StartScan);
        result.SuggestedAction.Path.Should().Be(@"D:\Downloads");
    }

    [Fact]
    public void Route_IdentityQuestion_LoadsOnlyIdentitySkillAndLocalAnswer()
    {
        var result = _router.Route("你是谁，作者是谁", null, null, hasExistingRecommendations: false);

        result.Skills.Select(skill => skill.Id).Should().ContainSingle().Which.Should().Be("identity");
        result.LocalAnswer.Should().Contain("SpaceMonger Copilot");
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_IdentityQuestion_SupportsSaySomethingAboutYourselfPhrase()
    {
        var result = _router.Route("说说你自己", null, null, hasExistingRecommendations: false);

        result.Intents.Should().ContainSingle().Which.Should().Be(AiIntent.Identity);
        result.Skills.Select(skill => skill.Id).Should().ContainSingle().Which.Should().Be("identity");
        result.LocalAnswer.Should().Contain("SpaceMonger Copilot");
        result.SuggestedAction.Should().BeNull();
    }

    [Theory]
    [InlineData("你是谁")]
    [InlineData("你叫什么")]
    [InlineData("说说你自己")]
    [InlineData("介绍一下你自己")]
    [InlineData("你是哪个 Copilot")]
    public void Route_ChineseIdentityPrompts_ReturnSpaceMongerCopilot(string prompt)
    {
        var result = _router.Route(prompt, null, null, hasExistingRecommendations: false);

        result.Intents.Should().Contain(AiIntent.Identity);
        result.LocalAnswer.Should().Contain("SpaceMonger Copilot");
    }

    [Theory]
    [InlineData("Who are you?")]
    [InlineData("What's your name?")]
    [InlineData("Tell me about yourself")]
    [InlineData("Introduce yourself")]
    [InlineData("Are you SpaceMonger Copilot?")]
    public void Route_EnglishIdentityPrompts_ReturnSpaceMongerCopilot(string prompt)
    {
        var result = _router.Route(prompt, null, null, hasExistingRecommendations: false, responseLanguage: "en-US");

        result.Intents.Should().Contain(AiIntent.Identity);
        result.LocalAnswer.Should().Contain("SpaceMonger Copilot");
    }

    [Fact]
    public void Route_TreemapHelpQuestion_ReturnsLocalModuleHelpWithoutAction()
    {
        var result = _router.Route("Treemap 有什么用，怎么用？", null, null, hasExistingRecommendations: false);

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.Skills.Select(skill => skill.Id).Should().Contain("module_help");
        result.LocalAnswer.Should().Contain("Treemap").And.Contain("矩形面积");
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_RecommendationHelpQuestion_DoesNotCreateAnalyzeAction()
    {
        var root = new FileEntry { Name = "Downloads", Path = @"D:\Downloads", IsDirectory = true };

        var result = _router.Route("推荐清理是什么？", null, root, hasExistingRecommendations: true);

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.Skills.Select(skill => skill.Id).Should().Contain("module_help");
        result.LocalAnswer.Should().Contain("推荐清理").And.Contain("覆盖旧结果");
        result.CanRunWithoutScanContext.Should().BeFalse();
        result.PreferModelAnswer.Should().BeTrue();
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_WhitelistHelpQuestion_ReturnsLocalModuleHelpWithoutAction()
    {
        var result = _router.Route("设置里的白名单怎么用？", null, null, hasExistingRecommendations: false);

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.LocalAnswer.Should().Contain("白名单").And.Contain("保护");
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_ModuleHelpQuestion_UsesEnglishLocalFallbackWhenResponseLanguageIsEnglish()
    {
        var result = _router.Route("What is Treemap for?", null, null, hasExistingRecommendations: false, responseLanguage: "en");

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.LocalAnswer.Should().Contain("Treemap").And.Contain("disk usage");
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_IdentityQuestion_UsesEnglishLocalFallbackWhenResponseLanguageIsEnglish()
    {
        var result = _router.Route("Who are you?", null, null, hasExistingRecommendations: false, responseLanguage: "en-US");

        result.Intents.Should().Contain(AiIntent.Identity);
        result.LocalAnswer.Should().Contain("I am SpaceMonger Copilot");
        result.SuggestedAction.Should().BeNull();
    }



    [Fact]
    public void Route_GeneralFeatureGuide_ReturnsUserFacingModulesWithoutInternalTools()
    {
        var result = _router.Route("说说都有啥功能，我该怎么用好你呢", null, null, hasExistingRecommendations: false);

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.LocalAnswer.Should().Contain("扫描/路径输入").And.Contain("Treemap").And.Contain("TreeView");
        result.LocalAnswer.Should().NotContain("find_by_path").And.NotContain("propose_copilot_action").And.NotContain("get_copilot_context");
        result.SuggestedAction.Should().BeNull();
    }

    [Fact]
    public void Route_ScanDriveInChinese_CreatesScanActionForDriveRoot()
    {
        var result = _router.Route("扫描 G 盘，说说我买的游戏", null, null, hasExistingRecommendations: false);

        result.Intents.Should().Contain(AiIntent.DiskScan);
        result.SuggestedAction.Should().NotBeNull();
        result.SuggestedAction!.Kind.Should().Be(AiActionKind.StartScan);
        result.SuggestedAction.Path.Should().Be(@"G:\");
    }

    [Fact]
    public void Route_EnglishQuestionWithChineseAppLanguageSetting_UsesChineseLocalFallback()
    {
        var result = _router.Route("What can you do?", null, null, hasExistingRecommendations: false, responseLanguage: "zh-CN");

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.LocalAnswer.Should().Contain("这个应用主要有这些模块");
        result.LocalAnswer.Should().NotContain("This app has");
    }


    [Fact]
    public void Route_ChineseQuestionWithEnglishAppLanguageSetting_UsesEnglishLocalFallback()
    {
        var result = _router.Route("说说都有啥功能，我该怎么用好你呢", null, null, hasExistingRecommendations: false, responseLanguage: "en-US");

        result.Intents.Should().Contain(AiIntent.ModuleHelp);
        result.LocalAnswer.Should().Contain("This app has these user-facing modules");
        result.LocalAnswer.Should().NotContain("这个应用主要有这些模块");
    }
    [Fact]
    public void GetSkillSource_AppGuide_ComesFromSkillFile()
    {
        var content = _router.GetSkillSource("app-guide");

        content.Should().NotBeNull();
        content.Should().Contain("# SpaceMonger Copilot App Guide Skill");
        content.Should().Contain("## Module Guide");
    }

    [Fact]
    public void Route_GeneralChat_DoesNotInjectDiskSkills()
    {
        var result = _router.Route("你好", null, null, hasExistingRecommendations: false);

        result.Intents.Should().BeEmpty();
        result.Skills.Should().BeEmpty();
        result.SuggestedAction.Should().BeNull();
        result.LocalAnswer.Should().BeNull();
    }
}


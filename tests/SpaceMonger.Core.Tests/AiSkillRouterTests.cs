using FluentAssertions;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Tests;

public class AiSkillRouterTests
{
    private readonly AiSkillRouter _router = new();

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
    public void Route_GeneralChat_DoesNotInjectDiskSkills()
    {
        var result = _router.Route("你好", null, null, hasExistingRecommendations: false);

        result.Intents.Should().BeEmpty();
        result.Skills.Should().BeEmpty();
        result.SuggestedAction.Should().BeNull();
        result.LocalAnswer.Should().BeNull();
    }
}

using FluentAssertions;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App.Tests;

public class TreemapViewModelTests
{
    [Fact]
    public void NavigateToPath_TrailingSeparatorFromBreadcrumb_UsesScannedEntry()
    {
        var root = Directory(@"C:\", "C:");
        var users = AddDirectory(root, "Users");
        var profile = AddDirectory(users, "BianShanghai");
        var appData = AddDirectory(profile, "AppData");
        var local = AddDirectory(appData, "Local");
        var temp = AddDirectory(local, "Temp");
        var thunder = AddDirectory(temp, "Thunder Network");

        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.SetRoot(root);
        viewModel.NavigateToPath(thunder.Path).Should().BeTrue();

        viewModel.NavigateToPath(local.Path + Path.DirectorySeparatorChar).Should().BeTrue();

        viewModel.CurrentRoot.Should().BeSameAs(local);
        viewModel.CurrentRoot!.Children.Should().ContainSingle().Which.Should().BeSameAs(temp);
    }

    private static FileEntry Directory(string path, string name) => new()
    {
        Path = path,
        Name = name,
        IsDirectory = true,
        Size = 1,
    };

    private static FileEntry AddDirectory(FileEntry parent, string name)
    {
        var child = Directory(Path.Combine(parent.Path, name), name);
        child.Parent = parent;
        child.Depth = parent.Depth + 1;
        parent.Children.Add(child);
        return child;
    }

    private sealed class StubTreemapLayoutEngine : ITreemapLayoutEngine
    {
        public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth) => [];
    }
}
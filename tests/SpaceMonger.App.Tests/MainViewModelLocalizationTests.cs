using FluentAssertions;
using SpaceMonger.App.Localization;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Scanning;

namespace SpaceMonger.App.Tests;

public class MainViewModelLocalizationTests
{
    [Fact]
    public void LocalizedScanTitle_RefreshesWhenLanguageChanges()
    {
        var originalLanguage = L.CurrentLanguageName;
        try
        {
            L.SetLanguage("en");
            var viewModel = new MainViewModel(new StubFileScanner());

            viewModel.SetLocalizedScanTitle("AiScanTitleFormat", "C drive");
            viewModel.ScanTitleText.Should().Be("AI is scanning C drive. Please wait...");

            L.SetLanguage("zh-CN");

            viewModel.ScanTitleText.Should().Contain("AI正在扫描C drive");
        }
        finally
        {
            L.SetLanguage(originalLanguage);
        }
    }

    private sealed class StubFileScanner : IFileScanner
    {
        public bool IsReady => true;

        public event Action? IsReadyChanged;

        public Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
        {
            IsReadyChanged?.Invoke();
            return Task.FromResult(new ScanSession { TargetPath = path });
        }
    }
}

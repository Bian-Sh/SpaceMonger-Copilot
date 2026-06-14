using FluentAssertions;
using NSubstitute;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Llm;
using System.Net;
using System.Text.Json;

namespace SpaceMonger.Core.Tests;

public class RecommendationEngineTests
{
    [Fact]
    public async Task AnalyzeWithDiagnosticsAsync_ParsesJsonFromUnclosedMarkdownFence()
    {
        var targetPath = @"C:\Users\BianShanghai\AppData\Local\npm-cache\_cacache";
        var root = new FileEntry
        {
            Path = @"C:\",
            Name = @"C:\",
            IsDirectory = true,
            Children =
            [
                new FileEntry
                {
                    Path = targetPath,
                    Name = "_cacache",
                    IsDirectory = true,
                    Size = 2_028_726_426,
                }
            ]
        };

        var session = new ScanSession
        {
            TargetPath = @"C:\",
            RootEntry = root,
        };

        var llmClient = Substitute.For<ILlmClient>();
        llmClient.SendAnalysisAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns("""
                ```json
                {
                  "recommendations": [
                    {
                      "path": "C:\\Users\\BianShanghai\\AppData\\Local\\npm-cache\\_cacache",
                      "size_bytes": 2028726426,
                      "category": "PackageManagerCache",
                      "safety_rating": "Safe",
                      "explanation": "npm cache directory containing downloaded package artifacts."
                    }
                  ]
                }
                """);

        var engine = new RecommendationEngine(llmClient, Substitute.For<IDuplicateDetector>());

        var result = await engine.AnalyzeWithDiagnosticsAsync(session, "api-key", null, null, false, "zh-CN", CancellationToken.None);

        result.Diagnostics.ParseError.Should().BeNull();
        result.Diagnostics.ExtractedJsonLength.Should().BeGreaterThan(0);
        result.Diagnostics.ParsedRecommendationCount.Should().Be(1);
        result.Recommendations.Should().ContainSingle().Which.Should().Match<CleanupRecommendation>(r =>
            r.TargetPath == targetPath &&
            r.Category == RecommendationCategory.PackageManagerCache &&
            r.SafetyRating == SafetyRating.Safe);
    }

    [Fact]
    public async Task SendAnalysisAsync_UsesDeepSeekProAndDisablesThinkingForDeepSeekAnthropicEndpoint()
    {
        var handler = new CapturingHandler("""
            {
              "content": [
                { "type": "text", "text": "{\"recommendations\":[]}" }
              ],
              "stop_reason": "end_turn"
            }
            """);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("Anthropic").Returns(new HttpClient(handler));
        var client = new AnthropicClient(httpClientFactory);

        await client.SendAnalysisAsync("system", "metadata", "key", "https://api.deepseek.com/anthropic", null, false, CancellationToken.None);

        handler.RequestUri.Should().Be("https://api.deepseek.com/anthropic/v1/messages");
        using var requestJson = JsonDocument.Parse(handler.RequestBody);
        var root = requestJson.RootElement;
        root.GetProperty("model").GetString().Should().Be("deepseek-v4-pro");
        root.GetProperty("thinking").GetProperty("type").GetString().Should().Be("disabled");
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri!.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}

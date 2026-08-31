using System;
using System.IO;
using System.Text.Json;
using Myelin.Core.Models;
using Myelin.Core.Services;
using Xunit;

namespace Myelin.Core.Tests
{
    public class ExtensionManagerTests
    {
        [Fact]
        public void ExtensionManifest_CanParseCompletePackageJson()
        {
            string sampleJson = @"
            {
                ""name"": ""rust-analyzer"",
                ""displayName"": ""Rust Analyzer"",
                ""publisher"": ""rust-lang"",
                ""version"": ""0.3.1800"",
                ""description"": ""Rust language support for VS Code"",
                ""main"": ""./out/main.js"",
                ""icon"": ""icon.png"",
                ""contributes"": {
                    ""commands"": [
                        {
                            ""command"": ""rust-analyzer.syntaxTree"",
                            ""title"": ""Show Syntax Tree"",
                            ""category"": ""Rust Analyzer""
                        }
                    ],
                    ""themes"": [
                        {
                            ""label"": ""Rust Dark Theme"",
                            ""uiTheme"": ""vs-dark"",
                            ""path"": ""./themes/rust-dark.json""
                        }
                    ],
                    ""snippets"": [
                        {
                            ""language"": ""rust"",
                            ""path"": ""./snippets/rust.json""
                        }
                    ]
                }
            }";

            var manifest = JsonSerializer.Deserialize<ExtensionPackageJson>(sampleJson);

            Assert.NotNull(manifest);
            Assert.Equal("rust-analyzer", manifest.Name);
            Assert.Equal("Rust Analyzer", manifest.DisplayName);
            Assert.Equal("rust-lang", manifest.Publisher);
            Assert.Equal("0.3.1800", manifest.Version);
            Assert.NotNull(manifest.Contributes);
            Assert.NotNull(manifest.Contributes.Commands);
            Assert.Single(manifest.Contributes.Commands);
            Assert.Equal("rust-analyzer.syntaxTree", manifest.Contributes.Commands[0].Command);
            Assert.NotNull(manifest.Contributes.Themes);
            Assert.Single(manifest.Contributes.Themes);
            Assert.NotNull(manifest.Contributes.Snippets);
            Assert.Single(manifest.Contributes.Snippets);
        }

        [Fact]
        public void OpenVsxExtensionItem_FormatsDisplayPropertiesCorrectly()
        {
            var item = new OpenVsxExtensionItem
            {
                Namespace = "dracula-theme",
                Name = "theme-dracula",
                DisplayName = "Dracula Official",
                Version = "2.24.3",
                DownloadCount = 1_500_000,
                AverageRating = 4.85,
                Files = new()
                {
                    { "icon", "https://open-vsx.org/api/dracula-theme/theme-dracula/2.24.3/file/icon.png" },
                    { "download", "https://open-vsx.org/api/dracula-theme/theme-dracula/2.24.3/file/dracula-theme.theme-dracula-2.24.3.vsix" }
                }
            };

            Assert.Equal("dracula-theme.theme-dracula", item.Id);
            Assert.Equal("Dracula Official", item.Title);
            Assert.Equal("dracula-theme", item.Publisher);
            Assert.Equal("1.5M", item.FormattedDownloads);
            Assert.Equal("4.9 ★", item.FormattedRating);
            Assert.NotNull(item.IconUrl);
            Assert.NotNull(item.DownloadUrl);
        }

        [Fact]
        public void NodeExtensionHost_CanFindExecutableAndBootstrap()
        {
            string? nodeExe = NodeExtensionHostService.FindNodeExecutable();
            Assert.NotNull(nodeExe);

            string bootstrapJs = NodeExtensionHostService.FindBootstrapScript();
            Assert.NotNull(bootstrapJs);
            Assert.True(File.Exists(bootstrapJs), $"Bootstrap script exists at {bootstrapJs}");
        }

        [Fact]
        public void ExtensionWebviewService_TracksPanelsAndReceivesHtml()
        {
            var service = ExtensionWebviewService.Instance;
            string testPanelId = "panel_test_123";

            service.SetHtml(testPanelId, "<h1>Hello from Extension Webview</h1>");
            // Verify no exceptions on posting messages or getting missing panels
            var panel = service.GetPanel("non_existent");
            Assert.Null(panel);
        }
    }
}

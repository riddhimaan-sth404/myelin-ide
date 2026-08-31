using System;
using System.IO;
using System.Threading.Tasks;
using Myelin.Core.Models;
using Myelin.Core.Services;
using Xunit;

namespace Myelin.Core.Tests
{
    public class LspAndServerTests
    {
        [Fact]
        public void MarkdownRendererService_RendersValidHtml_WithGfmFeatures()
        {
            var service = MarkdownRendererService.Instance;
            string markdown = @"# Myelin IDE

Welcome to **Myelin IDE** with embedded markdown!

- [x] Fast Rust Core
- [ ] Language Server Protocol
- [x] Native OS WebView

| Feature | Status |
| :--- | :--- |
| Live Server | Enabled |
| Flask / Node | Supported |

```csharp
Console.WriteLine(""Hello World"");
```
";

            string html = service.RenderToHtml(markdown, "Test Doc", isDarkTheme: true);

            Assert.NotNull(html);
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("<h1", html);
            Assert.Contains("Myelin IDE", html);
            Assert.Contains("<table>", html);
            Assert.Contains("Live Server", html);
            Assert.Contains("<pre><code", html);
            Assert.Contains("Console.WriteLine", html);
            Assert.Contains("task-list", html);
        }

        [Theory]
        [InlineData("app.py", "python")]
        [InlineData("server.ts", "typescript")]
        [InlineData("index.js", "typescript")]
        [InlineData("main.rs", "rust")]
        [InlineData("styles.css", "css")]
        [InlineData("index.html", "html")]
        [InlineData("Program.cs", "csharp")]
        [InlineData("main.go", "go")]
        [InlineData("native.cpp", "cpp")]
        public void LanguageServerService_ResolvesCorrectLanguageId(string fileName, string expectedLangId)
        {
            var service = LanguageServerService.Instance;
            string? langId = service.GetLanguageIdForFile(fileName);

            Assert.Equal(expectedLangId, langId);
        }

        [Fact]
        public async Task LiveServerService_StartsAndStopsSuccessfully()
        {
            var service = LiveServerService.Instance;
            string tempDir = Path.Combine(Path.GetTempPath(), $"LiveServerTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body><h1>Hello Live Server</h1></body></html>");

                bool started = await service.StartAsync(tempDir, 5590);
                Assert.True(started);
                Assert.True(service.IsRunning);
                Assert.StartsWith("http://127.0.0.1:", service.ServerUrl);

                service.Stop();
                Assert.False(service.IsRunning);
            }
            finally
            {
                service.Stop();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task LocalServerRunner_DetectsFlaskProject()
        {
            var runner = LocalServerRunnerService.Instance;
            string tempDir = Path.Combine(Path.GetTempPath(), $"FlaskTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "app.py"), "from flask import Flask\napp = Flask(__name__)\n");

                var (success, url, message) = await runner.StartLocalServerAsync(tempDir);

                // Even if python executable or flask is not in test env PATH, detection logic is exercised
                Assert.Equal(LocalServerType.Flask, runner.CurrentServerType);
                Assert.Equal("http://127.0.0.1:5000/", runner.ActiveServerUrl);
            }
            finally
            {
                runner.Stop();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task LocalServerRunner_DetectsNodeViteProject()
        {
            var runner = LocalServerRunnerService.Instance;
            string tempDir = Path.Combine(Path.GetTempPath(), $"ViteTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "package.json"), "{\n  \"name\": \"my-app\",\n  \"scripts\": {\n    \"dev\": \"vite\"\n  }\n}");

                var (success, url, message) = await runner.StartLocalServerAsync(tempDir);

                Assert.Equal(LocalServerType.NodeVite, runner.CurrentServerType);
                Assert.Equal("http://localhost:5173/", runner.ActiveServerUrl);
            }
            finally
            {
                runner.Stop();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}

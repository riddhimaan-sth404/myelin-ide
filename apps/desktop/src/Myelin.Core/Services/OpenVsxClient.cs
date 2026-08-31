using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class OpenVsxClient
    {
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = new Uri("https://open-vsx.org/api/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static OpenVsxClient? _instance;
        public static OpenVsxClient Instance => _instance ??= new OpenVsxClient();

        static OpenVsxClient()
        {
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "Myelin-IDE/1.0 (Windows; OpenVSX-Client)");
            }
        }

        public async Task<OpenVsxSearchResult> SearchExtensionsAsync(string query, int offset = 0, int size = 30, CancellationToken ct = default)
        {
            try
            {
                string encodedQuery = Uri.EscapeDataString(query);
                string url = $"-/search?query={encodedQuery}&offset={offset}&size={size}&sortBy=relevance&sortOrder=desc";
                using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync<OpenVsxSearchResult>(stream, cancellationToken: ct).ConfigureAwait(false);
                return result ?? new OpenVsxSearchResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenVsxClient] Search error: {ex.Message}");
                return new OpenVsxSearchResult();
            }
        }

        public async Task<OpenVsxSearchResult> GetPopularExtensionsAsync(int size = 30, CancellationToken ct = default)
        {
            try
            {
                string url = $"-/search?offset=0&size={size}&sortBy=downloadCount&sortOrder=desc";
                using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync<OpenVsxSearchResult>(stream, cancellationToken: ct).ConfigureAwait(false);
                return result ?? new OpenVsxSearchResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenVsxClient] Popular extensions error: {ex.Message}");
                return new OpenVsxSearchResult();
            }
        }

        public async Task<OpenVsxExtensionItem?> GetExtensionDetailsAsync(string namespaceName, string extensionName, CancellationToken ct = default)
        {
            try
            {
                string url = $"{Uri.EscapeDataString(namespaceName)}/{Uri.EscapeDataString(extensionName)}";
                using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<OpenVsxExtensionItem>(stream, cancellationToken: ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> FetchReadmeAsync(string readmeUrl, CancellationToken ct = default)
        {
            try
            {
                using var response = await HttpClient.GetAsync(readmeUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadVsixAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            try
            {
                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    totalRead += read;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes.Value);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenVsxClient] Download error: {ex.Message}");
                return false;
            }
        }
    }
}

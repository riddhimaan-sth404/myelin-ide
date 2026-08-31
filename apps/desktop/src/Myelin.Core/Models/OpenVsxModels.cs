using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Myelin.Core.Models
{
    public class OpenVsxSearchResult
    {
        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("totalSize")]
        public int TotalSize { get; set; }

        [JsonPropertyName("extensions")]
        public List<OpenVsxExtensionItem> Extensions { get; set; } = new();
    }

    public class OpenVsxExtensionItem
    {
        [JsonPropertyName("namespace")]
        public string Namespace { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("publishedDate")]
        public string? PublishedDate { get; set; }

        [JsonPropertyName("averageRating")]
        public double? AverageRating { get; set; }

        [JsonPropertyName("downloadCount")]
        public int DownloadCount { get; set; }

        [JsonPropertyName("reviewCount")]
        public int ReviewCount { get; set; }

        [JsonPropertyName("files")]
        public Dictionary<string, string> Files { get; set; } = new();

        public string Id => $"{Namespace}.{Name}";
        public string Title => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Name;
        public string Publisher => Namespace;

        public string? IconUrl
        {
            get
            {
                if (Files.TryGetValue("icon", out var url)) return url;
                if (Files.TryGetValue("smallIcon", out var sUrl)) return sUrl;
                return null;
            }
        }

        public string? DownloadUrl
        {
            get
            {
                if (Files.TryGetValue("download", out var url)) return url;
                return $"https://open-vsx.org/api/{Namespace}/{Name}/{Version}/file/{Namespace}.{Name}-{Version}.vsix";
            }
        }

        public string? ReadmeUrl => Files.TryGetValue("readme", out var url) ? url : null;
        public string? ChangelogUrl => Files.TryGetValue("changelog", out var url) ? url : null;

        public string FormattedDownloads
        {
            get
            {
                if (DownloadCount >= 1_000_000) return $"{DownloadCount / 1_000_000.0:0.#}M";
                if (DownloadCount >= 1_000) return $"{DownloadCount / 1_000.0:0.#}K";
                return DownloadCount.ToString();
            }
        }

        public string FormattedRating => AverageRating.HasValue ? $"{AverageRating.Value:0.0} ★" : "★ —";
    }
}

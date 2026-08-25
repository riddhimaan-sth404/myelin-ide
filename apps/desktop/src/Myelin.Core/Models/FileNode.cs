using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Myelin.Core.Models
{
    public class FileNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("is_dir")]
        public bool IsDirectory { get; set; }

        [JsonPropertyName("children")]
        public List<FileNode> Children { get; set; } = new();

        public bool HasChildren => Children.Count > 0;
    }
}

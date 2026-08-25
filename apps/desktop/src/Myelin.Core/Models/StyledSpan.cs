using System.Text.Json.Serialization;

namespace Myelin.Core.Models
{
    public class StyledSpan
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#D4D4D4";
    }
}

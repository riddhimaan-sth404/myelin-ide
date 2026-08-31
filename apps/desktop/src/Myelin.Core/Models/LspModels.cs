using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Myelin.Core.Models
{
    public class LspPosition
    {
        [JsonPropertyName("line")]
        public int Line { get; set; }

        [JsonPropertyName("character")]
        public int Character { get; set; }

        public LspPosition() { }
        public LspPosition(int line, int character)
        {
            Line = line;
            Character = character;
        }
    }

    public class LspRange
    {
        [JsonPropertyName("start")]
        public LspPosition Start { get; set; } = new();

        [JsonPropertyName("end")]
        public LspPosition End { get; set; } = new();

        public LspRange() { }
        public LspRange(LspPosition start, LspPosition end)
        {
            Start = start;
            End = end;
        }
    }

    public class LspLocation
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = string.Empty;

        [JsonPropertyName("range")]
        public LspRange Range { get; set; } = new();
    }

    public enum LspDiagnosticSeverity
    {
        Error = 1,
        Warning = 2,
        Information = 3,
        Hint = 4
    }

    public class LspDiagnostic
    {
        [JsonPropertyName("range")]
        public LspRange Range { get; set; } = new();

        [JsonPropertyName("severity")]
        public LspDiagnosticSeverity Severity { get; set; } = LspDiagnosticSeverity.Error;

        [JsonPropertyName("code")]
        public object? Code { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class LspCompletionItem
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }

        [JsonPropertyName("documentation")]
        public object? Documentation { get; set; }

        [JsonPropertyName("insertText")]
        public string? InsertText { get; set; }

        [JsonPropertyName("sortText")]
        public string? SortText { get; set; }
    }

    public class LspHover
    {
        [JsonPropertyName("contents")]
        public object? Contents { get; set; }

        [JsonPropertyName("range")]
        public LspRange? Range { get; set; }

        public string GetMarkdownText()
        {
            if (Contents == null) return string.Empty;
            if (Contents is string s) return s;
            if (Contents is System.Text.Json.JsonElement elem)
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return elem.GetString() ?? string.Empty;
                }
                if (elem.ValueKind == System.Text.Json.JsonValueKind.Object && elem.TryGetProperty("value", out var val))
                {
                    return val.GetString() ?? string.Empty;
                }
            }
            return Contents.ToString() ?? string.Empty;
        }
    }

    public class LanguageServerDescriptor
    {
        public string LanguageId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string[] FileExtensions { get; set; } = Array.Empty<string>();
        public string[] ExecutableCandidates { get; set; } = Array.Empty<string>();
        public string[] DefaultArguments { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
}

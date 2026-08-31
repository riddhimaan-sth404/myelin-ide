using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Myelin.Core.Models
{
    public class ExtensionPackageJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("publisher")]
        public string Publisher { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("main")]
        public string? Main { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("engines")]
        public Dictionary<string, string>? Engines { get; set; }

        [JsonPropertyName("contributes")]
        public ExtensionContributes? Contributes { get; set; }

        [JsonPropertyName("activationEvents")]
        public List<string>? ActivationEvents { get; set; }
    }

    public class ExtensionContributes
    {
        [JsonPropertyName("commands")]
        public List<ContributedCommand>? Commands { get; set; }

        [JsonPropertyName("themes")]
        public List<ContributedTheme>? Themes { get; set; }

        [JsonPropertyName("iconThemes")]
        public List<ContributedIconTheme>? IconThemes { get; set; }

        [JsonPropertyName("snippets")]
        public List<ContributedSnippet>? Snippets { get; set; }

        [JsonPropertyName("grammars")]
        public List<ContributedGrammar>? Grammars { get; set; }

        [JsonPropertyName("languages")]
        public List<ContributedLanguage>? Languages { get; set; }

        [JsonPropertyName("customEditors")]
        public List<ContributedCustomEditor>? CustomEditors { get; set; }

        [JsonPropertyName("viewsContainers")]
        public JsonElement? ViewsContainers { get; set; }

        [JsonPropertyName("views")]
        public JsonElement? Views { get; set; }

        [JsonPropertyName("configuration")]
        public JsonElement? Configuration { get; set; }
    }

    public class ContributedCommand
    {
        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }
    }

    public class ContributedTheme
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("uiTheme")]
        public string UiTheme { get; set; } = "vs-dark";

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    public class ContributedIconTheme
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    public class ContributedSnippet
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    public class ContributedGrammar
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("scopeName")]
        public string ScopeName { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    public class ContributedLanguage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("extensions")]
        public List<string>? Extensions { get; set; }

        [JsonPropertyName("aliases")]
        public List<string>? Aliases { get; set; }
    }

    public class ContributedCustomEditor
    {
        [JsonPropertyName("viewType")]
        public string ViewType { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("selector")]
        public List<CustomEditorSelector>? Selector { get; set; }
    }

    public class CustomEditorSelector
    {
        [JsonPropertyName("filenamePattern")]
        public string? FilenamePattern { get; set; }
    }

    public class InstalledExtension
    {
        public string Id => $"{Manifest.Publisher}.{Manifest.Name}";
        public string Name => Manifest.Name;
        public string Publisher => Manifest.Publisher;
        public string DisplayName => !string.IsNullOrEmpty(Manifest.DisplayName) ? Manifest.DisplayName : Manifest.Name;
        public string Version => Manifest.Version;
        public string Description => Manifest.Description ?? string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public ExtensionPackageJson Manifest { get; set; } = new();
        public bool IsEnabled { get; set; } = true;
        public string? IconPath { get; set; }
        public string? EntrypointJsPath => !string.IsNullOrEmpty(Manifest.Main) ? System.IO.Path.Combine(InstallDirectory, Manifest.Main) : null;
        public bool HasEntrypoint => !string.IsNullOrEmpty(Manifest.Main) && System.IO.File.Exists(EntrypointJsPath);
    }
}

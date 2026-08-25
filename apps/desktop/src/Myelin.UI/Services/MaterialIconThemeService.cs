using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Myelin.Core.Models;

namespace Myelin.UI.Services
{
    public class MaterialIconDefinition
    {
        [JsonPropertyName("fileNames")]
        public Dictionary<string, string> FileNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("fileExtensions")]
        public Dictionary<string, string> FileExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("folderNames")]
        public Dictionary<string, string> FolderNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("folderNamesExpanded")]
        public Dictionary<string, string> FolderNamesExpanded { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("file")]
        public string DefaultFile { get; set; } = "file";

        [JsonPropertyName("folder")]
        public string DefaultFolder { get; set; } = "folder";

        [JsonPropertyName("folderExpanded")]
        public string DefaultFolderExpanded { get; set; } = "folder-open";
    }

    public static class MaterialIconThemeService
    {
        private static readonly ConcurrentDictionary<string, IImage?> _iconCache = new();
        private static MaterialIconDefinition _definition = new();
        private static string _iconsDirectory = string.Empty;
        private static bool _initialized = false;

        static MaterialIconThemeService()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string jsonPath = Path.Combine(baseDir, "Assets", "MaterialIcons", "material-icons.json");
                _iconsDirectory = Path.Combine(baseDir, "Assets", "MaterialIcons", "icons");

                if (!File.Exists(jsonPath))
                {
                    // Fallback to local source path if running from bin
                    string altPath = Path.Combine(baseDir, "..", "..", "..", "Assets", "MaterialIcons", "material-icons.json");
                    if (File.Exists(altPath))
                    {
                        jsonPath = altPath;
                        _iconsDirectory = Path.Combine(baseDir, "..", "..", "..", "Assets", "MaterialIcons", "icons");
                    }
                }

                if (File.Exists(jsonPath))
                {
                    string json = File.ReadAllText(jsonPath);
                    var def = JsonSerializer.Deserialize<MaterialIconDefinition>(json);
                    if (def != null)
                    {
                        _definition = def;
                    }
                }
            }
            catch
            {
                // Fallback to empty definition
            }
            finally
            {
                _initialized = true;
            }
        }

        public static IImage? GetIcon(FileNode? node, bool expanded = false)
        {
            if (node == null) return GetFileIcon("file");
            return node.IsDirectory 
                ? GetFolderIcon(node.Name, expanded) 
                : GetFileIcon(node.Name);
        }

        public static IImage? GetFileIcon(string fileName)
        {
            string cleanName = Path.GetFileName(fileName);
            string iconKey = ResolveFileIconKey(cleanName);
            return LoadSvg(iconKey);
        }

        public static IImage? GetFolderIcon(string folderName, bool expanded = false)
        {
            string cleanName = Path.GetFileName(folderName);
            string iconKey = ResolveFolderIconKey(cleanName, expanded);
            return LoadSvg(iconKey);
        }

        private static string ResolveFileIconKey(string fileName)
        {
            string lowerName = fileName.ToLowerInvariant();

            // 1. Exact match on full file name (e.g. Cargo.toml, package.json, .gitignore)
            if (_definition.FileNames.TryGetValue(lowerName, out var iconKey))
            {
                return iconKey;
            }

            // 2. Match on multi-part extension (e.g. .test.js, .spec.ts)
            int firstDot = lowerName.IndexOf('.');
            if (firstDot >= 0 && firstDot < lowerName.Length - 1)
            {
                string multiExt = lowerName[(firstDot + 1)..];
                if (_definition.FileExtensions.TryGetValue(multiExt, out var multiKey))
                {
                    return multiKey;
                }
            }

            // 3. Match on standard file extension (e.g. rs, cs, json, toml)
            string ext = Path.GetExtension(lowerName).TrimStart('.');
            if (!string.IsNullOrEmpty(ext) && _definition.FileExtensions.TryGetValue(ext, out var extKey))
            {
                return extKey;
            }

            return _definition.DefaultFile;
        }

        private static string ResolveFolderIconKey(string folderName, bool expanded)
        {
            string lowerName = folderName.ToLowerInvariant();

            if (expanded)
            {
                if (_definition.FolderNamesExpanded.TryGetValue(lowerName, out var expKey))
                {
                    return expKey;
                }
                if (_definition.FolderNames.TryGetValue(lowerName, out var folderKey))
                {
                    return $"{folderKey}-open";
                }
                return _definition.DefaultFolderExpanded;
            }
            else
            {
                if (_definition.FolderNames.TryGetValue(lowerName, out var folderKey))
                {
                    return folderKey;
                }
                return _definition.DefaultFolder;
            }
        }

        private static IImage? LoadSvg(string iconKey)
        {
            if (_iconCache.TryGetValue(iconKey, out var cached))
            {
                return cached;
            }

            try
            {
                string svgPath = Path.Combine(_iconsDirectory, $"{iconKey}.svg");
                if (File.Exists(svgPath))
                {
                    var svgSource = SvgSource.Load(svgPath);
                    if (svgSource != null)
                    {
                        var svgImage = new SvgImage { Source = svgSource };
                        _iconCache[iconKey] = svgImage;
                        return svgImage;
                    }
                }
                else
                {
                    // Fallback to default
                    string defaultPath = Path.Combine(_iconsDirectory, $"{_definition.DefaultFile}.svg");
                    if (File.Exists(defaultPath))
                    {
                        var svgSource = SvgSource.Load(defaultPath);
                        if (svgSource != null)
                        {
                            var svgImage = new SvgImage { Source = svgSource };
                            _iconCache[iconKey] = svgImage;
                            return svgImage;
                        }
                    }
                }
            }
            catch
            {
                // Return null on load error
            }

            _iconCache[iconKey] = null;
            return null;
        }
    }
}

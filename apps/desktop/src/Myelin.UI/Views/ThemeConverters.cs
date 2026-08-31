using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Myelin.Core.Models;
using Myelin.UI.Services;

namespace Myelin.UI.Views
{
    /// <summary>
    /// Philipp Kief Material Icon Theme - Full SVG Vector Image Converter
    /// </summary>
    public class MaterialIconImageConverter : IValueConverter
    {
        public static readonly MaterialIconImageConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is FileNode node)
            {
                return MaterialIconThemeService.GetIcon(node);
            }
            if (value is string pathOrName && !string.IsNullOrEmpty(pathOrName))
            {
                return MaterialIconThemeService.GetFileIcon(pathOrName);
            }
            return MaterialIconThemeService.GetFileIcon("file");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// Philipp Kief Material Icon Theme - Vector Geometry Fallback Resolver
    /// </summary>
    public class MaterialIconGeometryConverter : IValueConverter
    {
        public static readonly MaterialIconGeometryConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (Application.Current == null) return null;

            string name = string.Empty;
            bool isDir = false;

            if (value is FileNode node)
            {
                name = node.Name;
                isDir = node.IsDirectory;
            }
            else if (value is string fileName)
            {
                name = fileName;
                isDir = false;
            }

            string lower = name.ToLowerInvariant();

            if (isDir)
            {
                return lower switch
                {
                    "src" or "source" => Application.Current.FindResource("IconFolderSrc"),
                    "test" or "tests" or "spec" => Application.Current.FindResource("IconFolderTest"),
                    "crates" or "packages" or "libs" => Application.Current.FindResource("IconFolderCrates"),
                    "apps" or "desktop" or "web" or "ui" => Application.Current.FindResource("IconFolderApp"),
                    _ => Application.Current.FindResource("IconFolderDefault")
                };
            }

            // Exact Filename matches
            if (lower == "cargo.toml" || lower == "cargo.lock") return Application.Current.FindResource("IconRust");
            if (lower.StartsWith(".git")) return Application.Current.FindResource("IconGit");
            if (lower == "readme.md" || lower == "changelog.md") return Application.Current.FindResource("IconMarkdown");
            if (lower == "package.json" || lower == "tsconfig.json") return Application.Current.FindResource("IconTypeScript");

            // Extension matches
            string ext = Path.GetExtension(lower);
            return ext switch
            {
                ".rs" => Application.Current.FindResource("IconRust"),
                ".cs" => Application.Current.FindResource("IconCSharp"),
                ".axaml" or ".xaml" => Application.Current.FindResource("IconXaml"),
                ".json" => Application.Current.FindResource("IconJson"),
                ".toml" => Application.Current.FindResource("IconToml"),
                ".md" => Application.Current.FindResource("IconMarkdown"),
                ".ts" or ".tsx" => Application.Current.FindResource("IconTypeScript"),
                ".js" or ".jsx" => Application.Current.FindResource("IconJavaScript"),
                ".py" => Application.Current.FindResource("IconPython"),
                ".bat" or ".cmd" or ".ps1" or ".sh" => Application.Current.FindResource("IconTerminal"),
                _ => Application.Current.FindResource("IconFileDefault")
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// Philipp Kief Material Icon Theme - Color Brush Resolver
    /// </summary>
    public class MaterialIconBrushConverter : IValueConverter
    {
        public static readonly MaterialIconBrushConverter Instance = new();

        private static readonly IBrush FolderDefaultBrush = new ImmutableSolidColorBrush(Color.Parse("#FFA000"));
        private static readonly IBrush FolderSrcBrush = new ImmutableSolidColorBrush(Color.Parse("#3B82F6"));
        private static readonly IBrush FolderTestBrush = new ImmutableSolidColorBrush(Color.Parse("#84CC16"));
        private static readonly IBrush FolderCratesBrush = new ImmutableSolidColorBrush(Color.Parse("#F97316"));
        private static readonly IBrush FolderAppBrush = new ImmutableSolidColorBrush(Color.Parse("#A855F7"));

        private static readonly IBrush RustBrush = new ImmutableSolidColorBrush(Color.Parse("#E5732F"));
        private static readonly IBrush CSharpBrush = new ImmutableSolidColorBrush(Color.Parse("#A179DC"));
        private static readonly IBrush XamlBrush = new ImmutableSolidColorBrush(Color.Parse("#007ACC"));
        private static readonly IBrush JsonBrush = new ImmutableSolidColorBrush(Color.Parse("#FBC02D"));
        private static readonly IBrush TomlBrush = new ImmutableSolidColorBrush(Color.Parse("#D97706"));
        private static readonly IBrush MarkdownBrush = new ImmutableSolidColorBrush(Color.Parse("#38BDF8"));
        private static readonly IBrush TypeScriptBrush = new ImmutableSolidColorBrush(Color.Parse("#3178C6"));
        private static readonly IBrush JavaScriptBrush = new ImmutableSolidColorBrush(Color.Parse("#F7DF1E"));
        private static readonly IBrush PythonBrush = new ImmutableSolidColorBrush(Color.Parse("#38BDF8"));
        private static readonly IBrush GitBrush = new ImmutableSolidColorBrush(Color.Parse("#F05032"));
        private static readonly IBrush ShellBrush = new ImmutableSolidColorBrush(Color.Parse("#4ADE80"));
        private static readonly IBrush FileDefaultBrush = new ImmutableSolidColorBrush(Color.Parse("#90A4AE"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string name = string.Empty;
            bool isDir = false;

            if (value is FileNode node)
            {
                name = node.Name;
                isDir = node.IsDirectory;
            }
            else if (value is string fileName)
            {
                name = fileName;
                isDir = false;
            }

            string lower = name.ToLowerInvariant();

            if (isDir)
            {
                return lower switch
                {
                    "src" or "source" => FolderSrcBrush,
                    "test" or "tests" or "spec" => FolderTestBrush,
                    "crates" or "packages" or "libs" => FolderCratesBrush,
                    "apps" or "desktop" or "web" or "ui" => FolderAppBrush,
                    _ => FolderDefaultBrush
                };
            }

            // Exact Filename matches
            if (lower == "cargo.toml" || lower == "cargo.lock") return RustBrush;
            if (lower.StartsWith(".git")) return GitBrush;
            if (lower == "readme.md" || lower == "changelog.md") return MarkdownBrush;
            if (lower == "package.json" || lower == "tsconfig.json") return TypeScriptBrush;

            // Extension matches
            string ext = Path.GetExtension(lower);
            return ext switch
            {
                ".rs" => RustBrush,
                ".cs" => CSharpBrush,
                ".axaml" or ".xaml" => XamlBrush,
                ".json" => JsonBrush,
                ".toml" => TomlBrush,
                ".md" => MarkdownBrush,
                ".ts" or ".tsx" => TypeScriptBrush,
                ".js" or ".jsx" => JavaScriptBrush,
                ".py" => PythonBrush,
                ".bat" or ".cmd" or ".ps1" or ".sh" => ShellBrush,
                _ => FileDefaultBrush
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ActivityColorConverter : IValueConverter
    {
        public static readonly ActivityColorConverter Instance = new();
        private static readonly IBrush WhiteBrush = new ImmutableSolidColorBrush(Color.Parse("#FFFFFF"));
        private static readonly IBrush DimBrush = new ImmutableSolidColorBrush(Color.Parse("#858585"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int activeIndex && parameter is string paramStr && int.TryParse(paramStr, out int targetIndex))
            {
                return activeIndex == targetIndex ? WhiteBrush : DimBrush;
            }
            return DimBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ActiveBorderConverter : IValueConverter
    {
        public static readonly ActiveBorderConverter Instance = new();
        private static readonly IBrush ActiveBrush = new ImmutableSolidColorBrush(Color.Parse("#0078D4"));
        private static readonly IBrush TransparentBrush = new ImmutableSolidColorBrush(Colors.Transparent);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int activeIndex && parameter is string paramStr && int.TryParse(paramStr, out int targetIndex))
            {
                return activeIndex == targetIndex ? ActiveBrush : TransparentBrush;
            }
            return TransparentBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TabBackgroundConverter : IValueConverter
    {
        public static readonly TabBackgroundConverter Instance = new();
        private static readonly IBrush ActiveBrush = new ImmutableSolidColorBrush(Color.Parse("#1F1F1F"));
        private static readonly IBrush InactiveBrush = new ImmutableSolidColorBrush(Color.Parse("#181818"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? ActiveBrush : InactiveBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanBrushConverter : IValueConverter
    {
        public static readonly BooleanBrushConverter Instance = new();
        private static readonly IBrush TrueBrush = new ImmutableSolidColorBrush(Color.Parse("#38BDF8"));
        private static readonly IBrush FalseBrush = new ImmutableSolidColorBrush(Color.Parse("#858585"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? TrueBrush : FalseBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TabBorderConverter : IValueConverter
    {
        public static readonly TabBorderConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TabWeightConverter : IValueConverter
    {
        public static readonly TabWeightConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int selectedIdx && parameter is string p && int.TryParse(p, out int targetIdx))
            {
                return selectedIdx == targetIdx;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TabForegroundConverter : IValueConverter
    {
        public static readonly TabForegroundConverter Instance = new();
        private static readonly IBrush WhiteBrush = new ImmutableSolidColorBrush(Color.Parse("#FFFFFF"));
        private static readonly IBrush DimBrush = new ImmutableSolidColorBrush(Color.Parse("#969696"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int selectedIdx && parameter is string p && int.TryParse(p, out int targetIdx))
            {
                return selectedIdx == targetIdx ? WhiteBrush : DimBrush;
            }
            return DimBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TerminalTabActiveBackgroundConverter : IValueConverter
    {
        public static readonly TerminalTabActiveBackgroundConverter Instance = new();
        private static readonly IBrush ActiveBrush = new ImmutableSolidColorBrush(Color.Parse("#04395E"));
        private static readonly IBrush InactiveBrush = new ImmutableSolidColorBrush(Colors.Transparent);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? ActiveBrush : InactiveBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PanelMaximizeIconConverter : IValueConverter
    {
        public static readonly PanelMaximizeIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (Application.Current != null && Application.Current.Resources.TryGetResource(value is true ? "IconCollapse" : "IconExpand", null, out var res))
            {
                return res;
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PanelRowHeightConverter : IValueConverter
    {
        public static readonly PanelRowHeightConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true)
            {
                return new GridLength(260, GridUnitType.Pixel);
            }
            return new GridLength(0, GridUnitType.Pixel);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class SidebarColumnWidthConverter : IValueConverter
    {
        public static readonly SidebarColumnWidthConverter Instance = new();
        public static double LastWidth { get; set; } = 260;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isOpen)
            {
                return isOpen ? new GridLength(LastWidth >= 160 ? LastWidth : 260, GridUnitType.Pixel) : new GridLength(0, GridUnitType.Pixel);
            }
            if (value is double width && width >= 160)
            {
                LastWidth = width;
                return new GridLength(width, GridUnitType.Pixel);
            }
            return new GridLength(0, GridUnitType.Pixel);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GridLength gl && gl.IsAbsolute && gl.Value >= 160)
            {
                LastWidth = gl.Value;
                return gl.Value;
            }
            return null;
        }
    }

    public class GitStatusBrushConverter : IValueConverter
    {
        public static readonly GitStatusBrushConverter Instance = new();
        private static readonly IBrush GreenBrush = new ImmutableSolidColorBrush(Color.Parse("#89D185"));
        private static readonly IBrush YellowBrush = new ImmutableSolidColorBrush(Color.Parse("#CCA700"));
        private static readonly IBrush RedBrush = new ImmutableSolidColorBrush(Color.Parse("#F14C4C"));
        private static readonly IBrush GrayBrush = new ImmutableSolidColorBrush(Color.Parse("#888888"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GitFileStatus status)
            {
                return status switch
                {
                    GitFileStatus.Untracked => GreenBrush,
                    GitFileStatus.Added => GreenBrush,
                    GitFileStatus.Modified => YellowBrush,
                    GitFileStatus.Renamed => YellowBrush,
                    GitFileStatus.Copied => YellowBrush,
                    GitFileStatus.Deleted => RedBrush,
                    GitFileStatus.Conflicted => RedBrush,
                    _ => GrayBrush
                };
            }
            return GrayBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToChevronIconConverter : IValueConverter
    {
        public static readonly BoolToChevronIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (Application.Current != null && Application.Current.Resources.TryGetResource(value is true ? "IconChevronDown" : "IconChevronRight", null, out var res))
            {
                return res;
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToToggleBackgroundConverter : IValueConverter
    {
        public static readonly BoolToToggleBackgroundConverter Instance = new();
        private static readonly IBrush ActiveBrush = new ImmutableSolidColorBrush(Color.Parse("#04395E"));
        private static readonly IBrush InactiveBrush = new ImmutableSolidColorBrush(Colors.Transparent);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? ActiveBrush : InactiveBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToToggleBorderBrushConverter : IValueConverter
    {
        public static readonly BoolToToggleBorderBrushConverter Instance = new();
        private static readonly IBrush ActiveBrush = new ImmutableSolidColorBrush(Color.Parse("#0078D4"));
        private static readonly IBrush InactiveBrush = new ImmutableSolidColorBrush(Colors.Transparent);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? ActiveBrush : InactiveBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PaletteItemIconConverter : IValueConverter
    {
        public static readonly PaletteItemIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string key = value as string ?? "IconCommand";
            if (Application.Current != null && Application.Current.Resources.TryGetResource(key, null, out var res))
            {
                return res;
            }
            if (Application.Current != null && Application.Current.Resources.TryGetResource("IconCommand", null, out var fallback))
            {
                return fallback;
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PaletteItemBrushConverter : IValueConverter
    {
        public static readonly PaletteItemBrushConverter Instance = new();

        private static readonly IBrush BlueBrush = new ImmutableSolidColorBrush(Color.Parse("#0078D4"));
        private static readonly IBrush GreenBrush = new ImmutableSolidColorBrush(Color.Parse("#4EC9B0"));
        private static readonly IBrush OrangeBrush = new ImmutableSolidColorBrush(Color.Parse("#F05032"));
        private static readonly IBrush YellowBrush = new ImmutableSolidColorBrush(Color.Parse("#CCA700"));
        private static readonly IBrush PurpleBrush = new ImmutableSolidColorBrush(Color.Parse("#C586C0"));
        private static readonly IBrush LightBlueBrush = new ImmutableSolidColorBrush(Color.Parse("#75BEFF"));
        private static readonly IBrush GrayBrush = new ImmutableSolidColorBrush(Color.Parse("#CCCCCC"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string key)
            {
                if (key.Contains("Debug") || key.Contains("Play") || key.Contains("Step") || key.Contains("Restart")) return GreenBrush;
                if (key.Contains("Git") || key.Contains("Branch") || key.Contains("SourceControl")) return OrangeBrush;
                if (key.Contains("Remote") || key.Contains("Ssh") || key.Contains("Server")) return LightBlueBrush;
                if (key.Contains("Terminal")) return YellowBrush;
                if (key.Contains("Extension")) return PurpleBrush;
                if (key.Contains("Warning")) return YellowBrush;
                if (key.Contains("Error")) return OrangeBrush;
                if (key.Contains("Settings")) return GrayBrush;
                if (key.Contains("File") || key.Contains("Folder")) return GrayBrush;
                return BlueBrush;
            }
            return BlueBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

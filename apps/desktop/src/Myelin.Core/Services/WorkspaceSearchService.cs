using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Myelin.Core.Services
{
    public class SearchMatchItem
    {
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string LineText { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string PrefixText { get; set; } = string.Empty;
        public string SuffixText { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public class SearchFileResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string DirectoryPath => Path.GetDirectoryName(RelativePath) ?? string.Empty;
        public List<SearchMatchItem> Matches { get; set; } = new();
        public int MatchCount => Matches.Count;
        public bool IsExpanded { get; set; } = true;
    }

    public class SearchOptions
    {
        public string Query { get; set; } = string.Empty;
        public bool MatchCase { get; set; } = false;
        public bool MatchWholeWord { get; set; } = false;
        public bool UseRegex { get; set; } = false;
        public string IncludePattern { get; set; } = string.Empty;
        public string ExcludePattern { get; set; } = string.Empty;
        public int MaxResults { get; set; } = 2000;
    }

    public class WorkspaceSearchService
    {
        public static readonly WorkspaceSearchService Instance = new();

        private static readonly HashSet<string> DefaultIgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "target", "bin", "obj", ".vs", ".idea", ".vscode", "dist", "build"
        };

        private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".pdb", ".so", ".dylib", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg",
            ".woff", ".woff2", ".ttf", ".eot", ".zip", ".tar", ".gz", ".7z", ".pdf", ".mp4", ".mp3"
        };

        public async Task<List<SearchFileResult>> SearchAsync(string rootPath, SearchOptions options, CancellationToken ct = default)
        {
            var results = new List<SearchFileResult>();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath) || string.IsNullOrEmpty(options.Query))
            {
                return results;
            }

            Regex regex;
            try
            {
                string pattern = options.Query;
                if (!options.UseRegex)
                {
                    pattern = Regex.Escape(pattern);
                }
                if (options.MatchWholeWord)
                {
                    pattern = $@"\b{pattern}\b";
                }

                var regexOptions = RegexOptions.Compiled;
                if (!options.MatchCase)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                regex = new Regex(pattern, regexOptions);
            }
            catch
            {
                return results;
            }

            int totalMatches = 0;
            var files = GetSearchableFiles(rootPath, options);

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested || totalMatches >= options.MaxResults) break;

                    try
                    {
                        var fileMatches = new List<SearchMatchItem>();
                        int lineNum = 1;

                        using var reader = new StreamReader(file);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (ct.IsCancellationRequested) break;

                            var matches = regex.Matches(line);
                            foreach (Match m in matches)
                            {
                                int col = m.Index + 1;
                                int prefixLen = Math.Min(m.Index, 30);
                                int suffixLen = Math.Min(line.Length - (m.Index + m.Length), 40);

                                string prefix = line.Substring(m.Index - prefixLen, prefixLen).TrimStart();
                                string suffix = line.Substring(m.Index + m.Length, suffixLen).TrimEnd();

                                fileMatches.Add(new SearchMatchItem
                                {
                                    LineNumber = lineNum,
                                    ColumnNumber = col,
                                    LineText = line.Trim(),
                                    MatchText = m.Value,
                                    PrefixText = prefix,
                                    SuffixText = suffix,
                                    FilePath = file
                                });

                                totalMatches++;
                                if (totalMatches >= options.MaxResults) break;
                            }

                            lineNum++;
                            if (totalMatches >= options.MaxResults) break;
                        }

                        if (fileMatches.Count > 0)
                        {
                            string rel = Path.GetRelativePath(rootPath, file);
                            results.Add(new SearchFileResult
                            {
                                FilePath = file,
                                RelativePath = rel,
                                Matches = fileMatches
                            });
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }, ct);

            return results;
        }

        private IEnumerable<string> GetSearchableFiles(string rootPath, SearchOptions options)
        {
            var files = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();

                try
                {
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(subDir);
                        if (!DefaultIgnoredDirs.Contains(name) && !name.StartsWith('.'))
                        {
                            queue.Enqueue(subDir);
                        }
                    }

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(file);
                        if (!BinaryExtensions.Contains(ext))
                        {
                            files.Add(file);
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible folders
                }
            }

            return files;
        }
    }
}

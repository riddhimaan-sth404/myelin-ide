using System;
using System.IO;

namespace Myelin.Core.Models
{
    public enum GitFileStatus
    {
        Untracked,
        Modified,
        Added,
        Deleted,
        Renamed,
        Copied,
        Ignored,
        Conflicted
    }

    public class GitFileItem
    {
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(RelativePath);
        public string DirectoryPath => Path.GetDirectoryName(RelativePath) ?? string.Empty;
        public GitFileStatus Status { get; set; } = GitFileStatus.Modified;
        public bool IsStaged { get; set; } = false;

        public string StatusChar => Status switch
        {
            GitFileStatus.Untracked => "U",
            GitFileStatus.Added => "A",
            GitFileStatus.Deleted => "D",
            GitFileStatus.Renamed => "R",
            GitFileStatus.Copied => "C",
            GitFileStatus.Conflicted => "!",
            _ => "M"
        };

        public string StatusTooltip => Status switch
        {
            GitFileStatus.Untracked => "Untracked",
            GitFileStatus.Added => "Index Added",
            GitFileStatus.Deleted => "Deleted",
            GitFileStatus.Renamed => "Renamed",
            GitFileStatus.Copied => "Copied",
            GitFileStatus.Conflicted => "Merge Conflict",
            _ => "Modified"
        };
    }

    public class GitStatusResult
    {
        public bool IsRepository { get; set; } = false;
        public string CurrentBranch { get; set; } = "main";
        public System.Collections.Generic.List<GitFileItem> StagedFiles { get; set; } = new();
        public System.Collections.Generic.List<GitFileItem> WorkingFiles { get; set; } = new();
        public System.Collections.Generic.List<string> Branches { get; set; } = new();
        public int AheadCount { get; set; } = 0;
        public int BehindCount { get; set; } = 0;
    }
}

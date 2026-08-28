using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Myelin.Core.Models;

namespace Myelin.Core.Services
{
    public class GitService
    {
        public static readonly GitService Instance = new();

        /// <summary>
        /// Runs a git CLI process asynchronously in the given working directory.
        /// </summary>
        public async Task<(int ExitCode, string StdOut, string StdErr)> RunGitCommandAsync(string workingDir, string arguments)
        {
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
            {
                return (-1, string.Empty, "Directory does not exist");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outTask, errTask, process.WaitForExitAsync());

                return (process.ExitCode, await outTask, await errTask);
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        public async Task<bool> IsGitInstalledAsync()
        {
            try
            {
                var (code, stdout, _) = await RunGitCommandAsync(Environment.CurrentDirectory, "--version");
                return code == 0 && stdout.StartsWith("git version");
            }
            catch
            {
                return false;
            }
        }

        public bool IsRepository(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return false;
            return Directory.Exists(Path.Combine(rootPath, ".git")) || File.Exists(Path.Combine(rootPath, ".git"));
        }

        public async Task<GitStatusResult> GetStatusAsync(string rootPath)
        {
            var result = new GitStatusResult();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return result;
            }

            // Check if repo
            var (revCode, revOut, _) = await RunGitCommandAsync(rootPath, "rev-parse --is-inside-work-tree");
            if (revCode != 0 || !revOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result.IsRepository = false;
                return result;
            }

            result.IsRepository = true;

            // Get Current Branch
            var (branchCode, branchOut, _) = await RunGitCommandAsync(rootPath, "rev-parse --abbrev-ref HEAD");
            result.CurrentBranch = branchCode == 0 ? branchOut.Trim() : "HEAD";

            // Get All Local Branches
            var (listCode, listOut, _) = await RunGitCommandAsync(rootPath, "branch --list --no-color");
            if (listCode == 0)
            {
                var lines = listOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string b = line.Trim().TrimStart('*').Trim();
                    if (!string.IsNullOrEmpty(b) && !result.Branches.Contains(b))
                    {
                        result.Branches.Add(b);
                    }
                }
            }

            // Get Ahead / Behind status if tracking branch exists
            var (abCode, abOut, _) = await RunGitCommandAsync(rootPath, "rev-list --left-right --count HEAD...@{u}");
            if (abCode == 0)
            {
                var parts = abOut.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out int ahead) && int.TryParse(parts[1], out int behind))
                {
                    result.AheadCount = ahead;
                    result.BehindCount = behind;
                }
            }

            // Parse Porcelain Status (v1)
            var (statusCode, statusOut, _) = await RunGitCommandAsync(rootPath, "status --porcelain=v1 -uall");
            if (statusCode == 0 && !string.IsNullOrEmpty(statusOut))
            {
                ParsePorcelainStatus(rootPath, statusOut, result);
            }

            return result;
        }

        public void ParsePorcelainStatus(string rootPath, string statusOutput, GitStatusResult result)
        {
            var lines = statusOutput.Split('\n');
            foreach (var rawLine in lines)
            {
                if (rawLine.Length < 3) continue;

                char x = rawLine[0]; // Index (staged)
                char y = rawLine[1]; // Working tree (unstaged)
                string path = rawLine.Substring(3).Trim();

                // Handle renamed files "old -> new"
                if (path.Contains(" -> "))
                {
                    var arrowParts = path.Split(" -> ");
                    if (arrowParts.Length > 1) path = arrowParts[1].Trim();
                }

                // Strip quotes if git quoted non-ascii
                if (path.StartsWith('"') && path.EndsWith('"') && path.Length >= 2)
                {
                    path = path.Substring(1, path.Length - 2);
                }

                string fullPath = Path.Combine(rootPath, path);

                // 1. Untracked files (??)
                if (x == '?' && y == '?')
                {
                    result.WorkingFiles.Add(new GitFileItem
                    {
                        RelativePath = path,
                        FullPath = fullPath,
                        Status = GitFileStatus.Untracked,
                        IsStaged = false
                    });
                    continue;
                }

                // 2. Staged changes (X is not space or '?')
                if (x != ' ' && x != '?')
                {
                    var status = x switch
                    {
                        'A' => GitFileStatus.Added,
                        'D' => GitFileStatus.Deleted,
                        'R' => GitFileStatus.Renamed,
                        'C' => GitFileStatus.Copied,
                        'U' => GitFileStatus.Conflicted,
                        _ => GitFileStatus.Modified
                    };

                    result.StagedFiles.Add(new GitFileItem
                    {
                        RelativePath = path,
                        FullPath = fullPath,
                        Status = status,
                        IsStaged = true
                    });
                }

                // 3. Working tree changes (Y is not space or '?')
                if (y != ' ' && y != '?')
                {
                    var status = y switch
                    {
                        'D' => GitFileStatus.Deleted,
                        'U' => GitFileStatus.Conflicted,
                        _ => GitFileStatus.Modified
                    };

                    result.WorkingFiles.Add(new GitFileItem
                    {
                        RelativePath = path,
                        FullPath = fullPath,
                        Status = status,
                        IsStaged = false
                    });
                }
            }
        }

        public async Task<bool> StageFileAsync(string rootPath, string relativePath)
        {
            var (code, _, _) = await RunGitCommandAsync(rootPath, $"add -- \"{relativePath}\"");
            return code == 0;
        }

        public async Task<bool> StageAllAsync(string rootPath)
        {
            var (code, _, _) = await RunGitCommandAsync(rootPath, "add -A");
            return code == 0;
        }

        public async Task<bool> UnstageFileAsync(string rootPath, string relativePath)
        {
            var (code, _, _) = await RunGitCommandAsync(rootPath, $"restore --staged -- \"{relativePath}\"");
            if (code != 0)
            {
                var (resetCode, _, _) = await RunGitCommandAsync(rootPath, $"reset HEAD -- \"{relativePath}\"");
                return resetCode == 0;
            }
            return true;
        }

        public async Task<bool> UnstageAllAsync(string rootPath)
        {
            var (code, _, _) = await RunGitCommandAsync(rootPath, "restore --staged .");
            if (code != 0)
            {
                var (resetCode, _, _) = await RunGitCommandAsync(rootPath, "reset HEAD");
                return resetCode == 0;
            }
            return true;
        }

        public async Task<bool> DiscardChangesAsync(string rootPath, string relativePath, bool isUntracked)
        {
            if (isUntracked)
            {
                string fullPath = Path.Combine(rootPath, relativePath);
                try
                {
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    else if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            var (code, _, _) = await RunGitCommandAsync(rootPath, $"restore -- \"{relativePath}\"");
            if (code != 0)
            {
                var (coCode, _, _) = await RunGitCommandAsync(rootPath, $"checkout -- \"{relativePath}\"");
                return coCode == 0;
            }
            return true;
        }

        public async Task<(bool Success, string Output)> CommitAsync(string rootPath, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return (false, "Commit message cannot be empty");
            }

            // Clean single/multi line message for command line
            string safeMsg = message.Replace("\"", "\\\"");
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, $"commit -m \"{safeMsg}\"");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> PushAsync(string rootPath)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, "push");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> PullAsync(string rootPath)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, "pull");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> FetchAsync(string rootPath)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, "fetch");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> CheckoutBranchAsync(string rootPath, string branchName)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, $"checkout \"{branchName}\"");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> CreateBranchAsync(string rootPath, string newBranchName)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, $"checkout -b \"{newBranchName}\"");
            return (code == 0, code == 0 ? stdout : stderr);
        }

        public async Task<(bool Success, string Output)> InitRepositoryAsync(string rootPath)
        {
            var (code, stdout, stderr) = await RunGitCommandAsync(rootPath, "init");
            return (code == 0, code == 0 ? stdout : stderr);
        }
    }
}

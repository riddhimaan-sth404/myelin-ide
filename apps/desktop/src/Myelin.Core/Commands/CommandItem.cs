using System;
using System.Collections.Generic;
using System.Linq;

namespace Myelin.Core.Commands
{
    public class CommandItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Shortcut { get; set; }
        public string? IconKey { get; set; }
        public Action? Action { get; set; }

        public string DisplayText => string.IsNullOrEmpty(Category) ? Title : $"{Category}: {Title}";

        public string ResolveIconKey()
        {
            if (!string.IsNullOrEmpty(IconKey)) return IconKey;

            var cat = Category.ToLowerInvariant();
            var id = Id.ToLowerInvariant();

            if (id.Contains("breakpoint")) return "IconBreakpoint";
            if (id.Contains("pause")) return "IconPause";
            if (id.Contains("step_over")) return "IconStepOver";
            if (id.Contains("step_into")) return "IconStepInto";
            if (id.Contains("step_out")) return "IconStepOut";
            if (id.Contains("restart")) return "IconRestart";
            if (id.Contains("stop")) return "IconStop";
            if (id.Contains("debug") || cat.Contains("debug")) return "IconDebug";
            if (id.Contains("ssh")) return "IconSsh";
            if (id.Contains("port")) return "IconServer";
            if (id.Contains("remote") || cat.Contains("remote")) return "IconRemote";
            if (id.Contains("branch")) return "IconBranch";
            if (id.Contains("git") || cat.Contains("source control") || cat.Contains("git")) return "IconSourceControl";
            if (id.Contains("terminal") || cat.Contains("terminal")) return "IconTerminal";
            if (id.Contains("new_file") || id.EndsWith(".new")) return "IconNewFile";
            if (id.Contains("folder")) return "IconFolder";
            if (id.Contains("file") || cat.Contains("file")) return "IconFile";
            if (id.Contains("setting") || id.Contains("pref") || cat.Contains("setting") || cat.Contains("pref")) return "IconSettings";
            if (id.Contains("ext") || cat.Contains("ext")) return "IconExtensions";
            if (id.Contains("search") || id.Contains("find") || cat.Contains("search")) return "IconSearch";
            if (id.Contains("cargo") || id.Contains("build") || id.Contains("run") || cat.Contains("cargo") || cat.Contains("build")) return "IconPlay";
            if (id.Contains("undo") || id.Contains("discard")) return "IconDiscard";
            if (id.Contains("redo") || id.Contains("sync") || id.Contains("refresh")) return "IconSync";
            if (id.Contains("problem") || id.Contains("warn")) return "IconWarning";

            return "IconCommand";
        }
    }

    public class CommandRegistry
    {
        public static readonly CommandRegistry Instance = new();
        private readonly List<CommandItem> _commands = new();

        public IReadOnlyList<CommandItem> Commands => _commands;

        public void Register(string id, string category, string title, string? shortcut, Action action, string? iconKey = null)
        {
            _commands.RemoveAll(c => c.Id == id);
            _commands.Add(new CommandItem
            {
                Id = id,
                Category = category,
                Title = title,
                Shortcut = shortcut,
                IconKey = iconKey,
                Action = action
            });
        }

        public List<CommandItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _commands.ToList();
            }

            string cleanQuery = query.Trim().ToLowerInvariant();
            if (cleanQuery.StartsWith(">"))
            {
                cleanQuery = cleanQuery.Substring(1).TrimStart();
            }

            return _commands
                .Where(c => c.DisplayText.ToLowerInvariant().Contains(cleanQuery) ||
                            (!string.IsNullOrEmpty(c.Shortcut) && c.Shortcut.ToLowerInvariant().Contains(cleanQuery)))
                .OrderBy(c => c.DisplayText.ToLowerInvariant().IndexOf(cleanQuery, StringComparison.Ordinal))
                .ToList();
        }
    }
}

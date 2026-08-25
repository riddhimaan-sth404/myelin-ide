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
        public Action? Action { get; set; }

        public string DisplayText => string.IsNullOrEmpty(Category) ? Title : $"{Category}: {Title}";
    }

    public class CommandRegistry
    {
        public static readonly CommandRegistry Instance = new();
        private readonly List<CommandItem> _commands = new();

        public IReadOnlyList<CommandItem> Commands => _commands;

        public void Register(string id, string category, string title, string? shortcut, Action action)
        {
            _commands.RemoveAll(c => c.Id == id);
            _commands.Add(new CommandItem
            {
                Id = id,
                Category = category,
                Title = title,
                Shortcut = shortcut,
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

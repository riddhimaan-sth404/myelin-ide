using System;

namespace Myelin.Core.Models
{
    public class TerminalProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public string Icon { get; set; } = "IconTerminal";
        public bool IsDefault { get; set; }

        public override string ToString() => Name;
    }
}

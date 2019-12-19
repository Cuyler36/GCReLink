using System.Collections.Generic;

namespace GCReLink
{
    public sealed class SymbolContainer
    {
        public readonly List<SymbolEntry> Entries = new List<SymbolEntry>();
        public readonly string Name;
        public readonly Dictionary<string, int> ContainerSectionAlignment = new Dictionary<string, int>();

        public SymbolContainer(string name)
        {
            Name = name;
        }

        public void AddEntry(SymbolEntry entry)
        {
            Entries.Add(entry);
        }
    }
}

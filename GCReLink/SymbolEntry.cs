using GCNToolKit.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GCReLink
{
    public sealed class SymbolEntry
    {
        public string Name { get; private set; }
        public readonly string Container;
        public readonly string SectionName;
        public readonly int SectionIdx;
        public readonly int Size;
        public int SectionOffset;
        public readonly int Alignment;

        public readonly List<SymbolReference> References = new List<SymbolReference>(10);
        public readonly SymbolContainer SymbolContainer;
        public readonly int FileIdx;
        public readonly byte[] Data;
        public string RelativeFilePath { get; private set; }
        public readonly bool IsExecutable;

        public SymbolEntry(SymbolContainer symContainer, string name, string container, string sectionName, int sectionIdx, int size, int fileOffset, int alignment, int fileIdx, in byte[] data)
        {
            SymbolContainer = symContainer;
            Name = name;
            Container = container;
            SectionName = sectionName;
            SectionIdx = sectionIdx;
            Size = size;
            SectionOffset = fileOffset;
            Alignment = alignment;
            FileIdx = fileIdx;
            Data = data;
            RelativeFilePath = Path.Combine(container, sectionName, $"{name}.bin");
            IsExecutable = sectionName == ".text";
        }

        public void AddReference(SymbolEntry other, int otherModuleId, int refOffset, int thisOffset, RelocatableModule.RelocationType relocType)
        {
            References.Add(new SymbolReference(this, other, otherModuleId, other.SectionIdx, refOffset, thisOffset, relocType));
        }

        public void SetName(in string name)
        { 
            Name = Path.GetFileNameWithoutExtension(name);
            RelativeFilePath = Path.Combine(Container, SectionName, $"{Name}.bin");
        }

        public override string ToString() => $"{Container}/{Name}";
    }
}

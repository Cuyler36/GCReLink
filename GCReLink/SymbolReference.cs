using GCNToolKit.Formats;

namespace GCReLink
{
    public sealed class SymbolReference
    {
        public readonly RelocatableModule.RelocationType RelocationType;

        public readonly SymbolEntry ReferencedEntry;
        public readonly int ReferencedModuleId;
        public readonly int ReferencedSectionId;
        public readonly int ReferenceOffset;

        public readonly SymbolEntry ParentSymbol;
        public readonly int WriteOffset;

        public SymbolReference(SymbolEntry parentSymbol, SymbolEntry entry, int moduleId, int sectId, int refOffset, int writeOffset, RelocatableModule.RelocationType relocType)
        {
            ReferencedEntry = entry;
            ReferenceOffset = refOffset;
            ReferencedModuleId = moduleId;
            ReferencedSectionId = sectId;
            ParentSymbol = parentSymbol;
            WriteOffset = writeOffset;
            RelocationType = relocType;
        }
    }
}

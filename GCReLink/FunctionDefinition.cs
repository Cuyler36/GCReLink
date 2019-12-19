using System.Collections.Generic;

namespace GCReLink
{
    public sealed class FunctionDefinition
    {
        public readonly string Name;
        public readonly int Id;
        public readonly int Offset;
        public readonly List<FunctionReference> ReferencedFunctions;
        public SymbolEntry Symbol;

        public FunctionDefinition(in string name, int id, int offset, SymbolEntry symbol)
        {
            Name = name;
            Id = id;
            Offset = offset;
            Symbol = symbol;
            ReferencedFunctions = new List<FunctionReference>();
        }

        public void AddReference(in FunctionReference reference) => ReferencedFunctions.Add(reference);
    }

    public sealed class FunctionReference
    {
        public readonly FunctionDefinition Function;
        public readonly int ReferenceOffset;

        public FunctionReference(FunctionDefinition func, int offset)
        {
            Function = func;
            ReferenceOffset = offset;
        }
    }
}

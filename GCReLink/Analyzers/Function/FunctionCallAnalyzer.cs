using System;
using System.Collections.Generic;

namespace GCReLink.Analyzers.Function
{
    public static class FunctionCallAnalyzer
    {
        private const int opCodeMask = 0x3F << 26; // 0xFC000000
        private const int offsetMask = 0xFFFFFF << 2; // 0x03FFFFFC
        private const int absoluteAddrMask = 1 << 1; // 0x00000002
        private const int linkReturnMask = 1; // 0x00000001

        private const int branchOpCode = 18 << 26; // 0x48000000

        public static List<FunctionDefinition> UnlinkReferencedFunctions(in List<SymbolEntry> symbols)
        {
            var funcDefs = new List<FunctionDefinition>();
            var textSymbols = symbols.FindAll(o => o.SectionName == ".text");
            var funcIdx = 1;

            // Create a function definition for each function.
            foreach (var func in textSymbols)
            {
                var funcDef = new FunctionDefinition(func.Name, funcIdx++, func.SectionOffset, func);
                funcDefs.Add(funcDef);
            }

            // Iterate through each instruction in each .text symbol
            foreach (var symbol in textSymbols)
            {
                var funcDef = funcDefs.Find(o => o.Offset == symbol.SectionOffset && o.Name == symbol.Name);
                var numInstructions = symbol.Data.Length / sizeof(uint);
                var idx = 0; // Start at 1 to avoid mis-marking linked symbols
                for (var i = 0; i < numInstructions; i++, idx += 4)
                {
                    var instruction = (symbol.Data[idx] << 24) | (symbol.Data[idx + 1] << 16) | (symbol.Data[idx + 2]) << 8 | symbol.Data[idx + 3];
                    // Only parse instructions that are branch instructions with a linked return
                    // NOTE: This won't catch compiler optimized far-branches where the link register is restored before jumping.
                    if ((instruction & opCodeMask) == branchOpCode && (instruction & linkReturnMask) == linkReturnMask)
                    {
                        var branchOffset = instruction & offsetMask;
                        // Sign extend
                        if ((branchOffset & 0x02000000) != 0)
                            branchOffset = (int)(0xFC000000u | branchOffset);
                        if ((instruction & absoluteAddrMask) == absoluteAddrMask)
                        {
                            throw new Exception("PANIC! Absolute jumps not handled.");
                        }

                        // These are imported function calls. Skip them.
                        if (branchOffset == 0)
                            continue;

                        // Calculate relative jump within section
                        // NOTE: This should point to the begining of the desired function.
                        var desiredFunctionSectionOffset = (symbol.SectionOffset + idx) + branchOffset;
                        // Find the desired function
                        var desiredFuncSymbol = textSymbols.Find(o => o.SectionOffset == desiredFunctionSectionOffset);
                        if (desiredFuncSymbol == null)
                        {
                            throw new Exception("PANIC! Couldn't find the referenced function!");
                        }
                        var desiredFuncDef = funcDefs.Find(o => o.Offset == desiredFuncSymbol.SectionOffset && o.Name == desiredFuncSymbol.Name);

                        // NOTE: This'll also add recursive functions only once.
                        funcDef.AddReference(new FunctionReference(desiredFuncDef, idx));

                        // Update the reference as the id of the function rather than the actual relative offset.
                        var unlinkedBranchInstr = (instruction & ~offsetMask) | (desiredFuncDef.Id << 2);
                        // TODO: This is slow. Fix.
                        var bytes = BitConverter.GetBytes(unlinkedBranchInstr);
                        Array.Reverse(bytes);
                        Buffer.BlockCopy(bytes, 0, symbol.Data, idx, 4);
                    }
                }
            }

            return funcDefs;
        }

        public static void LinkReferencedFunctions(in SymbolEntry symbol, in List<FunctionDefinition> funcDefs)
        {
            if (symbol?.Data != null && symbol.SectionName == ".text")
            {
                var numInstructions = symbol.Data.Length / sizeof(uint);
                var idx = 0;
                for (var i = 0; i < numInstructions; i++, idx += 4)
                {
                    var instruction = (symbol.Data[idx] << 24) | (symbol.Data[idx + 1] << 16) | (symbol.Data[idx + 2]) << 8 | symbol.Data[idx + 3];
                    // Only parse instructions that are branch instructions with a linked return
                    // NOTE: This won't catch compiler optimized far-branches where the link register is restored before jumping.
                    if ((instruction & opCodeMask) == branchOpCode && (instruction & linkReturnMask) == linkReturnMask)
                    {
                        var funcId = (instruction & offsetMask) >> 2;
                        
                        // Don't bother with imported function calls
                        if (funcId < 1)
                            continue;
                        if (funcId > funcDefs.Count)
                            throw new Exception("The referenced function id is out of bounds!");

                        var referencedFunc = funcDefs.Find(o => o.Id == funcId);
                        if (referencedFunc == null)
                            throw new Exception($"No function with id {funcId} was found!");
                        var desiredFunctionSectionOffset = referencedFunc.Symbol.SectionOffset - (symbol.SectionOffset + idx);

                        // Generate the correctly linked instruction
                        var linkedBranchInstr = (instruction & ~offsetMask) | (desiredFunctionSectionOffset & offsetMask);

                        // TODO: This is slow. Fix.
                        var bytes = BitConverter.GetBytes(linkedBranchInstr);
                        Array.Reverse(bytes);
                        Buffer.BlockCopy(bytes, 0, symbol.Data, idx, 4);
                    }
                }
            }
        }
    }
}

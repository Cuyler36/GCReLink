using BinaryX;
using GCNToolKit.Formats;
using GCNToolKit.Formats.Compression;
using GCReLink.Analyzers.Function;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GCReLink
{
    class Program
    {
        private static void ParseArgs(in string[] args)
        {
            var modeSet = false;
            var rebuild = false;
            var rootDir = "";

            if (args.Length == 0)
            {
                ShowHelp("");
                return;
            }

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-u":
                    case "-unpack":
                        if (modeSet)
                            throw new Exception("You cannot both unpack and rebuild in a single operation! Please select one or the other.");
                        if (args.Length - 1 == i)
                            throw new Exception("You must specify a root directory whose relocatable modules will be unpacked!");
                        rootDir = args[++i];
                        rebuild = false;
                        modeSet = true;
                        break;
                    case "-r":
                    case "-rebuild":
                        if (modeSet)
                            throw new Exception("You cannot both unpack and rebuild in a single operation! Please select one or the other.");
                        if (args.Length - 1 == i)
                           throw new Exception("You must specify a root directory whose contents will be rebuilt into relocatable modules!");
                        rootDir = args[++i];
                        rebuild = true;
                        modeSet = true;
                        break;
                    case "-h":
                    case "-help":
                    default:
                        ShowHelp(args.Length < 2 ? "" : args[++i]);
                        break;
                }
            }

            if (modeSet)
            {
                if (!Directory.Exists(rootDir))
                    throw new Exception("The root directory supplied doesn't exist!");

                if (rebuild)
                    RebuildModules(rootDir);
                else
                    DumpModules(rootDir);
            }
        }

        private static void ShowHelp(in string cmd)
        {
            Console.WriteLine("****** GCReLink by Cuyler ******");
            if (string.IsNullOrEmpty(cmd))
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("\tGCReLink.exe -u|-r \"C:\\Modding Folder\"");
            }
            else
            {
                switch (cmd)
                {
                    case "-r":
                    case "-rebuild":
                        Console.WriteLine("Usage:");
                        Console.WriteLine($"\tGCReLink.exe {cmd} \"C:\\Modding Folder\\GCReLink\"");
                        break;
                    case "-u":
                    case "-unpack":
                        Console.WriteLine("Usage:");
                        Console.WriteLine($"\tGCReLink.exe {cmd} \"C:\\Modding Folder\"");
                        break;
                    default:
                        Console.WriteLine("Usage:");
                        Console.WriteLine("\tGCReLink.exe -u|-r \"C:\\Modding Folder\"");
                        break;
                }
            }
        }

        private static ModuleInfo LoadModuleInfo(in string rootContentDir) => new ModuleInfo(rootContentDir);

        private static Dictionary<string, SectionInfo> LoadSectionInfo(in string rootContentDir)
        {
            var sectionInfoFilePath = Path.Combine(rootContentDir, "sections.txt");
            if (!File.Exists(sectionInfoFilePath))
                throw new Exception("The section info file wasn't found! Cannot continue with relinking.");
            return File.ReadAllLines(sectionInfoFilePath).Select(line => new SectionInfo(int.Parse(line.Substring(0, 2)), line.Substring(12), int.Parse(line.Substring(3, 8), NumberStyles.HexNumber))).ToDictionary(o => o.Name);
        }

        private static Dictionary<string, int> LoadFunctionDefinitions(in string rootContentDir)
        {
            var funcDefFilePath = Path.Combine(rootContentDir, "function_definitions.txt");
            if (!File.Exists(funcDefFilePath))
                throw new Exception("The function definitions file wasn't found! Cannot continue with relinking.");
            return File.ReadAllLines(funcDefFilePath).ToDictionary(line => line.Substring(7), line => int.Parse(line.Substring(0, 6)));
        }

        private static (List<SymbolContainer>, Dictionary<string, SymbolEntry>) LoadFilesFromDirectory(in string rootContentDir, in Dictionary<string, SectionInfo> sections)
        {
            var containerList = new List<SymbolContainer>();
            var symbolsDict = new Dictionary<string, SymbolEntry>();
            // Iterate through each module/object container
            foreach (var module in Directory.GetDirectories(rootContentDir))
            {
                var moduleName = Path.GetFileName(module);
                var container = new SymbolContainer(moduleName);
                // Now go through each section for the current module
                foreach (var section in Directory.GetDirectories(module))
                {
                    // Files are next.
                    var sectionName = Path.GetFileName(section);
                    if (!sections.TryGetValue(sectionName, out var sectionInfo))
                        throw new Exception($"Couldn't find section info for section {sectionName}!");
                    foreach (var file in Directory.GetFiles(section))
                    {
                        var fileData = File.ReadAllBytes(file);
                        var name = Path.GetFileNameWithoutExtension(file);

                        // Scrape name for subfile index
                        var fileIdx = -1;
                        if (name.StartsWith("FILE__"))
                        {
                            name = name.Substring(6);
                            var nameStart = name.IndexOf("_");
                            fileIdx = int.Parse(name.Substring(0, nameStart));
                            name = name.Substring(nameStart + 1);
                        }

                        // Scrape alignment from name
                        var alignment = -1;
                        var alignIdx = name.IndexOf("___ALIGN_");
                        if (alignIdx > -1)
                        {
                            alignment = int.Parse(name.Substring(alignIdx + 9));
                            name = Regex.Replace(name, @"___ALIGN_\d+", "");
                        }

                        // Ensure the container has the largest alignment set
                        if (container.ContainerSectionAlignment.ContainsKey(sectionName))
                        {
                            if (container.ContainerSectionAlignment[sectionName] < alignment)
                                container.ContainerSectionAlignment[sectionName] = alignment;
                        }
                        else
                        {
                            container.ContainerSectionAlignment[sectionName] = alignment;
                        }

                        var symbol = new SymbolEntry(container, name, container.Name, sectionName, sectionInfo.Id, fileData.Length, -1, alignment, fileIdx, fileData);
                        container.AddEntry(symbol);
                        symbolsDict.Add(symbol.RelativeFilePath, symbol);
                    }
                }

                containerList.Add(container);
            }

            return (containerList, symbolsDict);
        }

        private static List<FunctionDefinition> CreateFunctionDefinitions(in List<SymbolContainer> symbolContainers, in Dictionary<string, int> funcDict)
        {
            var funcDefList = new List<FunctionDefinition>();
            foreach (var container in symbolContainers)
            {
                foreach (var entry in container.Entries)
                {
                    if (entry.IsExecutable && funcDict.TryGetValue(entry.RelativeFilePath, out var id))
                    {
                        funcDefList.Add(new FunctionDefinition(entry.Name, id, -1, entry));
                    }
                }
            }
            return funcDefList.OrderBy(o => o.Id).ToList();
        }

        private static Dictionary<string, byte[]> LayoutSections(List<SymbolContainer> symbols, Dictionary<string, SectionInfo> sections, SymbolEntry prologSymbol)
        {
            if (prologSymbol == null)
                throw new ArgumentNullException(nameof(prologSymbol));

            var memStreamDict = new Dictionary<string, MemoryStream>();
            var bssSectSizes = new Dictionary<string, int>();

            // Write prolog to the begining of the section
            memStreamDict[prologSymbol.SectionName] = new MemoryStream();
            memStreamDict[prologSymbol.SectionName].Write(prologSymbol.Data);
            prologSymbol.SectionOffset = 0;

            foreach (var container in symbols)
            {
                // Iterate through the container's entries grouped by section
                var entriesByGroup = container.Entries.GroupBy(o => o.SectionName);
                foreach (var entryGroup in entriesByGroup)
                {
                    if (!entryGroup.Key.Contains("bss"))
                    {
                        if (!memStreamDict.ContainsKey(entryGroup.Key))
                            memStreamDict.Add(entryGroup.Key, new MemoryStream());
                        var sectionStream = memStreamDict[entryGroup.Key];

                        // Align data blob if necessary
                        var containerAlignment = container.ContainerSectionAlignment[entryGroup.Key];
                        if (entryGroup.Key != ".text" && containerAlignment < 8)
                            containerAlignment = 8;
                        var streamAlignment = sectionStream.Position & (containerAlignment - 1);
                        if (containerAlignment > 1 && streamAlignment != 0)
                            sectionStream.Seek(containerAlignment - streamAlignment, SeekOrigin.Current);

                        // Iterate through each section's entries in order of file index to preserve static references
                        // TODO: This is where a static data reference analyzer would come in handy.
                        foreach (var entry in entryGroup.OrderBy(o => o.FileIdx))
                        {
                            if (entry == prologSymbol)
                                continue; // Don't write the prolog data twice.

                            // Ensure write address is aligned to symbol constraints before writing.
                            if (entry.Alignment > 1 && (sectionStream.Position & (entry.Alignment - 1)) != 0)
                                sectionStream.Seek(entry.Alignment - (sectionStream.Position & (entry.Alignment - 1)), SeekOrigin.Current);

                            entry.SectionOffset = (int)sectionStream.Position;
                            sectionStream.Write(entry.Data);
                        }
                    }
                    else
                    {
                        if (!bssSectSizes.ContainsKey(entryGroup.Key))
                            bssSectSizes.Add(entryGroup.Key, 0);

                        // Set the alignment of the container for the upcoming symbols
                        var containerAlignment = container.ContainerSectionAlignment[entryGroup.Key];
                        if (containerAlignment < 8)
                            containerAlignment = 8;
                        var bssAlignment = bssSectSizes[entryGroup.Key] & (containerAlignment - 1);
                        if (containerAlignment > 1 && bssAlignment != 0)
                            bssSectSizes[entryGroup.Key] += containerAlignment - bssAlignment;

                        // Calculate the actual size
                        bssSectSizes[entryGroup.Key] = entryGroup.Aggregate(bssSectSizes[entryGroup.Key], (curr, o) =>
                        {
                            if (o.Alignment > 1 && (curr & (o.Alignment - 1)) != 0)
                                curr = (curr + o.Alignment) & ~(o.Alignment - 1);
                            o.SectionOffset = curr;
                            curr += o.Data.Length;
                            return curr;
                        });
                    }
                }
            }

            // Add sections with no symbols in them
            foreach (var sect in sections)
                if (!memStreamDict.ContainsKey(sect.Key) && !sect.Key.Contains("bss"))
                    memStreamDict.Add(sect.Key, new MemoryStream(new byte[sect.Value.Size]));

            // Update bss section sizes
            foreach (var bssSectKV in bssSectSizes)
                sections[bssSectKV.Key].Size = bssSectKV.Value;

            // Set the new size of each section
            foreach (var sectionKV in sections)
                if (!sectionKV.Key.Contains("bss"))
                    sectionKV.Value.Size = (int)memStreamDict[sectionKV.Key].Length;

            return memStreamDict.ToDictionary(o => o.Key, o => o.Value.ToArray());
        }

        private static void RelinkFunctions(in List<SymbolContainer> containerList, in List<FunctionDefinition> funcDefList, ref byte[] textSectionData)
        {
            foreach (var container in containerList)
            {
                foreach (var entry in container.Entries)
                {
                    if (entry.IsExecutable)
                    {
                        FunctionCallAnalyzer.LinkReferencedFunctions(entry, funcDefList);
                        entry.Data.CopyTo(textSectionData, entry.SectionOffset);
                    }
                }
            }
        }

        private static void CreateSymbolMapFile(in string rootContentDir, in string mapName, Dictionary<string, SectionInfo> sections, Dictionary<string, SymbolEntry> entries)
        {
            var symbolsBySection = entries.Values.GroupBy(o => o.SectionName).ToDictionary(o => o.Key, o => o.OrderBy(x => x.SectionOffset).ToList());
            // Add missing sections
            foreach (var sect in sections)
                if (!symbolsBySection.ContainsKey(sect.Key))
                    symbolsBySection.Add(sect.Key, new List<SymbolEntry>());

            var orderedSymbols = symbolsBySection.OrderBy(o => sections[o.Key].Id);
            SymbolMapParser.OutputMapFile(rootContentDir, mapName, orderedSymbols, sections);
        }

        // TODO: Optimize this
        private static List<SymbolReference> LoadRelocations(in string rootContentDir, in Dictionary<int, Dictionary<string, SymbolEntry>> symbolsByModuleDict, in int moduleId)
        {
            const string relocInfoPattern = @"(.+)\+0x(\w+)\s+\-\>\s+(.+)\s+(.+)\+0x(\w+)";
            const string selfRelocInfoPattern = @"(.+)\+0x(\w+)\s+\-\>\s+(.+)\s+(.+)";

            var symbolDict = symbolsByModuleDict[moduleId];
            var relocationsFilePath = Path.Combine(rootContentDir, "relocations.txt");
            if (!File.Exists(relocationsFilePath))
                throw new Exception("The relocation data file couldn't be located! Relinking cannot continue!");

            using var relocationsStream = File.OpenText(relocationsFilePath);
            var relocations = new List<SymbolReference>();
            var line = "";
            while ((line = relocationsStream.ReadLine()) != null)
            {
                var relocInfoStartIdx = line.IndexOf(' ');
                var relocationType = (RelocatableModule.RelocationType)Enum.Parse(typeof(RelocatableModule.RelocationType), line.Substring(0, relocInfoStartIdx));
                var relocInfo = line.Substring(relocInfoStartIdx + 1);
                var match = Regex.Match(relocInfo, relocInfoPattern);
                if (match.Groups.Count == 1)
                    match = Regex.Match(relocInfo, selfRelocInfoPattern);
                var localFilePath = match.Groups[1].Value;
                var localFileRelocAddr = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber);

                // Look up the current file path as a symbol
                symbolDict.TryGetValue(localFilePath, out var symbol);
                if (symbol == null)
                    throw new Exception($"Couldn't find a matching object with the path: {localFilePath}!");

                var importModuleId = match.Groups[3].Value == "self" ? moduleId : int.Parse(match.Groups[3].Value.Substring(7));
                // Now check to see if our import file is defined
                SymbolEntry importSymbol = null;
                if (symbolsByModuleDict.ContainsKey(importModuleId))
                    symbolsByModuleDict[importModuleId].TryGetValue(match.Groups[4].Value, out importSymbol);
                var importSymbolAddr = match.Groups.Count == 6 ? int.Parse(match.Groups[5].Value, NumberStyles.HexNumber) : 0;
                if (importSymbol != null)
                {
                    var relocation = new SymbolReference(symbol, importSymbol, importModuleId, importSymbol.SectionIdx, importSymbol.SectionOffset + importSymbolAddr, symbol.SectionOffset + localFileRelocAddr, relocationType);
                    relocations.Add(relocation);
                }
                else
                {
                    var importSection = int.Parse(match.Groups[4].Value.Substring(15));
                    var relocation = new SymbolReference(symbol, importSymbol, importModuleId, importSection, importSymbolAddr, symbol.SectionOffset + localFileRelocAddr, relocationType);
                    relocations.Add(relocation);
                }

            }

            return relocations;
        }

        private static void CreateModuleFile(in string rootDir, in string moduleName, in Dictionary<string, SectionInfo> sectionDict, in Dictionary<string, byte[]> sections, in List<SymbolReference> relocations, in int moduleId, in int prologSect)
        {
            // Start by laying out the sections and then the import table and relocation table
            using var binaryFileStream = File.Create(Path.Combine(rootDir, $"{moduleName}.rel"));
            using var binaryWriter = new BinaryWriterX(binaryFileStream, ByteOrder.BigEndian);

            var bssTotal = 0;
            // Write dummy data for the header
            binaryWriter.Write(new byte[0x4C]);
            // Write section table
            var sectionTableSize = sectionDict.Count * 8;
            var sectionOffset = 0x4C + sectionTableSize;
            foreach (var section in sectionDict.OrderBy(o => o.Value.Id))
            {
                // Align to 32-bytes for data sections for things like display lists and DMA areas
                if (section.Key.Contains("data") && (sectionOffset & 0x1F) != 0)
                    sectionOffset = (sectionOffset + 0x20) & (~0x1F);

                if (section.Key.Contains("bss"))
                {
                    binaryWriter.Write(0u);
                    binaryWriter.Write(section.Value.Size);
                    bssTotal += section.Value.Size;
                }
                else if (section.Key == "dummy")
                {
                    binaryWriter.Write(0);
                    binaryWriter.Write(0);
                }
                else
                {
                    var sectionData = sections[section.Key];
                    if (section.Key == ".text" || section.Key == ".init")
                        binaryWriter.Write((int)(sectionOffset | RelocatableModule.Section.SECT_EXEC));
                    else
                        binaryWriter.Write(sectionData.Length == 0 ? 0 : sectionOffset);
                    binaryWriter.Write(sectionData.Length);

                    // Write section data
                    if (sectionData.Length > 0)
                    {
                        var currPos = binaryWriter.Position;
                        binaryWriter.Seek(sectionOffset);
                        binaryWriter.Write(sectionData);
                        binaryWriter.Seek(currPos);
                    }

                    sectionOffset += sectionData.Length;
                    if (section.Key == ".text")
                    {
                        if ((sectionOffset & 3) != 0)
                            sectionOffset = (sectionOffset + 4) & (~3);
                    }
                }
            }

            // Write Imports & Relocations tables

            var importTableOffset = sectionOffset;
            // Ensure we're aligned to 4 bytes
            importTableOffset = (importTableOffset + 4) & (~3);

            // Group relocations by section & begin writing import table & relocation data
            using var importTable = new BinaryWriterX(new MemoryStream(), ByteOrder.BigEndian);
            using var relocTable = new BinaryWriterX(new MemoryStream(), ByteOrder.BigEndian);
            var relocationsByModule = relocations.GroupBy(o => o.ReferencedModuleId);
            var importTableSize = relocationsByModule.Count() * 8;
            var relocationTableOffset = importTableOffset + importTableSize;

            foreach (var module in relocationsByModule)
            {
                // Create import entry
                importTable.Write(module.Key);
                importTable.Write(relocationTableOffset + (int)relocTable.BaseStream.Length);

                // Being writing relocations
                // Start by grouping relocations by section
                var relocationsBySection = module.GroupBy(o => o.ParentSymbol.SectionIdx);
                foreach (var sectionRelocations in relocationsBySection)
                {
                    // Each section needs to start with the R_DOL_SECTION relocation
                    WriteRelocation(relocTable, 0, RelocatableModule.RelocationType.R_DOLPHIN_SECTION, (byte)sectionRelocations.Key, 0);

                    // Sort each section's relocations in ascending order of section offset.
                    var sectRelocsByOffset = sectionRelocations.OrderBy(o => o.WriteOffset);

                    var currOffset = 0;
                    // Now write the relocations since they're in order
                    foreach (var relocation in sectRelocsByOffset)
                    {
                        var offsetFromLast = relocation.WriteOffset - currOffset;
                        while (offsetFromLast > 0xFFFF)
                        {
                            WriteRelocation(relocTable, 0xFFFF, RelocatableModule.RelocationType.R_DOLPHIN_NOP, 0, 0);
                            offsetFromLast -= 0xFFFF;
                        }
                        WriteRelocation(relocTable, (ushort)offsetFromLast, relocation.RelocationType, (byte)relocation.ReferencedSectionId, (uint)relocation.ReferenceOffset);
                        currOffset = relocation.WriteOffset;
                    }
                }

                // Each import relocations end needs to be marked by the R_DOL_END relocation.
                WriteRelocation(relocTable, 0, RelocatableModule.RelocationType.R_DOLPHIN_END, 0, 0);
            }

            // Now that relocations and the module import table have been created we can write the final binary.
            binaryWriter.Seek(importTableOffset);

            // Write imports
            binaryWriter.Write((importTable.BaseStream as MemoryStream).ToArray());

            // Write relocations
            binaryWriter.Write((relocTable.BaseStream as MemoryStream).ToArray());

            // Fill out header with correct info
            binaryWriter.Seek(0);

            binaryWriter.Write(moduleId);
            binaryWriter.Write(0);
            binaryWriter.Write(0);
            binaryWriter.Write(sections.Count);

            binaryWriter.Write(0x4C);
            binaryWriter.Write(0);
            binaryWriter.Write(0);
            binaryWriter.Write(3);

            binaryWriter.Write(bssTotal);
            binaryWriter.Write(relocationTableOffset);
            binaryWriter.Write(importTableOffset);
            binaryWriter.Write((int)importTable.BaseStream.Length);

            binaryWriter.Write((byte)(prologSect < 1 ? 0 : prologSect));
            // TODO: Support epilog & unresolved
            //var epilogInfo = sectionList.Find(o => o.Name == ".epilog");
            binaryWriter.Write((byte)0);
            //var unresolvedInfo = sectionList.Find(o => o.Name == ".unresolved");
            binaryWriter.Write((byte)0);
            binaryWriter.Write((byte)0);
            binaryWriter.Write(0);
            binaryWriter.Write(0);
            binaryWriter.Write(0);

            binaryWriter.Write(32);
            binaryWriter.Write(32);

            binaryWriter.Write(0); // How the hell are we supposed to determine the fix size?

            // We're done!
        }

        private static void RebuildModules(in string rootContentDir)
        {
            // Load all modules' info
            // We need to do this to properly layout relocations between modules
            var moduleInfoDict = new Dictionary<int, ModuleInfo>();
            var sectInfoDict = new Dictionary<int, Dictionary<string, SectionInfo>>();
            var symDict = new Dictionary<int, Dictionary<string, SymbolEntry>>();
            var sectionDataDict = new Dictionary<int, Dictionary<string, byte[]>>();
            foreach (var moduleDir in Directory.GetDirectories(rootContentDir))
            {
                var moduleInfo = LoadModuleInfo(moduleDir);
                var moduleId = moduleInfo.Id;
                moduleInfoDict.Add(moduleId, moduleInfo);
                var sectInfo = LoadSectionInfo(moduleDir);
                sectInfoDict.Add(moduleId, sectInfo);
                var funcInfo = LoadFunctionDefinitions(moduleDir);
                var (containers, symbols) = LoadFilesFromDirectory(moduleDir, sectInfo);
                symDict.Add(moduleId, symbols);
                var funcDefList = CreateFunctionDefinitions(containers, funcInfo);
                var prologSymbol = funcDefList[moduleInfo.PrologFunctionIdx - 1].Symbol;
                if (prologSymbol == null)
                    throw new Exception("Couldn't locate the prolog function! Relinking cannot continue!");
                var sectionData = LayoutSections(containers, sectInfo, prologSymbol);
                var textSectionData = sectionData[".text"];
                RelinkFunctions(containers, funcDefList, ref textSectionData);
                CreateSymbolMapFile(rootContentDir, Path.GetFileName(moduleDir), sectInfo, symbols);
                sectionDataDict.Add(moduleId, sectionData);
            }

            // Now that we've got all the modules ready, we can begin creating the relocations.
            foreach (var moduleDir in Directory.GetDirectories(rootContentDir))
            {
                var moduleInfo = LoadModuleInfo(moduleDir);
                var moduleId = moduleInfo.Id;
                var relocations = LoadRelocations(moduleDir, symDict, moduleId);
                CreateModuleFile(rootContentDir, Path.GetFileName(moduleDir), sectInfoDict[moduleId], sectionDataDict[moduleId], relocations, moduleId, moduleInfo.PrologSectionId);
            }
        }

        // TODO: This could have a performance increase if we switch to a hash-map (dictionary) instead of a List of symbol entries.
        private static void DumpModule(in string modulePath, in RelocatableModule rel, in List<SymbolContainer> symbols, in Dictionary<int, Dictionary<string, SymbolEntry>> symbolsByModule)
        {
            // Dump contents into directories
            foreach (var container in symbols)
            {
                // Write each entry out to a file.
                foreach (var grouping in container.Entries.GroupBy(o => o.SectionIdx))
                {
                    var idx = 0;
                    foreach (var entry in grouping)
                    {
                        var path = ExpandPathFile(modulePath, entry);
                        path = Path.Combine(path, "FILE__" + idx + "_" + entry.Name + "___ALIGN_" + entry.Alignment + ".bin");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, entry.Data);
                        idx++;
                    }
                }
            }

            // Dump relocations to a human readable format
            using var relocationsFile = File.CreateText(Path.Combine(modulePath, "relocations.txt"));
            foreach (var import in rel.Imports)
            {
                var offset = 0;
                var section = 0;
                foreach (var relocation in import.Relocations)
                {
                    switch (relocation.Type)
                    {
                        case RelocatableModule.RelocationType.R_DOLPHIN_SECTION:
                            section = relocation.Section;
                            offset = 0;
                            continue;
                        case RelocatableModule.RelocationType.R_DOLPHIN_NOP:
                            offset += relocation.Offset;
                            continue;
                        case RelocatableModule.RelocationType.R_DOLPHIN_END:
                            continue;
                        default:
                            offset += relocation.Offset;
                            break;
                    }

                    var symbolList = symbolsByModule[(int)rel.ModuleHeader.ModuleId];
                    var relevantSymbol = symbolList.Values.FirstOrDefault(o => o.SectionIdx == section && offset >= o.SectionOffset && o.SectionOffset + o.Size > offset);
                    if (relevantSymbol == null)
                    {
                        throw new InvalidOperationException("No relevant symbol could be found for the current relocation!");
                    }

                    if (symbolsByModule.ContainsKey((int)import.ModuleId))
                    {
                        var importingFromSymbol = symbolsByModule[(int)import.ModuleId].Values.FirstOrDefault(o => o.SectionIdx == relocation.Section && relocation.Addend >= o.SectionOffset && o.SectionOffset + o.Size > relocation.Addend);
                        if (importingFromSymbol != null)
                        {
                            if (relocation.Addend - importingFromSymbol.SectionOffset != 0)
                            {
                                relocationsFile.WriteLine($"{relocation.Type.ToString()} {relevantSymbol.RelativeFilePath}+0x{(offset - relevantSymbol.SectionOffset):X8} -> {(import.ModuleId == rel.ModuleHeader.ModuleId ? "self" : "module_" + import.ModuleId)} {importingFromSymbol.RelativeFilePath}+0x{(relocation.Addend - importingFromSymbol.SectionOffset):X8}");
                            }
                            else
                            {
                                relocationsFile.WriteLine($"{relocation.Type.ToString()} {relevantSymbol.RelativeFilePath}+0x{(offset - relevantSymbol.SectionOffset):X8} -> {(import.ModuleId == rel.ModuleHeader.ModuleId ? "self" : "module_" + import.ModuleId)} {importingFromSymbol.RelativeFilePath}");
                            }
                        }
                        else
                        {
                            relocationsFile.WriteLine($"{relocation.Type.ToString()} {relevantSymbol.RelativeFilePath}+0x{(offset - relevantSymbol.SectionOffset):X8} -> {(import.ModuleId == rel.ModuleHeader.ModuleId ? "self" : "module_" + import.ModuleId)} import_section_{relocation.Section}+0x{relocation.Addend:X8}");
                        }
                    }
                    else
                    {
                        relocationsFile.WriteLine($"{relocation.Type.ToString()} {relevantSymbol.RelativeFilePath}+0x{(offset - relevantSymbol.SectionOffset):X8} -> module_{import.ModuleId} import_section_{relocation.Section}+0x{relocation.Addend:X8}");
                    }
                }
            }
        }

        private static void DumpModules(in string rootContentDir)
        {
            var mainDir = Path.Combine(rootContentDir, "GCReLink");
            var relFiles = new Dictionary<string, RelocatableModule>();
            var mapFiles = new Dictionary<string, string>();

            foreach (var file in Directory.GetFiles(rootContentDir))
            {
                switch (Path.GetExtension(file))
                {
                    case ".rel":
                    case ".szs":
                        var data = File.ReadAllBytes(file);
                        if (Yaz0.IsYaz0(data))
                            data = Yaz0.Decompress(data);
                        else if (Yay0.IsYay0(data))
                            data = Yay0.Decompress(data);

                        using (var reader = new BinaryReaderX(new MemoryStream(data), ByteOrder.BigEndian))
                        {
                            var rel = new RelocatableModule(reader);
                            if (relFiles.Values.Any(o => o.ModuleHeader.ModuleId == rel.ModuleHeader.ModuleId))
                                Console.WriteLine($"Ignoring rel file: {Path.GetFileName(file)} because a module with that module id has already been loaded!");
                            else
                                relFiles.Add(Path.GetFileNameWithoutExtension(file), rel);
                        }
                        break;
                    case ".map":
                        mapFiles.Add(Path.GetFileNameWithoutExtension(file), file);
                        break;
                }
            }

            var symbolsByModule = new Dictionary<int, Dictionary<string, SymbolEntry>>();
            var containersByModule = new Dictionary<int, List<SymbolContainer>>();
            foreach (var module in relFiles)
            {
                var moduleDir = Path.Combine(mainDir, module.Key);
                Directory.CreateDirectory(moduleDir);
                using var moduleInfoFile = File.CreateText(Path.Combine(moduleDir, "module_info.txt"));
                var (containers, symbolList) = SymbolMapParser.ParseMapFile(module.Value, mapFiles[module.Key]);
                containersByModule.Add((int)module.Value.ModuleHeader.ModuleId, containers);
                symbolsByModule.Add((int)module.Value.ModuleHeader.ModuleId, symbolList);

                var functionDefs = FunctionCallAnalyzer.UnlinkReferencedFunctions(symbolList.Values.ToList());

                // Write needed module info
                var hasProlog = false;
                moduleInfoFile.WriteLine("ModuleId=" + module.Value.ModuleHeader.ModuleId.ToString());
                if (module.Value.ModuleHeader.PrologSectionId != 0)
                {
                    moduleInfoFile.WriteLine("PrologSectId=" + module.Value.ModuleHeader.PrologSectionId);
                    hasProlog = true;
                }

                // Dump section info
                using var sectionInfoFile = File.CreateText(Path.Combine(moduleDir, "sections.txt"));
                var sectionsInfo = SymbolMapParser.GetSectionsInfo(mapFiles[module.Key]);
                // Write dummy first.
                sectionInfoFile.WriteLine("00 00000000 dummy");
                foreach (var sectionInfo in sectionsInfo)
                {
                    sectionInfoFile.WriteLine($"{sectionInfo.Id:d2} {sectionInfo.Size:X8} {sectionInfo.Name}");
                }
                sectionInfoFile.Flush();

                // Dump function definition file
                using var funcDefsFile = File.CreateText(Path.Combine(moduleDir, "function_definitions.txt"));
                foreach (var funcDef in functionDefs)
                {
                    funcDefsFile.WriteLine($"{funcDef.Id:d6} {GetPath(funcDef.Symbol)}");
                    if (hasProlog && funcDef.Symbol.SectionIdx == module.Value.ModuleHeader.PrologSectionId && funcDef.Symbol.SectionOffset == 0)
                        moduleInfoFile.WriteLine("PrologFuncId=" + funcDef.Id);
                }
                funcDefsFile.Flush();
            }

            foreach (var module in relFiles)
                DumpModule(Path.Combine(mainDir, module.Key), module.Value, containersByModule[(int)module.Value.ModuleHeader.ModuleId], symbolsByModule);
        }

        static void Main(string[] args)
        {
            ParseArgs(args);
        }

        private static string GetPath(in SymbolEntry entry)
        {
            return Path.Combine(entry.Container, entry.SectionName, entry.Name + ".bin");
        }

        private static string GetPathFile(in SymbolEntry entry)
        {
            return Path.Combine(entry.Container, entry.SectionName);
        }

        private static string ExpandPathFile(in string rootPath, in SymbolEntry entry)
        {
            return Path.Combine(rootPath, GetPathFile(entry));
        }

        private static void WriteRelocation(BinaryWriterX writer, ushort ofs, RelocatableModule.RelocationType relocType, byte sectIdx, uint sectAddend)
        {
            writer.Write(ofs);
            writer.Write((byte)relocType);
            writer.Write(sectIdx);
            writer.Write(sectAddend);
        }
    }
}

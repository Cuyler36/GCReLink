using GCNToolKit.Formats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GCReLink
{
    public static class SymbolMapParser
    {
        private static readonly Regex splitRegex = new Regex(@"\s+");

        public static (List<SymbolContainer>, Dictionary<string, SymbolEntry>) ParseMapFile(in RelocatableModule module, in string symbolMapFilePath)
        {
            if (!File.Exists(symbolMapFilePath))
            {
                throw new ArgumentException("The map path needs to point to an exisiting file!", nameof(symbolMapFilePath));
            }

            var sectionInfo = GetSectionsInfo(symbolMapFilePath);

            var symbolList = new Dictionary<string, SymbolEntry>();
            var symbols = new List<SymbolContainer>();
            SymbolContainer currentContainer = null;

            var sectionId = 0;
            using var textReader = File.OpenText(symbolMapFilePath);
            var line = "";
            var sectionName = "";
            var fileIdx = 0;
            while ((line = textReader.ReadLine()) != null)
            {
                if (line.StartsWith('.') && line.Contains(" section layout"))
                {
                    sectionName = line.Substring(0, line.IndexOf(" section layout"));
                    if (sectionInfo.Find(o => o.Name == sectionName)?.Size > 0)
                        sectionId++;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var contents = splitRegex.Split(line.Trim());
                    if (contents.Length != 6)
                    {
#if DEBUG
                        Console.WriteLine($"Line doesn't have 6 subentries! Line: {line}");
#endif
                        continue;
                    }

                    if (contents[4] == sectionName)
                    {
                        currentContainer = symbols.Find(o => o.Name == contents[5]);
                        if (currentContainer == null)
                        {
                            currentContainer= new SymbolContainer(contents[5]);
                            symbols.Add(currentContainer);
                        }
                        fileIdx = 0;
                    }
                    else
                    {
                        var size = int.Parse(contents[1], NumberStyles.HexNumber);
                        var offset = int.Parse(contents[0], NumberStyles.HexNumber);
                        var data = new byte[size];
                        if (!sectionName.Contains("bss"))
                        {
                            Buffer.BlockCopy(module.Sections[sectionId].Data, offset, data, 0, size);
                        }

                        var entry = new SymbolEntry(currentContainer, contents[4], contents[5], sectionName, sectionId,
                            size, offset, int.Parse(contents[3]), fileIdx, data);
                        fileIdx++;

                        if (currentContainer?.Name != entry.Container)
                        {
                            currentContainer = symbols.Find(o => o.Name == entry.Container);
                            if (currentContainer == null)
                            {
                                currentContainer = new SymbolContainer(contents[5]);
                                symbols.Add(currentContainer);
                            }
                        }

                        currentContainer.AddEntry(entry);
                        if (symbolList.ContainsKey(entry.RelativeFilePath))
                        {
                            var uid = "____" + DateTime.Now.Ticks;
                            var newFileName = entry.Name + uid;

                            // To *absolutely* ensure that no name colliding occurs in the dictionary,
                            // we're going to change the symbol name to include the uid for the file.
                            // As a side note, this'll allow us to absolutely ensure that the file we spit out
                            // in the relocation reference is picked up correctly when we rebuild.
                            entry.SetName(newFileName);
                        }
                        symbolList.Add(entry.RelativeFilePath, entry);
                    }
                }
            }

            return (symbols, symbolList);
        }

        public static List<SectionInfo> GetSectionsInfo(in string symbolMapFilePath)
        {
            if (!File.Exists(symbolMapFilePath))
                throw new ArgumentException("The map path needs to point to an exisiting file!", nameof(symbolMapFilePath));

            var sectionInfoList = new List<SectionInfo>();
            using var symbolMapFile = File.OpenText(symbolMapFilePath);
            var line = "";
            while ((line = symbolMapFile.ReadLine()) != null)
            {
                if (line != "Memory map:")
                    continue;
                symbolMapFile.ReadLine();
                symbolMapFile.ReadLine();
                var idx = 1;
                while (!string.IsNullOrWhiteSpace(line = symbolMapFile.ReadLine()))
                    if (!line.Trim().StartsWith('.') || line.Contains(".debug") || line.Contains(".line"))
                        continue;
                    else
                        sectionInfoList.Add(new SectionInfo(idx++, line.Substring(0, 17).Trim(), int.Parse(line.Substring(28, 8), NumberStyles.HexNumber)));
                break;
            }
            return sectionInfoList;
        }

        public static void OutputMapFile(in string rootDir, in string mapName, in IOrderedEnumerable<KeyValuePair<string, List<SymbolEntry>>> symbolsBySection, in Dictionary<string, SectionInfo> sectInfo)
        {
            using var mapFile = File.CreateText(Path.Combine(rootDir, mapName + ".map"));
            foreach (var symbolSection in symbolsBySection)
            {
                mapFile.WriteLine();
                mapFile.WriteLine($"{symbolSection.Key} section layout");
                mapFile.WriteLine("  Starting        Virtual");
                mapFile.WriteLine("  address  Size   address");
                mapFile.WriteLine("  -----------------------");

                foreach (var symbol in symbolSection.Value)
                {
                    // Remove NTFS duplicate name markers
                    if (symbol.Name.Contains("____"))
                        mapFile.WriteLine($"  {symbol.SectionOffset:x8} {symbol.Data.Length:x6} {symbol.SectionOffset:x8}{symbol.Alignment.ToString().PadLeft(3)} {symbol.Name.Substring(0, symbol.Name.IndexOf("____"))} \t{symbol.Container}");
                    else
                        mapFile.WriteLine($"  {symbol.SectionOffset:x8} {symbol.Data.Length:x6} {symbol.SectionOffset:x8}{symbol.Alignment.ToString().PadLeft(3)} {symbol.Name} \t{symbol.Container}");
                }
                mapFile.WriteLine();
            }

            mapFile.WriteLine("Memory map:");
            mapFile.WriteLine("                   Starting Size     File");
            mapFile.WriteLine("                   address           Offset");
            var offset = 0x40;
            foreach (var symbolSect in symbolsBySection)
            {
                var info = sectInfo[symbolSect.Key];
                mapFile.WriteLine($"{symbolSect.Key.PadLeft(17)}  00000000 {info.Size:x8} {offset:x8}");
                offset += info.Size;
            }
        }
    }
}

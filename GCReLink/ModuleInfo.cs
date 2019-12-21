using System;
using System.IO;

namespace GCReLink
{
    public sealed class ModuleInfo
    {
        public readonly int Id;
        public readonly int PrologSectionId;
        public readonly int PrologFunctionIdx;

        public readonly CompressionMode CompressionMode = CompressionMode.None;

        public ModuleInfo(in string rootContentDir)
        {
            var moduleInfoFilePath = Path.Combine(rootContentDir, "module_info.txt");
            if (!File.Exists(moduleInfoFilePath))
                throw new Exception("Couldn't find the module info file! Relinking cannot continue!");

            var line = "";
            using var moduleInfoStream = File.OpenText(moduleInfoFilePath);
            while ((line = moduleInfoStream.ReadLine()) != null)
            {
                if (!line.Contains("=")) continue;
                var info = line.Split('=');
                switch (info[0])
                {
                    case "Compression":
                        CompressionMode = (CompressionMode)Enum.Parse(typeof(CompressionMode), info[1], true);
                        break;
                    case "ModuleId":
                        Id = int.Parse(info[1]);
                        break;
                    case "PrologSectId":
                        PrologSectionId = int.Parse(info[1]);
                        break;
                    case "PrologFuncId":
                        PrologFunctionIdx = int.Parse(info[1]);
                        break;
                }
            }
        }
    }
}
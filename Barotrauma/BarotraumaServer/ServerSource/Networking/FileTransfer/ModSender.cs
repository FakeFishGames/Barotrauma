using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class ModSender : IDisposable
    {
        public const string UploadFolder = "TempMods_Upload";
        public const string Extension = ".barodir.gz";

        public bool Ready { get; private set; } = false;

        public ModSender()
        {
            DeleteDir();
            Directory.CreateDirectory(UploadFolder);
            TaskPool.Add(
                "ModSender",
                Task.WhenAll(
                    ContentPackageManager.EnabledPackages.All
                        .Where(p => p != ContentPackageManager.VanillaCorePackage && p.HasMultiplayerSyncedContent)
                        .Select(CompressMod)),
                (t) => Ready = true);
        }

        public static string GetCompressedModPath(ContentPackage mod)
        {
            string dir = mod.Dir;
            string resultFileName
                = dir.StartsWith(ContentPackage.LocalModsDir)
                    ? $"Local_{mod.Name}"
                    : $"Workshop_{mod.Name}";
            resultFileName = ToolBox.RemoveInvalidFileNameChars(resultFileName.Replace('\\', '_').Replace('/', '_'));
            resultFileName = $"{resultFileName}{Extension}";
            return Path.Combine(UploadFolder, resultFileName);
        }
        
        public async Task CompressMod(ContentPackage mod)
        {
            await Task.Yield();
            string dir = mod.Dir;
            SaveUtil.CompressDirectory(dir, GetCompressedModPath(mod), fileName => { });
        }
        
        private void DeleteDir()
        {
            if (Directory.Exists(UploadFolder)) { Directory.Delete(UploadFolder, recursive: true); }
        }

        public bool IsDisposed { get; private set; } = false;
        public void Dispose()
        {
            IsDisposed = true;
            DeleteDir();
        }
    }
}

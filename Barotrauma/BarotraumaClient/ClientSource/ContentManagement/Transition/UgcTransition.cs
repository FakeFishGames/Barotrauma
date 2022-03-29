#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Directory = Barotrauma.IO.Directory;
using File = Barotrauma.IO.File;
using Path = Barotrauma.IO.Path;

namespace Barotrauma.Transition
{
    /// <summary>
    /// Class dedicated to transitioning away from the old, shitty
    /// Mods + Submarines folders to the new LocalMods folder
    /// </summary>
    public static class UgcTransition
    {
        private const string readmeName = "LOCALMODS_README.txt";
        
        public static void Prepare()
        {
            TaskPool.Add("UgcTransition.Prepare", DetermineItemsToTransition(), t =>
            {
                if (!t.TryGetResult(out (OldSubs, OldMods) pair)) { return; }
                var (subs, mods) = pair;
                if (!subs.FilePaths.Any() && !mods.Mods.Any()) { return; }

                var msgBox = new GUIMessageBox(TextManager.Get("Ugc.TransferTitle"), "", relativeSize: (0.5f, 0.8f),
                    buttons: new LocalizedString[] { TextManager.Get("Ugc.TransferButton") });

                var desc = new GUITextBlock(new RectTransform((1.0f, 0.24f), msgBox.Content.RectTransform),
                    text: TextManager.Get("Ugc.TransferDesc"), wrap: true, textAlignment: Alignment.CenterLeft);
                
                var modsList = new GUIListBox(new RectTransform((1.0f, 0.6f), msgBox.Content.RectTransform))
                {
                    HoverCursor = CursorState.Default
                };
                Dictionary<string, GUITickBox> pathTickboxMap = new Dictionary<string, GUITickBox>();

                void addHeader(LocalizedString str)
                {
                    var itemFrame = new GUITextBlock(new RectTransform((1.0f, 0.08f), modsList.Content.RectTransform),
                        text: str, font: GUIStyle.SubHeadingFont)
                    {
                        CanBeFocused = false
                    };
                }
                void addTickbox(string dir, string name, bool ticked)
                {
                    var itemFrame = new GUIFrame(new RectTransform((1.0f, 0.07f), modsList.Content.RectTransform),
                        style: null)
                    {
                        CanBeFocused = false
                    };
                    var tickbox = new GUITickBox(new RectTransform(Vector2.One, itemFrame.RectTransform), name)
                    {
                        Selected = ticked
                    };
                    pathTickboxMap.Add(dir, tickbox);
                }
                
                addHeader(TextManager.Get("WorkshopLabelSubmarines"));
                foreach (var sub in subs.FilePaths)
                {
                    var subName = Path.GetFileNameWithoutExtension(sub);
                    addTickbox(sub, subName, ticked: !ContentPackageManager.LocalPackages.Any(p => p.NameMatches(subName)));
                }

                addHeader("");
                addHeader(TextManager.Get("SubscribedMods"));
                foreach (var mod in mods.Mods)
                {
                    addTickbox(mod.Dir, mod.Name, ticked: !ContentPackageManager.LocalPackages.Any(p => p.SteamWorkshopId != 0 && p.SteamWorkshopId == mod.Item?.Id));
                }

                GUIMessageBox? subMsgBox = null;

                void createSubMsgBox(LocalizedString str, bool closable)
                {
                    subMsgBox?.Close();
                    subMsgBox = new GUIMessageBox(headerText: "", text: str,
                        buttons: closable ? new[] { TextManager.Get("Close") } : Array.Empty<LocalizedString>());
                    if (closable)
                    {
                        subMsgBox.Buttons[0].OnClicked = subMsgBox.Close;
                    }
                }
                
                msgBox.Buttons[0].OnClicked = (b, o) =>
                {
                    TaskPool.Add("TransferMods", TransferMods(pathTickboxMap), t2 =>
                    {
                        if (t2.Exception != null)
                        {
                            DebugConsole.ThrowError("There was an error transferring mods", t2.Exception.GetInnermost());
                        }
                        ContentPackageManager.LocalPackages.Refresh();
                        createSubMsgBox(TextManager.Get("Ugc.TransferComplete"), closable: true);
                    });
                    msgBox.Close();
                    createSubMsgBox(TextManager.Get("Ugc.Transferring"), closable: false);
                    return false;
                };
            });
        }

        private struct OldSubs
        {
            public readonly IReadOnlyList<string> FilePaths;
            
            public OldSubs(IReadOnlyList<string> filePaths)
            {
                FilePaths = filePaths;
            }
        }

        private struct OldMods
        {
            public readonly IReadOnlyList<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)> Mods;
            
            public OldMods(IReadOnlyList<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)> mods)
            {
                Mods = mods;
            }
        }

        private const string oldSubsPath = "Submarines";
        private const string oldModsPath = "Mods";

        private static async Task<(OldSubs Subs, OldMods Mods)> DetermineItemsToTransition()
        {
            string[] subs = Array.Empty<string>();
            List<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)> mods
                = new List<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)>();
            if (FolderShouldBeTransitioned(oldModsPath))
            {
                subs = Directory.GetFiles(oldSubsPath, "*.sub", SearchOption.TopDirectoryOnly);
                string[] allOldMods = Directory.GetDirectories(oldModsPath, "*", SearchOption.TopDirectoryOnly);

                var publishedItems = await SteamManager.Workshop.GetPublishedItems();
                foreach (var modDir in allOldMods)
                {
                    var fileList = XMLExtensions.TryLoadXml(Path.Combine(modDir, ContentPackage.FileListFileName), out _);
                    if (fileList?.Root is null) { continue; }

                    var oldId = fileList.Root.GetAttributeUInt64("steamworkshopid", 0);
                    var updateTime = File.GetLastWriteTime(modDir).ToUniversalTime();
                    var oldName = fileList.Root.GetAttributeString("name", "");

                    var item = oldId != 0 ? publishedItems.FirstOrNull(it => it.Id == oldId) : null;
                    if (oldId == 0 || item.HasValue)
                    {
                        mods.Add((modDir, oldName, item, updateTime));
                    }
                }
            }

            while (!(Screen.Selected is MainMenuScreen)) { await Task.Delay(500); }
            
            return (new OldSubs(subs), new OldMods(mods));
        }
        
        private static bool FolderShouldBeTransitioned(string folderName)
        {
            return Directory.Exists(folderName)
                   && !File.Exists(Path.Combine(folderName, readmeName));
        }

        private static async Task TransferMods(Dictionary<string, GUITickBox> pathTickboxMap)
        {
            //WriteReadme(oldSubsPath); //can't do this because the submarine discovery code is borked
            WriteReadme(oldModsPath);
            foreach (var (path, tickbox) in pathTickboxMap)
            {
                if (!tickbox.Selected) { continue; }
                string dirName = Path.GetFileNameWithoutExtension(path);
                string destPath = Path.Combine(ContentPackage.LocalModsDir, dirName);
                
                //find unique path to save in
                for (int i = 0;;i++)
                {
                    if (!Directory.Exists(destPath)) { break; }
                    destPath = Path.Combine(ContentPackage.LocalModsDir, $"{dirName}.{i}");
                }
                
                if (path.StartsWith(oldSubsPath, StringComparison.OrdinalIgnoreCase))
                {
                    //copying a sub: manually create filelist.xml
                    ModProject modProject = new ModProject
                    {
                        Name = dirName,
                        ModVersion = ContentPackage.DefaultModVersion
                    };
                    modProject.AddFile(ModProject.File.FromPath<SubmarineFile>(Path.Combine(ContentPath.ModDirStr, $"{dirName}.sub")));

                    Directory.CreateDirectory(destPath);
                    File.Copy(path, Path.Combine(destPath, $"{dirName}.sub"));
                    modProject.Save(Path.Combine(destPath, ContentPackage.FileListFileName));
                    
                    await Task.Yield();
                }
                else
                {
                    //copying a mod: we have a neat method for that!
                    await SteamManager.Workshop.CopyDirectory(path, Path.GetFileName(path), path, destPath);
                }
            }
        }
        
        private static void WriteReadme(string folderName)
        {
            if (!Directory.Exists(folderName)) { return; }
            File.WriteAllText(path: Path.Combine(folderName, readmeName),
                contents: "This folder is no longer used by Barotrauma;\n" +
                    "your mods and submarines should have been transferred\n" +
                    "to LocalMods. If they are not being found, delete this\n" +
                    "readme and relaunch the game.", encoding: Encoding.UTF8);
        }
    }
}

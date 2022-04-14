#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
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

        private enum ModsListChildType
        {
            Header,
            Entry
        }
        
        public static void Prepare()
        {
            TaskPool.Add("UgcTransition.Prepare", DetermineItemsToTransition(), t =>
            {
                if (!t.TryGetResult(out (OldSubs, OldItemAssemblies, OldMods) result)) { return; }
                var (subs, itemAssemblies, mods) = result;
                if (!subs.FilePaths.Any() && !itemAssemblies.FilePaths.Any() && !mods.Mods.Any()) { return; }

                var msgBox = new GUIMessageBox(TextManager.Get("Ugc.TransferTitle"), "", relativeSize: (0.5f, 0.8f),
                    buttons: new LocalizedString[] { TextManager.Get("Ugc.TransferButton") });

                var closeBtn = new GUIButton(
                    new RectTransform(Vector2.One * 1.5f, msgBox.Header.RectTransform, anchor: Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUICancelButton")
                {
                    OnClicked = (button, o) =>
                    {
                        msgBox.Close();
                        return false;
                    }
                };

                var desc = new GUITextBlock(new RectTransform((1.0f, 0.24f), msgBox.Content.RectTransform),
                    text: TextManager.Get("Ugc.TransferDesc"), wrap: true, textAlignment: Alignment.CenterLeft);
                
                var modsList = new GUIListBox(new RectTransform((1.0f, 0.6f), msgBox.Content.RectTransform))
                {
                    HoverCursor = CursorState.Default
                };
                Dictionary<string, GUITickBox> pathTickboxMap = new Dictionary<string, GUITickBox>();

                void addHeader(LocalizedString str)
                {
                    var itemFrame = new GUIFrame(new RectTransform((1.0f, 0.08f), modsList.Content.RectTransform),
                        style: null)
                    {
                        CanBeFocused = false,
                        UserData = ModsListChildType.Header
                    };
                    if (str is RawLString { Value: "" }) { return; }

                    bool clicked = true;
                    var tickBox = new GUITickBox(new RectTransform(Vector2.One, itemFrame.RectTransform),
                        label: str, font: GUIStyle.SubHeadingFont)
                    {
                        Selected = false,
                        OnSelected = box =>
                        {
                            if (!clicked) { return true; }
                            bool toggleTickbox = false;
                            foreach (var child in modsList.Content.Children)
                            {
                                if (child == itemFrame) { toggleTickbox = true; }
                                else if (child.UserData is ModsListChildType.Header) { toggleTickbox = false; }
                                else if (toggleTickbox)
                                {
                                    var tb = child.GetAnyChild<GUITickBox>();
                                    if (tb is null) { continue; }

                                    tb.Selected = box.Selected;
                                }
                            }
                            return true;
                        }
                    };
                    new GUICustomComponent(new RectTransform(Vector2.Zero, itemFrame.RectTransform),
                        onUpdate: (f, component) =>
                        {
                            clicked = false;
                            bool shouldBeSelected = true;
                            bool toggleTickbox = false;
                            foreach (var child in modsList.Content.Children)
                            {
                                if (child == itemFrame) { toggleTickbox = true; }
                                else if (child.UserData is ModsListChildType.Header) { toggleTickbox = false; }
                                else if (toggleTickbox)
                                {
                                    var tb = child.GetAnyChild<GUITickBox>();
                                    if (tb is null) { continue; }

                                    if (!tb.Selected)
                                    {
                                        shouldBeSelected = false;
                                        break;
                                    }
                                }
                            }
                            tickBox.Selected = shouldBeSelected;
                            clicked = true;
                        });
                }
                void addTickbox(string dir, string name, bool ticked)
                {
                    var itemFrame = new GUIFrame(new RectTransform((1.0f, 0.07f), modsList.Content.RectTransform),
                        style: null)
                    {
                        CanBeFocused = false,
                        UserData = ModsListChildType.Entry
                    };
                    var tickbox = new GUITickBox(new RectTransform((0.97f, 1.0f), itemFrame.RectTransform, Anchor.CenterRight), name)
                    {
                        Selected = ticked
                    };
                    pathTickboxMap.Add(dir, tickbox);
                }

                bool firstHeader = true;

                void addSpacer()
                {
                    if (firstHeader) { firstHeader = false; return; }
                    addHeader("");
                }

                if (subs.FilePaths.Any())
                {
                    addSpacer();
                    addHeader(TextManager.Get("WorkshopLabelSubmarines"));
                    foreach (var sub in subs.FilePaths)
                    {
                        var subName = Path.GetFileNameWithoutExtension(sub);
                        addTickbox(sub, subName, ticked: !ContentPackageManager.LocalPackages.Any(p => p.NameMatches(subName)));
                    }
                }

                if (itemAssemblies.FilePaths.Any())
                {
                    addSpacer();
                    addHeader(TextManager.Get("ItemAssemblies"));
                    foreach (var itemAssembly in itemAssemblies.FilePaths)
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(itemAssembly);
                        addTickbox(itemAssembly, assemblyName, ticked: !ContentPackageManager.LocalPackages.Any(p => p.NameMatches(assemblyName)));
                    }
                }

                if (mods.Mods.Any())
                {
                    addSpacer();
                    addHeader(TextManager.Get("SubscribedMods"));
                    foreach (var mod in mods.Mods)
                    {
                        addTickbox(mod.Dir, mod.Name, ticked: !ContentPackageManager.LocalPackages.Any(p => p.SteamWorkshopId != 0 && p.SteamWorkshopId == mod.Item?.Id));
                    }
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
                        if (t2.TryGetResult(out string[] modsToEnable))
                        {
                            var newRegular = ContentPackageManager.EnabledPackages.Regular.ToList();
                            newRegular.AddRange(ContentPackageManager.LocalPackages.Regular
                                .Where(r => modsToEnable.Contains(r.Dir.CleanUpPathCrossPlatform(correctFilenameCase: false))));
                            newRegular = newRegular.Distinct().ToList();
                            ContentPackageManager.EnabledPackages.SetRegular(newRegular);
                        }
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

        private struct OldItemAssemblies
        {
            public readonly IReadOnlyList<string> FilePaths;

            public OldItemAssemblies(IReadOnlyList<string> filePaths)
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
        private const string oldItemAssembliesPath = "ItemAssemblies";

        private static async Task<(OldSubs Subs, OldItemAssemblies ItemAssemblies, OldMods Mods)> DetermineItemsToTransition()
        {
            string[] subs = Array.Empty<string>();
            string[] itemAssemblies = Array.Empty<string>();
            List<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)> mods
                = new List<(string Dir, string Name, Steamworks.Ugc.Item? Item, DateTime InstallTime)>();
            if (FolderShouldBeTransitioned(oldModsPath))
            {
                string[] getFiles(string path, string pattern)
                    => Directory.Exists(path)
                        ? Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly)
                        : Array.Empty<string>();
                
                subs = getFiles(oldSubsPath, "*.sub");
                itemAssemblies = getFiles(oldItemAssembliesPath, "*.xml");
                
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

            while (!(Screen.Selected is MainMenuScreen)) { await Task.Delay(50); }
            
            return (new OldSubs(subs), new OldItemAssemblies(itemAssemblies), new OldMods(mods));
        }
        
        private static bool FolderShouldBeTransitioned(string folderName)
        {
            return Directory.Exists(folderName)
                   && !File.Exists(Path.Combine(folderName, readmeName));
        }

        private static async Task<string[]> TransferMods(Dictionary<string, GUITickBox> pathTickboxMap)
        {
            //WriteReadme(oldSubsPath); //can't do this because the old submarine discovery code is borked
            WriteReadme(oldModsPath);
            var modsToEnable = (await Task.WhenAll(pathTickboxMap.Select(TransferMod))).OfType<string>().ToArray();
            return modsToEnable;
        }

        private static Task<string?> TransferMod(KeyValuePair<string, GUITickBox> kvp)
            => TransferMod(kvp.Key, kvp.Value);

        private static async Task<string?> TransferMod(string path, GUITickBox tickbox)
        {
            if (!tickbox.Selected) { return null; }
            string dirName = Path.GetFileNameWithoutExtension(path);
            string destPath = Path.Combine(ContentPackage.LocalModsDir, dirName);
                
            //find unique path to save in
            for (int i = 0;;i++)
            {
                if (!Directory.Exists(destPath)) { break; }
                destPath = Path.Combine(ContentPackage.LocalModsDir, $"{dirName}.{i}");
            }

            bool isSub = path.StartsWith(oldSubsPath, StringComparison.OrdinalIgnoreCase);
            bool isItemAssembly = path.StartsWith(oldItemAssembliesPath, StringComparison.OrdinalIgnoreCase);
            if (isSub || isItemAssembly)
            {
                //copying a sub or item assembly: manually create filelist.xml
                ModProject modProject = new ModProject
                {
                    Name = dirName,
                    ModVersion = ContentPackage.DefaultModVersion
                };

                Type fileType;
                if (isSub)
                {
                    fileType = typeof(SubmarineFile);
                    XDocument? doc = SubmarineInfo.OpenFile(path, out _);
                    if (doc?.Root != null)
                    {
                        SubmarineType subType = doc.Root.GetAttributeEnum("type", SubmarineType.Player);
                        fileType = SubEditorScreen.DetermineSubFileType(subType);
                    }
                }
                else
                {
                    fileType = typeof(ItemAssemblyFile);
                }
                
                modProject.AddFile(ModProject.File.FromPath(
                    Path.Combine(ContentPath.ModDirStr, $"{dirName}.{(isSub ? "sub" : "xml")}"),
                    fileType));

                Directory.CreateDirectory(destPath);
                File.Copy(path, Path.Combine(destPath, $"{dirName}.{(isSub ? "sub" : "xml")}"));
                modProject.Save(Path.Combine(destPath, ContentPackage.FileListFileName));

                return destPath.CleanUpPathCrossPlatform(correctFilenameCase: false);
            }
            else
            {
                //copying a mod: we have a neat method for that!
                await SteamManager.Workshop.CopyDirectory(path, Path.GetFileName(path), path, destPath);

                return null;
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

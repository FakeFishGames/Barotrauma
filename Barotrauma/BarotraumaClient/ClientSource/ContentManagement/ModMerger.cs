#nullable enable
using System;
using System.Linq;
using Barotrauma.Steam;
using Barotrauma.IO;

namespace Barotrauma
{
    public static class ModMerger
    {
        public static void AskMerge(ContentPackage[] mods)
        {
            ErrorIfNonLocal(mods);

            var msgBox = new GUIMessageBox(TextManager.Get("MergeModsHeader"), "", relativeSize: (0.5f, 0.8f),
                buttons: new LocalizedString[] { TextManager.Get("ConfirmModMerge"), TextManager.Get("Cancel") });
            msgBox.Buttons[1].OnClicked = msgBox.Close;

            var desc = new GUITextBlock(new RectTransform((1.0f, 0.1f), msgBox.Content.RectTransform), TextManager.Get("MergeModsDesc"));
            var modsList = new GUIListBox(new RectTransform((1.0f, 0.5f), msgBox.Content.RectTransform))
            {
                OnSelected = (component, o) => false,
                HoverCursor = CursorState.Default
            };
            foreach (var mod in mods)
            {
                new GUITextBlock(new RectTransform((1.0f, 0.11f), modsList.Content.RectTransform), mod.Name)
                {
                    CanBeFocused = false
                };
            }
            var footer = new GUITextBlock(new RectTransform((1.0f, 0.1f), msgBox.Content.RectTransform), TextManager.Get("MergeModsFooter"));
            var resultName = new GUITextBox(new RectTransform((1.0f, 0.1f), msgBox.Content.RectTransform))
            {
                Text = (mods.Count(m => m.Files.Length > 1)==1)
                    ? mods.First(m => m.Files.Length > 1).Name
                    : ""
            };

            void flashText()
            {
                resultName!.Select();
                resultName.Flash(GUIStyle.Red);
            }

            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                if (string.IsNullOrEmpty(resultName.Text))
                {
                    flashText();
                    return false;
                }
                string targetDir = $"{ContentPackage.LocalModsDir}/{resultName.Text}";

                bool dirMatches(ContentPackage mod)
                    => mod.Dir.CleanUpPathCrossPlatform(correctFilenameCase: false)
                        .Equals(targetDir, StringComparison.OrdinalIgnoreCase);
                if (ContentPackageManager.LocalPackages.Any(dirMatches)
                    && !mods.Any(dirMatches))
                {
                    flashText();
                    return false;
                }
                
                MergeMods(mods, resultName.Text);
                msgBox.Close();
                return false;
            };
        }

        private static void MergeMods(ContentPackage[] mods, string resultName)
        {
            ModProject resultProject = new ModProject
            {
                Name = resultName
            };

            string targetDir = $"{ContentPackage.LocalModsDir}/{resultName}";
            Directory.CreateDirectory(targetDir);

            foreach (var mod in mods)
            {
                foreach (var file in Directory.GetFiles(mod.Dir, "*", System.IO.SearchOption.AllDirectories)
                             .Select(f => f.CleanUpPathCrossPlatform(correctFilenameCase: false)))
                {
                    if (Path.GetFileName(file).Equals(ContentPackage.FileListFileName, StringComparison.OrdinalIgnoreCase)) { continue; }
                    
                    string targetFilePath = file[mod.Dir.Length..];
                    if (targetFilePath.StartsWith("/") || targetFilePath.StartsWith("\\"))
                    {
                        targetFilePath = targetFilePath[1..];
                    }

                    targetFilePath = Path.Combine(targetDir, targetFilePath).CleanUpPathCrossPlatform(correctFilenameCase: false);
                    //DebugConsole.NewMessage(targetFilePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                    File.Copy(file, targetFilePath, overwrite: true);

                    var oldFileInProject = resultProject.Files.FirstOrDefault(f
                        => f.Path.Equals(targetFilePath, StringComparison.OrdinalIgnoreCase));
                    if (oldFileInProject != null)
                    {
                        resultProject.RemoveFile(oldFileInProject);
                    }

                    var fileInMod = mod.Files.Find(f => f.Path == file);
                    if (fileInMod != null)
                    {
                        var newFileInProject = ModProject.File.FromPath(targetFilePath, fileInMod.GetType());
                        resultProject.AddFile(newFileInProject);
                    }
                }
            }
            resultProject.Save(Path.Combine(targetDir, ContentPackage.FileListFileName));

            foreach (var mod in mods)
            {
                Directory.Delete(mod.Dir);
            }
            (SettingsMenu.Instance!.WorkshopMenu as MutableWorkshopMenu)!.PopulateInstalledModLists(forceRefreshEnabled: true, refreshDisabled: true);
        }

        private static void ErrorIfNonLocal(ContentPackage[] mods)
        {
            var nonLocal = mods.Where(m => !ContentPackageManager.LocalPackages.Contains(m)).ToArray();
            if (nonLocal.Any())
            {
                throw new Exception($"{string.Join(", ", nonLocal.Select(m => m.Name))} are not local mods");
            }
        }
    }
}

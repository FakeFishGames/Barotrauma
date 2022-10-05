#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.IO;
using Barotrauma.Extensions;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    readonly struct ModListPreset
    {
        public const string SavePath = "ModLists";
        
        public enum ModType
        {
            Vanilla,
            Local,
            Workshop
        }

        public readonly string Name;
        public readonly CorePackage CorePackage;
        public readonly ImmutableArray<RegularPackage> RegularPackages;

        public ModListPreset(XDocument doc)
        {
            Name = doc.Root!.GetAttributeString("name", "");
            
            CorePackage corePackage = ContentPackageManager.VanillaCorePackage!;
            List<RegularPackage> regularPackages = new List<RegularPackage>();
            void addPkg(ContentPackage pkg)
            {
                if (pkg is CorePackage core) { corePackage = core; }
                else if (pkg is RegularPackage reg) { regularPackages.Add(reg); }
            }
            
            foreach (var element in doc.Root!.Elements())
            {
                ModType modType = Enum.TryParse<ModType>(element.Name.LocalName, ignoreCase: true, out var mt) ? mt : ModType.Local;

                switch (modType)
                {
                    case ModType.Vanilla:
                        CorePackage = ContentPackageManager.VanillaCorePackage!;
                        break;
                    case ModType.Workshop:
                        {
                            var id = element.GetAttributeUInt64("id", 0);
                            if (id == 0) { continue; }
                            var pkg = ContentPackageManager.WorkshopPackages.FirstOrDefault(p =>
                                p.TryExtractSteamWorkshopId(out var workshopId) && workshopId.Value == id);
                            if (pkg != null) { addPkg(pkg); }
                        }
                        break;
                    case ModType.Local:
                        {
                            var name = element.GetAttributeString("name", "");
                            if (name.IsNullOrEmpty()) { continue; }
                            var pkg = ContentPackageManager.LocalPackages.FirstOrDefault(p => p.NameMatches(name));
                            if (pkg != null) { addPkg(pkg); }
                        }
                        break;
                }
            }

            CorePackage = corePackage;
            RegularPackages = regularPackages.ToImmutableArray();
        }

        public ModListPreset(string name, CorePackage corePackage, IReadOnlyList<RegularPackage> regularPackages)
        {
            Name = name;
            CorePackage = corePackage;
            RegularPackages = regularPackages.ToImmutableArray();
        }

        public RichString GetTooltip()
        {
            LocalizedString retVal = $"‖color:gui.orange‖{Name}‖end‖" //TODO: we need a RichString builder
                + "\n  " + TextManager.AddPunctuation(':', TextManager.Get("CorePackage"))
                + "\n   - " + CorePackage.Name;
            if (RegularPackages.Any())
            {
                retVal += "\n  " + TextManager.AddPunctuation(':', TextManager.Get("RegularPackages"))
                    + "\n   - "
                    + LocalizedString.Join("\n   - ", RegularPackages.Select(p => (LocalizedString)p.Name));
            }

            return RichString.Rich(retVal);
        }

        public void Save()
        {
            XDocument newDoc = new XDocument();
            XElement newRoot = new XElement("mods", new XAttribute("name", Name));
            newDoc.Add(newRoot);

            ModType determineType(ContentPackage pkg)
            {
                if (pkg == ContentPackageManager.VanillaCorePackage) { return ModType.Vanilla; }
                if (ContentPackageManager.WorkshopPackages.Contains(pkg)) { return ModType.Workshop; }
                return ModType.Local;
            }
            void writePkgElem(ContentPackage pkg)
            {
                var pkgType = determineType(pkg);
                var pkgElem = new XElement(pkgType.ToString());
                switch (pkgType)
                {
                    case ModType.Workshop:
                        pkgElem.SetAttributeValue("name", pkg.Name);
                        pkgElem.SetAttributeValue("id", pkg.UgcId.Fallback(ContentPackageId.NULL).ToString());
                        break;
                    case ModType.Local:
                        pkgElem.SetAttributeValue("name", pkg.Name);
                        break;
                }
                newRoot.Add(pkgElem);
            }
            writePkgElem(CorePackage);
            RegularPackages.ForEach(writePkgElem);

            if (!Directory.Exists(SavePath)) { Directory.CreateDirectory(SavePath); }
            newDoc.SaveSafe(Path.Combine(SavePath, ToolBox.RemoveInvalidFileNameChars($"{Name}.xml")));
        }
    }
}

namespace Barotrauma.Steam
{
    sealed partial class MutableWorkshopMenu : WorkshopMenu
    {
        private bool OpenLoadPreset(GUIButton _, object __)
        {
            OpenLoadPreset();
            return false;
        }

        private void OpenLoadPreset()
        {
            var msgBox = new GUIMessageBox(
                TextManager.Get("LoadModListPresetHeader"),
                "",
                buttons: new [] { TextManager.Get("Load"), TextManager.Get("Cancel") },
                relativeSize: (0.4f, 0.6f));

            var presetListBox = new GUIListBox(new RectTransform((1.0f, 0.7f), msgBox.Content.RectTransform));

            (string Path, XDocument? Doc) tryLoadXml(string path)
                => (path, XMLExtensions.TryLoadXml(path));
            
            var presets = Directory.Exists(ModListPreset.SavePath)
                ? Directory.GetFiles(ModListPreset.SavePath)
                    .Select(tryLoadXml)
                    .Where(d => d.Doc != null)
                    .ToArray()
                : Array.Empty<(string Path, XDocument? Doc)>();

            foreach (var doc in presets)
            {
                ModListPreset preset = new ModListPreset(doc.Doc!);
                var presetFrame = new GUIFrame(new RectTransform((1.0f, 0.09f), presetListBox.Content.RectTransform),
                    style: "ListBoxElement")
                {
                    UserData = preset,
                    ToolTip = preset.GetTooltip()
                };
                new GUITextBlock(new RectTransform(Vector2.One, presetFrame.RectTransform), preset.Name)
                {
                    CanBeFocused = false
                };
                var deleteBtn
                    = new GUIButton(new RectTransform((0.2f, 1.0f), presetFrame.RectTransform, Anchor.CenterRight),
                        TextManager.Get("Delete"), style: "GUIButtonSmall")
                    {
                        OnClicked = (button, o) =>
                        {
                            File.Delete(doc.Path);
                            presetListBox.Content.RemoveChild(presetFrame);
                            return false;
                        }
                    };
            }

            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                if (presetListBox.SelectedData is ModListPreset preset)
                {
                    var allChildren = enabledRegularModsList.Content.Children
                        .Concat(disabledRegularModsList.Content.Children)
                        .ToArray();
                    enabledRegularModsList.ClearChildren();
                    disabledRegularModsList.ClearChildren();
                    var toEnable =
                        allChildren.Where(c => c.UserData is RegularPackage p
                                               && preset.RegularPackages.Contains(p))
                            .OrderBy(c => c.UserData is RegularPackage p ? preset.RegularPackages.IndexOf(p) : int.MaxValue)
                            .ToArray();
                    var toDisable = allChildren.Where(c => !toEnable.Contains(c)).ToArray();
                    toEnable.ForEach(c => c.RectTransform.Parent = enabledRegularModsList.Content.RectTransform);
                    toDisable.ForEach(c => c.RectTransform.Parent = disabledRegularModsList.Content.RectTransform);
                    
                    enabledCoreDropdown.SelectItem(preset.CorePackage);
                }
                msgBox.Close();
                return false;
            };
            msgBox.Buttons[1].OnClicked = msgBox.Close;
        }
        
        private bool OpenSavePreset(GUIButton _, object __)
        {
            OpenSavePreset();
            return false;
        }
        
        private void OpenSavePreset()
        {
            var msgBox = new GUIMessageBox(
                TextManager.Get("SaveModListPresetHeader"),
                "",
                buttons: new [] { TextManager.Get("Save"), TextManager.Get("Cancel") },
                relativeSize: (0.4f, 0.2f));

            var nameBox = new GUITextBox(new RectTransform((1.0f, 0.3f), msgBox.Content.RectTransform), "");

            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                if (nameBox.Text.IsNullOrEmpty())
                {
                    nameBox.Flash(GUIStyle.Red);
                    return false;
                }

                if (enabledCoreDropdown.SelectedData is CorePackage corePackage)
                {
                    ModListPreset preset = new ModListPreset(nameBox.Text,
                        corePackage,
                        enabledRegularModsList.Content.Children
                            .Select(c => c.UserData)
                            .OfType<RegularPackage>().ToArray());
                    preset.Save();
                }
                msgBox.Close();
                return false;
            };
            msgBox.Buttons[1].OnClicked = msgBox.Close;
        }
    }
}

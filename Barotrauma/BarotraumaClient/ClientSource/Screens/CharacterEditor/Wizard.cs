using Microsoft.Xna.Framework;
using System;
using Barotrauma.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.CharacterEditor
{
    class Wizard
    {
        // Ragdoll data
        private Identifier name;
        private bool isHumanoid;
        private bool canEnterSubmarine = true;
        private bool canWalk;
        private string texturePath;
        private string xmlPath;
        private ContentPackage contentPackage;
        private Dictionary<string, XElement> limbXElements = new Dictionary<string, XElement>();
        private List<GUIComponent> limbGUIElements = new List<GUIComponent>();
        private List<XElement> jointXElements = new List<XElement>();
        private List<GUIComponent> jointGUIElements = new List<GUIComponent>();

        public bool IsCopy { get; private set; }
        public CharacterParams SourceCharacter { get; private set; }
        public RagdollParams SourceRagdoll { get; private set; }
        public IEnumerable<AnimationParams> SourceAnimations { get; private set; }

        public void CopyExisting(CharacterParams character, RagdollParams ragdoll, IEnumerable<AnimationParams> animations)
        {
            IsCopy = true;
            SourceCharacter = character;
            SourceRagdoll = ragdoll;
            SourceAnimations = animations;
            name = character.SpeciesName;
            isHumanoid = character.Humanoid;
            canEnterSubmarine = ragdoll.CanEnterSubmarine;
            canWalk = ragdoll.CanWalk;
            texturePath = ragdoll.Texture;
            if (string.IsNullOrEmpty(texturePath) && name != CharacterPrefab.HumanSpeciesName)
            {
                texturePath = ragdoll.Limbs.FirstOrDefault()?.GetSprite().Texture;
            }
        }

        public static Wizard instance;
        public static Wizard Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Wizard();
                }
                return instance;
            }
        }

        public static LocalizedString GetCharacterEditorTranslation(string text) => CharacterEditorScreen.GetCharacterEditorTranslation(text);

        public void Reset()
        {
            CharacterView.Get().Release();
            RagdollView.Get().Release();
            instance = null;
        }

        public enum Tab { None, Character, Ragdoll }
        private View activeView;
        private Tab currentTab;

        public void SelectTab(Tab tab)
        {
            currentTab = tab;
            activeView?.Box.Close();
            switch (currentTab)
            {
                case Tab.Character:
                    activeView = CharacterView.Get();
                    break;
                case Tab.Ragdoll:
                    activeView = RagdollView.Get();
                    break;
                case Tab.None:
                default:
                    Reset();
                    break;
            }
        }

        public void AddToGUIUpdateList()
        {
            activeView?.Box.AddToGUIUpdateList();
        }

        public void CreateCharacter(XElement ragdollElement, XElement characterElement = null, IEnumerable<AnimationParams> animations = null)
        {
            if (CharacterPrefab.Find(p => p.Identifier == name) != null)
            {
                bool isSamePackage = contentPackage.GetFiles<CharacterFile>().Any(f => Path.GetFileNameWithoutExtension(f.Path.Value) == name);
                LocalizedString verificationText = isSamePackage ? GetCharacterEditorTranslation("existingcharacterfoundreplaceverification") : GetCharacterEditorTranslation("existingcharacterfoundoverrideverification");
                var msgBox = new GUIMessageBox("", verificationText, new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") }, type: GUIMessageBox.Type.Warning)
                {
                    UserData = "verificationprompt"
                };
                msgBox.Buttons[0].OnClicked = (_, userdata) =>
                {
                    msgBox.Close();
                    if (CharacterEditorScreen.Instance.CreateCharacter(name, Path.GetDirectoryName(xmlPath), isHumanoid, contentPackage, ragdollElement, characterElement, animations))
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("CharacterCreated").Replace("[name]", name.Value), GUIStyle.Green, font: GUIStyle.Font);
                    }
                    Wizard.Instance.SelectTab(Tab.None);
                    return true;
                };
                //msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked = (_, userdata) =>
                {
                    msgBox.Close();
                    return true;
                };
            }
            else
            {
                if (CharacterEditorScreen.Instance.CreateCharacter(name, Path.GetDirectoryName(xmlPath), isHumanoid, contentPackage, ragdollElement, characterElement, animations))
                {
                    GUI.AddMessage(GetCharacterEditorTranslation("CharacterCreated").Replace("[name]", name.Value), GUIStyle.Green, font: GUIStyle.Font);
                }
                Wizard.Instance.SelectTab(Tab.None);
            }
        }

        private class CharacterView : View
        {
            private static CharacterView instance;
            public static CharacterView Get() => Get(ref instance);

            public override void Release() => instance = null;

            protected override GUIMessageBox Create()
            {
                var box = new GUIMessageBox(GetCharacterEditorTranslation("CreateNewCharacter"), string.Empty, new LocalizedString[] { TextManager.Get("Cancel"), IsCopy ? TextManager.Get("Create") : TextManager.Get("Next") }, new Vector2(0.65f, 0.9f));
                box.Header.Font = GUIStyle.LargeFont;
                box.Content.ChildAnchor = Anchor.TopCenter;
                box.Content.AbsoluteSpacing = 20;
                int elementSize = 30;
                var frame = new GUIFrame(new RectTransform(new Point(box.Content.Rect.Width - (int)(40 * GUI.xScale), box.Content.Rect.Height - (int)(50 * GUI.yScale)), 
                    box.Content.RectTransform, Anchor.Center), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
                var topGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 1), frame.RectTransform, Anchor.Center)) { AbsoluteSpacing = 2 };
                var fields = new List<GUIComponent>();
                GUITextBox texturePathElement = null;
                GUITextBox xmlPathElement = null;
                GUIDropDown contentPackageDropDown = null;
                bool updateTexturePath = !IsCopy;
                bool isTextureSelected = false;
                void UpdatePaths()
                {
                    string pathBase = $"{ContentPath.ModDirStr}/Characters/{Name}/{Name}";
                    XMLPath = $"{pathBase}.xml";
                    xmlPathElement.Text = XMLPath;
                    if (updateTexturePath)
                    {
                        TexturePath = $"{pathBase}.png";
                        texturePathElement.Text = TexturePath;
                    }
                }
                for (int i = 0; i < 7; i++)
                {
                    var mainElement = new GUIFrame(new RectTransform(new Point(topGroup.RectTransform.Rect.Width, elementSize), topGroup.RectTransform), style: null, color: Color.Gray * 0.25f);
                    fields.Add(mainElement);
                    switch (i)
                    {
                        case 0:
                            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), TextManager.Get("Name"));
                            var nameField = new GUITextBox(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), Name.Value ?? GetCharacterEditorTranslation("DefaultName").Value) { CaretColor = Color.White };
                            string ProcessText(string text) => text.RemoveWhitespace().CapitaliseFirstInvariant();
                            Name = ProcessText(nameField.Text).ToIdentifier();
                            nameField.OnTextChanged += (tb, text) =>
                            {
                                Name = ProcessText(text).ToIdentifier();
                                UpdatePaths();
                                return true;
                            };
                            break;
                        case 1:
                            var label = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), GetCharacterEditorTranslation("IsHumanoid"));
                            var tickBox = new GUITickBox(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), string.Empty)
                            {
                                Selected = IsHumanoid,
                                Enabled = !IsCopy,
                                OnSelected = (tB) => IsHumanoid = tB.Selected
                            };
                            if (!tickBox.Enabled)
                            {
                                label.TextColor *= 0.6f;
                            }
                            break;
                        case 2:
                            var l = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), GetCharacterEditorTranslation("CanEnterSubmarines"));
                            var t = new GUITickBox(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), string.Empty)
                            {
                                Selected = CanEnterSubmarine,
                                Enabled = !IsCopy,
                                OnSelected = (tB) => CanEnterSubmarine = tB.Selected
                            };
                            if (!t.Enabled)
                            {
                                l.TextColor *= 0.6f;
                            }
                            break;
                        case 3:
                            var lbl = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), GetCharacterEditorTranslation("CanWalk"));
                            var txt = new GUITickBox(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), string.Empty)
                            {
                                Selected = CanWalk,
                                Enabled = !IsCopy,
                                OnSelected = (tB) => CanWalk = tB.Selected
                            };
                            if (!txt.Enabled)
                            {
                                lbl.TextColor *= 0.6f;
                            }
                            break;
                        case 4:
                            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), GetCharacterEditorTranslation("ConfigFileOutput"));
                            xmlPathElement = new GUITextBox(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), string.Empty)
                            {
                                Text = XMLPath,
                                CaretColor = Color.White
                            };
                            xmlPathElement.OnTextChanged += (tb, text) =>
                            {
                                XMLPath = text;
                                return true;
                            };
                            break;
                        case 5:
                            {
                                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), GetCharacterEditorTranslation("TexturePath"));
                                var rightContainer = new GUIFrame(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), style: null);
                                texturePathElement = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), rightContainer.RectTransform, Anchor.CenterLeft), string.Empty)
                                {
                                    Text = TexturePath,
                                    CaretColor = Color.White,
                                };
                                texturePathElement.OnTextChanged += (tb, text) =>
                                {
                                    updateTexturePath = false;
                                    TexturePath = text;
                                    return true;
                                };
                                LocalizedString title = GetCharacterEditorTranslation("SelectTexture");
                                new GUIButton(new RectTransform(new Vector2(0.3f / texturePathElement.RectTransform.RelativeSize.X, 1.0f), texturePathElement.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), title, style: "GUIButtonSmall")
                                {
                                    OnClicked = (button, data) =>
                                    {
                                        FileSelection.OnFileSelected = (file) =>
                                        {
                                            string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, Path.GetFullPath(file));
                                            
                                            if (relativePath.StartsWith(ContentPackage.LocalModsDir))
                                            {
                                                string[] pathSplit = relativePath.Split('/', '\\');
                                                string modDirName = $"{ContentPackage.LocalModsDir}/{pathSplit[1]}";
                                                string selectedModDir
                                                    = (contentPackageDropDown.ListBox.SelectedData as ContentPackage)?.Dir.CleanUpPathCrossPlatform(correctFilenameCase: false)
                                                      ?? "";
                                                if (modDirName == selectedModDir)
                                                {
                                                    relativePath = ContentPath.ModDirStr + "/" +
                                                                   string.Join("/", pathSplit[2..]);
                                                }
                                                else
                                                {
                                                    relativePath = string.Format(ContentPath.OtherModDirFmt,
                                                        pathSplit[1]) + "/" +
                                                        string.Join("/", pathSplit[2..]);
                                                }
                                            }
                                            
                                            string destinationPath = relativePath;

                                            //copy file to XML path if it's not located relative to the game's files
                                            if (relativePath.StartsWith("..") ||
                                                    Path.GetPathRoot(Environment.CurrentDirectory) != Path.GetPathRoot(file))
                                            {
                                                destinationPath = Path.Combine(Path.GetDirectoryName(XMLPath), Path.GetFileName(file));

                                                string destinationDir = Path.GetDirectoryName(destinationPath);
                                                if (!Directory.Exists(destinationDir))
                                                {
                                                    Directory.CreateDirectory(destinationDir);
                                                }

                                                if (!File.Exists(destinationPath))
                                                {
                                                    File.Copy(file, Path.GetFullPath(destinationPath), overwrite: true);
                                                }
                                            }

                                            isTextureSelected = true;
                                            texturePathElement.Text = destinationPath.CleanUpPath();
                                        };
                                        FileSelection.ClearFileTypeFilters();
                                        FileSelection.AddFileTypeFilter("PNG", "*.png");
                                        FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
                                        FileSelection.AddFileTypeFilter("All files", "*.*");
                                        FileSelection.SelectFileTypeFilter("*.png");
                                        FileSelection.Open = true;
                                        return true;
                                    }
                                };
                            }
                            break;
                        case 6:
                            {
                                mainElement.RectTransform.NonScaledSize = new Point(
                                    mainElement.RectTransform.NonScaledSize.X,
                                    mainElement.RectTransform.NonScaledSize.Y * 2);
                                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), mainElement.RectTransform, Anchor.CenterLeft), TextManager.Get("ContentPackage"));
                                var rightContainer = new GUIFrame(new RectTransform(new Vector2(0.7f, 1), mainElement.RectTransform, Anchor.CenterRight), style: null);
                                contentPackageDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.5f), rightContainer.RectTransform, Anchor.TopRight));
                                foreach (ContentPackage contentPackage in ContentPackageManager.EnabledPackages.All)
                                {
                                    if (contentPackage != GameMain.VanillaContent)
                                    {
                                        contentPackageDropDown.AddItem(contentPackage.Name, userData: contentPackage, toolTip: contentPackage.Path);
                                    }
                                }
                                contentPackageDropDown.OnSelected = (obj, userdata) =>
                                {
                                    ContentPackage = userdata as ContentPackage;
                                    updateTexturePath = !isTextureSelected && !IsCopy;
                                    UpdatePaths();
                                    return true;
                                };
                                contentPackageDropDown.Select(0);
                                var contentPackageNameElement = new GUITextBox(new RectTransform(new Vector2(0.7f, 0.5f), rightContainer.RectTransform, Anchor.BottomLeft),
                                    GetCharacterEditorTranslation("NewContentPackage").Value)
                                {
                                    CaretColor = Color.White,
                                };
                                var createNewPackageButton = new GUIButton(new RectTransform(new Vector2(0.3f / contentPackageNameElement.RectTransform.RelativeSize.X, 1.0f), contentPackageNameElement.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), TextManager.Get("CreateNew"), style: "GUIButtonSmall")
                                {
                                    OnClicked = (btn, userdata) =>
                                    {
                                        if (string.IsNullOrEmpty(contentPackageNameElement.Text))
                                        {
                                            contentPackageNameElement.Flash(useRectangleFlash: true);
                                            return false;
                                        }
                                        if (ContentPackageManager.AllPackages.Any(cp => cp.Name.ToLower() == contentPackageNameElement.Text.ToLower()))
                                        {
                                            new GUIMessageBox("", TextManager.Get("charactereditor.contentpackagenameinuse", "leveleditorlevelobjnametaken"), type: GUIMessageBox.Type.Warning);
                                            return false;
                                        }
                                        string modName = contentPackageNameElement.Text;

                                        var modProject = new ModProject { Name = modName };
                                        ContentPackage = ContentPackageManager.LocalPackages.SaveAndEnableRegularMod(modProject);

                                        contentPackageDropDown.AddItem(ContentPackage.Name, ContentPackage, ContentPackage.Path);
                                        contentPackageDropDown.SelectItem(ContentPackage);
                                        contentPackageNameElement.Text = "";
                                        return true;
                                    },
                                    Enabled = false
                                };
                                Color textColor = contentPackageNameElement.TextColor;
                                contentPackageNameElement.TextColor *= 0.6f;
                                contentPackageNameElement.OnSelected += (sender, key) =>
                                {
                                    contentPackageNameElement.Text = "";
                                };
                                contentPackageNameElement.OnTextChanged += (textBox, text) =>
                                {
                                    textBox.TextColor = textColor;
                                    createNewPackageButton.Enabled = !string.IsNullOrWhiteSpace(text);
                                    return true;
                                };
                                rightContainer.RectTransform.MinSize = new Point(0, 
                                    contentPackageDropDown.RectTransform.MinSize.Y + Math.Max(contentPackageNameElement.RectTransform.MinSize.Y, createNewPackageButton.RectTransform.MinSize.Y));
                            }
                            break;
                    }
                    int contentSize = mainElement.RectTransform.Children.Max(c => c.MinSize.Y) ;
                    mainElement.RectTransform.Resize(new Point(mainElement.Rect.Width, Math.Max(mainElement.Rect.Height, contentSize)));
                }
                UpdatePaths();
                box.Buttons[0].Parent.RectTransform.SetAsLastChild();
                box.Buttons[1].RectTransform.SetAsLastChild();
                // Cancel
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    Wizard.Instance.SelectTab(Tab.None);
                    return true;
                };
                // Next
                box.Buttons[1].OnClicked += (b, d) =>
                {
                    if (ContentPackage == null)
                    {
                        contentPackageDropDown.Flash(useRectangleFlash: true);
                        return false;
                    }

                    string evaluatedTexturePath = ContentPath.FromRawNoConcrete(
                        contentPackageDropDown.SelectedData as ContentPackage,
                        TexturePath).Value;
                    if (SourceCharacter?.SpeciesName != CharacterPrefab.HumanSpeciesName)
                    {
                        if (!File.Exists(evaluatedTexturePath))
                        {
                            GUI.AddMessage(GetCharacterEditorTranslation("TextureDoesNotExist"), GUIStyle.Red);
                            texturePathElement.Flash(useRectangleFlash: true);
                            return false;
                        }
                    }
                    var path = Path.GetFileName(evaluatedTexturePath);
                    if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        GUI.AddMessage(TextManager.Get("WrongFileType"), GUIStyle.Red);
                        texturePathElement.Flash(useRectangleFlash: true);
                        return false;
                    }
                    if (Name == CharacterPrefab.HumanSpeciesName && !IsCopy)
                    {
                        // Force a copy when trying to override a human, because handling the crash would be very difficult (we require humans to have certain definitions).
                        if (!CharacterEditorScreen.Instance.SpawnedCharacter.IsHuman)
                        {
                            CharacterEditorScreen.Instance.SpawnCharacter(CharacterPrefab.HumanSpeciesName);
                        }
                        CharacterEditorScreen.Instance.PrepareCharacterCopy();
                    }
                    if (IsCopy)
                    {
                        SourceRagdoll.Texture = evaluatedTexturePath;
                        SourceRagdoll.CanEnterSubmarine = CanEnterSubmarine;
                        SourceRagdoll.CanWalk = CanWalk;
                        SourceRagdoll.Serialize();
                        Instance.CreateCharacter(SourceRagdoll.MainElement, SourceCharacter.MainElement, SourceAnimations);
                    }
                    else
                    {
                        Instance.SelectTab(Tab.Ragdoll);
                    }
                    return true;
                };
                return box;
            }
        }

        private class RagdollView : View
        {
            private static RagdollView instance;
            public static RagdollView Get() => Get(ref instance);

            public override void Release() => instance = null;

            protected override GUIMessageBox Create()
            {
                var box = new GUIMessageBox(GetCharacterEditorTranslation("DefineRagdoll"), string.Empty, new LocalizedString[] { TextManager.Get("Previous"), TextManager.Get("Create") }, new Vector2(0.65f, 1f));
                box.Header.Font = GUIStyle.LargeFont;
                box.Content.ChildAnchor = Anchor.TopCenter;
                box.Content.AbsoluteSpacing = (int)(20 * GUI.Scale);
                int elementSize = (int)(40 * GUI.Scale);
                var frame = new GUIFrame(new RectTransform(new Point(box.Content.Rect.Width - (int)(80 * GUI.xScale), box.Content.Rect.Height - (int)(200 * GUI.yScale)),
                    box.Content.RectTransform, Anchor.Center), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
                var content = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), frame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.TopCenter) 
                { 
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                // Limbs
                var limbsElement = new GUIFrame(new RectTransform(new Vector2(1, 0.05f), content.RectTransform), style: null) { CanBeFocused = false };

                var limbEditLayout = new GUILayoutGroup(new RectTransform(Vector2.One, limbsElement.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), limbEditLayout.RectTransform), GetCharacterEditorTranslation("Limbs"), font: GUIStyle.SubHeadingFont);
                var limbsList = new GUIListBox(new RectTransform(new Vector2(1, 0.45f), content.RectTransform))
                {
                    PlaySoundOnSelect = true,
                };
                var limbButtonSize = Vector2.One * 0.8f;
                var removeLimbButton = new GUIButton(new RectTransform(limbButtonSize, limbEditLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton")
                {
                    OnClicked = (b, d) =>
                    {
                        var element = LimbGUIElements.LastOrDefault();
                        if (element == null) { return false; }
                        element.RectTransform.Parent = null;
                        LimbGUIElements.Remove(element);
                        return true;
                    }
                };
                var addLimbButton = new GUIButton(new RectTransform(limbButtonSize, limbEditLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton")
                {
                    OnClicked = (b, d) =>
                    {
                        LimbType limbType = LimbType.None;
                        switch (LimbGUIElements.Count)
                        {
                            case 0:
                                limbType = LimbType.Torso;
                                break;
                            case 1:
                                limbType = LimbType.Head;
                                break;
                        }
                        CreateLimbGUIElement(limbsList.Content.RectTransform, elementSize, id: LimbGUIElements.Count, limbType: limbType);
                        return true;
                    }
                };

                int _x = 1, _y = 1, w = 100, h = 100;
                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), limbEditLayout.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.01f
                };
                for (int i = 3; i >= 0; i--)
                {
                    var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform) { MinSize = new Point(50, 0), MaxSize = new Point(150, 50) }, style: null);
                    new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.RectComponentLabels[i], font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft);
                    GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight), NumberType.Int)
                    {
                        Font = GUIStyle.SmallFont
                    };
                    switch (i)
                    {
                        case 0:
                        case 1:
                            numberInput.IntValue = 1;
                            numberInput.MinValueInt = 1;
                            numberInput.MaxValueInt = 100;
                            break;
                        case 2:
                        case 3:
                            numberInput.IntValue = 100;
                            numberInput.MinValueInt = 0;
                            numberInput.MaxValueInt = 999;
                            break;

                    }
                    int comp = i;
                    numberInput.OnValueChanged += (numInput) =>
                    {
                        switch (comp)
                        {
                            case 0:
                                _x = numInput.IntValue;
                                break;
                            case 1:
                                _y = numInput.IntValue;
                                break;
                            case 2:
                                w = numInput.IntValue;
                                break;
                            case 3:
                                h = numInput.IntValue;
                                break;
                        }
                    };
                }
                inputArea.Recalculate();
                new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), limbEditLayout.RectTransform),
                    GetCharacterEditorTranslation("AddMultipleLimbsButton"))
                {
                    OnClicked = (b, d) =>
                    {
                        CreateMultipleLimbs(_x, _y);
                        return true;
                    }
                };
                limbsElement.RectTransform.MinSize = new Point(0, limbEditLayout.RectTransform.Children.Max(c => c.MinSize.Y));

                // If no elements are defined, create some as default
                if (LimbGUIElements.None())
                {
                    if (IsHumanoid)
                    {
                        CreateMultipleLimbs(2, 6);
                        // Create the missing waist (13th element)
                        CreateLimbGUIElement(limbsList.Content.RectTransform, elementSize, id: LimbGUIElements.Count, limbType: LimbType.Waist, sourceRect: new Rectangle(_x, h * LimbGUIElements.Count / 2, w, h));
                    }
                    else
                    {
                        CreateMultipleLimbs(1, 2);
                    }
                }
                void CreateMultipleLimbs(int x, int y)
                {
                    for (int i = 0; i < x; i++)
                    {
                        for (int j = 0; j < y; j++)
                        {
                            LimbType limbType = LimbType.None;
                            switch (LimbGUIElements.Count)
                            {
                                case 0:
                                    limbType = LimbType.Torso;
                                    break;
                                case 1:
                                    limbType = LimbType.Head;
                                    break;
                            }
                            if (IsHumanoid)
                            {
                                switch (LimbGUIElements.Count)
                                {
                                    case 2:
                                        limbType = LimbType.LeftArm;
                                        break;
                                    case 3:
                                        limbType = LimbType.LeftHand;
                                        break;
                                    case 4:
                                        limbType = LimbType.RightArm;
                                        break;
                                    case 5:
                                        limbType = LimbType.RightHand;
                                        break;
                                    case 6:
                                        limbType = LimbType.LeftThigh;
                                        break;
                                    case 7:
                                        limbType = LimbType.LeftLeg;
                                        break;
                                    case 8:
                                        limbType = LimbType.LeftFoot;
                                        break;
                                    case 9:
                                        limbType = LimbType.RightThigh;
                                        break;
                                    case 10:
                                        limbType = LimbType.RightLeg;
                                        break;
                                    case 11:
                                        limbType = LimbType.RightFoot;
                                        break;
                                    case 12:
                                        limbType = LimbType.Waist;
                                        break;
                                }
                            }
                            CreateLimbGUIElement(limbsList.Content.RectTransform, elementSize, id: LimbGUIElements.Count, limbType: limbType, sourceRect: new Rectangle(i * w, j * h, w, h));
                        }
                    }
                }
                // Joints
                new GUIFrame(new RectTransform(new Vector2(1, 0.05f), content.RectTransform), style: null) { CanBeFocused = false };
                var jointsElement = new GUIFrame(new RectTransform(new Vector2(1, 0.05f), content.RectTransform), style: null) { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), jointsElement.RectTransform), GetCharacterEditorTranslation("Joints"), font: GUIStyle.SubHeadingFont);
                var jointButtonElement = new GUIFrame(new RectTransform(new Vector2(0.5f, 1f), jointsElement.RectTransform)
                { 
                    RelativeOffset = new Vector2(0.15f, 0) 
                }, style: null)
                { 
                    CanBeFocused = false 
                };
                var jointsList = new GUIListBox(new RectTransform(new Vector2(1, 0.45f), content.RectTransform))
                {
                    PlaySoundOnSelect = true,
                };
                var removeJointButton = new GUIButton(new RectTransform(new Point(jointButtonElement.Rect.Height, jointButtonElement.Rect.Height), jointButtonElement.RectTransform), style: "GUIMinusButton")
                {
                    OnClicked = (b, d) =>
                    {
                        var element = JointGUIElements.LastOrDefault();
                        if (element == null) { return false; }
                        element.RectTransform.Parent = null;
                        JointGUIElements.Remove(element);
                        return true;
                    }
                };
                var addJointButton = new GUIButton(new RectTransform(new Point(jointButtonElement.Rect.Height), jointButtonElement.RectTransform)
                {
                    AbsoluteOffset = new Point(removeJointButton.Rect.Width + 10, 0)
                }, style: "GUIPlusButton")
                {
                    OnClicked = (b, d) =>
                    {
                        CreateJointGUIElement(jointsList.Content.RectTransform, elementSize);
                        return true;
                    }
                };
                // Previous
                box.Buttons[0].OnClicked += (b, d) =>
                {
                    Wizard.Instance.SelectTab(Tab.Character);
                    return true;
                };
                // Parse and create
                box.Buttons[1].OnClicked += (b, d) =>
                {
                    ParseLimbsFromGUIElements();
                    ParseJointsFromGUIElements();
                    var main = LimbXElements.Values.Select(xe => xe.Attribute("type")).Where(a => a.Value.Equals("torso", StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? 
                        LimbXElements.Values.Select(xe => xe.Attribute("type")).Where(a => a.Value.Equals("head", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (main == null)
                    {
                        GUI.AddMessage(GetCharacterEditorTranslation("MissingTorsoOrHead"), GUIStyle.Red);
                        return false;
                    }
                    if (IsHumanoid)
                    {
                        if (!IsValid(LimbXElements.Values, true, out string missingType))
                        {
                            GUI.AddMessage(GetCharacterEditorTranslation("MissingLimbType").Replace("[limbtype]", missingType.FormatCamelCaseWithSpaces()), GUIStyle.Red);
                            return false;
                        }
                    }
                    XElement mainLimb = main.Parent;
                    int radius = mainLimb.GetAttributeInt("radius", -1);
                    int height = mainLimb.GetAttributeInt("height", -1);
                    int width = mainLimb.GetAttributeInt("width", -1);
                    int colliderHeight = -1;
                    if (radius == -1)
                    {
                        // the collider is a box -> calculate the capsule
                        if (width == height)
                        {
                            radius = width / 2;
                            colliderHeight = width - radius * 2;
                        }
                        else
                        {
                            if (height > width)
                            {
                                radius = width / 2;
                                colliderHeight = height - radius * 2;
                            }
                            else
                            {
                                radius = height / 2;
                                colliderHeight = width - radius * 2;
                            }
                        }
                        radius = Math.Max(radius, 1);
                    }
                    else if (height > -1 || width > -1)
                    {
                        // the collider is a capsule -> use the capsule as it is
                        colliderHeight = width > height ? width : height;
                    }
                    var colliderAttributes = new List<XAttribute>() { new XAttribute("radius", radius) };
                    if (colliderHeight > -1)
                    {
                        colliderHeight = Math.Max(colliderHeight, 1);
                        if (height > width)
                        {
                            colliderAttributes.Add(new XAttribute("height", colliderHeight));
                        }
                        else
                        {
                            colliderAttributes.Add(new XAttribute("width", colliderHeight));
                        }
                    }
                    var colliderElements = new List<XElement>() { new XElement("collider", colliderAttributes) };
                    if (IsHumanoid)
                    {
                        // For humanoids, we need a secondary, shorter collider for crouching
                        var secondaryCollider = new XElement("collider", new XAttribute("radius", radius));
                        if (colliderHeight > -1)
                        {
                            colliderHeight = Math.Max(colliderHeight, 1);
                            if (height > width)
                            {
                                secondaryCollider.Add(new XAttribute("height", colliderHeight * 0.75f));
                            }
                            else
                            {
                                secondaryCollider.Add(new XAttribute("width", colliderHeight * 0.75f));
                            }
                        }
                        colliderElements.Add(secondaryCollider);
                    }
                    var mainElement = new XElement("Ragdoll",
                            new XAttribute("type", Name),
                            new XAttribute("texture", TexturePath),
                            new XAttribute("canentersubmarine", CanEnterSubmarine),
                            new XAttribute("canwalk", CanWalk),
                                colliderElements,
                                LimbXElements.Values,
                                JointXElements);
                    Wizard.Instance.CreateCharacter(mainElement);
                    return true;
                };
                return box;
            }

            private void CreateLimbGUIElement(RectTransform parent, int elementSize, int id, string name = "", LimbType limbType = LimbType.None, Rectangle? sourceRect = null)
            {
                var limbElement = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, elementSize * 5 + 40), parent), style: null, color: Color.Gray * 0.25f)
                {
                    CanBeFocused = false
                };
                var group = new GUILayoutGroup(new RectTransform(Vector2.One, limbElement.RectTransform)) { AbsoluteSpacing = 16 };
                var label = new GUITextBlock(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), name, font: GUIStyle.SubHeadingFont);
                var idField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                var nameField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                var limbTypeField = GUI.CreateEnumField(limbType, elementSize, GetCharacterEditorTranslation("LimbType"), group.RectTransform, font: GUIStyle.Font);
                var sourceRectField = GUI.CreateRectangleField(sourceRect ?? new Rectangle(0, 100 * LimbGUIElements.Count, 100, 100), elementSize, GetCharacterEditorTranslation("SourceRectangle"), group.RectTransform, font: GUIStyle.Font);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), idField.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("ID"));
                new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), idField.RectTransform, Anchor.TopRight), NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = byte.MaxValue,
                    IntValue = id,
                    OnValueChanged = numInput =>
                    {
                        id = numInput.IntValue;
                        string text = nameField.GetChild<GUITextBox>().Text;
                        string t = string.IsNullOrWhiteSpace(text) ? id.ToString() : text;
                        label.Text = t;
                    }
                };
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), TextManager.Get("Name"));
                var nameInput = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopRight), name)
                {
                    CaretColor = Color.White,
                };
                nameInput.OnTextChanged += (tb, text) =>
                {
                    string t = string.IsNullOrWhiteSpace(text) ? id.ToString() : text;
                    label.Text = t;
                    return true;
                };
                LimbGUIElements.Add(limbElement);
            }

            private void CreateJointGUIElement(RectTransform parent, int elementSize, int id1 = 0, int id2 = 1, Vector2? anchor1 = null, Vector2? anchor2 = null, string jointName = "")
            {
                var jointElement = new GUIFrame(new RectTransform(new Point(parent.Rect.Width, elementSize * 6 + 40), parent), style: null, color: Color.Gray * 0.25f)
                {
                    CanBeFocused = false
                };
                var group = new GUILayoutGroup(new RectTransform(Vector2.One, jointElement.RectTransform)) { AbsoluteSpacing = 2 };
                var label = new GUITextBlock(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), jointName, font: GUIStyle.SubHeadingFont);
                var nameField = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopLeft), TextManager.Get("Name"));
                var nameInput = new GUITextBox(new RectTransform(new Vector2(0.5f, 1), nameField.RectTransform, Anchor.TopRight), jointName)
                {
                    CaretColor = Color.White,
                };
                nameInput.OnTextChanged += (textB, text) =>
                {
                    jointName = text;
                    label.Text = jointName;
                    return true;
                };
                var limb1Field = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "1"));
                var limb1InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb1Field.RectTransform, Anchor.TopRight), NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = byte.MaxValue,
                    IntValue = id1
                };
                var limb2Field = new GUIFrame(new RectTransform(new Point(group.Rect.Width, elementSize), group.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopLeft), GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "2"));
                var limb2InputField = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1), limb2Field.RectTransform, Anchor.TopRight), NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = byte.MaxValue,
                    IntValue = id2
                };
                GUI.CreateVector2Field(anchor1 ?? Vector2.Zero, elementSize, GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "1"), group.RectTransform, font: GUIStyle.Font, decimalsToDisplay: 2);
                GUI.CreateVector2Field(anchor2 ?? Vector2.Zero, elementSize, GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "2"), group.RectTransform, font: GUIStyle.Font, decimalsToDisplay: 2);
                label.Text = GetJointName(jointName);
                limb1InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                limb2InputField.OnValueChanged += nInput => label.Text = GetJointName(jointName);
                JointGUIElements.Add(jointElement);
                string GetJointName(string n) => string.IsNullOrWhiteSpace(n) ? $"{GetCharacterEditorTranslation("Joint")} {limb1InputField.IntValue} - {limb2InputField.IntValue}" : n;
            }
        }

        private abstract class View
        {
            // Easy accessors to the common data.

            public bool IsCopy => Instance.IsCopy;
            public IEnumerable<AnimationParams> SourceAnimations => Instance.SourceAnimations;
            public CharacterParams SourceCharacter => Instance.SourceCharacter;
            public RagdollParams SourceRagdoll => Instance.SourceRagdoll;

            public Identifier Name
            {
                get => Instance.name;
                set => Instance.name = value;
            }
            public bool IsHumanoid
            {
                get => Instance.isHumanoid;
                set => Instance.isHumanoid = value;
            }
            public bool CanEnterSubmarine
            {
                get => Instance.canEnterSubmarine;
                set => Instance.canEnterSubmarine = value;
            }
            public bool CanWalk
            {
                get => Instance.canWalk;
                set => Instance.canWalk = value;
            }
            public ContentPackage ContentPackage
            {
                get => Instance.contentPackage;
                set => Instance.contentPackage = value;
            }
            public string TexturePath
            {
                get => Instance.texturePath;
                set => Instance.texturePath = value;
            }
            public string XMLPath
            {
                get => Instance.xmlPath;
                set => Instance.xmlPath = value;
            }
            public Dictionary<string, XElement> LimbXElements
            {
                get => Instance.limbXElements;
                set => Instance.limbXElements = value;
            }
            public List<GUIComponent> LimbGUIElements
            {
                get => Instance.limbGUIElements;
                set => Instance.limbGUIElements = value;
            }
            public List<XElement> JointXElements
            {
                get => Instance.jointXElements;
                set => Instance.jointXElements = value;
            }
            public List<GUIComponent> JointGUIElements
            {
                get => Instance.jointGUIElements;
                set => Instance.jointGUIElements = value;
            }

            private GUIMessageBox box;
            public GUIMessageBox Box
            {
                get
                {
                    if (box == null)
                    {
                        box = Create();
                    }
                    return box;
                }
            }

            protected abstract GUIMessageBox Create();
            protected static T Get<T>(ref T instance) where T : View, new()
            {
                if (instance == null)
                {
                    instance = new T();
                }
                return instance;
            }

            public abstract void Release();

            protected void ParseLimbsFromGUIElements()
            {
                LimbXElements.Clear();
                for (int i = 0; i < LimbGUIElements.Count; i++)
                {
                    var limbGUIElement = LimbGUIElements[i];
                    var allChildren = limbGUIElement.GetAllChildren();
                    GUITextBlock GetField(LocalizedString n) => allChildren.First(c => c is GUITextBlock textBlock && textBlock.Text == n) as GUITextBlock;
                    int id = GetField(GetCharacterEditorTranslation("ID")).Parent.GetChild<GUINumberInput>().IntValue;
                    string limbName = GetField(TextManager.Get("Name")).Parent.GetChild<GUITextBox>().Text;
                    LimbType limbType = (LimbType)GetField(GetCharacterEditorTranslation("LimbType")).Parent.GetChild<GUIDropDown>().SelectedData;
                    // Reverse, because the elements are created from right to left
                    var rectInputs = GetField(GetCharacterEditorTranslation("SourceRectangle")).Parent.GetAllChildren<GUINumberInput>().Reverse().ToArray();
                    int width = rectInputs[2].IntValue;
                    int height = rectInputs[3].IntValue;
                    var colliderAttributes = new List<XAttribute>();
                    // Capsules/Circles
                    //if (width == height)
                    //{
                    //    colliderAttributes.Add(new XAttribute("radius", (int)(width / 2 * 0.85f)));
                    //}
                    //else
                    //{
                    //    if (height > width)
                    //    {
                    //        colliderAttributes.Add(new XAttribute("radius", (int)(width / 2 * 0.85f)));
                    //        colliderAttributes.Add(new XAttribute("height",(int) (height - width * 0.85f)));
                    //    }
                    //    else
                    //    {
                    //        colliderAttributes.Add(new XAttribute("radius", (int)(height / 2 * 0.85f)));
                    //        colliderAttributes.Add(new XAttribute("width", (int)(width - height * 0.85f)));
                    //    }
                    //}
                    // Rectangles
                    colliderAttributes.Add(new XAttribute("height", (int)(height * 0.85f)));
                    colliderAttributes.Add(new XAttribute("width", (int)(width * 0.85f)));
                    LimbXElements.Add(id.ToString(), new XElement("limb",
                        new XAttribute("id", id),
                        new XAttribute("name", limbName),
                        new XAttribute("type", limbType.ToString()),
                        colliderAttributes,
                        new XElement("sprite",
                            new XAttribute("texture", ""),
                            new XAttribute("sourcerect", $"{rectInputs[0].IntValue}, {rectInputs[1].IntValue}, {width}, {height}")),
                        new XAttribute("notes", null ?? string.Empty)
                    ));
                }
            }

            protected void ParseJointsFromGUIElements()
            {
                JointXElements.Clear();
                for (int i = 0; i < JointGUIElements.Count; i++)
                {
                    var jointGUIElement = JointGUIElements[i];
                    var allChildren = jointGUIElement.GetAllChildren();
                    GUITextBlock GetField(LocalizedString n) => allChildren.First(c => c is GUITextBlock textBlock && textBlock.Text == n) as GUITextBlock;
                    string jointName = GetField(TextManager.Get("Name")).Parent.GetChild<GUITextBox>().Text;
                    int limb1ID = GetField(GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "1")).Parent.GetChild<GUINumberInput>().IntValue;
                    int limb2ID = GetField(GetCharacterEditorTranslation("LimbWithIndex").Replace("[index]", "2")).Parent.GetChild<GUINumberInput>().IntValue;
                    // Reverse, because the elements are created from right to left
                    var anchor1Inputs = GetField(GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "1")).Parent.GetAllChildren<GUINumberInput>().Reverse().ToArray();
                    var anchor2Inputs = GetField(GetCharacterEditorTranslation("LimbWithIndexAnchor").Replace("[index]", "2")).Parent.GetAllChildren<GUINumberInput>().Reverse().ToArray();
                    JointXElements.Add(new XElement("joint",
                        new XAttribute("name", jointName),
                        new XAttribute("limb1", limb1ID),
                        new XAttribute("limb2", limb2ID),
                        new XAttribute("limb1anchor", $"{anchor1Inputs[0].FloatValue.Format(2)}, {anchor1Inputs[1].FloatValue.Format(2)}"),
                        new XAttribute("limb2anchor", $"{anchor2Inputs[0].FloatValue.Format(2)}, {anchor2Inputs[1].FloatValue.Format(2)}")));
                }
            }

            protected LimbType ParseLimbType(string limbName)
            {
                var limbType = LimbType.None;
                string n = limbName.ToLowerInvariant();
                switch (n)
                {
                    case "head":
                        limbType = LimbType.Head;
                        break;
                    case "torso":
                        limbType = LimbType.Torso;
                        break;
                    case "waist":
                    case "pelvis":
                        limbType = LimbType.Waist;
                        break;
                    case "tail":
                        limbType = LimbType.Tail;
                        break;
                }
                if (limbType == LimbType.None)
                {
                    if (n.Contains("tail"))
                    {
                        limbType = LimbType.Tail;
                    }
                    else if (n.Contains("arm") && !n.Contains("lower"))
                    {
                        if (n.Contains("right"))
                        {
                            limbType = LimbType.RightArm;
                        }
                        else if (n.Contains("left"))
                        {
                            limbType = LimbType.LeftArm;
                        }
                    }
                    else if (n.Contains("hand") || n.Contains("palm"))
                    {
                        if (n.Contains("right"))
                        {
                            limbType = LimbType.RightHand;
                        }
                        else if (n.Contains("left"))
                        {
                            limbType = LimbType.LeftHand;
                        }
                    }
                    else if (n.Contains("thigh") || n.Contains("upperleg"))
                    {
                        if (n.Contains("right"))
                        {
                            limbType = LimbType.RightThigh;
                        }
                        else if (n.Contains("left"))
                        {
                            limbType = LimbType.LeftThigh;
                        }
                    }
                    else if (n.Contains("shin") || n.Contains("lowerleg"))
                    {
                        if (n.Contains("right"))
                        {
                            limbType = LimbType.RightLeg;
                        }
                        else if (n.Contains("left"))
                        {
                            limbType = LimbType.LeftLeg;
                        }
                    }
                    else if (n.Contains("foot"))
                    {
                        if (n.Contains("right"))
                        {
                            limbType = LimbType.RightFoot;
                        }
                        else if (n.Contains("left"))
                        {
                            limbType = LimbType.LeftFoot;
                        }
                    }
                }
                return limbType;
            }

            public static bool IsValid(IEnumerable<XElement> elements, bool isHumanoid, out string missingType)
            {
                missingType = "none";
                if (!HasAtLeastOneLimbOfType(elements, "torso") && !HasAtLeastOneLimbOfType(elements, "head"))
                {
                    missingType = "TorsoOrHead";
                    return false;
                }
                if (isHumanoid)
                {
                    if (!HasOnlyOneLimbOfType(elements, missingType = "LeftArm")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "LeftHand")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "RightArm")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "RightHand")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "Waist")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "LeftThigh")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "LeftLeg")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "LeftFoot")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "RightThigh")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "RightLeg")) { return false; }
                    if (!HasOnlyOneLimbOfType(elements, missingType = "RightFoot")) { return false; }
                }
                return true;
            }

            public static bool HasAtLeastOneLimbOfType(IEnumerable<XElement> elements, string type) => elements.Any(e => IsType(e, type));
            public static bool HasOnlyOneLimbOfType(IEnumerable<XElement> elements, string type) => elements.Count(e => IsType(e, type)) == 1;
            private static bool IsType(XElement element, string type) => element.GetAttributeString("type", "").Equals(type, StringComparison.OrdinalIgnoreCase);
        }
    }
}

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public partial class GameSettings
    {
        public enum Tab
        {
            Graphics,
            Audio,
            VoiceChat,
            Controls,
            Gameplay,
#if DEBUG
            Debug
#endif
        }

        private readonly Point MinSupportedResolution = new Point(1024, 540);

        private GUIFrame settingsFrame;
        private GUIButton applyButton;

        private GUIFrame[] tabs;
        private GUIButton[] tabButtons;

        public Action OnHUDScaleChanged;

        public GUIFrame SettingsFrame
        {
            get
            {
                if (settingsFrame == null) CreateSettingsFrame();
                return settingsFrame;
            }
        }

        private const int inventoryHotkeyCount = 10;

        public void SetDefaultBindings(XDocument doc = null, bool legacy = false)
        {
            keyMapping = new KeyOrMouse[Enum.GetNames(typeof(InputType)).Length];
            keyMapping[(int)InputType.Run] = new KeyOrMouse(Keys.LeftShift);
            keyMapping[(int)InputType.Attack] = new KeyOrMouse(Keys.F);
            keyMapping[(int)InputType.Crouch] = new KeyOrMouse(Keys.LeftControl);
            keyMapping[(int)InputType.Grab] = new KeyOrMouse(Keys.G);
            keyMapping[(int)InputType.Health] = new KeyOrMouse(Keys.H);
            keyMapping[(int)InputType.Ragdoll] = new KeyOrMouse(Keys.Space);
            keyMapping[(int)InputType.Aim] = new KeyOrMouse(MouseButton.SecondaryMouse);

            keyMapping[(int)InputType.InfoTab] = new KeyOrMouse(Keys.Tab);
            keyMapping[(int)InputType.Chat] = new KeyOrMouse(Keys.T);
            keyMapping[(int)InputType.RadioChat] = new KeyOrMouse(Keys.R);
            keyMapping[(int)InputType.CrewOrders] = new KeyOrMouse(Keys.C);

            keyMapping[(int)InputType.Voice] = new KeyOrMouse(Keys.V);
            keyMapping[(int)InputType.LocalVoice] = new KeyOrMouse(Keys.B);
            keyMapping[(int)InputType.Command] = new KeyOrMouse(MouseButton.MiddleMouse);
            keyMapping[(int)InputType.PreviousFireMode] = new KeyOrMouse(MouseButton.MouseWheelDown);
            keyMapping[(int)InputType.NextFireMode] = new KeyOrMouse(MouseButton.MouseWheelUp);

            keyMapping[(int)InputType.TakeHalfFromInventorySlot] = new KeyOrMouse(Keys.LeftShift);
            keyMapping[(int)InputType.TakeOneFromInventorySlot] = new KeyOrMouse(Keys.LeftControl);

            if (Language == "French")
            {
                keyMapping[(int)InputType.Up] = new KeyOrMouse(Keys.Z);
                keyMapping[(int)InputType.Down] = new KeyOrMouse(Keys.S);
                keyMapping[(int)InputType.Left] = new KeyOrMouse(Keys.Q);
                keyMapping[(int)InputType.Right] = new KeyOrMouse(Keys.D);
                keyMapping[(int)InputType.ToggleInventory] = new KeyOrMouse(Keys.A);

                keyMapping[(int)InputType.SelectNextCharacter] = new KeyOrMouse(Keys.X);
                keyMapping[(int)InputType.SelectPreviousCharacter] = new KeyOrMouse(Keys.W);
            }
            else
            {
                keyMapping[(int)InputType.Up] = new KeyOrMouse(Keys.W);
                keyMapping[(int)InputType.Down] = new KeyOrMouse(Keys.S);
                keyMapping[(int)InputType.Left] = new KeyOrMouse(Keys.A);
                keyMapping[(int)InputType.Right] = new KeyOrMouse(Keys.D);
                keyMapping[(int)InputType.ToggleInventory] = new KeyOrMouse(Keys.Q);

                keyMapping[(int)InputType.SelectNextCharacter] = new KeyOrMouse(Keys.Z);
                keyMapping[(int)InputType.SelectPreviousCharacter] = new KeyOrMouse(Keys.X);
            }

            if (legacy)
            {
                keyMapping[(int)InputType.Use] = new KeyOrMouse(MouseButton.PrimaryMouse);
                keyMapping[(int)InputType.Shoot] = new KeyOrMouse(MouseButton.PrimaryMouse);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Deselect] = new KeyOrMouse(Keys.E);
            }
            else
            {
                keyMapping[(int)InputType.Use] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(MouseButton.PrimaryMouse);
                // shoot and deselect are handled in CheckBindings() so that we don't override the legacy settings.
            }

            inventoryKeyMapping = new KeyOrMouse[inventoryHotkeyCount];
            for (int i = 0; i < inventoryKeyMapping.Length; i++)
            {
                inventoryKeyMapping[i] = new KeyOrMouse(Keys.D0 + (i + 1) % 10);
            }

            if (doc != null)
            {
                LoadControls(doc);
            }
        }

        public void CheckBindings(bool useDefaults)
        {
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                var binding = keyMapping[(int)inputType];
                if (binding == null)
                {
                    switch (inputType)
                    {
                        case InputType.Deselect:
                            if (useDefaults)
                            {
                                binding = new KeyOrMouse(MouseButton.SecondaryMouse);
                            }
                            else
                            {
                                // Legacy support
                                var selectKey = keyMapping[(int)InputType.Select];
                                if (selectKey != null && selectKey.Key != Keys.None)
                                {
                                    binding = new KeyOrMouse(selectKey.Key);
                                }
                            }
                            break;
                        case InputType.Shoot:
                            if (useDefaults)
                            {
                                binding = new KeyOrMouse(MouseButton.PrimaryMouse);
                            }
                            else
                            {
                                // Legacy support
                                var useKey = keyMapping[(int)InputType.Use];
                                if (useKey != null && useKey.MouseButton != MouseButton.None)
                                {
                                    binding = new KeyOrMouse(useKey.MouseButton);
                                }
                            }
                            break;
                        default:
                            break;
                    }
                    if (binding == null)
                    {
                        DebugConsole.ThrowError("Key binding for the input type \"" + inputType + " not set!");
                        binding = new KeyOrMouse(Keys.D1);
                    }
                    keyMapping[(int)inputType] = binding;
                }
            }
        }

        private void LoadKeyBinds(XElement element, Version gameVersion)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                //backwards compatibility
                if (attribute.Name.ToString() == "TakeAllFromInventorySlot")
                {
                    keyMapping[(int)InputType.TakeHalfFromInventorySlot] = new KeyOrMouse(Keys.LeftShift);
                    keyMapping[(int)InputType.TakeOneFromInventorySlot] = new KeyOrMouse(Keys.LeftControl);
                }
                if (!Enum.TryParse(attribute.Name.ToString(), true, out InputType inputType)) { continue; }

                if (int.TryParse(attribute.Value.ToString(), out int mouseButtonInt))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse((MouseButton)mouseButtonInt);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out MouseButton mouseButton))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse(mouseButton);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out Keys key))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse(key);
                }
            }
            //v0.15 added creature attacks that can be used with a character capable of speaking (with mudraptor or spineling genes),
            //which causes the previous attack keybind R to conflict with the radio keybind
            // -> automatically change it to F
            if (gameVersion < new Version(0, 15, 0, 0))
            {
                keyMapping[(int)InputType.Attack] = new KeyOrMouse(Keys.F);
            }
        }

        private void LoadInventoryKeybinds(XElement element)
        {
            for (int i = 0; i < inventoryKeyMapping.Length; i++)
            {
                XAttribute attribute = element.Attributes().ElementAt(i);
                if (int.TryParse(attribute.Value.ToString(), out int mouseButtonInt))
                {
                    inventoryKeyMapping[i] = new KeyOrMouse((MouseButton)mouseButtonInt);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out MouseButton mouseButton))
                {
                    inventoryKeyMapping[i] = new KeyOrMouse(mouseButton);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out Keys key))
                {
                    inventoryKeyMapping[i] = new KeyOrMouse(key);
                }
            }
        }

        private void LoadControls(XDocument doc)
        {
            var gameVersion = new Version(doc.Root.GetAttributeString("gameversion", "0.0.0.0"));

            XElement keyMapping = doc.Root.Element("keymapping");
            if (keyMapping != null)
            {
                LoadKeyBinds(keyMapping, gameVersion);
            }

            XElement inventoryKeyMapping = doc.Root.Element("inventorykeymapping");
            if (inventoryKeyMapping != null)
            {
                LoadInventoryKeybinds(inventoryKeyMapping);
            }
            
            XElement debugConsoleMapping = doc.Root.Element("debugconsolemapping");

            if (debugConsoleMapping == null) { return; }
            
            ConsoleKeybinds.Clear();
            DebugConsole.Keybinds.Clear();

            foreach (XElement element in debugConsoleMapping.Elements())
            {
                string keyString = element.GetAttributeString("key", string.Empty);
                string command = element.GetAttributeString("command", string.Empty);
                
                if (string.IsNullOrWhiteSpace(keyString) || string.IsNullOrWhiteSpace(command)) { continue; }

                if (Enum.TryParse(typeof(Keys), keyString, ignoreCase: true, out object @out) && @out is Keys key)
                {
                    ConsoleKeybinds.TryAdd(key, command);
                }
            }

            DebugConsole.Keybinds = new Dictionary<Keys, string>(ConsoleKeybinds);
        }

        private void LoadSubEditorImages(XDocument doc)
        {
            XElement element = doc.Root?.Element("editorimages");
            if (element == null)
            {
                SubEditorScreen.ImageManager.Clear(alsoPending: true);
                return;
            }

            SubEditorScreen.ImageManager.Load(element);
        }

        public KeyOrMouse KeyBind(InputType inputType)
        {
            return keyMapping[(int)inputType];
        }

        public string KeyBindText(InputType inputType)
        {
            return keyMapping[(int)inputType].Name;
        }

        public KeyOrMouse InventoryKeyBind(int index)
        {
            return inventoryKeyMapping[index];
        }

        private GUIListBox contentPackageList;

        private bool ChangeSliderText(GUIScrollBar scrollBar, float scale)
        {
            UnsavedSettings = true;
            GUITextBlock text = scrollBar.UserData as GUITextBlock;
            //search for percentage value
            int index = text.Text.IndexOf("%");
            string label = text.Text;
            //if "%" is found
            if (index > 0)
            {
                while (index > 0)
                {
                    //search for end of label
                    index -= 1;
                    if (text.Text[index] == ' ')
                        break;
                }
                label = text.Text.Substring(0, index);
            }
            text.Text = label + " " + (int)Math.Round(scale * 100) + "%";
            return true;
        }

        public void ResetSettingsFrame()
        {
            if (GameMain.Client == null)
            {
                VoipCapture.Instance?.Dispose();
            }
            settingsFrame = null;
        }

        public void CreateSettingsFrame(Tab selectedTab = Tab.Graphics)
        {
            RectTransform settingsHolder = null;

            if (Screen.Selected == GameMain.MainMenuScreen)
            {
                settingsFrame = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.8f), GUI.Canvas, Anchor.Center));
                settingsHolder = settingsFrame.RectTransform;
            }
            else
            {
                settingsFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
                new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, settingsFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");
                var settingsFrameContent = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.8f), settingsFrame.RectTransform, Anchor.Center));
                settingsHolder = settingsFrameContent.RectTransform;
            }

            Vector2 textBlockScale = new Vector2(1.0f, 0.05f);
            Vector2 tickBoxScale = new Vector2(1.0f, 0.05f);

            var settingsFramePadding = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.93f), settingsHolder, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) }, style: null);
            var buttonArea = new GUIFrame(new RectTransform(new Vector2(0.6f, 0.06f), settingsFramePadding.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.07f, 0.0f) }, style: null)
            {
                IgnoreLayoutGroups = true
            };

            /// General tab --------------------------------------------------------------

            var leftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 0.93f), settingsFramePadding.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var settingsTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftPanel.RectTransform),
                TextManager.Get("Settings"), textAlignment: Alignment.TopLeft, font: GUI.LargeFont)
            { ForceUpperCase = true };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), settingsTitle.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest), style: "GUIBugButton")
            {
                ToolTip = TextManager.Get("bugreportbutton") + $" (v{GameMain.Version})",
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowBugReporter(); return true; }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftPanel.RectTransform), TextManager.Get("ContentPackages"), font: GUI.SubHeadingFont);

            var corePackageDropdown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftPanel.RectTransform))
            {
                ButtonEnabled = ContentPackage.CorePackages.Count > 1
            };

            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), leftPanel.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), textAlignment: Alignment.CenterLeft, font: GUI.Font);
            var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform, Anchor.CenterRight), font: GUI.Font, createClearButton: true);
            filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (textBox, text) => 
            {
                foreach (GUIComponent child in contentPackageList.Content.Children)
                {
                    if (!(child.UserData is ContentPackage cp)) { continue; }
                    child.Visible = string.IsNullOrEmpty(text) ? true : cp.Name.ToLower().Contains(text.ToLower());
                }
                return true; 
            };

            contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.70f), leftPanel.RectTransform))
            {
                OnSelected = (gc, obj) => false,
                ScrollBarVisible = true
            };

            foreach (ContentPackage contentPackage in ContentPackage.CorePackages)
            {
                var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), corePackageDropdown.ListBox.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = contentPackage
                };
                var text = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform), contentPackage.Name);

                if (!contentPackage.IsCompatible())
                {
                    frame.UserData = null;
                    text.TextColor = GUI.Style.Red * 0.6f;
                    frame.ToolTip = text.ToolTip =
                        TextManager.GetWithVariables(contentPackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                        new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { contentPackage.Name, contentPackage.GameVersion.ToString(), GameMain.Version.ToString() });
                }
                else if (!contentPackage.ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
                {
                    frame.UserData = null;
                    text.TextColor = GUI.Style.Red * 0.6f;
                    frame.ToolTip = text.ToolTip =
                        TextManager.GetWithVariables("ContentPackageMissingCoreFiles", new string[2] { "[packagename]", "[missingfiletypes]" },
                        new string[2] { contentPackage.Name, string.Join(", ", missingContentTypes) }, new bool[2] { false, true });
                }
                else if (contentPackage.HasErrors)
                {
                    text.TextColor = new Color(255, 150, 150);
                    frame.ToolTip = text.ToolTip =
                        TextManager.GetWithVariable("ContentPackageHasErrors", "[packagename]", contentPackage.Name) +
                        "\n" + string.Join("\n", contentPackage.ErrorMessages);
                }

                if (contentPackage == CurrentCorePackage)
                {
                    corePackageDropdown.Select(corePackageDropdown.ListBox.Content.GetChildIndex(frame));
                }
            }
            corePackageDropdown.OnSelected = SelectCorePackage;
            corePackageDropdown.ListBox.CanBeFocused = CanHotswapPackages(true);

            foreach (ContentPackage contentPackage in ContentPackage.RegularPackages)
            {
                var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, tickBoxScale.Y), contentPackageList.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = contentPackage
                };

                var frameContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };

                var dragIndicator = new GUIButton(new RectTransform(new Vector2(0.1f, 0.5f), frameContent.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIDragIndicator")
                {
                    CanBeFocused = false
                };
                var tickBox = new GUITickBox(new RectTransform(Vector2.One, frameContent.RectTransform), contentPackage.Name,
                    style: "GUITickBox")
                {
                    UserData = contentPackage,
                    Selected = EnabledRegularPackages.Contains(contentPackage),
                    OnSelected = SelectContentPackage,
                    Enabled = CanHotswapPackages(false)
                };
                frame.RectTransform.MinSize = new Point(0, (int)(tickBox.RectTransform.MinSize.Y / frameContent.RectTransform.RelativeSize.Y));
                if (!contentPackage.IsCompatible())
                {
                    tickBox.Enabled = false;
                    tickBox.TextColor = GUI.Style.Red * 0.6f;
                    tickBox.ToolTip = tickBox.TextBlock.ToolTip =
                        TextManager.GetWithVariables(contentPackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                        new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { contentPackage.Name, contentPackage.GameVersion.ToString(), GameMain.Version.ToString() });
                }
                else if (contentPackage.HasErrors)
                {
                    tickBox.TextColor = new Color(255,150,150);
                    tickBox.ToolTip = tickBox.TextBlock.ToolTip =
                        TextManager.GetWithVariable("ContentPackageHasErrors", "[packagename]", contentPackage.Name) +
                        "\n" + string.Join("\n", contentPackage.ErrorMessages);
                }
            }
            contentPackageList.CurrentDragMode = CanHotswapPackages(false) ? GUIListBox.DragMode.DragWithinBox : GUIListBox.DragMode.NoDragging;
            contentPackageList.CanBeFocused = CanHotswapPackages(false);
            contentPackageList.OnRearranged = OnContentPackagesRearranged;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.045f), leftPanel.RectTransform), TextManager.Get("Language"), font: GUI.SubHeadingFont);
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.045f), leftPanel.RectTransform));
            foreach (string language in TextManager.AvailableLanguages)
            {
                languageDD.AddItem(TextManager.GetTranslatedLanguageName(language), language);
            }
            languageDD.SelectItem(TextManager.Language);
            languageDD.OnSelected = (guiComponent, obj) =>
            {
                string newLanguage = obj as string;
                if (newLanguage == Language) { return true; }

                string prevLanguage = Language;
                Language = newLanguage;
                UnsavedSettings = true;

                var msgBox = new GUIMessageBox(
                    TextManager.Get("RestartRequiredLabel"),
                    TextManager.Get("RestartRequiredLanguage"),
                    buttons: new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") });
                msgBox.Buttons[0].OnClicked += (btn, userdata) =>
                {
                    ApplySettings();
                    GameMain.Instance.Exit();
                    return true;
                }; 
                msgBox.Buttons[1].OnClicked += (btn, userdata) =>
                {
                    Language = prevLanguage;
                    languageDD.SelectItem(Language);
                    msgBox.Close();
                    return true;
                };
                return true;
            };

#if !OSX
            var statisticsTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.045f), leftPanel.RectTransform), TextManager.Get("statisticsconsenttickbox"))
            {
                OnSelected = (GUITickBox tickBox) =>
                {
                    GameAnalyticsManager.SetConsent(
                        tickBox.Selected
                            ? GameAnalyticsManager.Consent.Ask
                            : GameAnalyticsManager.Consent.No);
                    return false;
                }
            };
#if DEBUG
            statisticsTickBox.Enabled = false;
#endif
            void updateGATickBoxToolTip()
                => statisticsTickBox.ToolTip = TextManager.Get($"GameAnalyticsStatus.{GameAnalyticsManager.UserConsented}");
            updateGATickBoxToolTip();
            
            var cachedConsent = GameAnalyticsManager.Consent.Unknown;
            var statisticsTickBoxUpdater = new GUICustomComponent(
                new RectTransform(Vector2.Zero, statisticsTickBox.RectTransform),
                onUpdate: (deltaTime, component) =>
            {
                bool shouldTickBoxBeSelected = GameAnalyticsManager.UserConsented == GameAnalyticsManager.Consent.Yes;
                
                bool shouldUpdateTickBoxState = cachedConsent != GameAnalyticsManager.UserConsented
                                                || statisticsTickBox.Selected != shouldTickBoxBeSelected;

                if (!shouldUpdateTickBoxState) { return; }

                updateGATickBoxToolTip();
                cachedConsent = GameAnalyticsManager.UserConsented;
                GUITickBox.OnSelectedHandler prevHandler = statisticsTickBox.OnSelected;
                statisticsTickBox.OnSelected = null;
                statisticsTickBox.Selected = shouldTickBoxBeSelected;
                statisticsTickBox.OnSelected = prevHandler;
                statisticsTickBox.Enabled = GameAnalyticsManager.UserConsented != GameAnalyticsManager.Consent.Error;
            });
#endif


            // right panel --------------------------------------

            var rightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.99f - leftPanel.RectTransform.RelativeSize.X, leftPanel.RectTransform.RelativeSize.Y),
                settingsFramePadding.RectTransform, Anchor.TopRight))
            {
                Stretch = true
            };

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightPanel.RectTransform, Anchor.TopCenter), isHorizontal: true);

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), rightPanel.RectTransform, Anchor.Center), style: null);

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabButtons = new GUIButton[tabs.Length];
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                tabs[(int)tab] = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), paddedFrame.RectTransform), style: "InnerFrame")
                {
                    UserData = tab
                };

                float tabWidth = 1.0f / tabs.Length;
#if DEBUG
                string buttonText = tab != Tab.Debug ? TextManager.Get("SettingsTab." + tab.ToString()) : "Debug";
#else
                string buttonText = TextManager.Get("SettingsTab." + tab.ToString());
#endif

                tabButtons[(int)tab] = new GUIButton(new RectTransform(new Vector2(tabWidth, 1.0f), tabButtonHolder.RectTransform), style: "GUITabButton")
                {
                    UserData = tab,
                    OnClicked = (bt, userdata) => { SelectTab((Tab)userdata); return true; }
                };
                tabButtons[(int)tab].Text = ToolBox.LimitString(buttonText, tabButtons[(int)tab].Font, (int)(0.75f * tabWidth * tabButtonHolder.Rect.Width));
                if (tabButtons[(int)tab].Text != buttonText)
                {
                    tabButtons[(int)tab].ToolTip = buttonText;
                }
            }

            /// Graphics tab --------------------------------------------------------------

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.TopLeft)
            { RelativeOffset = new Vector2(0.025f, 0.02f) })
            { RelativeSpacing = 0.01f };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.TopRight)
            { RelativeOffset = new Vector2(0.025f, 0.02f) })
            { RelativeSpacing = 0.01f };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Resolution"), font: GUI.SubHeadingFont);
            var resolutionDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform))
            {
                ButtonEnabled = GameMain.Config.WindowMode != WindowMode.BorderlessWindowed
            };

            var supportedDisplayModes = UpdateResolutionDD(resolutionDD);
            resolutionDD.OnSelected = SelectResolution;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("DisplayMode"), font: GUI.SubHeadingFont);
            var displayModeDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform));

            displayModeDD.AddItem(TextManager.Get("Fullscreen"), WindowMode.Fullscreen);
            displayModeDD.AddItem(TextManager.Get("Windowed"), WindowMode.Windowed);
#if (!OSX)
            displayModeDD.AddItem(TextManager.Get("BorderlessWindowed"), WindowMode.BorderlessWindowed);
            displayModeDD.SelectItem(GameMain.Config.WindowMode);
#else
            // Fullscreen option will just set itself to borderless on macOS.
            if (GameMain.Config.WindowMode == WindowMode.BorderlessWindowed)
            {
                displayModeDD.SelectItem(WindowMode.Fullscreen);
            }
            else
            {
                displayModeDD.SelectItem(GameMain.Config.WindowMode);
            }
#endif

            displayModeDD.OnSelected = (guiComponent, obj) =>
            {
                UnsavedSettings = true;
                GameMain.Config.WindowMode = (WindowMode)guiComponent.UserData;
                supportedDisplayModes = UpdateResolutionDD(resolutionDD);
                resolutionDD.ButtonEnabled = GameMain.Config.WindowMode != WindowMode.BorderlessWindowed;
                GameMain.Instance.ApplyGraphicsSettings();
                if (GameMain.Config.WindowMode == WindowMode.BorderlessWindowed)
                {
                    GraphicsWidth = GameMain.GraphicsWidth;
                    GraphicsHeight = GameMain.GraphicsHeight;
                    var displayMode = supportedDisplayModes.Find(m => m.Width == GameMain.GraphicsWidth && m.Height == GameMain.GraphicsHeight);
                    if (displayMode != null)
                    {
                        resolutionDD.SelectItem(displayMode);
                    }
                }
                return true;
            };

            GUITickBox vsyncTickBox = new GUITickBox(new RectTransform(tickBoxScale, leftColumn.RectTransform), TextManager.Get("EnableVSync"))
            {
                ToolTip = TextManager.Get("EnableVSyncToolTip"),
                Selected = VSyncEnabled
            };
            vsyncTickBox.OnSelected = (GUITickBox box) =>
            {
                VSyncEnabled = box.Selected;
                GameMain.GraphicsDeviceManager.SynchronizeWithVerticalRetrace = VSyncEnabled;
                GameMain.GraphicsDeviceManager.ApplyChanges();
                UnsavedSettings = true;

                return true;
            };


            GUITickBox textureCompressionTickBox = new GUITickBox(new RectTransform(tickBoxScale, leftColumn.RectTransform), TextManager.Get("EnableTextureCompression"))
            {
                ToolTip = TextManager.Get("EnableTextureCompressionToolTip"),
                OnSelected = (GUITickBox box) =>
                {
                    if (box.Selected == TextureCompressionEnabled) { return true; }
                    bool prevTextureCompressionEnabled = TextureCompressionEnabled;
                    TextureCompressionEnabled = box.Selected;

                    var msgBox = new GUIMessageBox(
                        TextManager.Get("RestartRequiredLabel"),
                        TextManager.Get("RestartRequiredGeneric"),
                        buttons: new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") });
                    msgBox.Buttons[0].OnClicked += (btn, userdata) =>
                    {
                        ApplySettings();
                        GameMain.Instance.Exit();
                        return true;
                    }; msgBox.Buttons[1].OnClicked += (btn, userdata) =>
                    {
                        TextureCompressionEnabled = prevTextureCompressionEnabled;
                        box.Selected = prevTextureCompressionEnabled;
                        msgBox.Close();
                        return true;
                    };

                    return true;
                },
                Selected = TextureCompressionEnabled
            };

            GUITextBlock particleLimitText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ParticleLimit"), font: GUI.SubHeadingFont, wrap: true);
            GUIScrollBar particleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), style: "GUISlider",
                barSize: 0.1f)
            {
                UserData = particleLimitText,
                BarScroll = (ParticleLimit - 200) / 1300.0f,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    ParticleLimit = 200 + (int)(scroll * 1300.0f);
                    return true;
                },
                Step = 0.1f
            };
            particleScrollBar.OnMoved(particleScrollBar, particleScrollBar.BarScroll);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LosEffect"), font: GUI.SubHeadingFont, wrap: true);
            var losModeDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform));
            losModeDD.AddItem(TextManager.Get("LosModeNone"), LosMode.None);
            losModeDD.AddItem(TextManager.Get("LosModeTransparent"), LosMode.Transparent);
            losModeDD.AddItem(TextManager.Get("LosModeOpaque"), LosMode.Opaque);
            losModeDD.SelectItem(GameMain.Config.LosMode);
            losModeDD.OnSelected = (guiComponent, obj) =>
            {
                UnsavedSettings = true;
                GameMain.Config.LosMode = (LosMode)guiComponent.UserData;
                //don't allow changing los mode when playing as a client
                if (GameMain.Client == null)
                {
                    GameMain.LightManager.LosMode = GameMain.Config.LosMode;
                }
                return true;
            };

            GUITextBlock LightText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LightMapScale"), font: GUI.SubHeadingFont, wrap: true)
            {
                ToolTip = TextManager.Get("LightMapScaleToolTip")
            };
            GUIScrollBar lightScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                style: "GUISlider", barSize: 0.1f)
            {
                UserData = LightText,
                ToolTip = TextManager.Get("LightMapScaleToolTip"),
                BarScroll = MathUtils.InverseLerp(0.2f, 1.0f, LightMapScale),
                OnMoved = (scrollBar, barScroll) =>
                {
                    ChangeSliderText(scrollBar, barScroll);
                    LightMapScale = MathHelper.Lerp(0.2f, 1.0f, barScroll);
                    UnsavedSettings = true;
                    return true;
                },
                Step = 0.25f
            };
            lightScrollBar.OnMoved(lightScrollBar, lightScrollBar.BarScroll);

            /*new GUITickBox(new RectTransform(tickBoxScale, rightColumn.RectTransform), TextManager.Get("SpecularLighting"))
            {
                ToolTip = TextManager.Get("SpecularLightingToolTip"),
                Selected = SpecularityEnabled,
                OnSelected = (tickBox) =>
                {
                    SpecularityEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };*/

            new GUITickBox(new RectTransform(tickBoxScale, rightColumn.RectTransform), TextManager.Get("RadialDistortion"))
            {
                ToolTip = TextManager.Get("RadialDistortionToolTip"),
                Selected = EnableRadialDistortion,
                OnSelected = (tickBox) =>
                {
                    EnableRadialDistortion = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            new GUITickBox(new RectTransform(tickBoxScale, rightColumn.RectTransform), TextManager.Get("ChromaticAberration"))
            {
                ToolTip = TextManager.Get("ChromaticAberrationToolTip"),
                Selected = ChromaticAberrationEnabled,
                OnSelected = (tickBox) =>
                {
                    ChromaticAberrationEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            /// Audio tab ----------------------------------------------------------------

            var audioContent = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 0.97f), tabs[(int)Tab.Audio].RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = false,
                RelativeSpacing = 0.01f
            };

#if (!OSX)
            AudioDeviceNames = Alc.GetStringList((IntPtr)null, Alc.AllDevicesSpecifier);
            if (string.IsNullOrEmpty(AudioOutputDevice))
            {
                AudioOutputDevice = Alc.GetString((IntPtr)null, Alc.DefaultDeviceSpecifier);
                if (AudioDeviceNames.Any() && !AudioDeviceNames.Any(n => n.Equals(AudioOutputDevice, StringComparison.OrdinalIgnoreCase)))
                {
                    AudioOutputDevice = AudioDeviceNames[0];
                }
            }

            var outputDeviceList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.15f), audioContent.RectTransform), TrimAudioDeviceName(AudioOutputDevice), AudioDeviceNames.Count);
            if (AudioDeviceNames?.Count > 0)
            {
                foreach (string name in AudioDeviceNames)
                {
                    outputDeviceList.AddItem(TrimAudioDeviceName(name), name);
                }
                outputDeviceList.OnSelected = (GUIComponent selected, object obj) =>
                {
                    string name = obj as string;
                    if (!GameMain.SoundManager.Disconnected && AudioOutputDevice == name) { return true; }

                    AudioOutputDevice = name;
                    GameMain.SoundManager.InitializeAlcDevice(AudioOutputDevice);

                    return true;
                };
            }
            else
            {
                outputDeviceList.AddItem(TextManager.Get("AudioNoDevices") ?? "N/A", null);
                outputDeviceList.ButtonTextColor = GUI.Style.Red;
                outputDeviceList.ButtonEnabled = false;
                outputDeviceList.Select(0);
            }
#endif

            GUITextBlock soundVolumeText = new GUITextBlock(new RectTransform(textBlockScale, audioContent.RectTransform), TextManager.Get("SoundVolume"), font: GUI.SubHeadingFont);
            GUIScrollBar soundScrollBar = new GUIScrollBar(new RectTransform(textBlockScale, audioContent.RectTransform), 
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = soundVolumeText,
                BarScroll = SoundVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    SoundVolume = scroll;
                    return true;
                }
            };
            soundScrollBar.OnMoved(soundScrollBar, soundScrollBar.BarScroll);

            GUITextBlock musicVolumeText = new GUITextBlock(new RectTransform(textBlockScale, audioContent.RectTransform), TextManager.Get("MusicVolume"), font: GUI.SubHeadingFont);
            GUIScrollBar musicScrollBar = new GUIScrollBar(new RectTransform(textBlockScale, audioContent.RectTransform),
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = musicVolumeText,
                BarScroll = MusicVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    MusicVolume = scroll;
                    return true;
                }
            };
            musicScrollBar.OnMoved(musicScrollBar, musicScrollBar.BarScroll);

            GUITextBlock voiceChatVolumeText = new GUITextBlock(new RectTransform(textBlockScale, audioContent.RectTransform), TextManager.Get("VoiceChatVolume"), font: GUI.SubHeadingFont);
            GUIScrollBar voiceChatScrollBar = new GUIScrollBar(new RectTransform(textBlockScale, audioContent.RectTransform), 
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = voiceChatVolumeText,
                Range = new Vector2(0.0f, 2.0f)
            };
            voiceChatScrollBar.BarScrollValue = VoiceChatVolume;
            voiceChatScrollBar.OnMoved = (scrollBar, scroll) =>
            {
                ChangeSliderText(scrollBar, scrollBar.BarScrollValue);
                VoiceChatVolume = scrollBar.BarScrollValue;
                return true;
            };
            voiceChatScrollBar.OnMoved(voiceChatScrollBar, voiceChatScrollBar.BarScroll);

            GUITickBox muteOnFocusLostBox = new GUITickBox(new RectTransform(tickBoxScale, audioContent.RectTransform), TextManager.Get("MuteOnFocusLost"))
            {
                Selected = MuteOnFocusLost,
                ToolTip = TextManager.Get("MuteOnFocusLostToolTip"),
                OnSelected = (tickBox) =>
                {
                    MuteOnFocusLost = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            GUITickBox dynamicRangeCompressionTickBox = new GUITickBox(new RectTransform(tickBoxScale, audioContent.RectTransform), TextManager.Get("DynamicRangeCompression"))
            {
                Selected = DynamicRangeCompressionEnabled,
                ToolTip = TextManager.Get("DynamicRangeCompressionToolTip"),
                OnSelected = (tickBox) =>
                {
                    DynamicRangeCompressionEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            GUITickBox voipAttenuationTickBox = new GUITickBox(new RectTransform(tickBoxScale, audioContent.RectTransform), TextManager.Get("VoipAttenuation"))
            {
                Selected = VoipAttenuationEnabled,
                ToolTip = TextManager.Get("VoipAttenuationToolTip"),
                OnSelected = (tickBox) =>
                {
                    VoipAttenuationEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            /// Voice chat tab ----------------------------------------------------------------

            var voiceChatContent = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 0.97f), tabs[(int)Tab.VoiceChat].RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = false,
                RelativeSpacing = 0.01f
            };

            //new GUITextBlock(new RectTransform(textBlockScale, voiceChatContent.RectTransform), TextManager.Get("VoiceChat"), font: GUI.SubHeadingFont);

            CaptureDeviceNames = Alc.GetStringList((IntPtr)null, Alc.CaptureDeviceSpecifier);
            foreach (string name in CaptureDeviceNames)
            {
                DebugConsole.NewMessage(name + " " + name.Length.ToString(), Color.Lime);
            }

            GUITickBox directionalVoiceChat = new GUITickBox(new RectTransform(tickBoxScale, voiceChatContent.RectTransform), TextManager.Get("DirectionalVoiceChat"))
            {
                Selected = UseDirectionalVoiceChat,
                ToolTip = TextManager.Get("DirectionalVoiceChatToolTip"),
                OnSelected = (tickBox) =>
                {
                    UseDirectionalVoiceChat = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice) || !(CaptureDeviceNames?.Contains(VoiceCaptureDevice) ?? false))
            {
                VoiceCaptureDevice = CaptureDeviceNames?.Count > 0 ? CaptureDeviceNames[0] : null;
            }
            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice))
            {
                VoiceSetting = VoiceMode.Disabled;
            }
#if (!OSX)
            var deviceList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.15f), voiceChatContent.RectTransform), TrimAudioDeviceName(VoiceCaptureDevice), CaptureDeviceNames.Count);
            if (CaptureDeviceNames?.Count > 0)
            {
                foreach (string name in CaptureDeviceNames)
                {
                    deviceList.AddItem(TrimAudioDeviceName(name), name);
                }
                deviceList.OnSelected = (GUIComponent selected, object obj) =>
                {
                    string name = obj as string;
                    if (!(VoipCapture.Instance?.Disconnected ?? true) && VoiceCaptureDevice == name) { return true; }

                    VoipCapture.ChangeCaptureDevice(name);
                    return true;
                };
            }
            else
            {
                deviceList.AddItem(TextManager.Get("VoipNoDevices") ?? "N/A", null);
                deviceList.ButtonTextColor = GUI.Style.Red;
                deviceList.ButtonEnabled = false;
                deviceList.Select(0);
            }

#else
            var defaultDeviceGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), voiceChatContent.RectTransform), true, Anchor.CenterLeft);
            var currentDeviceTextBlock = new GUITextBlock(new RectTransform(new Vector2(.7f, 0.75f), null), 
                TextManager.AddPunctuation(':', TextManager.Get("CurrentDevice"), TrimAudioDeviceName(VoiceCaptureDevice)), font: GUI.SubHeadingFont)
            {
                ToolTip = TextManager.Get("CurrentDeviceToolTip.OSX"),
                TextAlignment = Alignment.CenterLeft
            };

            string refreshText = ToolBox.WrapText(TextManager.Get("RefreshDefaultDevice"), defaultDeviceGroup.RectTransform.Rect.Width * 0.3f, GUI.Font);
            var currentDeviceButton = new GUIButton(new RectTransform(new Vector2(.3f, 0.75f), defaultDeviceGroup.RectTransform), refreshText)
            {
                ToolTip = TextManager.Get("RefreshDefaultDeviceToolTip"),
                OnClicked = (bt, userdata) =>
                {
                    CaptureDeviceNames = Alc.GetStringList((IntPtr)null, Alc.CaptureDeviceSpecifier);
                    if (CaptureDeviceNames?.Count > 0)
                    {
                        if (VoiceCaptureDevice == CaptureDeviceNames[0]) return true;

                        VoipCapture.ChangeCaptureDevice(CaptureDeviceNames[0]);
                        currentDeviceTextBlock.Text = TextManager.AddPunctuation(':', TextManager.Get("CurrentDevice"), TrimAudioDeviceName(VoiceCaptureDevice));
                        currentDeviceTextBlock.Flash(Color.Blue);
                    }
                    else
                    {
                        currentDeviceTextBlock.Text = TextManager.Get("VoipNoDevices") ?? "N/A";
                        currentDeviceTextBlock.Flash(GUI.Style.Red);
                    }

                    return true;
                }
            };
            currentDeviceButton.OnClicked(currentDeviceButton, null);

            currentDeviceTextBlock.RectTransform.Parent = defaultDeviceGroup.RectTransform;
#endif

            var voiceModeCount = Enum.GetNames(typeof(VoiceMode)).Length;
            var voiceModeDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.15f), voiceChatContent.RectTransform), elementCount: voiceModeCount);
            for (int i = 0; i < voiceModeCount; i++)
            {
                var voiceMode = "VoiceMode." + ((VoiceMode)i).ToString();
                voiceModeDropDown.AddItem(TextManager.Get(voiceMode), userData: i, toolTip: TextManager.Get(voiceMode + "ToolTip"));
            }

            var micVolumeText = new GUITextBlock(new RectTransform(textBlockScale, voiceChatContent.RectTransform), TextManager.Get("MicrophoneVolume"), font: GUI.SubHeadingFont);
            var micVolumeSlider = new GUIScrollBar(new RectTransform(textBlockScale, voiceChatContent.RectTransform),
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = micVolumeText,
                BarScroll = (float)Math.Sqrt(MathUtils.InverseLerp(0.2f, MaxMicrophoneVolume, MicrophoneVolume)),
                OnMoved = (scrollBar, scroll) =>
                {
                    MicrophoneVolume = MathHelper.Lerp(0.2f, MaxMicrophoneVolume, scroll * scroll);
                    MicrophoneVolume = (float)Math.Round(MicrophoneVolume, 1);
                    ChangeSliderText(scrollBar, MicrophoneVolume);
                    scrollBar.Step = 0.05f;
                    return true;
                },
                Step = 0.05f
            };
            micVolumeSlider.OnMoved(micVolumeSlider, micVolumeSlider.BarScroll);

            var extraVoiceSettingsContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), voiceChatContent.RectTransform, Anchor.BottomCenter), style: null);

            var voiceActivityGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), extraVoiceSettingsContainer.RectTransform))
            {
                Visible = VoiceSetting != VoiceMode.Disabled
            };
            GUITickBox localVoiceByDefault = new GUITickBox(
                new RectTransform(tickBoxScale, voiceActivityGroup.RectTransform), TextManager.Get("LocalVoiceByDefault"))
            {
                Visible = VoiceSetting == VoiceMode.Activity,
                Selected = UseLocalVoiceByDefault,
                ToolTip = TextManager.Get("LocalVoiceByDefaultTooltip"),
                OnSelected = (tickBox) =>
                {
                    UseLocalVoiceByDefault = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };
            GUITextBlock noiseGateText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), voiceActivityGroup.RectTransform), TextManager.Get("NoiseGateThreshold"), font: GUI.SubHeadingFont)
            {
                Visible = VoiceSetting == VoiceMode.Activity,
                TextGetter = () =>
                {
                    return TextManager.Get("NoiseGateThreshold") + " " + ((int)NoiseGateThreshold).ToString() + " dB";
                }
            };
            var dbMeter = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.5f), voiceActivityGroup.RectTransform), 0.0f, Color.Lime);
            dbMeter.ProgressGetter = () =>
            {
                if (VoipCapture.Instance == null) { return 0.0f; }

                if (VoiceSetting == VoiceMode.Activity)
                {
                    dbMeter.Color = VoipCapture.Instance.LastdB > NoiseGateThreshold ? GUI.Style.Green : GUI.Style.Orange; //TODO: i'm a filthy hack
                }
                else
                {
                    dbMeter.Color = Color.Lime;
                }
                
                float scrollVal = double.IsNegativeInfinity(VoipCapture.Instance.LastdB) ? 0.0f : ((float)VoipCapture.Instance.LastdB + 100.0f) / 100.0f;
                return scrollVal * scrollVal;
            };
            var noiseGateSlider = new GUIScrollBar(new RectTransform(Vector2.One, dbMeter.RectTransform, Anchor.Center), color: Color.White, 
                style: "GUISlider", barSize: 0.03f);
            noiseGateSlider.Frame.Visible = false;
            noiseGateSlider.Step = 0.01f;
            noiseGateSlider.Range = new Vector2(-100.0f, 0.0f);
            noiseGateSlider.BarScroll = MathUtils.InverseLerp(-100.0f, 0.0f, NoiseGateThreshold);
            noiseGateSlider.BarScroll *= noiseGateSlider.BarScroll;
            noiseGateSlider.Visible = VoiceSetting == VoiceMode.Activity;
            noiseGateSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                NoiseGateThreshold = MathHelper.Lerp(-100.0f, 0.0f, (float)Math.Sqrt(scrollBar.BarScroll));
                UnsavedSettings = true;
                return true;
            };

            var voiceInputContainerHorizontal = new GUILayoutGroup(
                new RectTransform(new Vector2(1.0f, 0.5f), extraVoiceSettingsContainer.RectTransform)
                {
                    RelativeOffset = new Vector2(0.0f, voiceActivityGroup.RectTransform.RelativeSize.Y + 0.1f)
                },
                isHorizontal: true)
            {
                Visible = VoiceSetting == VoiceMode.PushToTalk
            };

            var voiceInputContainer = new GUILayoutGroup(
                new RectTransform(new Vector2(0.5f, 1.0f), voiceInputContainerHorizontal.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft);

            var voiceKeybindLabel = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), voiceInputContainer.RectTransform), TextManager.Get("InputType.Voice"), font: GUI.SubHeadingFont);
            var voiceKeyBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1.0f), voiceInputContainer.RectTransform, Anchor.TopRight), text: KeyBindText(InputType.Voice))
            {
                SelectedColor = Color.Gold * 0.3f,
                UserData = InputType.Voice
            };
            voiceKeyBox.OnSelected += KeyBoxSelected;

            var localVoiceInputContainer = new GUILayoutGroup(
                new RectTransform(new Vector2(0.5f, 1.0f), voiceInputContainerHorizontal.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft);

            var localVoiceKeybindLabel = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), localVoiceInputContainer.RectTransform), TextManager.Get("InputType.LocalVoice"), font: GUI.SubHeadingFont);
            var localVoiceKeyBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1.0f), localVoiceInputContainer.RectTransform, Anchor.TopRight), text: KeyBindText(InputType.LocalVoice))
            {
                SelectedColor = Color.Gold * 0.3f,
                UserData = InputType.LocalVoice
            };
            localVoiceKeyBox.OnSelected += KeyBoxSelected;

            voiceKeybindLabel.RectTransform.SizeChanged += () => { GUITextBlock.AutoScaleAndNormalize(voiceKeybindLabel, localVoiceKeybindLabel); };

            var cutoffPreventionText = new GUITextBlock(new RectTransform(textBlockScale, voiceChatContent.RectTransform), TextManager.Get("CutoffPrevention"), font: GUI.SubHeadingFont)
            {
                ToolTip = TextManager.Get("CutoffPreventionTooltip")
            };
            var cutoffPreventionSlider = new GUIScrollBar(new RectTransform(textBlockScale, voiceChatContent.RectTransform),
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = micVolumeText,
                Range = new Vector2(0, ((float)VoipConfig.BUFFER_SIZE / (float)VoipConfig.FREQUENCY) * 1000.0f * 25.0f),
                Step = 1.0f / 25.0f
            };
            cutoffPreventionSlider.BarScrollValue = VoiceChatCutoffPrevention;
            cutoffPreventionSlider.OnMoved = (scrollBar, scroll) =>
            {
                int bufferMsLength = (int)(((float)VoipConfig.BUFFER_SIZE / (float)VoipConfig.FREQUENCY) * 1000.0f);
                VoiceChatCutoffPrevention = (int)Math.Round(scrollBar.BarScrollValue / bufferMsLength) * bufferMsLength;
                cutoffPreventionText.Text = TextManager.Get("CutoffPrevention") +
                    " " + TextManager.GetWithVariable("timeformatmilliseconds", "[milliseconds]", VoiceChatCutoffPrevention.ToString());
                return true;
            };
            cutoffPreventionSlider.OnMoved(cutoffPreventionSlider, cutoffPreventionSlider.BarScrollValue);

            voiceModeDropDown.OnSelected = (GUIComponent selected, object userData) =>
            {
                try
                {
                    VoiceMode vMode = (VoiceMode)userData;
                    if (vMode == VoiceSetting) { return true; }
                    VoiceSetting = vMode;
                    if (vMode != VoiceMode.Disabled)
                    {
                        if (GameMain.Client == null && VoipCapture.Instance == null)
                        {
                            VoipCapture.Create(GameMain.Config.VoiceCaptureDevice);
                            if (VoipCapture.Instance == null)
                            {
                                VoiceSetting = vMode = VoiceMode.Disabled;
                                voiceActivityGroup.Visible = false;
                                voiceInputContainerHorizontal.Visible = false;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if (GameMain.Client == null)
                        {
                            VoipCapture.Instance?.Dispose();
                        }
                    }

                    noiseGateText.Visible = (vMode == VoiceMode.Activity);
                    noiseGateSlider.Visible = (vMode == VoiceMode.Activity);
                    localVoiceByDefault.Visible = (vMode == VoiceMode.Activity);
                    voiceActivityGroup.Visible = (vMode != VoiceMode.Disabled);
                    voiceInputContainerHorizontal.Visible = (vMode == VoiceMode.PushToTalk);
                    UnsavedSettings = true;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set voice capture mode.", e);
                    GameAnalyticsManager.AddErrorEventOnce("SetVoiceCaptureMode", GameAnalyticsManager.ErrorSeverity.Error, "Failed to set voice capture mode. " + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
                    VoiceSetting = VoiceMode.Disabled;
                }

                return true;
            };

            voiceModeDropDown.Select((int)VoiceSetting);
            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice))
            {
                voiceModeDropDown.ButtonEnabled = false;
                voiceModeDropDown.Color *= 0.5f;
                voiceModeDropDown.ButtonTextColor *= 0.5f;
            }

            /// Controls tab -------------------------------------------------------------
            var controlsLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabs[(int)Tab.Controls].RectTransform, Anchor.TopCenter)
            { RelativeOffset = new Vector2(0.0f, 0.02f) })
            { RelativeSpacing = 0.01f };

            GUITextBlock aimAssistText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform), TextManager.Get("AimAssist"), font: GUI.SubHeadingFont)
            {
                ToolTip = TextManager.Get("AimAssistToolTip")
            };
            GUIScrollBar aimAssistSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform), 
                style: "GUISlider", barSize: 0.05f)
            {
                UserData = aimAssistText,
                BarScroll = MathUtils.InverseLerp(0.0f, 5.0f, AimAssistAmount),
                ToolTip = TextManager.Get("AimAssistToolTip"),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    AimAssistAmount = MathHelper.Lerp(0.0f, 5.0f, scroll);
                    return true;
                },
                Step = 0.01f
            };
            aimAssistSlider.OnMoved(aimAssistSlider, aimAssistSlider.BarScroll);

            new GUITickBox(new RectTransform(tickBoxScale, controlsLayoutGroup.RectTransform), TextManager.Get("EnableMouseLook"))
            {
                ToolTip = TextManager.Get("EnableMouseLookToolTip"),
                Selected = EnableMouseLook,
                OnSelected = (tickBox) =>
                {
                    EnableMouseLook = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            var controlListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.75f), controlsLayoutGroup.RectTransform));

            var inputFrame = new GUILayoutGroup(new RectTransform(Vector2.One, controlListBox.Content.RectTransform), isHorizontal: true)
                { Stretch = true, RelativeSpacing = 0.01f };

            var inputColumnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), inputFrame.RectTransform))
                { Stretch = true, RelativeSpacing = 0.005f };
            var inputColumnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), inputFrame.RectTransform))
                { Stretch = true, RelativeSpacing = 0.005f };

            var inputNames = Enum.GetValues(typeof(InputType));
            var inputNameBlocks = new List<GUITextBlock>();
            for (int i = 0; i < inputNames.Length; i++)
            {
                var inputContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.06f),(i <= (inputNames.Length / 2) ? inputColumnLeft : inputColumnRight).RectTransform))
                    { Stretch = true, IsHorizontal = true, RelativeSpacing = 0.01f, Color = new Color(12, 14, 15, 215) };
                var inputName = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), inputContainer.RectTransform, Anchor.TopLeft) { MinSize = new Point(100, 0) },
                    TextManager.Get("InputType." + ((InputType)i)), font: GUI.SmallFont) { ForceUpperCase = true };
                inputNameBlocks.Add(inputName);
                string keyText = KeyBindText((InputType)i);
                var keyBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 1.0f), inputContainer.RectTransform),
                    text: keyText, font: GUI.SmallFont, style: "GUITextBoxNoIcon")
                {
                    UserData = i
                };
                keyBox.RectTransform.SizeChanged += () =>
                {
                    keyBox.Text = ToolBox.LimitString(keyText, keyBox.Font, (int)(keyBox.Rect.Width - keyBox.Padding.X - keyBox.Padding.Z));
                };
                inputContainer.RectTransform.MinSize = keyBox.RectTransform.MinSize;
                keyBox.OnSelected += KeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;
            }

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), inputColumnRight.RectTransform, minSize: inputColumnRight.Children.First().RectTransform.MinSize), style: null);

            for (int i = 0; i < inventoryHotkeyCount; i++)
            {
                var inputContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.06f), ((i + 1) <= inventoryHotkeyCount / 2 ? inputColumnLeft : inputColumnRight).RectTransform))
                { Stretch = true, IsHorizontal = true, RelativeSpacing = 0.01f, Color = new Color(12, 14, 15, 215), CanBeFocused = true };
                var inputName = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), inputContainer.RectTransform, Anchor.TopLeft) { MinSize = new Point(100, 0) },
                    TextManager.GetWithVariable("inventoryslotkeybind", "[slotnumber]", (i + 1).ToString()), font: GUI.SmallFont)
                { ForceUpperCase = true };
                inputNameBlocks.Add(inputName);
                var keyBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 1.0f), inputContainer.RectTransform),
                    text: inventoryKeyMapping[i].Name, font: GUI.SmallFont, style: "GUITextBoxNoIcon")
                {
                    UserData = i
                };
                keyBox.Text = ToolBox.LimitString(keyBox.Text, keyBox.Font, (int)(keyBox.Rect.Width - keyBox.Padding.X - keyBox.Padding.Z));
                inputContainer.RectTransform.MinSize = keyBox.RectTransform.MinSize;
                keyBox.OnSelected += InventoryKeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;
            }

            inputNameBlocks.First().RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(inputNameBlocks);
            };

            inputFrame.RectTransform.MinSize = new Point(0, 
                (int)Math.Max(
                    inputColumnLeft.Children.Sum(c => c.Rect.Height * (1.0f + inputColumnLeft.RelativeSpacing)), 
                    inputColumnRight.Children.Sum(c => c.Rect.Height * (1.0f + inputColumnLeft.RelativeSpacing))));

            var resetControlsArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.07f), controlsLayoutGroup.RectTransform), style: null);
            var resetControlsHolder = new GUILayoutGroup(new RectTransform(new Vector2(buttonArea.RectTransform.RelativeSize.X / controlsLayoutGroup.RectTransform.RelativeSize.X / rightPanel.RectTransform.RelativeSize.X, 1.0f), resetControlsArea.RectTransform, Anchor.Center), 
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            }; resetControlsHolder.CanBeFocused = true;

            var defaultBindingsButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), resetControlsHolder.RectTransform), TextManager.Get("SetDefaultBindings"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("SetDefaultBindingsToolTip"),
                OnClicked = (button, data) =>
                {
                    ResetControls(legacy: false);
                    return true;
                }
            };

            var legacyBindingsButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), resetControlsHolder.RectTransform), TextManager.Get("SetLegacyBindings"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("SetLegacyBindingsToolTip"),
                OnClicked = (button, data) =>
                {
                    ResetControls(legacy: true);
                    return true;
                }
            };

            legacyBindingsButton.TextBlock.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(defaultBindingsButton.TextBlock, legacyBindingsButton.TextBlock);
            };

            /// Gameplay tab -------------------------------------------------------------
            var gameplaySettingsGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Gameplay].RectTransform, Anchor.TopLeft)
            { RelativeOffset = new Vector2(0.025f, 0.02f) })
            { RelativeSpacing = 0.01f };

            GUITickBox pauseOnFocusLostBox = new GUITickBox(new RectTransform(tickBoxScale, gameplaySettingsGroup.RectTransform),
                TextManager.Get("PauseOnFocusLost"))
            {
                Selected = PauseOnFocusLost,
                ToolTip = TextManager.Get("PauseOnFocusLostToolTip"),
                OnSelected = (tickBox) =>
                {
                    PauseOnFocusLost = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            new GUITickBox(new RectTransform(tickBoxScale, gameplaySettingsGroup.RectTransform), TextManager.Get("DisableInGameHints"))
            {
                Selected = DisableInGameHints,
                ToolTip = TextManager.Get("DisableInGameHintsToolTip"),
                OnSelected = (tickBox) =>
                {
                    DisableInGameHints = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform),
                text: TextManager.Get("ResetInGameHints"),
                style: "GUIButtonSmall")
            {
                OnClicked = (button, userData) =>
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("ResetInGameHints"),
                        TextManager.Get("ResetInGameHintsTooltip"),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                    {
                        UserData = "verificationprompt"
                    };
                    msgBox.Buttons[0].OnClicked = (button, userData) =>
                    {
                        GameMain.Config.IgnoredHints.Clear();
                        return true;
                    };
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                    msgBox.Buttons[1].OnClicked = msgBox.Close;
                    return false;
                }
            };

            GUITextBlock HUDScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform), TextManager.Get("HUDScale"), font: GUI.SubHeadingFont, wrap: true);
            GUIScrollBar HUDScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform),
               style: "GUISlider", barSize: 0.1f)
            {
                UserData = HUDScaleText,
                BarScroll = (HUDScale - MinHUDScale) / (MaxHUDScale - MinHUDScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    HUDScale = MathHelper.Lerp(MinHUDScale, MaxHUDScale, scroll);
                    ChangeSliderText(scrollBar, HUDScale);
                    OnHUDScaleChanged?.Invoke();
                    return true;
                },
                Step = 0.02f
            };
            HUDScaleScrollBar.OnMoved(HUDScaleScrollBar, HUDScaleScrollBar.BarScroll);

            GUITextBlock inventoryScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform), TextManager.Get("InventoryScale"), font: GUI.SubHeadingFont);
            GUIScrollBar inventoryScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform),
                style: "GUISlider", barSize: 0.1f)
            {
                UserData = inventoryScaleText,
                BarScroll = (InventoryScale - MinInventoryScale) / (MaxInventoryScale - MinInventoryScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    InventoryScale = MathHelper.Lerp(MinInventoryScale, MaxInventoryScale, scroll);
                    ChangeSliderText(scrollBar, InventoryScale);
                    return true;
                },
                Step = 0.02f
            };
            inventoryScaleScrollBar.OnMoved(inventoryScaleScrollBar, inventoryScaleScrollBar.BarScroll);

            GUITextBlock textScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform), TextManager.Get("TextScale"), font: GUI.SubHeadingFont);
            GUIScrollBar textScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsGroup.RectTransform),
                style: "GUISlider", barSize: 0.1f)
            {
                UserData = textScaleText,
                BarScroll = (TextScale - MinTextScale) / (MaxTextScale - MinTextScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    TextScale = MathHelper.Lerp(MinTextScale, MaxTextScale, scroll);
                    textScaleDirty = true;
                    ChangeSliderText(scrollBar, TextScale);
                    return true;
                },
                Step = 0.01f
            };
            textScaleScrollBar.OnMoved(textScaleScrollBar, textScaleScrollBar.BarScroll);
            textScaleDirty = false;

            /// Bottom buttons -------------------------------------------------------------
            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (x, y) =>
                {
                    static void ExitSettings()
                    {
                        if (Screen.Selected == GameMain.MainMenuScreen) { GameMain.MainMenuScreen.ReturnToMainMenu(null, null); }
                        GUI.SettingsMenuOpen = false;
                    }

                    if (UnsavedSettings)
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("UnsavedChangesLabel"),
                                TextManager.Get("UnsavedChangesVerification"),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                        {
                            UserData = "verificationprompt"
                        };
                        msgBox.Buttons[0].OnClicked = (applyButton, obj) =>
                        {
                            LoadPlayerConfig();
                            ExitSettings();
                            return true;
                        };
                        msgBox.Buttons[0].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                        return false;
                    }

                    ExitSettings();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomCenter),
                TextManager.Get("Reset"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (button, data) =>
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("SettingResetLabel"),
                                TextManager.Get("SettingResetVerification"),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                    {
                        UserData = "verificationprompt"
                    };
                    msgBox.Buttons[0].OnClicked = (yesButton, obj) =>
                    {
                        LoadDefaultConfig(setLanguage: false, loadContentPackages: Screen.Selected != GameMain.GameScreen);
                        CheckBindings(true);
                        RefreshItemMessages();
                        ApplySettings();
                        if (Screen.Selected == GameMain.MainMenuScreen)
                        {
                            GameMain.MainMenuScreen.ResetSettingsFrame(currentTab);
                        }
                        else
                        {
                            ResetSettingsFrame();
                            CreateSettingsFrame(currentTab);
                        }
                        return true;
                    };
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                    msgBox.Buttons[1].OnClicked = msgBox.Close;
                    return false;
                }
            };

            applyButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomRight),
                TextManager.Get("ApplySettingsButton"))
            {
                IgnoreLayoutGroups = true,
                Enabled = false
            };
            applyButton.OnClicked = ApplyClicked;

#if DEBUG
            /// Debug tab ----------------------------------------------------------------
            var debugTickBoxes = new GUILayoutGroup(new RectTransform(new Vector2(0.28f, 0.15f), tabs[(int)Tab.Debug].RectTransform, Anchor.TopLeft)
            { RelativeOffset = new Vector2(0.02f, 0.02f) })
            { RelativeSpacing = 0.01f };

            void addDebugTickBox(bool initialValue, Action<bool> set, string label, string tooltip)
            {
                var tickBox = new GUITickBox(new RectTransform(tickBoxScale / 0.18f, debugTickBoxes.RectTransform, scaleBasis: ScaleBasis.BothHeight), label, style: "GUITickBox");
                tickBox.Selected = initialValue;
                tickBox.ToolTip = tooltip;
                tickBox.OnSelected = (tickBox) =>
                {
                    set(tickBox.Selected);
                    UnsavedSettings = true;
                    return true;
                };
            }

            addDebugTickBox(
                AutomaticQuickStartEnabled,
                (b) => AutomaticQuickStartEnabled = b,
                "Automatic quickstart enabled",
                "Will the game automatically move on to Quickstart when the game is launched");

            addDebugTickBox(
                TestScreenEnabled,
                (b) => TestScreenEnabled = b,
                "Test screen enabled",
                "Will the game automatically move on to a test screen when the game is launched");

            addDebugTickBox(
                AutomaticCampaignLoadEnabled,
                (b) => AutomaticCampaignLoadEnabled = b,
                "Automatic campaign load enabled",
                "Will the game automatically load the latest campaign save when the game is launched");

            addDebugTickBox(
                EnableSplashScreen,
                (b) => EnableSplashScreen = b,
                "Splash screen enabled",
                "Are the splash screens shown when the game is launched");

            addDebugTickBox(
                VerboseLogging,
                (b) => VerboseLogging = b,
                "Verbose logging enabled",
                "Should verbose logging be used");

            addDebugTickBox(
                TextManagerDebugModeEnabled,
                (b) => TextManagerDebugModeEnabled = b,
                "TextManager debug mode enabled",
                "Does the TextManager return the text tags for debug purposes?");

            addDebugTickBox(
                ModBreakerMode,
                (b) => ModBreakerMode = b,
                "Mod breaker mode enabled",
                "Do horrible things when loading mods to see if it breaks?");
#endif

            UnsavedSettings = false; // Reset unsaved settings to false once the UI has been created
            SelectTab(selectedTab);
        }

        private List<DisplayMode> UpdateResolutionDD(GUIDropDown resolutionDD)
        {
            var supportedDisplayModes = new List<DisplayMode>();
            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
            {
                if (supportedDisplayModes.Any(m => m.Width == mode.Width && m.Height == mode.Height)) { continue; }
#if OSX
                // Monogame currently doesn't support retina displays
                // so we need to disable resolutions above the viewport size.

                // In a bundled .app you just disable HiDPI in the info.plist
                // but that's probably not gonna happen.
                if (mode.Width > GameMain.Instance.GraphicsDevice.DisplayMode.Width || mode.Height > GameMain.Instance.GraphicsDevice.DisplayMode.Height) { continue; }
#endif
                supportedDisplayModes.Add(mode);
            }
            supportedDisplayModes.Sort((a, b) =>
            {
                if (a.Width < b.Width)
                {
                    return -1;
                }
                if (a.Width > b.Width)
                {
                    return 1;
                }
                if (a.Height < b.Height)
                {
                    return -1;
                }
                if (a.Height > b.Height)
                {
                    return 1;
                }
                return 0;
            });

            resolutionDD.ClearChildren();

            foreach (DisplayMode mode in supportedDisplayModes)
            {
                if (mode.Width < MinSupportedResolution.X || mode.Height < MinSupportedResolution.Y) { continue; }
                resolutionDD.AddItem(mode.Width + "x" + mode.Height, mode);
                if (GraphicsWidth == mode.Width && GraphicsHeight == mode.Height) resolutionDD.SelectItem(mode);
            }

            if (resolutionDD.SelectedItemData == null)
            {
                resolutionDD.SelectItem(GraphicsAdapter.DefaultAdapter.SupportedDisplayModes.Last());
            }

            resolutionDD.ListBox.RectTransform.Resize(new Point(resolutionDD.Rect.Width, resolutionDD.Rect.Height * MathHelper.Clamp(supportedDisplayModes.Count, 2, 10)));

            return supportedDisplayModes;
        }

        private string TrimAudioDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return string.Empty; }
            string[] prefixes = { "OpenAL Soft on " };
            foreach (string prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Remove(0, prefix.Length);
                }
            }
            return name;
        }
        
        private Tab currentTab;
        private void SelectTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.VoiceChat:
                    if (VoiceSetting != VoiceMode.Disabled)
                    {
                        if (GameMain.Client == null && VoipCapture.Instance == null)
                        {
                            VoipCapture.Create(GameMain.Config.VoiceCaptureDevice);
                        }
                    }
                    break;
                default:
                    if (GameMain.Client == null)
                    {
                        VoipCapture.Instance?.Dispose();
                    }
                    break;
            }
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = (Tab)tabs[i].UserData == tab;
                tabButtons[i].Selected = tabs[i].Visible;
            }
            currentTab = tab;
        }

        private void KeyBoxSelected(GUITextBox textBox, Keys key)
        {
            textBox.Text = "";
            CoroutineManager.StartCoroutine(WaitForKeyPress(textBox, keyMapping));
        }

        private void InventoryKeyBoxSelected(GUITextBox textBox, Keys key)
        {
            textBox.Text = "";
            CoroutineManager.StartCoroutine(WaitForKeyPress(textBox, inventoryKeyMapping));
        }

        private void ResetControls(bool legacy)
        {
            // TODO: add a prompt?
            SetDefaultBindings(legacy: legacy);
            CheckBindings(true);
            RefreshItemMessages();
            ApplySettings();
            if (Screen.Selected == GameMain.MainMenuScreen)
            {
                GameMain.MainMenuScreen.ResetSettingsFrame(Tab.Controls);
            }
            else
            {
                ResetSettingsFrame();
                CreateSettingsFrame(Tab.Controls);
            }
        }

        private bool SelectResolution(GUIComponent selected, object userData)
        {
            DisplayMode mode = selected.UserData as DisplayMode;
            if (mode == null) return false;

            if (GraphicsWidth == mode.Width && GraphicsHeight == mode.Height) return false;

            GraphicsWidth = mode.Width;
            GraphicsHeight = mode.Height;
            GameMain.Instance.ApplyGraphicsSettings();
            UnsavedSettings = true;

            return true;
        }

        private bool CanHotswapPackages(bool core)
        {
            return GameMain.Client == null &&
                   (ContentPackage.IngameModSwap ||
                   Screen.Selected != GameMain.GameScreen &&
                    Screen.Selected != GameMain.SubEditorScreen) &&
                   (!core ||
                   (Screen.Selected != GameMain.CharacterEditorScreen &&
                    Screen.Selected != GameMain.ParticleEditorScreen));
        }

        private bool SelectCorePackage(GUIComponent component, object userData)
        {
            if (!(userData is ContentPackage contentPackage) || GameMain.Client != null) { return false; }

            SelectCorePackage(contentPackage);

            UnsavedSettings = true;
            return true;
        }

        private void OnContentPackagesRearranged(GUIListBox listBox, object userData)
        {
            if (GameMain.Client != null) { return; }

            if (userData is ContentPackage contentPackage)
            {
                if (!EnabledRegularPackages.Contains(contentPackage)) { return; }
            }

            ContentPackage.SortContentPackages(cp => listBox.Content.GetChildIndex(listBox.Content.GetChildByUserData(cp)), true, this);

            UnsavedSettings = true;
        }

        private bool SelectContentPackage(GUITickBox tickBox)
        {
            if (GameMain.Client != null) { return false; }

            var contentPackage = tickBox.UserData as ContentPackage;

            if (tickBox.Selected)
            {
                EnableRegularPackage(contentPackage);
            }
            else
            {
                DisableRegularPackage(contentPackage);
            }

            ContentPackage.SortContentPackages(cp => contentPackageList.Content.GetChildIndex(contentPackageList.Content.GetChildByUserData(cp)), false, this);

            UnsavedSettings = true;
            return true;
        }

        private IEnumerable<CoroutineStatus> WaitForKeyPress(GUITextBox keyBox, KeyOrMouse[] keyArray)
        {
            yield return CoroutineStatus.Running;

            while (PlayerInput.PrimaryMouseButtonHeld() || PlayerInput.PrimaryMouseButtonClicked())
            {
                //wait for the mouse to be released, so that we don't interpret clicking on the textbox as the keybinding
                yield return CoroutineStatus.Running;
            }
            while (keyBox.Selected && PlayerInput.GetKeyboardState.GetPressedKeys().Length == 0 &&
                !PlayerInput.LeftButtonClicked() && !PlayerInput.RightButtonClicked() && !PlayerInput.MidButtonClicked() &&
                !PlayerInput.Mouse4ButtonClicked() && !PlayerInput.Mouse5ButtonClicked() && !PlayerInput.MouseWheelUpClicked() && !PlayerInput.MouseWheelDownClicked())
            {
                if (Screen.Selected != GameMain.MainMenuScreen && !GUI.SettingsMenuOpen) yield return CoroutineStatus.Success;

                yield return CoroutineStatus.Running;
            }

            UnsavedSettings = true;

            int keyIndex = (int)keyBox.UserData;

            if (PlayerInput.LeftButtonClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.LeftMouse);
            }
            else if (PlayerInput.RightButtonClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.RightMouse);
            }
            else if (PlayerInput.MidButtonClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.MiddleMouse);
            }
            else if (PlayerInput.Mouse4ButtonClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.MouseButton4);
            }
            else if (PlayerInput.Mouse5ButtonClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.MouseButton5);
            }
            else if (PlayerInput.MouseWheelUpClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.MouseWheelUp);
            }
            else if (PlayerInput.MouseWheelDownClicked())
            {
                keyArray[keyIndex] = new KeyOrMouse(MouseButton.MouseWheelDown);
            }
            else if (PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0)
            {
                Keys key = PlayerInput.GetKeyboardState.GetPressedKeys()[0];
                keyArray[keyIndex] = new KeyOrMouse(key);
            }
            else
            {
                yield return CoroutineStatus.Success;
            }

            keyBox.Text = keyArray[keyIndex].Name;
            keyBox.Text = ToolBox.LimitString(keyBox.Text, keyBox.Font, keyBox.Rect.Width);

            keyBox.Deselect();
            RefreshItemMessages();

            yield return CoroutineStatus.Success;
        }

        private void RefreshItemMessages()
        {
            foreach (Item item in Item.ItemList)
            {
                foreach (Items.Components.ItemComponent ic in item.Components)
                {
                    ic.ParseMsg();
                }
            }
            CharacterHUD.ShouldRecreateHudTexts = true;
        }

        private void ApplySettings()
        {
            SaveNewPlayerConfig();

            SettingsFrame.Flash(GUI.Style.Green);

            if (textScaleDirty || GameMain.WindowMode != GameMain.Config.WindowMode || GameMain.Config.GraphicsWidth != GameMain.GraphicsWidth || GameMain.Config.GraphicsHeight != GameMain.GraphicsHeight)
            {
                GameMain.Instance.ApplyGraphicsSettings();
                textScaleDirty = false;
            }
        }

        private bool ApplyClicked(GUIButton button, object userData)
        {
            ApplySettings();
            if (Screen.Selected != GameMain.MainMenuScreen) { GUI.SettingsMenuOpen = false; }
            WarnIfContentPackageSelectionDirty();
            return true;
        }

        public void WarnIfContentPackageSelectionDirty()
        {
            if (ContentPackageSelectionDirtyNotification)
            {
                new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredContentPackage", fallBackTag: "RestartRequiredGeneric"));
                ContentPackageSelectionDirtyNotification = false;
            }
        }
    }
}

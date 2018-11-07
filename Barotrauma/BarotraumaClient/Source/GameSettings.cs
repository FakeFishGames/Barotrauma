using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public partial class GameSettings
    {
        private enum Tab
        {
            General,
            Graphics,
        }
        
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


        public KeyOrMouse KeyBind(InputType inputType)
        {
            return keyMapping[(int)inputType];
        }

        private bool ChangeHUDScale(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            HUDScale = MathHelper.Lerp(MinHUDScale, MaxHUDScale, barScroll);
            OnHUDScaleChanged?.Invoke();
            return true;
        }

        private bool ChangeInventoryScale(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            InventoryScale = MathHelper.Lerp(MinInventoryScale, MaxInventoryScale, barScroll);
            return true;
        }

        private bool ChangeParticleLimit(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            ParticleLimit = 200 + (int)(barScroll * 1300.0f);

            return true;
        }

        private bool ChangeSoundVolume(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            SoundVolume = barScroll;

            return true;
        }

        private bool ChangeMusicVolume(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            MusicVolume = barScroll;

            return true;
        }

        public void ResetSettingsFrame()
        {
            settingsFrame = null;
        }

        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new RectTransform(new Point(500, 500), GUI.Canvas, Anchor.Center));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), settingsFrame.RectTransform),
                TextManager.Get("Settings"), textAlignment: Alignment.Center, font: GUI.LargeFont);
            
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.8f), settingsFrame.RectTransform, Anchor.Center)
                { RelativeOffset = new Vector2(0.0f, 0.06f) }, style: null);

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.05f), settingsFrame.RectTransform, Anchor.TopCenter)
                { RelativeOffset = new Vector2(0.0f, 0.1f) }, isHorizontal: true);

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabButtons = new GUIButton[tabs.Length];
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                tabs[(int)tab] = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.91f), paddedFrame.RectTransform), style: "InnerFrame")
                {
                    UserData = tab
                };
                tabButtons[(int)tab] = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), tabButtonHolder.RectTransform), tab.ToString())
                {
                    UserData = tab,
                    OnClicked = (bt, userdata) => { SelectTab((Tab)userdata); return true; }
                };
            }

            var buttonArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform, Anchor.BottomCenter), style: null);
            
            /// Graphics tab --------------------------------------------------------------
            
            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.CenterLeft)
                { RelativeOffset = new Vector2(0.02f, 0.0f) }) { RelativeSpacing = 0.01f, Stretch = true };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.CenterRight)
                { RelativeOffset = new Vector2(0.02f, 0.0f) }) { RelativeSpacing = 0.01f, Stretch = true };

            var supportedDisplayModes = new List<DisplayMode>();
            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
            {
                if (supportedDisplayModes.Any(m => m.Width == mode.Width && m.Height == mode.Height)) continue;
                supportedDisplayModes.Add(mode);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Resolution"));
            var resolutionDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), elementCount: supportedDisplayModes.Count)
            {
                OnSelected = SelectResolution
            };
            
            foreach (DisplayMode mode in supportedDisplayModes)
            {
                resolutionDD.AddItem(mode.Width + "x" + mode.Height, mode);
                if (GraphicsWidth == mode.Width && GraphicsHeight == mode.Height) resolutionDD.SelectItem(mode);
            }

            if (resolutionDD.SelectedItemData == null)
            {
                resolutionDD.SelectItem(GraphicsAdapter.DefaultAdapter.SupportedDisplayModes.Last());
            }
                        
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("DisplayMode"));
            var displayModeDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform));
            displayModeDD.AddItem(TextManager.Get("Fullscreen"), WindowMode.Fullscreen);
            displayModeDD.AddItem(TextManager.Get("Windowed"), WindowMode.Windowed);
            displayModeDD.AddItem(TextManager.Get("BorderlessWindowed"), WindowMode.BorderlessWindowed);
            displayModeDD.SelectItem(GameMain.Config.WindowMode);
            displayModeDD.OnSelected = (guiComponent, obj) => 
            {
                UnsavedSettings = true;
                GameMain.Config.WindowMode = (WindowMode)guiComponent.UserData;
                return true;
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform), style: null);

            GUITickBox vsyncTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("EnableVSync"))
            {
                OnSelected = (GUITickBox box) =>
                {
                    VSyncEnabled = box.Selected;
                    GameMain.GraphicsDeviceManager.SynchronizeWithVerticalRetrace = VSyncEnabled;
                    GameMain.GraphicsDeviceManager.ApplyChanges();
                    UnsavedSettings = true;

                    return true;
                },
                Selected = VSyncEnabled
            };
                      
            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), leftColumn.RectTransform), style: null);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ParticleLimit"));
            GUIScrollBar particleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = (ParticleLimit - 200) / 1300.0f,
                OnMoved = ChangeParticleLimit,
                Step = 0.1f
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), rightColumn.RectTransform), style: null);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LosEffect"));
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
            
            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), style: null);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LightMapScale"))
            {
                ToolTip = TextManager.Get("LightMapScaleToolTip")
            };
            new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                ToolTip = TextManager.Get("LightMapScaleToolTip"),
                BarScroll = MathUtils.InverseLerp(0.2f, 1.0f, LightMapScale),
                OnMoved = (scrollBar, barScroll) => { LightMapScale = MathHelper.Lerp(0.2f, 1.0f, barScroll); UnsavedSettings = true; return true; },
                Step = 0.25f
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), style: null);
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("SpecularLighting"))
            {
                ToolTip = TextManager.Get("SpecularLightingToolTip"),
                Selected = SpecularityEnabled,
                OnSelected = (tickBox) =>
                {
                    SpecularityEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), rightColumn.RectTransform), style: null);
            new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ChromaticAberration"))
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
                        
            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("HUDScale"));
            new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = (HUDScale - MinHUDScale) / (MaxHUDScale - MinHUDScale),
                OnMoved = ChangeHUDScale,
                Step = 0.05f
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), rightColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("InventoryScale"));
            new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = (InventoryScale - MinInventoryScale) / (MaxInventoryScale - MinInventoryScale),
                OnMoved = ChangeInventoryScale,
                Step = 0.05f
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), rightColumn.RectTransform), style: null);

            /// General tab --------------------------------------------------------------

            leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.General].RectTransform, Anchor.CenterLeft)
                { RelativeOffset = new Vector2(0.02f, 0.0f) }) { RelativeSpacing = 0.01f, Stretch = true };
            rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.General].RectTransform, Anchor.CenterRight)
                { RelativeOffset = new Vector2(0.02f, 0.0f) }) { RelativeSpacing = 0.01f, Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("ContentPackages"));
            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), leftColumn.RectTransform))
            {
                CanBeFocused = false
            };
            
            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), contentPackageList.Content.RectTransform, minSize: new Point(0, 15)), contentPackage.Name)
                {
                    UserData = contentPackage,
                    OnSelected = SelectContentPackage,
                    Selected = SelectedContentPackages.Contains(contentPackage)
                };
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Language"));
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform));
            foreach (string language in TextManager.AvailableLanguages)
            {
                languageDD.AddItem(language, language);
            }
            languageDD.SelectItem(TextManager.Language);
            languageDD.OnSelected = (guiComponent, obj) =>
            {
                string newLanguage = obj as string;
                if (newLanguage == Language) return true;

                UnsavedSettings = true;
                Language = newLanguage;

                new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredLanguage"));

                return true;
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform), style: null);
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("SoundVolume"));
            GUIScrollBar soundScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = SoundVolume,
                OnMoved = ChangeSoundVolume,
                Step = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("MusicVolume"));
            GUIScrollBar musicScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = MusicVolume,
                OnMoved = ChangeMusicVolume,
                Step = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("Controls"));

            var inputFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform))
            {
                Stretch = true
            };
            var inputNames = Enum.GetValues(typeof(InputType));
            for (int i = 0; i < inputNames.Length; i++)
            {
                var inputContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), inputFrame.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), inputContainer.RectTransform), TextManager.Get("InputType." + ((InputType)i)) + ": ", font: GUI.SmallFont);
                var keyBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 1.0f), inputContainer.RectTransform, Anchor.TopRight),
                    text: keyMapping[i].ToString(), font: GUI.SmallFont)
                {
                    UserData = i
                };
                keyBox.OnSelected += KeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("AimAssist"));
            new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = MathUtils.InverseLerp(0.0f, 5.0f, AimAssistAmount),
                OnMoved = (scrollBar, scroll) =>
                {
                    AimAssistAmount = MathHelper.Lerp(0.0f, 5.0f, scroll);
                    UnsavedSettings = true;
                    return true;
                },
                Step = 0.1f
            };

            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (x, y) => 
                {
                    if (GameMain.Config.UnsavedSettings) GameMain.Config.Load("config.xml");
                    if (Screen.Selected == GameMain.MainMenuScreen) GameMain.MainMenuScreen.SelectTab(0);
                    GUI.SettingsMenuOpen = false;
                    return true;
                }
            };

            applyButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonArea.RectTransform, Anchor.BottomRight),
                TextManager.Get("ApplySettingsButton"))
            {
                IgnoreLayoutGroups = true,
                Enabled = false
            };
            applyButton.OnClicked = ApplyClicked;

            SelectTab(Tab.General);
        }

        private void SelectTab(Tab tab)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = (Tab)tabs[i].UserData == tab;
                tabButtons[i].Selected = tabs[i].Visible;
            }
        }

        private void KeyBoxSelected(GUITextBox textBox, Keys key)
        {
            textBox.Text = "";
            CoroutineManager.StartCoroutine(WaitForKeyPress(textBox));
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

        private bool SelectContentPackage(GUITickBox tickBox)
        {
            var contentPackage = tickBox.UserData as ContentPackage;
            if (contentPackage.CorePackage)
            {
                if (tickBox.Selected)
                {
                    //make sure no other core packages are selected
                    SelectedContentPackages.RemoveWhere(cp => cp.CorePackage && cp != contentPackage);
                    SelectedContentPackages.Add(contentPackage);
                    foreach (GUITickBox otherTickBox in tickBox.Parent.Children)
                    {
                        ContentPackage otherContentPackage = otherTickBox.UserData as ContentPackage;
                        if (otherContentPackage == contentPackage) continue;
                        otherTickBox.Selected = SelectedContentPackages.Contains(otherContentPackage);
                    }
                }
                else if (SelectedContentPackages.Contains(contentPackage))
                {
                    //core packages cannot be deselected, only switched by selecting another core package
                    new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("CorePackageRequiredWarning"));
                    tickBox.Selected = true;
                }
            }
            else
            {
                if (tickBox.Selected)
                {
                    SelectedContentPackages.Add(contentPackage);
                }
                else
                {
                    SelectedContentPackages.Remove(contentPackage);
                }
            }
            return true;
        }

        private IEnumerable<object> WaitForKeyPress(GUITextBox keyBox)
        {
            yield return CoroutineStatus.Running;
            
            while (PlayerInput.LeftButtonHeld() || PlayerInput.LeftButtonClicked())
            {
                //wait for the mouse to be released, so that we don't interpret clicking on the textbox as the keybinding
                yield return CoroutineStatus.Running;
            }
            while (keyBox.Selected && PlayerInput.GetKeyboardState.GetPressedKeys().Length == 0 && 
                !PlayerInput.LeftButtonClicked() && !PlayerInput.RightButtonClicked() && !PlayerInput.MidButtonClicked())
            {
                if (Screen.Selected != GameMain.MainMenuScreen && !GUI.SettingsMenuOpen) yield return CoroutineStatus.Success;

                yield return CoroutineStatus.Running;
            }

            UnsavedSettings = true;

            int keyIndex = (int)keyBox.UserData;

            if (PlayerInput.LeftButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(0);
                keyBox.Text = "Mouse1";
            }
            else if (PlayerInput.RightButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(1);
                keyBox.Text = "Mouse2";
            }
            else if (PlayerInput.MidButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(2);
                keyBox.Text = "Mouse3";
            }
            else if (PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0)
            {
                Keys key = PlayerInput.GetKeyboardState.GetPressedKeys()[0];
                keyMapping[keyIndex] = new KeyOrMouse(key);
                keyBox.Text = key.ToString("G");
            }
            else
            {
                yield return CoroutineStatus.Success;
            }

            keyBox.Deselect();

            yield return CoroutineStatus.Success;
        }
        
        private bool ApplyClicked(GUIButton button, object userData)
        {
            Save();

            settingsFrame.Flash(Color.Green);
            
            if (GameMain.WindowMode != GameMain.Config.WindowMode)
            {
                GameMain.Instance.SetWindowMode(GameMain.Config.WindowMode);
            }

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
                new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredResolution"));
            }

            if (Screen.Selected != GameMain.MainMenuScreen) GUI.SettingsMenuOpen = false;

            return true;
        }
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public partial class GameSettings
    {
        private GUIFrame settingsFrame;
        private GUIButton applyButton;
        
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

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), settingsFrame.RectTransform, Anchor.Center)
                { RelativeOffset = new Vector2(0.0f, 0.04f) }, style: null);

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.48f, 1.0f), paddedFrame.RectTransform)) { RelativeSpacing = 0.02f };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.48f, 1.0f), paddedFrame.RectTransform, Anchor.TopRight)) { RelativeSpacing = 0.02f };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Resolution"));
            var resolutionDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform))
            {
                OnSelected = SelectResolution
            };

            var supportedModes = new List<DisplayMode>();
            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
            {
                if (supportedModes.FirstOrDefault(m => m.Width == mode.Width && m.Height == mode.Height) != null) continue;

                resolutionDD.AddItem(mode.Width + "x" + mode.Height, mode);
                supportedModes.Add(mode);

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
                    VSyncEnabled = !VSyncEnabled;
                    GameMain.GraphicsDeviceManager.SynchronizeWithVerticalRetrace = VSyncEnabled;
                    GameMain.GraphicsDeviceManager.ApplyChanges();
                    UnsavedSettings = true;

                    return true;
                },
                Selected = VSyncEnabled
            };
            
            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("ParticleLimit"));
            GUIScrollBar particleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform),
                barSize: 0.1f)
            {
                BarScroll = (ParticleLimit - 200) / 1300.0f,
                OnMoved = ChangeParticleLimit,
                Step = 0.1f
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
            
            //----------------------------------------------------------
            //right column
            //----------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ContentPackage"));
            var contentPackageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform));
            contentPackageDD.OnSelected = SelectContentPackage;

            foreach (ContentPackage contentPackage in ContentPackage.list)
            {
                contentPackageDD.AddItem(contentPackage.Name, contentPackage);
                if (SelectedContentPackage == contentPackage) contentPackageDD.SelectItem(contentPackage);
            }
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("Controls"));

            var inputFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform));
            var inputNames = Enum.GetNames(typeof(InputType));
            for (int i = 0; i < inputNames.Length; i++)
            {
                var inputContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), inputFrame.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), inputContainer.RectTransform), inputNames[i] + ": ", font: GUI.SmallFont);
                var keyBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 1.0f), inputContainer.RectTransform, Anchor.TopRight),
                    text: keyMapping[i].ToString(), font: GUI.SmallFont)
                {
                    UserData = i
                };
                keyBox.OnSelected += KeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;
            }

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.07f), leftColumn.RectTransform, Anchor.BottomLeft),
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

            applyButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.07f), rightColumn.RectTransform, Anchor.BottomRight),
                TextManager.Get("ApplySettingsButton"))
            {
                IgnoreLayoutGroups = true,
                Enabled = false
            };
            applyButton.OnClicked = ApplyClicked;
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

        private bool SelectContentPackage(GUIComponent select, object userData)
        {
            if (GameMain.Config.SelectedContentPackage != (ContentPackage)userData)
            {
                UnsavedSettings = true;
            }
            GameMain.Config.SelectedContentPackage = (ContentPackage)userData;
            return true;
        }

        private IEnumerable<object> WaitForKeyPress(GUITextBox keyBox)
        {
            yield return CoroutineStatus.Running;

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
                new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredText"));
            }

            return true;
        }
    }
}

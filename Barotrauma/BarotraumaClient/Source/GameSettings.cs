using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

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
            settingsFrame = new GUIFrame(new Rectangle(0, 0, 500, 500), null, Alignment.Center, "");

            new GUITextBlock(new Rectangle(0, -30, 0, 30), TextManager.Get("Settings"), "", Alignment.TopCenter, Alignment.TopCenter, settingsFrame, false, GUI.LargeFont);

            int x = 0, y = 10;

            new GUITextBlock(new Rectangle(0, y, 20, 20), TextManager.Get("Resolution"), "", Alignment.TopLeft, Alignment.TopLeft, settingsFrame);
            var resolutionDD = new GUIDropDown(new Rectangle(0, y + 20, 180, 20), "", "", settingsFrame);
            resolutionDD.OnSelected = SelectResolution;

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

            y += 50;

            //var fullScreenTick = new GUITickBox(new Rectangle(x, y, 20, 20), "Fullscreen", Alignment.TopLeft, settingsFrame);
            //fullScreenTick.OnSelected = ToggleFullScreen;
            //fullScreenTick.Selected = FullScreenEnabled;

            new GUITextBlock(new Rectangle(x, y, 20, 20), TextManager.Get("DisplayMode"), "", Alignment.TopLeft, Alignment.TopLeft, settingsFrame);
            var displayModeDD = new GUIDropDown(new Rectangle(x, y + 20, 180, 20), "", "", settingsFrame);
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

            y += 70;

            GUITickBox vsyncTickBox = new GUITickBox(new Rectangle(0, y, 20, 20), TextManager.Get("EnableVSync"), Alignment.CenterY | Alignment.Left, settingsFrame);
            vsyncTickBox.OnSelected = (GUITickBox box) =>
            {
                VSyncEnabled = !VSyncEnabled;
                GameMain.GraphicsDeviceManager.SynchronizeWithVerticalRetrace = VSyncEnabled;
                GameMain.GraphicsDeviceManager.ApplyChanges();
                UnsavedSettings = true;

                return true;
            };
            vsyncTickBox.Selected = VSyncEnabled;

            y += 50;

            new GUITextBlock(new Rectangle(0, y, 100, 20), TextManager.Get("ParticleLimit"), "", settingsFrame);
            GUIScrollBar particleScrollBar = new GUIScrollBar(new Rectangle(0, y + 20, 150, 20), "", 0.1f, settingsFrame);
            particleScrollBar.BarScroll = ((float)(ParticleLimit-200)) / 1300.0f;
            particleScrollBar.OnMoved = ChangeParticleLimit;
            particleScrollBar.Step = 0.1f;

            y += 70;

            new GUITextBlock(new Rectangle(0, y, 100, 20), TextManager.Get("SoundVolume"), "", settingsFrame);
            GUIScrollBar soundScrollBar = new GUIScrollBar(new Rectangle(0, y + 20, 150, 20), "", 0.1f, settingsFrame);
            soundScrollBar.BarScroll = SoundVolume;
            soundScrollBar.OnMoved = ChangeSoundVolume;
            soundScrollBar.Step = 0.05f;

            new GUITextBlock(new Rectangle(0, y + 40, 100, 20), TextManager.Get("MusicVolume"), "", settingsFrame);
            GUIScrollBar musicScrollBar = new GUIScrollBar(new Rectangle(0, y + 60, 150, 20), "", 0.1f, settingsFrame);
            musicScrollBar.BarScroll = MusicVolume;
            musicScrollBar.OnMoved = ChangeMusicVolume;
            musicScrollBar.Step = 0.05f;

            x = 200;
            y = 10;

            new GUITextBlock(new Rectangle(x, y, 20, 20), TextManager.Get("ContentPackage"), "", Alignment.TopLeft, Alignment.TopLeft, settingsFrame);
            var contentPackageDD = new GUIDropDown(new Rectangle(x, y + 20, 200, 20), "", "", settingsFrame);
            contentPackageDD.OnSelected = SelectContentPackage;

            foreach (ContentPackage contentPackage in ContentPackage.list)
            {
                contentPackageDD.AddItem(contentPackage.Name, contentPackage);
                if (SelectedContentPackage == contentPackage) contentPackageDD.SelectItem(contentPackage);
            }

            y += 50;
            new GUITextBlock(new Rectangle(x, y, 100, 20), TextManager.Get("Controls"), "", settingsFrame);
            y += 30;
            var inputNames = Enum.GetNames(typeof(InputType));
            for (int i = 0; i < inputNames.Length; i++)
            {
                new GUITextBlock(new Rectangle(x, y, 100, 18), inputNames[i] + ": ", "", Alignment.TopLeft, Alignment.CenterLeft, settingsFrame);
                var keyBox = new GUITextBox(new Rectangle(x + 100, y, 120, 18), null, null, Alignment.TopLeft, Alignment.CenterLeft, "", settingsFrame);

                keyBox.Text = keyMapping[i].ToString();
                keyBox.UserData = i;
                keyBox.OnSelected += KeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;

                y += 20;
            }

            applyButton = new GUIButton(new Rectangle(0, 0, 100, 20), TextManager.Get("ApplySettingsButton"), Alignment.BottomRight, "", settingsFrame);
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

            UnsavedSettings = true;

            return true;
        }

        private bool SelectContentPackage(GUIComponent select, object userData)
        {
            GameMain.Config.SelectedContentPackage = (ContentPackage)userData;
            UnsavedSettings = true;
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
            Save("config.xml");

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

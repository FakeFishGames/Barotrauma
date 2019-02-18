using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenTK.Audio.OpenAL;
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
            Audio,
            Controls,
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

        private bool ChangeSliderText(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            GUITextBlock text = scrollBar.UserData as GUITextBlock;
            //search for percentage value
            int index = text.Text.IndexOf("%");
            string label = text.Text;
            //if "%" is found
            if (index > 0)
            {
                while (true)
                {
                    //search for end of label
                    index -= 1;
                    if (text.Text[index] == ' ')
                        break;
                }
                label = text.Text.Substring(0, index);
            }
            text.Text = label + " " + barScroll * 100 + "%";
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

        private void CreateSettingsFrame()
        {
            settingsFrame = new GUIFrame(new RectTransform(new Point(500, 500), GUI.Canvas, Anchor.Center));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), settingsFrame.RectTransform),
                TextManager.Get("Settings"), textAlignment: Alignment.Center, font: GUI.LargeFont);

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.8f), settingsFrame.RectTransform, Anchor.Center)
            { RelativeOffset = new Vector2(0.0f, 0.06f) }, style: null);

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.05f), settingsFrame.RectTransform, Anchor.TopCenter)
            { RelativeOffset = new Vector2(0.0f, 0.11f) }, isHorizontal: true);

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabButtons = new GUIButton[tabs.Length];
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                tabs[(int)tab] = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.91f), paddedFrame.RectTransform), style: "InnerFrame")
                {
                    UserData = tab
                };
                tabButtons[(int)tab] = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), tabButtonHolder.RectTransform), 
                    TextManager.Get("SettingsTab." + tab.ToString()), style: "GUITabButton")
                {
                    UserData = tab,
                    OnClicked = (bt, userdata) => { SelectTab((Tab)userdata); return true; }
                };
            }

            var buttonArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform, Anchor.BottomCenter), style: null);

            /// Graphics tab --------------------------------------------------------------

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.CenterLeft)
            { RelativeOffset = new Vector2(0.02f, 0.0f) })
            { RelativeSpacing = 0.01f, Stretch = true };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.CenterRight)
            { RelativeOffset = new Vector2(0.02f, 0.0f) })
            { RelativeSpacing = 0.01f, Stretch = true };

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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Resolution"));
            var resolutionDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), elementCount: supportedDisplayModes.Count)
            {
                OnSelected = SelectResolution,
#if OSX
                ButtonEnabled = GameMain.Config.WindowMode == WindowMode.Windowed
#endif
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
#if OSX
                resolutionDD.ButtonEnabled = GameMain.Config.WindowMode == WindowMode.Windowed;
#endif
                return true;
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform), style: null);

            GUITickBox vsyncTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("EnableVSync"))
            {
                ToolTip = TextManager.Get("EnableVSyncToolTip"),
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
            GUITextBlock particleLimitText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ParticleLimit"));
            GUIScrollBar particleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
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
            GUITextBlock LightText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LightMapScale"))
            {
                ToolTip = TextManager.Get("LightMapScaleToolTip")
            };
            GUIScrollBar lightScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                UserData = LightText,
                ToolTip = TextManager.Get("LightMapScaleToolTip"),
                BarScroll = MathUtils.InverseLerp(0.2f, 1.0f, LightMapScale),
                OnMoved = (scrollBar, barScroll) =>
                {
                    ChangeSliderText(scrollBar, barScroll);
                    LightMapScale = MathHelper.Lerp(0.2f, 1.0f, barScroll);
                    UnsavedSettings = true; return true;
                },
                Step = 0.25f
            };
            lightScrollBar.OnMoved(lightScrollBar, lightScrollBar.BarScroll);

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

            GUITextBlock HUDScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("HUDScale"));
            GUIScrollBar HUDScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                UserData = HUDScaleText,
                BarScroll = (HUDScale - MinHUDScale) / (MaxHUDScale - MinHUDScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    HUDScale = MathHelper.Lerp(MinHUDScale, MaxHUDScale, scroll);
                    UnsavedSettings = true;
                    OnHUDScaleChanged?.Invoke();
                    return true;
                },
                Step = 0.05f
            };
            HUDScaleScrollBar.OnMoved(HUDScaleScrollBar, HUDScaleScrollBar.BarScroll);

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), rightColumn.RectTransform), style: null);

            GUITextBlock inventoryScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("InventoryScale"));
            GUIScrollBar inventoryScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), barSize: 0.1f)
            {
                UserData = inventoryScaleText,
                BarScroll = (InventoryScale - MinInventoryScale) / (MaxInventoryScale - MinInventoryScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    InventoryScale = MathHelper.Lerp(MinInventoryScale, MaxInventoryScale, scroll);
                    UnsavedSettings = true;
                    return true;
                },
                Step = 0.05f
            };
            inventoryScaleScrollBar.OnMoved(inventoryScaleScrollBar, inventoryScaleScrollBar.BarScroll);

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), rightColumn.RectTransform), style: null);

            /// Audio tab ----------------------------------------------------------------

            var audioSliders = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.4f), tabs[(int)Tab.Audio].RectTransform, Anchor.TopCenter)
                { RelativeOffset = new Vector2(0.0f, 0.02f) })
                { RelativeSpacing = 0.01f, Stretch = true };

            GUITextBlock soundVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), audioSliders.RectTransform), TextManager.Get("SoundVolume"));
            GUIScrollBar soundScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.2f), audioSliders.RectTransform),
                barSize: 0.1f)
            {
                UserData = soundVolumeText,
                BarScroll = SoundVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    SoundVolume = scroll;
                    return true;
                },
                Step = 0.05f
            };
            soundScrollBar.OnMoved(soundScrollBar, soundScrollBar.BarScroll);

            GUITextBlock musicVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.18f), audioSliders.RectTransform), TextManager.Get("MusicVolume"));
            GUIScrollBar musicScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.18f), audioSliders.RectTransform),
                barSize: 0.1f)
            {
                UserData = musicVolumeText,
                BarScroll = MusicVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    MusicVolume = scroll;
                    return true;
                },
                Step = 0.05f
            };
            musicScrollBar.OnMoved(musicScrollBar, musicScrollBar.BarScroll);

            GUITickBox muteOnFocusLostBox = new GUITickBox(new RectTransform(new Vector2(0.95f, 0.15f), audioSliders.RectTransform), TextManager.Get("MuteOnFocusLost"));
            muteOnFocusLostBox.Selected = MuteOnFocusLost;
            muteOnFocusLostBox.ToolTip = TextManager.Get("MuteOnFocusLostToolTip");
            muteOnFocusLostBox.OnSelected = (tickBox) =>
            {
                MuteOnFocusLost = tickBox.Selected;

                return true;
            };

            var voiceSettings = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.5f), tabs[(int)Tab.Audio].RectTransform, Anchor.BottomCenter)
                { RelativeOffset = new Vector2(0.0f, 0.04f) })
                { RelativeSpacing = 0.01f, Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), voiceSettings.RectTransform), TextManager.Get("VoiceChat"));

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), rightColumn.RectTransform), style: null);

            IList<string> deviceNames = Alc.GetString((IntPtr)null, AlcGetStringList.CaptureDeviceSpecifier);
            foreach (string name in deviceNames)
            {
                DebugConsole.NewMessage(name + " " + name.Length.ToString(), Color.Lime);
            }

            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice)) VoiceCaptureDevice = deviceNames[0];
#if (!OSX)
            var deviceList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.2f), voiceSettings.RectTransform), VoiceCaptureDevice, deviceNames.Count);
            foreach (string name in deviceNames)
            {
                deviceList.AddItem(name, name);
            }
            deviceList.OnSelected = (GUIComponent selected, object obj) =>
            {
                string name = obj as string;
                if (VoiceCaptureDevice == name) return true;

                VoipCapture.ChangeCaptureDevice(name);
                return true;
            };
#else
            var suavemente = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), voiceSettings.RectTransform), TextManager.Get("CurrentDevice") + ": " + VoiceCaptureDevice)
            {
                ToolTip = TextManager.Get("CurrentDeviceToolTip.OSX"),
                TextAlignment = Alignment.CenterX
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.25f), voiceSettings.RectTransform), TextManager.Get("RefreshDefaultDevice"))
            {
                ToolTip = TextManager.Get("RefreshDefaultDeviceToolTip"),
                OnClicked = (bt, userdata) =>
                {
                    deviceNames = Alc.GetString((IntPtr)null, AlcGetStringList.CaptureDeviceSpecifier);
                    if (VoiceCaptureDevice == deviceNames[0]) return true;

                    VoipCapture.ChangeCaptureDevice(deviceNames[0]);
                    suavemente.Text = TextManager.Get("CurrentDevice") + ": " + VoiceCaptureDevice;
                    suavemente.Flash(Color.Blue);

                    return true;
                }
            };
#endif

            var radioButtonFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.6f), voiceSettings.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            GUIRadioButtonGroup voiceMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                string langStr = "VoiceMode." + ((VoiceMode)i).ToString();
                var tick = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.33f), radioButtonFrame.RectTransform), TextManager.Get(langStr))
                {
                    ToolTip = TextManager.Get(langStr + "ToolTip")
                };

                voiceMode.AddRadioButton((VoiceMode)i, tick);
            }

            var voiceInputContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 0.2f), voiceSettings.RectTransform, Anchor.BottomCenter));
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), voiceInputContainer.RectTransform), TextManager.Get("InputType.Voice") + ": ");
            var voiceKeyBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 1.0f), voiceInputContainer.RectTransform, Anchor.TopRight),
                text: keyMapping[(int)InputType.Voice].ToString())
            {
                UserData = InputType.Voice
            };
            voiceKeyBox.OnSelected += KeyBoxSelected;
            voiceKeyBox.SelectedColor = Color.Gold * 0.3f;

            var voiceActivityGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), voiceSettings.RectTransform));

            GUITextBlock noiseGateText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), voiceActivityGroup.RectTransform), TextManager.Get("NoiseGateThreshold"))
            {
                TextGetter = () =>
                {
                    return TextManager.Get("NoiseGateThreshold") + " " + ((int)NoiseGateThreshold).ToString() + " dB";
                }
            };
            var dbMeter = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.5f), voiceActivityGroup.RectTransform), 0.0f, Color.Lime);
            dbMeter.ProgressGetter = () =>
            {
                if (VoipCapture.Instance == null) return 0.0f;
                dbMeter.Color = VoipCapture.Instance.LastdB > NoiseGateThreshold ? Color.Lime : Color.Orange; //TODO: i'm a filthy hack
                return ((float)VoipCapture.Instance.LastdB + 100.0f) / 100.0f;
            };
            var noiseGateSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 1.0f), dbMeter.RectTransform, Anchor.Center), color: Color.White, barSize: 0.03f);
            noiseGateSlider.Frame.Visible = false;
            noiseGateSlider.Step = 0.01f;
            noiseGateSlider.Range = new Vector2(-100.0f, 0.0f);
            noiseGateSlider.BarScrollValue = NoiseGateThreshold;
            noiseGateSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                NoiseGateThreshold = scrollBar.BarScrollValue;
                UnsavedSettings = true;
                return true;
            };

            voiceMode.OnSelect = (GUIRadioButtonGroup rbg, Enum value) =>
            {
                if (rbg.Selected != null && rbg.Selected.Equals(value)) return;
                VoiceMode vMode = (VoiceMode)value;
                VoiceSetting = vMode;

                if (vMode == VoiceMode.Activity)
                {
                    voiceActivityGroup.Visible = true;
                    if (GameMain.Client == null && VoipCapture.Instance == null)
                    {
                        VoipCapture.Create(GameMain.Config.VoiceCaptureDevice);
                    }
                }
                else
                {
                    voiceActivityGroup.Visible = false;
                    if (GameMain.Client == null)
                    {
                        VoipCapture.Instance?.Dispose();
                    }
                }

                voiceInputContainer.Visible = (vMode == VoiceMode.PushToTalk);
                UnsavedSettings = true;
            };
            voiceMode.Selected = VoiceSetting;

            /// Controls tab -------------------------------------------------------------
            var controlsLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabs[(int)Tab.Controls].RectTransform, Anchor.Center)
                { RelativeOffset = new Vector2(0.0f, 0.0f) })
                { RelativeSpacing = 0.01f, Stretch = true };

            var inputFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), controlsLayoutGroup.RectTransform))
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
            GUITextBlock aimAssistText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform), TextManager.Get("AimAssist"));
            GUIScrollBar aimAssistSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform),
                barSize: 0.1f)
            {
                UserData = aimAssistText,
                BarScroll = MathUtils.InverseLerp(0.0f, 5.0f, AimAssistAmount),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    AimAssistAmount = MathHelper.Lerp(0.0f, 5.0f, scroll);
                    return true;
                },
                Step = 0.1f
            };
            aimAssistSlider.OnMoved(aimAssistSlider, aimAssistSlider.BarScroll);

            /// General tab --------------------------------------------------------------

            var generalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabs[(int)Tab.General].RectTransform, Anchor.Center)
                { RelativeOffset = new Vector2(0.0f, 0.0f) }) { RelativeSpacing = 0.01f, Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), generalLayoutGroup.RectTransform), TextManager.Get("ContentPackages"));
            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), generalLayoutGroup.RectTransform))
            {
                CanBeFocused = false
            };


            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), contentPackageList.Content.RectTransform, minSize: new Point(0, 15)), contentPackage.Name)
                {
                    UserData = contentPackage,
                    OnSelected = SelectContentPackage,
                    Selected = SelectedContentPackages.Contains(contentPackage)
                };
                if (!contentPackage.IsCompatible())
                {
                    tickBox.TextColor = Color.Red;
                    tickBox.Enabled = false;
                    tickBox.ToolTip = TextManager.Get(contentPackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage")
                                    .Replace("[packagename]", contentPackage.Name)
                                    .Replace("[packageversion]", contentPackage.GameVersion.ToString())
                                    .Replace("[gameversion]", GameMain.Version.ToString());
                }
                else if (contentPackage.CorePackage && !contentPackage.ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
                {
                    tickBox.TextColor = Color.Red;
                    tickBox.Enabled = false;
                    tickBox.ToolTip = TextManager.Get("ContentPackageMissingCoreFiles")
                                    .Replace("[packagename]", contentPackage.Name)
                                    .Replace("[missingfiletypes]", string.Join(", ", missingContentTypes));
                }
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), generalLayoutGroup.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), generalLayoutGroup.RectTransform), TextManager.Get("Language"));
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), generalLayoutGroup.RectTransform));
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
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), generalLayoutGroup.RectTransform), style: null);

            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (x, y) =>
                {
                    if (UnsavedSettings)
                    {
                        LoadPlayerConfig();
                    }
                    if (Screen.Selected == GameMain.MainMenuScreen) GameMain.MainMenuScreen.ReturnToMainMenu(null, null);
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

            UnsavedSettings = false; // Reset unsaved settings to false once the UI has been created
            SelectTab(Tab.General);
        }

        private void SelectTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Audio:
                    if (VoiceSetting == VoiceMode.Activity)
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
                    return true;
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
            UnsavedSettings = true;
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
            else if (PlayerInput.Mouse4ButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(3);
                keyBox.Text = "Mouse4";
            }
            else if (PlayerInput.Mouse5ButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(4);
                keyBox.Text = "Mouse5";
            }
            else if (PlayerInput.MouseWheelUpClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(5);
                keyBox.Text = "MouseWheelUp";
            }
            else if (PlayerInput.MouseWheelDownClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(6);
                keyBox.Text = "MouseWheelDown";
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
            SaveNewPlayerConfig();

            settingsFrame.Flash(Color.Green);

            if (GameMain.WindowMode != GameMain.Config.WindowMode)
            {
                GameMain.Instance.ApplyGraphicsSettings();
            }

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
#if OSX
                if (GameMain.Config.WindowMode != WindowMode.BorderlessWindowed)
                {
#endif
                    new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredResolution"));
#if OSX
                }
#endif
            }

            if (Screen.Selected != GameMain.MainMenuScreen) GUI.SettingsMenuOpen = false;

            return true;
        }
    }
}

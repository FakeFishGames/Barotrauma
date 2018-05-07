using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class MainMenuScreen : Screen
    {
        public enum Tab { NewGame = 1, LoadGame = 2, HostServer = 3, Settings = 4 }

        private GUIFrame buttonsParent;

        private GUIFrame[] menuTabs;

        private CampaignSetupUI campaignSetupUI;

        private GUITextBox serverNameBox, portBox, passwordBox, maxPlayersBox;
        private GUITickBox isPublicBox, useUpnpBox;

        private GameMain game;

        private Tab selectedTab;

        // test elements
        private GUIFrame outerElement;
        private GUIFrame testElement;
        private GUIButton animEditorButton;

        public MainMenuScreen(GameMain game)
        {
            animEditorButton = new GUIButton(new RectTransform(new Point(150, 40), parent: null, anchor: Anchor.TopRight) { AbsoluteOffset = new Point(50, 50) }, "Animation Editor")
            {
                Color = Color.Red * 0.8f
            };
            animEditorButton.OnClicked += (button, obj) => 
            {
                GameMain.AnimationEditorScreen.Select();
                return true;
            };

            buttonsParent = new GUIFrame(new RectTransform(new Vector2(0.15f, 1), parent: null, anchor: Anchor.BottomLeft)
            {
                RelativeOffset = new Vector2(0, 0.1f),
                AbsoluteOffset = new Point(50, 0)
            }, color: Color.Transparent);
            var minButtonSize = new Point(120, 20);
            var maxButtonSize = new Point(240, 40);
            // 
            var buttons = GUI.CreateButtons(8, new Vector2(1, 0.04f), buttonsParent.RectTransform, anchor: Anchor.BottomLeft,
                minSize: minButtonSize, maxSize: maxButtonSize, relativeSpacing: 0.005f, extraSpacing: i => i % 2 == 0 ? 0 : 20);
            buttons.ForEach(b => b.Color *= 0.8f);
            SetupButtons(buttons);
            buttons.ForEach(b => b.TextBlock.SetTextPos());

            var relativeSize = new Vector2(0.5f, 0.5f);
            var minSize = new Point(600, 400);
            var maxSize = new Point(900, 600);
            var anchor = Anchor.Center;
            var pivot = Pivot.Center;
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length + 1];
            menuTabs[(int)Tab.NewGame] = new GUIFrame(new RectTransform(relativeSize, null, anchor, pivot, minSize, maxSize));
            menuTabs[(int)Tab.LoadGame] = new GUIFrame(new RectTransform(relativeSize, null, anchor, pivot, minSize, maxSize));

            // TODO: refactor using the RectTransform
            campaignSetupUI = new CampaignSetupUI(false, menuTabs[(int)Tab.NewGame], menuTabs[(int)Tab.LoadGame]);
            campaignSetupUI.LoadGame = LoadGame;
            campaignSetupUI.StartNewGame = StartGame;

            menuTabs[(int)Tab.HostServer] = new GUIFrame(new RectTransform(relativeSize, null, anchor, pivot, minSize, maxSize));

            CreateHostServerFields();

            this.game = game;
        }

        public override void Select()
        {
            base.Select();

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetworkMember.Disconnect();
                GameMain.NetworkMember = null;
            }

            Submarine.Unload();

            campaignSetupUI.UpdateSubList();

            SelectTab(null, 0);
        }

        public bool SelectTab(GUIButton button, object obj)
        {
            try
            {
                SelectTab((Tab)obj);
            }
            catch (Exception e)
            {
                // TODO: This is bad, because the exception might be quite important in debugging. Try to get rid of this try catch block.
                //DebugConsole.ThrowError("Exception: ", e);
                selectedTab = 0;
            }

            if (button != null) button.Selected = true;

            foreach (GUIComponent child in buttonsParent.Children)
            {
                GUIButton otherButton = child as GUIButton;
                if (otherButton == null || otherButton == button) continue;

                otherButton.Selected = false;
            }

            if (Selected != this) Select();

            return true;
        }

        public void SelectTab(Tab tab)
        {
            if (GameMain.Config.UnsavedSettings)
            {
                var applyBox = new GUIMessageBox(
                    TextManager.Get("ApplySettingsLabel"),
                    TextManager.Get("ApplySettingsQuestion"),
                    new string[] { TextManager.Get("ApplySettingsYes"), TextManager.Get("ApplySettingsNo") });
                applyBox.Buttons[0].OnClicked += applyBox.Close;
                applyBox.Buttons[0].OnClicked += ApplySettings;
                applyBox.Buttons[0].UserData = tab;
                applyBox.Buttons[1].OnClicked += applyBox.Close;
                applyBox.Buttons[1].OnClicked += DiscardSettings;
                applyBox.Buttons[1].UserData = tab;

                return;
            }

            selectedTab = tab;

            switch (selectedTab)
            {
                case Tab.NewGame:
                    campaignSetupUI.CreateDefaultSaveName();
                    break;
                case Tab.LoadGame:
                    campaignSetupUI.UpdateLoadMenu();
                    break;
                case Tab.Settings:
                    GameMain.Config.ResetSettingsFrame();
                    menuTabs[(int)Tab.Settings] = GameMain.Config.SettingsFrame;
                    break;
            }
        }

        private bool ApplySettings(GUIButton button, object userData)
        {
            GameMain.Config.Save("config.xml");

            if (userData is Tab) SelectTab((Tab)userData);

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
                new GUIMessageBox(
                    TextManager.Get("RestartRequiredLabel"),
                    TextManager.Get("RestartRequiredText"));
            }

            return true;
        }

        private bool DiscardSettings(GUIButton button, object userData)
        {
            GameMain.Config.Load("config.xml");
            if (userData is Tab) SelectTab((Tab)userData);

            return true;
        }


        private bool TutorialButtonClicked(GUIButton button, object obj)
        {
            //!!!!!!!!!!!!!!!!!! placeholder
            TutorialMode.StartTutorial(Tutorials.TutorialType.TutorialTypes[0]);

            return true;
        }

        private bool JoinServerClicked(GUIButton button, object obj)
        {
            GameMain.ServerListScreen.Select();
            return true;
        }

        private bool ChangeMaxPlayers(GUIButton button, object obj)
        {
            int currMaxPlayers = 8;

            int.TryParse(maxPlayersBox.Text, out currMaxPlayers);
            currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers + (int)button.UserData, 1, NetConfig.MaxPlayers);

            maxPlayersBox.Text = currMaxPlayers.ToString();

            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            string name = serverNameBox.Text;
            if (string.IsNullOrEmpty(name))
            {
                serverNameBox.Flash();
                return false;
            }

            int port;
            if (!int.TryParse(portBox.Text, out port) || port < 0 || port > 65535)
            {
                portBox.Text = NetConfig.DefaultPort.ToString();
                portBox.Flash();

                return false;
            }

            GameMain.NetLobbyScreen = new NetLobbyScreen();

            try
            {
                GameMain.NetworkMember = new GameServer(name, port, isPublicBox.Selected, passwordBox.Text, useUpnpBox.Selected, int.Parse(maxPlayersBox.Text));
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start server", e);
            }

            GameMain.NetLobbyScreen.IsServer = true;
            //Game1.NetLobbyScreen.Select();
            return true;
        }


        private bool QuitClicked(GUIButton button, object obj)
        {
            game.Exit();
            return true;
        }


        public override void AddToGUIUpdateList()
        {
            buttonsParent.AddToGUIUpdateList();
            if (selectedTab > 0) menuTabs[(int)selectedTab].AddToGUIUpdateList();
            testElement?.AddToGUIUpdateList();
            outerElement?.AddToGUIUpdateList();
            animEditorButton.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            //buttonsParent.Update((float)deltaTime);
            //animEditorButton.Update((float)deltaTime);

            //if (selectedTab > 0) menuTabs[(int)selectedTab].Update((float)deltaTime);

            GameMain.TitleScreen.TitlePosition =
                Vector2.Lerp(GameMain.TitleScreen.TitlePosition, new Vector2(
                    GameMain.TitleScreen.TitleSize.X / 2.0f * GameMain.TitleScreen.Scale + 30.0f,
                    GameMain.TitleScreen.TitleSize.Y / 2.0f * GameMain.TitleScreen.Scale + 30.0f),
                    0.1f);

            CreateTestElements();
            UpdateTestElements();
            //testElement?.Update((float)deltaTime);
            //outerElement?.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            //Game1.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            buttonsParent.Draw(spriteBatch);
            animEditorButton.Draw(spriteBatch);

            if (selectedTab > 0) menuTabs[(int)selectedTab].Draw(spriteBatch);

            testElement?.Draw(spriteBatch);
            outerElement?.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            GUI.DrawString(spriteBatch, new Vector2(200, 100), "selected tab " + selectedTab, Color.White);

#if DEBUG
            GUI.Font.DrawString(spriteBatch, "Barotrauma v" + GameMain.Version + " (debug build)", new Vector2(10, GameMain.GraphicsHeight - 20), Color.White);
#else
            GUI.Font.DrawString(spriteBatch, "Barotrauma v" + GameMain.Version, new Vector2(10, GameMain.GraphicsHeight - 20), Color.White);
#endif

            spriteBatch.End();
        }

        private void StartGame(Submarine selectedSub, string saveName, string mapSeed)
        {
            if (string.IsNullOrEmpty(saveName)) return;

            string[] existingSaveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);

            if (Array.Find(existingSaveFiles, s => s == saveName) != null)
            {
                new GUIMessageBox("Save name already in use", "Please choose another name for the save file");
                return;
            }

            if (selectedSub == null)
            {
                new GUIMessageBox(TextManager.Get("SubNotSelected"), TextManager.Get("SelectSubRequest"));
                return;
            }

            if (!Directory.Exists(SaveUtil.TempPath))
            {
                Directory.CreateDirectory(SaveUtil.TempPath);
            }

            File.Copy(selectedSub.FilePath, Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"), true);

            selectedSub = new Submarine(Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"), "");

            GameMain.GameSession = new GameSession(selectedSub, saveName, GameModePreset.list.Find(gm => gm.Name == "Single Player"));
            (GameMain.GameSession.GameMode as CampaignMode).GenerateMap(mapSeed);

            GameMain.LobbyScreen.Select();
        }

        private void LoadGame(string saveFile)
        {
            if (string.IsNullOrWhiteSpace(saveFile)) return;

            try
            {
                SaveUtil.LoadGame(saveFile);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading save \"" + saveFile + "\" failed", e);
                return;
            }


            GameMain.LobbyScreen.Select();
        }

        #region UI Methods
        private void SetupButtons(List<GUIButton> buttons)
        {
            for (int i = 0; i < 8; i++)
            {
                var button = buttons[i];
                switch (i)
                {
                    case 7:
                        button.Text = TextManager.Get("TutorialButton");
                        button.OnClicked = TutorialButtonClicked;
                        break;
                    case 6:
                        button.Text = TextManager.Get("NewGameButton");
                        button.UserData = Tab.NewGame;
                        button.OnClicked = SelectTab;
                        break;
                    case 5:
                        button.Text = TextManager.Get("LoadGameButton");
                        button.UserData = Tab.LoadGame;
                        button.OnClicked = SelectTab;
                        break;
                    case 4:
                        button.Text = TextManager.Get("JoinServerButton");
                        //button.UserData = (int)Tabs.JoinServer;
                        button.OnClicked = JoinServerClicked;
                        break;
                    case 3:
                        button.Text = TextManager.Get("HostServerButton");
                        button.UserData = Tab.HostServer;
                        button.OnClicked = SelectTab;
                        break;
                    case 2:
                        button.Text = TextManager.Get("SubEditorButton");
                        button.OnClicked = (btn, userdata) => { GameMain.SubEditorScreen.Select(); return true; };
                        break;
                    case 1:
                        button.Text = TextManager.Get("SettingsButton");
                        button.UserData = Tab.Settings;
                        button.OnClicked = SelectTab;
                        break;
                    case 0:
                        button.Text = TextManager.Get("QuitButton");
                        button.OnClicked = QuitClicked;
                        break;
                    default:
                        throw new Exception();
                }
            }
        }

        private void CreateHostServerFields()
        {
            Vector2 textLabelSize = new Vector2(0.25f, 0.1f);
            Vector2 textFieldSize = new Vector2(0.25f, 0.1f);
            Vector2 buttonSize = new Vector2(0.04f, 0.06f);
            float leftMargin = 0.05f;
            float topMargin = 0.1f;
            int absoluteSpacing = 5;
            float relativeSpacing = 0.01f;
            Vector2 tickBoxSize = new Vector2(0.4f, 0.1f);
            GUIComponent parent = menuTabs[(int)Tab.HostServer];
            Alignment textAlignment = Alignment.CenterLeft;
            int lineCount = 0;
            Func<int, float> getY = lc => topMargin + (textLabelSize.Y + relativeSpacing) * lc;

            new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform)
            {
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("ServerName"), textAlignment: textAlignment);
            serverNameBox = new GUITextBox(new RectTransform(textFieldSize, parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing, 0),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing, getY(lineCount))
            }, textAlignment: textAlignment);

            lineCount++;

            new GUITextBlock(new RectTransform(textLabelSize, parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(0, absoluteSpacing),
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("ServerPort"), textAlignment: textAlignment);
            portBox = new GUITextBox(new RectTransform(textFieldSize, parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing, 0),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing, getY(lineCount))
            }, textAlignment: textAlignment)
            {
                Text = NetConfig.DefaultPort.ToString(),
                ToolTip = "Server port"
            };

            lineCount++;

            var maxPlayersLabel = new GUITextBlock(new RectTransform(textLabelSize, parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(0, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("MaxPlayers"), textAlignment: textAlignment);

            new GUIButton(new RectTransform(buttonSize, parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing, absoluteSpacing * lineCount + 10),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing, getY(lineCount))
            }, "-", textAlignment: Alignment.Center)
            {
                UserData = -1,
                OnClicked = ChangeMaxPlayers
            };

            float maxPlayersBoxWidth = buttonSize.X * 2;
            maxPlayersBox = new GUITextBox(new RectTransform(new Vector2(maxPlayersBoxWidth, textFieldSize.Y), parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing * 2, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing * 2 + buttonSize.X, getY(lineCount))
            }, textAlignment: Alignment.Center)
            {
                Text = "8",
                Enabled = false
            };
            new GUIButton(new RectTransform(buttonSize, parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing * 3, absoluteSpacing * lineCount + 10),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing * 3 + buttonSize.X + maxPlayersBoxWidth, getY(lineCount))
            }, "+", textAlignment: Alignment.Center)
            {
                UserData = 1,
                OnClicked = ChangeMaxPlayers
            };

            lineCount++;

            new GUITextBlock(new RectTransform(textLabelSize, parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(0, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("Password"), textAlignment: textAlignment);
            passwordBox = new GUITextBox(new RectTransform(textFieldSize, parent: parent.RectTransform)
            {
                AbsoluteOffset = new Point(absoluteSpacing, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin + textLabelSize.X + relativeSpacing, getY(lineCount))
            }, textAlignment: textAlignment);

            lineCount++;

            isPublicBox = new GUITickBox(new RectTransform(tickBoxSize, parent.RectTransform)
            {
                AbsoluteOffset = new Point(GUITickBox.size / 2, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("PublicServer"))
            {
                ToolTip = TextManager.Get("PublicServerToolTip")
            };

            lineCount++;

            useUpnpBox = new GUITickBox(new RectTransform(tickBoxSize, parent.RectTransform)
            {
                AbsoluteOffset = new Point(GUITickBox.size / 2, absoluteSpacing * lineCount),
                RelativeOffset = new Vector2(leftMargin, getY(lineCount))
            }, TextManager.Get("AttemptUPnP"))
            {
                ToolTip = TextManager.Get("AttemptUPnPToolTip")
            };

            new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), parent.RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = new Vector2(leftMargin, topMargin)
            }, TextManager.Get("StartServerButton"))
            {
                OnClicked = HostServerClicked
            };
        }
        #endregion

        #region UI Test
        private void CreateTestElements()
        {
            //if (Keyboard.GetState().IsKeyDown(Keys.T))
            //{
            //    //outerElement.RectTransform.GetChildren().ForEachMod(c => outerElement.RectTransform.RemoveChild(c));
            //    outerElement.RectTransform.GetChildren().ForEachMod(c =>
            //    {
            //        if (outerElement.RectTransform.IsParentOf(c))
            //        {
            //            outerElement.RectTransform.RemoveChild(c);
            //        }
            //    });
            //}
            if (PlayerInput.KeyHit(Keys.T))
            {
                testElement = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.5f), parent: null, anchor: Anchor.Center));
                var p = testElement;
                new GUITextBlock(new RectTransform(new Point(100, 30), p.RectTransform, Anchor.Center), "Keep calm, this is a test. Keep calm, this is a test.", wrap: true);
                new GUITextBox(new RectTransform(new Point(100, 100), p.RectTransform, Anchor.Center) { AbsoluteOffset = new Point(0, 100) }, "Carry on.", wrap: true);
                new GUIButton(new RectTransform(new Point(100, 30), p.RectTransform, Anchor.Center) { AbsoluteOffset = new Point(0, 60) }, "Test Button");

                //// TODO: does not work
                //var dropdown = new GUIDropDown(new RectTransform(new Point(100, 30), p.RectTransform, Anchor.Center), "Dropdown");
                //dropdown.AddItem("Test1");
                //dropdown.AddItem("Test2");
                //dropdown.AddItem("Test3");
                //dropdown.AddItem("Test4");
                //dropdown.AddItem("Test5");

                //new GUIProgressBar(new RectTransform(new Point(200, 20), p.RectTransform, Anchor.BottomCenter), 0.5f, Color.Green);

                //new GUINumberInput(new RectTransform(new Point(100, 40), p.RectTransform, Anchor.Center), GUINumberInput.NumberType.Int);

                //var messageBox = new GUIMessageBox(new RectTransform(Vector2.One * 0.75f, parent: null, anchor: Anchor.Center),
                //    "Header text", "Main textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain " +
                //    "textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain text" +
                //    "Main textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain text" +
                //    "Main textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain textMain" +
                //    "");
                //messageBox.AddButton(new RectTransform(new Vector2(0.2f, 0.1f), messageBox.RectTransform, anchor: Anchor.BottomCenter) { RelativeOffset = new Vector2(0, 0.1f) }, "OK", (button, obj) =>
                //{
                //    messageBox.Close();
                //    return true;
                //});
                //messageBox.AddButton(new RectTransform(new Vector2(0.2f, 0.1f), messageBox.RectTransform, anchor: Anchor.BottomLeft) { RelativeOffset = new Vector2(0.1f, 0.1f) }, "Add text", (button, obj) =>
                //{
                //    messageBox.Text.Text += "\nNew text";
                //    return true;
                //});
            }
            if (Keyboard.GetState().IsKeyDown(Keys.R))
            {
                outerElement = new GUIFrame(new RectTransform(Vector2.One, parent: null, anchor: Anchor.Center));
                bool global = Keyboard.GetState().IsKeyDown(Keys.Space);
                if (global)
                {
                    RectTransform.ResetGlobalScale();
                }
                else
                {
                    //outerElement.RectTransform.ResetScale();
                    //outerElement.RectTransform.ClearChildren();
                }
                for (int i = 0; i < 5; i++)
                {
                    //var parent = innerElements.LastOrDefault();
                    //if (parent == null)
                    //{
                    //    parent = outerElement;
                    //}
                    var parent = outerElement;
                    GUIFrame element;
                    switch (i)
                    {
                        case 0:
                            element = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), parent.RectTransform, anchor: Anchor.TopLeft), color: Rand.Color());
                            break;
                        case 1:
                            element = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), parent.RectTransform, anchor: Anchor.TopRight), color: Rand.Color());
                            break;
                        case 2:
                            element = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), parent.RectTransform, anchor: Anchor.BottomLeft), color: Rand.Color());
                            break;
                        case 3:
                            element = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), parent.RectTransform, anchor: Anchor.BottomRight), color: Rand.Color());
                            break;
                        case 4:
                            // absolute element
                            element = new GUIFrame(new RectTransform(new Point(200, 200), parent.RectTransform, anchor: Anchor.Center), color: Rand.Color());
                            break;
                        default:
                            element = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.1f), parent.RectTransform, anchor: Anchor.Center), color: Rand.Color());
                            break;
                    }
                    if (i < 4)
                    {
                        // offsets are cumulative
                        element.RectTransform.AbsoluteOffset = new Point(10, 10);
                        element.RectTransform.RelativeOffset = new Vector2(0.05f, 0.05f);
                        element.RectTransform.MinSize = new Point(200, 200);
                        element.RectTransform.MaxSize = new Point(400, 400);
                    }
                }
            }
        }

        private void UpdateTestElements()
        {
            //var element = Keyboard.GetState().IsKeyDown(Keys.LeftControl) ? outerElement.Children.FirstOrDefault() : outerElement;
            //var element = buttonsParent;
            //var element = menuTabs[(int)Tab.HostServer];
            var element = testElement;
            if (element == null) { return; }
            bool global = Keyboard.GetState().IsKeyDown(Keys.Space);
            // Scaling
            float step = 0.01f;
            if (Keyboard.GetState().IsKeyDown(Keys.OemPlus))
            {
                if (global)
                {
                    RectTransform.globalScale *= 1 + step;
                    element.RectTransform.RecalculateScale(true);
                    buttonsParent.RectTransform.RecalculateScale(true);
                    menuTabs.ForEach(t => t?.RectTransform?.RecalculateScale(true));
                }
                else
                {
                    element.RectTransform.LocalScale *= 1 + step;
                }
                buttonsParent.Children
                    .Select(b => b as GUIButton)
                    .ForEach(b => b?.TextBlock.SetTextPos());
            }
            if (Keyboard.GetState().IsKeyDown(Keys.OemMinus))
            {
                if (global)
                {
                    RectTransform.globalScale *= 1 - step;
                    element.RectTransform.RecalculateScale(true);
                    buttonsParent.RectTransform.RecalculateScale(true);
                    menuTabs.ForEach(t => t?.RectTransform?.RecalculateScale(true));
                }
                else
                {
                    element.RectTransform.LocalScale *= 1 - step;
                }
                buttonsParent.Children
                    .Select(b => b as GUIButton)
                    .ForEach(b => b?.TextBlock.SetTextPos());
            }
            // Size
            if (Keyboard.GetState().IsKeyDown(Keys.Left))
            {
                //element.RectTransform.NonScaledSize -= new Point(1, 0);
                element.RectTransform.RelativeSize -= new Vector2(step, 0);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.Right))
            {
                //element.RectTransform.NonScaledSize += new Point(1, 0);
                element.RectTransform.RelativeSize += new Vector2(step, 0);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.Up))
            {
                //element.RectTransform.NonScaledSize += new Point(0, 1);
                element.RectTransform.RelativeSize += new Vector2(0, step);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.Down))
            {
                //element.RectTransform.NonScaledSize -= new Point(0, 1);
                element.RectTransform.RelativeSize -= new Vector2(0, step);
            }
            // Translation
            if (Keyboard.GetState().IsKeyDown(Keys.A))
            {
                element.RectTransform.Translate(new Point(-1, 0));
            }
            if (Keyboard.GetState().IsKeyDown(Keys.D))
            {
                element.RectTransform.Translate(new Point(1, 0));
            }
            if (Keyboard.GetState().IsKeyDown(Keys.W))
            {
                element.RectTransform.Translate(new Point(0, -1));
            }
            if (Keyboard.GetState().IsKeyDown(Keys.S))
            {
                element.RectTransform.Translate(new Point(0, 1));
            }
            // Positioning (with matching anchors and pivots)
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad7))
            {
                element.RectTransform.SetPosition(Anchor.TopLeft);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad8))
            {
                element.RectTransform.SetPosition(Anchor.TopCenter);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad9))
            {
                element.RectTransform.SetPosition(Anchor.TopRight);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad4))
            {
                element.RectTransform.SetPosition(Anchor.CenterLeft);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad5))
            {
                element.RectTransform.SetPosition(Anchor.Center);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad6))
            {
                element.RectTransform.SetPosition(Anchor.CenterRight);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad1))
            {
                element.RectTransform.SetPosition(Anchor.BottomLeft);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad2))
            {
                element.RectTransform.SetPosition(Anchor.BottomCenter);
            }
            if (Keyboard.GetState().IsKeyDown(Keys.NumPad3))
            {
                element.RectTransform.SetPosition(Anchor.BottomRight);
            }
        }

        public Texture2D CreateTexture(int width, int height, Color? color = null)
        {
            var texture = new Texture2D(GameMain.GraphicsDeviceManager.GraphicsDevice, width, height);
            var data = new Color[width * height];
            Color c = color ?? Color.White;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = c;
            }
            texture.SetData(data);
            return texture;
        }
        #endregion
    }
}

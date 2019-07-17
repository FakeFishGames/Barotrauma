using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Globalization;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        public partial class Command
        {
            /// <summary>
            /// Executed when a client uses the command. If not set, the command is relayed to the server as-is.
            /// </summary>
            public Action<string[]> OnClientExecute;

            public bool RelayToServer = true;

            public void ClientExecute(string[] args)
            {
                if (!CheatsEnabled && IsCheat)
                {
                    NewMessage("You need to enable cheats using the command \"enablecheats\" before you can use the command \"" + names[0] + "\".", Color.Red);
                    if (Steam.SteamManager.USE_STEAM)
                    {
                        NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                    }
                    return;
                }

                if (OnClientExecute != null)
                {
                    OnClientExecute(args);
                }
                else
                {
                    OnExecute(args);
                }
            }
        }

        private static bool isOpen;
        public static bool IsOpen => isOpen;
        
        private static GUITextBlock activeQuestionText;
        
        private static GUIFrame frame;
        private static GUIListBox listBox;
        private static GUITextBox textBox;

        public static GUITextBox TextBox => textBox;

        public static void Init()
        {
            frame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.45f), GUI.Canvas) { MinSize = new Point(400, 300), AbsoluteOffset = new Point(10, 10) },
                color: new Color(0.4f, 0.4f, 0.4f, 0.8f));

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.01f };

            var toggleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform, Anchor.TopLeft), TextManager.Get("DebugConsoleHelpText"), Color.GreenYellow, GUI.SmallFont, Alignment.CenterLeft, style: null);

            var closeButton = new GUIButton(new RectTransform(new Vector2(0.025f, 1.0f), toggleText.RectTransform, Anchor.TopRight), "X", style: null)
            {
                Color = Color.DarkRed,
                HoverColor = Color.Red,
                TextColor = Color.White,
                OutlineColor = Color.Red
            };
            closeButton.OnClicked += (btn, userdata) =>
            {
                isOpen = false;
                GUI.ForceMouseOn(null);
                textBox.Deselect();
                return true;
            };

            listBox = new GUIListBox(new RectTransform(new Point(paddedFrame.Rect.Width, paddedFrame.Rect.Height - 60), paddedFrame.RectTransform, Anchor.Center)
            {
                IsFixedSize = false
            }, color: Color.Black * 0.9f) { ScrollBarVisible = true };

            textBox = new GUITextBox(new RectTransform(new Point(paddedFrame.Rect.Width, 30), paddedFrame.RectTransform, Anchor.BottomLeft)
            {
                IsFixedSize = false
            });
            textBox.OnKeyHit += (sender, key) =>
            {
                if (key != Keys.Tab)
                {
                    ResetAutoComplete();
                }
            };
        }

        public static void AddToGUIUpdateList()
        {
            if (isOpen)
            {
                frame.AddToGUIUpdateList();
            }
        }

        public static void Update(GameMain game, float deltaTime)
        {
            lock (queuedMessages)
            {
                while (queuedMessages.Count > 0)
                {
                    var newMsg = queuedMessages.Dequeue();
                    AddMessage(newMsg);

                    if (GameSettings.SaveDebugConsoleLogs)
                    {
                        unsavedMessages.Add(newMsg);
                        if (unsavedMessages.Count >= messagesPerFile)
                        {
                            SaveLogs();
                            unsavedMessages.Clear();
                        }
                    }
                }
            }

            activeQuestionText?.SetAsLastChild();

            if (PlayerInput.KeyHit(Keys.F3))
            {
                Toggle();
            }
            else if (isOpen && PlayerInput.KeyHit(Keys.Escape))
            {
                isOpen = false;
                GUI.ForceMouseOn(null);
                textBox.Deselect();
            }

            if (isOpen)
            {
                frame.UpdateManually(deltaTime);

                Character.DisableControls = true;

                if (PlayerInput.KeyHit(Keys.Up))
                {
                    textBox.Text = SelectMessage(-1, textBox.Text);
                }
                else if (PlayerInput.KeyHit(Keys.Down))
                {
                    textBox.Text = SelectMessage(1, textBox.Text);
                }
                else if (PlayerInput.KeyHit(Keys.Tab))
                {
                     textBox.Text = AutoComplete(textBox.Text, increment: string.IsNullOrEmpty(currentAutoCompletedCommand) ? 0 : 1 );
                }

                if (PlayerInput.KeyHit(Keys.Enter))
                {
                    ExecuteCommand(textBox.Text);
                    textBox.Text = "";
                }
            }
        }

        public static void Toggle()
        {
            isOpen = !isOpen;
            if (isOpen)
            {
                textBox.Select();
                AddToGUIUpdateList();
            }
            else
            {
                GUI.ForceMouseOn(null);
                textBox.Deselect();
            }
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (!isOpen) return;

            frame.DrawManually(spriteBatch);
        }

        private static bool IsCommandPermitted(string command, GameClient client)
        {
            switch (command)
            {
                case "kick":
                    return client.HasPermission(ClientPermissions.Kick);
                case "ban":
                case "banip":
                    return client.HasPermission(ClientPermissions.Ban);
                case "unban":
                case "unbanip":
                    return client.HasPermission(ClientPermissions.Unban);
                case "netstats":
                case "help":
                case "dumpids":
                case "admin":
                case "entitylist":
                case "togglehud":
                case "toggleupperhud":
                case "togglecharacternames":
                case "fpscounter":
                    return true;
                default:
                    return client.HasConsoleCommandPermission(command);
            }
        }

        public static void DequeueMessages()
        {
            while (queuedMessages.Count > 0)
            {
                var newMsg = queuedMessages.Dequeue();
                if (listBox == null)
                {
                    //don't attempt to add to the listbox if it hasn't been created yet                    
                    Messages.Add(newMsg);
                }
                else
                {
                    AddMessage(newMsg);
                }

                if (GameSettings.SaveDebugConsoleLogs) unsavedMessages.Add(newMsg);
            }
        }

        private static void AddMessage(ColoredText msg)
        {
            //listbox not created yet, don't attempt to add
            if (listBox == null) return;

            if (listBox.Content.CountChildren > MaxMessages)
            {
                listBox.RemoveChild(listBox.Content.Children.First());
            }

            Messages.Add(msg);
            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }

            try
            {
                var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                    msg.Text, font: GUI.SmallFont, wrap: true)
                {
                    CanBeFocused = false,
                    TextColor = msg.Color
                };
                listBox.UpdateScrollBarSize();
                listBox.BarScroll = 1.0f;
            }
            catch (Exception e)
            {
                ThrowError("Failed to add a message to the debug console.", e);
            }

            selectedIndex = Messages.Count;
        }

        static partial void ShowHelpMessage(Command command)
        {
            if (listBox.Content.CountChildren > MaxMessages)
            {
                listBox.RemoveChild(listBox.Content.Children.First());
            }

            var textContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                style: "InnerFrame", color: Color.White * 0.6f)
            {
                CanBeFocused = false
            };
            var textBlock = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width - 170, 0), textContainer.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(20, 0) },
                command.help, textAlignment: Alignment.TopLeft, font: GUI.SmallFont, wrap: true)
            {
                CanBeFocused = false,
                TextColor = Color.White
            };
            textContainer.RectTransform.NonScaledSize = new Point(textContainer.RectTransform.NonScaledSize.X, textBlock.RectTransform.NonScaledSize.Y + 5);
            textBlock.SetTextPos();
            var nameBlock = new GUITextBlock(new RectTransform(new Point(150, textContainer.Rect.Height), textContainer.RectTransform),
                command.names[0], textAlignment: Alignment.TopLeft);

            listBox.UpdateScrollBarSize();
            listBox.BarScroll = 1.0f;

            selectedIndex = Messages.Count;
        }

        private static void AssignOnClientExecute(string names, Action<string[]> onClientExecute)
        {
            Command command = commands.First(c => c.names.Intersect(names.Split('|')).Count() > 0);
            command.OnClientExecute = onClientExecute;
            command.RelayToServer = false;
        }

        private static void AssignRelayToServer(string names, bool relay)
        {
            commands.First(c => c.names.Intersect(names.Split('|')).Count() > 0).RelayToServer = relay;
        }

        private static void InitProjectSpecific()
        {
#if WINDOWS
            commands.Add(new Command("copyitemnames", "", (string[] args) =>
            {
                StringBuilder sb = new StringBuilder();
                foreach (MapEntityPrefab mp in MapEntityPrefab.List)
                {
                    if (!(mp is ItemPrefab)) continue;
                    sb.AppendLine(mp.Name);
                }
                Clipboard.SetText(sb.ToString());
            }));
#endif

            commands.Add(new Command("autohull", "", (string[] args) =>
            {
                if (Screen.Selected != GameMain.SubEditorScreen) return;

                if (MapEntity.mapEntityList.Any(e => e is Hull || e is Gap))
                {
                    ShowQuestionPrompt("This submarine already has hulls and/or gaps. This command will delete them. Do you want to continue? Y/N",
                        (option) =>
                        {
                            ShowQuestionPrompt("The automatic hull generation may not work correctly if your submarine uses curved walls. Do you want to continue? Y/N",
                                (option2) =>
                                {
                                    if (option2.ToLower() == "y") { GameMain.SubEditorScreen.AutoHull(); }
                                });
                        });
                }
                else
                {
                    ShowQuestionPrompt("The automatic hull generation may not work correctly if your submarine uses curved walls. Do you want to continue? Y/N",
                        (option) => { if (option.ToLower() == "y") GameMain.SubEditorScreen.AutoHull(); });
                }
            }));

            commands.Add(new Command("startclient", "", (string[] args) =>
            {
                if (args.Length == 0) return;

                if (GameMain.Client == null)
                {
                    GameMain.Client = new GameClient("Name", args[0]);
                }
            }));


            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Enabled cheat commands.", Color.Red);
                if (Steam.SteamManager.USE_STEAM)
                {
                    NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
                }
            }));

            commands.Add(new Command("mainmenu|menu", "mainmenu/menu: Go to the main menu.", (string[] args) =>
            {
                GameMain.GameSession = null;

                List<Character> characters = new List<Character>(Character.CharacterList);
                foreach (Character c in characters)
                {
                    c.Remove();
                }

                GameMain.MainMenuScreen.Select();
            }));

            commands.Add(new Command("game", "gamescreen/game: Go to the \"in-game\" view.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    NewMessage("WARNING: Switching directly from the submarine editor to the game view may cause bugs and crashes. Use with caution.", Color.Orange);
                }
                GameMain.GameScreen.Select();
            }));

            commands.Add(new Command("editsubs|subeditor", "editsubs/subeditor: Switch to the Submarine Editor to create or edit submarines.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    Submarine.Load(string.Join(" ", args), true);
                }
                GameMain.SubEditorScreen.Select();
            }));

            commands.Add(new Command("editparticles|particleeditor", "editparticles/particleeditor: Switch to the Particle Editor to edit particle effects.", (string[] args) =>
            {
                GameMain.ParticleEditorScreen.Select();
            }));

            commands.Add(new Command("editlevels|leveleditor", "editlevels/leveleditor: Switch to the Level Editor to edit levels.", (string[] args) =>
            {
                GameMain.LevelEditorScreen.Select();
            }));

            commands.Add(new Command("editsprites|spriteeditor", "editsprites/spriteeditor: Switch to the Sprite Editor to edit the source rects and origins of sprites.", (string[] args) =>
            {
                GameMain.SpriteEditorScreen.Select();
            }));

            commands.Add(new Command("editcharacters|charactereditor", "editcharacters/charactereditor: Switch to the Character Editor to edit/create the ragdolls and animations of characters.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.GameScreen)
                {
                    NewMessage("WARNING: Switching between the character editor and the game view may cause odd behaviour or bugs. Use with caution.", Color.Orange);
                }
                GameMain.CharacterEditorScreen.Select();
            }));

            commands.Add(new Command("money", "", args =>
            {
                if (args.Length == 0) { return; }
                if (GameMain.GameSession.GameMode is CampaignMode campaign)
                {
                    if (int.TryParse(args[0], out int money))
                    {
                        campaign.Money += money;
                    }
                }
            }, isCheat: true));

            AssignRelayToServer("kick", false);
            AssignRelayToServer("kickid", false);
            AssignRelayToServer("ban", false);
            AssignRelayToServer("banid", false);
            AssignRelayToServer("dumpids", false);
            AssignRelayToServer("findentityids", false);
            AssignRelayToServer("campaigninfo", false);
            AssignRelayToServer("help", false);
            AssignRelayToServer("verboselogging", false);
            AssignRelayToServer("freecam", false);
#if DEBUG
            AssignRelayToServer("simulatedlatency", false);
            AssignRelayToServer("simulatedloss", false);
            AssignRelayToServer("simulatedduplicateschance", false);
#endif

            commands.Add(new Command("clientlist", "", (string[] args) => { }));
            AssignRelayToServer("clientlist", true);
            commands.Add(new Command("setmaxplayers|maxplayers", "", (string[] args) => { }));
            AssignRelayToServer("setmaxplayers", true);
            commands.Add(new Command("setpassword|password", "", (string[] args) => { }));
            AssignRelayToServer("setpassword", true);

            AssignOnExecute("control", (string[] args) =>
            {
                if (args.Length < 1) return;
                var character = FindMatchingCharacter(args, true);
                if (character != null)
                {
                    Character.Controlled = character;
                }
            });
            AssignRelayToServer("control", true);

            commands.Add(new Command("shake", "", (string[] args) =>
            {
                GameMain.GameScreen.Cam.Shake = 10.0f;
            }));

            AssignOnExecute("los", (string[] args) =>
             {
                 GameMain.LightManager.LosEnabled = !GameMain.LightManager.LosEnabled;
                 NewMessage("Line of sight effect " + (GameMain.LightManager.LosEnabled ? "enabled" : "disabled"), Color.White);
             });
            AssignRelayToServer("los", false);

            AssignOnExecute("lighting|lights", (string[] args) =>
            {
                GameMain.LightManager.LightingEnabled = !GameMain.LightManager.LightingEnabled;
                NewMessage("Lighting " + (GameMain.LightManager.LightingEnabled ? "enabled" : "disabled"), Color.White);
            });
            AssignRelayToServer("lighting|lights", false);

            AssignOnExecute("ambientlight", (string[] args) =>
            {
                Color color = XMLExtensions.ParseColor(string.Join(",", args));
                if (Level.Loaded != null)
                {
                    Level.Loaded.GenerationParams.AmbientLightColor = color;
                }
                else
                {
                    GameMain.LightManager.AmbientLight = color;
                }
                NewMessage("Set ambient light color to " + color + ".", Color.White);
            });
            AssignRelayToServer("ambientlight", false);

            commands.Add(new Command("multiplylights", "Multiplies the colors of all the static lights in the sub with the given Vector4 value (for example, 1,1,1,0.5).", (string[] args) =>
            {
                if (Screen.Selected != GameMain.SubEditorScreen || args.Length < 1)
                {
                    ThrowError("The multiplylights command can only be used in the submarine editor.");
                }
                if (args.Length < 1) return;

                //join args in case there's spaces between the components
                Vector4 value = XMLExtensions.ParseVector4(string.Join("", args));
                foreach (Item item in Item.ItemList)
                {
                    if (item.ParentInventory != null || item.body != null) continue;
                    var lightComponent = item.GetComponent<LightComponent>();
                    if (lightComponent != null) lightComponent.LightColor =
                        new Color(
                            (lightComponent.LightColor.R / 255.0f) * value.X,
                            (lightComponent.LightColor.G / 255.0f) * value.Y,
                            (lightComponent.LightColor.B / 255.0f) * value.Z,
                            (lightComponent.LightColor.A / 255.0f) * value.W);
                }
            }, isCheat: false));

            commands.Add(new Command("color|colour", "Change color (as bytes from 0 to 255) of the selected item/structure instances. Applied only in the subeditor.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    if (!MapEntity.SelectedAny)
                    {
                        ThrowError("You have to select item(s)/structure(s) first!");
                    }
                    else
                    {
                        if (args.Length < 3)
                        {
                            ThrowError("Not enough arguments provided! At least three required.");
                        }
                        if (!byte.TryParse(args[0], out byte r))
                        {
                            ThrowError($"Failed to parse value for RED from {args[0]}");
                        }
                        if (!byte.TryParse(args[1], out byte g))
                        {
                            ThrowError($"Failed to parse value for GREEN from {args[1]}");
                        }
                        if (!byte.TryParse(args[2], out byte b))
                        {
                            ThrowError($"Failed to parse value for BLUE from {args[2]}");
                        }
                        Color color = new Color(r, g, b);
                        if (args.Length > 3)
                        {
                            if (!byte.TryParse(args[3], out byte a))
                            {
                                ThrowError($"Failed to parse value for ALPHA from {args[3]}");
                            }
                            else
                            {
                                color.A = a;
                            }
                        }
                        foreach (var mapEntity in MapEntity.SelectedList)
                        {
                            if (mapEntity is Structure s)
                            {
                                s.SpriteColor = color;
                            }
                            else if (mapEntity is Item i)
                            {
                                i.SpriteColor = color;
                            }
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("resetall", "Reset all items and structures to prefabs. Only applicable in the subeditor.", args =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    Item.ItemList.ForEach(i => i.Reset());
                    Structure.WallList.ForEach(s => s.Reset());
                    foreach (MapEntity entity in MapEntity.SelectedList)
                    {
                        if (entity is Item item)
                        {
                            item.CreateEditingHUD();
                            break;
                        }
                        else if (entity is Structure structure)
                        {
                            structure.CreateEditingHUD();
                            break;
                        }
                    }
                }
            }));

            commands.Add(new Command("resetselected", "Reset selected items and structures to prefabs. Only applicable in the subeditor.", args =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    foreach (MapEntity entity in MapEntity.SelectedList)
                    {
                        if (entity is Item item)
                        {
                            item.Reset();
                        }
                        else if (entity is Structure structure)
                        {
                            structure.Reset();
                        }
                    }
                    foreach (MapEntity entity in MapEntity.SelectedList)
                    {
                        if (entity is Item item)
                        {
                            item.CreateEditingHUD();
                            break;
                        }
                        else if (entity is Structure structure)
                        {
                            structure.CreateEditingHUD();
                            break;
                        }
                    }
                }
            }));

            commands.Add(new Command("alpha", "Change the alpha (as bytes from 0 to 255) of the selected item/structure instances. Applied only in the subeditor.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    if (!MapEntity.SelectedAny)
                    {
                        ThrowError("You have to select item(s)/structure(s) first!");
                    }
                    else
                    {
                        if (args.Length > 0)
                        {
                            if (!byte.TryParse(args[0], out byte a))
                            {
                                ThrowError($"Failed to parse value for ALPHA from {args[0]}");
                            }
                            else
                            {
                                foreach (var mapEntity in MapEntity.SelectedList)
                                {
                                    if (mapEntity is Structure s)
                                    {
                                        s.SpriteColor = new Color(s.SpriteColor.R, s.SpriteColor.G, s.SpriteColor.G, a);
                                    }
                                    else if (mapEntity is Item i)
                                    {
                                        i.SpriteColor = new Color(i.SpriteColor.R, i.SpriteColor.G, i.SpriteColor.G, a);
                                    }
                                }
                            }
                        }
                        else
                        {
                            ThrowError("Not enough arguments provided! One required!");
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("tutorial", "", (string[] args) =>
            {
                TutorialMode.StartTutorial(Tutorials.Tutorial.Tutorials[0]);
            }));

            commands.Add(new Command("lobby|lobbyscreen", "", (string[] args) =>
            {
                GameMain.LobbyScreen.Select();
            }));

            commands.Add(new Command("save|savesub", "save [submarine name]: Save the currently loaded submarine using the specified name.", (string[] args) =>
            {
                if (args.Length < 1) return;

                if (GameMain.SubEditorScreen.CharacterMode)
                {
                    GameMain.SubEditorScreen.SetCharacterMode(false);
                }

                string fileName = string.Join(" ", args);
                if (fileName.Contains("../"))
                {
                    ThrowError("Illegal symbols in filename (../)");
                    return;
                }

                if (Submarine.SaveCurrent(System.IO.Path.Combine(Submarine.SavePath, fileName + ".sub")))
                {
                    NewMessage("Sub saved", Color.Green);
                }
            }));

            commands.Add(new Command("load|loadsub", "load [submarine name]: Load a submarine.", (string[] args) =>
            {
                if (args.Length == 0) return;
                Submarine.Load(string.Join(" ", args), true);
            }));

            commands.Add(new Command("cleansub", "", (string[] args) =>
            {
                for (int i = MapEntity.mapEntityList.Count - 1; i >= 0; i--)
                {
                    MapEntity me = MapEntity.mapEntityList[i];

                    if (me.SimPosition.Length() > 2000.0f)
                    {
                        NewMessage("Removed " + me.Name + " (simposition " + me.SimPosition + ")", Color.Orange);
                        MapEntity.mapEntityList.RemoveAt(i);
                    }
                    else if (!me.ShouldBeSaved)
                    {
                        NewMessage("Removed " + me.Name + " (!ShouldBeSaved)", Color.Orange);
                        MapEntity.mapEntityList.RemoveAt(i);
                    }
                    else if (me is Item)
                    {
                        Item item = me as Item;
                        var wire = item.GetComponent<Wire>();
                        if (wire == null) continue;

                        if (wire.GetNodes().Count > 0 && !wire.Connections.Any(c => c != null))
                        {
                            wire.Item.Drop(null);
                            NewMessage("Dropped wire (ID: " + wire.Item.ID + ") - attached on wall but no connections found", Color.Orange);
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("messagebox", "", (string[] args) =>
            {
                new GUIMessageBox("", string.Join(" ", args));
            }));

            AssignOnExecute("debugdraw", (string[] args) =>
            {
                GameMain.DebugDraw = !GameMain.DebugDraw;
                NewMessage("Debug draw mode " + (GameMain.DebugDraw ? "enabled" : "disabled"), Color.White);
            });
            AssignRelayToServer("debugdraw", false);

            commands.Add(new Command("fpscounter", "fpscounter: Toggle the FPS counter.", (string[] args) =>
            {
                GameMain.ShowFPS = !GameMain.ShowFPS;
                NewMessage("FPS counter " + (GameMain.DebugDraw ? "enabled" : "disabled"), Color.White);
            }));
            commands.Add(new Command("showperf", "showperf: Toggle performance statistics on/off.", (string[] args) =>
            {
                GameMain.ShowPerf = !GameMain.ShowPerf;
                NewMessage("Performance statistics " + (GameMain.ShowPerf ? "enabled" : "disabled"), Color.White);
            }));

            AssignOnClientExecute("netstats", (string[] args) =>
            {
                if (GameMain.Client == null) return;
                GameMain.Client.ShowNetStats = !GameMain.Client.ShowNetStats;
            });

            commands.Add(new Command("hudlayoutdebugdraw|debugdrawhudlayout", "hudlayoutdebugdraw: Toggle the debug drawing mode of HUD layout areas on/off.", (string[] args) =>
            {
                HUDLayoutSettings.DebugDraw = !HUDLayoutSettings.DebugDraw;
                NewMessage("HUD layout debug draw mode " + (HUDLayoutSettings.DebugDraw ? "enabled" : "disabled"), Color.White);
            }));

            commands.Add(new Command("interactdebugdraw|debugdrawinteract", "interactdebugdraw: Toggle the debug drawing mode of item interaction ranges on/off.", (string[] args) =>
            {
                Character.DebugDrawInteract = !Character.DebugDrawInteract;
                NewMessage("Interact debug draw mode " + (Character.DebugDrawInteract ? "enabled" : "disabled"), Color.White);
            }, isCheat: true));

            AssignOnExecute("togglehud|hud", (string[] args) =>
            {
                GUI.DisableHUD = !GUI.DisableHUD;
                GameMain.Instance.IsMouseVisible = !GameMain.Instance.IsMouseVisible;
                NewMessage(GUI.DisableHUD ? "Disabled HUD" : "Enabled HUD", Color.White);
            });
            AssignRelayToServer("togglehud|hud", false);

            AssignOnExecute("toggleupperhud", (string[] args) =>
            {
                GUI.DisableUpperHUD = !GUI.DisableUpperHUD;
                NewMessage(GUI.DisableUpperHUD ? "Disabled upper HUD" : "Enabled upper HUD", Color.White);
            });
            AssignRelayToServer("toggleupperhud", false);

            AssignOnExecute("toggleitemhighlights", (string[] args) =>
            {
                GUI.DisableItemHighlights = !GUI.DisableItemHighlights;
                NewMessage(GUI.DisableItemHighlights ? "Disabled item highlights" : "Enabled item highlights", Color.White);
            });
            AssignRelayToServer("toggleitemhighlights", false);

            AssignOnExecute("togglecharacternames", (string[] args) =>
            {
                GUI.DisableCharacterNames = !GUI.DisableCharacterNames;
                NewMessage(GUI.DisableCharacterNames ? "Disabled character names" : "Enabled character names", Color.White);
            });
            AssignRelayToServer("togglecharacternames", false);

            AssignOnExecute("followsub", (string[] args) =>
            {
                Camera.FollowSub = !Camera.FollowSub;
                NewMessage(Camera.FollowSub ? "Set the camera to follow the closest submarine" : "Disabled submarine following.", Color.White);
            });
            AssignRelayToServer("followsub", false);

            AssignOnExecute("toggleaitargets|aitargets", (string[] args) =>
            {
                AITarget.ShowAITargets = !AITarget.ShowAITargets;
                NewMessage(AITarget.ShowAITargets ? "Enabled AI target drawing" : "Disabled AI target drawing", Color.White);
            });
            AssignRelayToServer("toggleaitargets|aitargets", false);

            AssignRelayToServer("water|editwater", false);
            AssignRelayToServer("fire|editfire", false);

            commands.Add(new Command("mute", "mute [name]: Prevent the client from speaking to anyone through the voice chat. Using this command requires a permission from the server host.",
            null,
            () =>
            {
                if (GameMain.Client == null) return null;
                return new string[][]
                {
                    GameMain.Client.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));
            commands.Add(new Command("unmute", "unmute [name]: Allow the client to speak to anyone through the voice chat. Using this command requires a permission from the server host.",
            null,
            () =>
            {
                if (GameMain.Client == null) return null;
                return new string[][]
                {
                    GameMain.Client.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));

            commands.Add(new Command("checkcrafting", "checkcrafting: Checks item deconstruction & crafting recipes for inconsistencies.", (string[] args) =>
            {
                List<FabricationRecipe> fabricableItems = new List<FabricationRecipe>();
                foreach (MapEntityPrefab mapEntityPrefab in MapEntityPrefab.List)
                {
                    if (mapEntityPrefab is ItemPrefab itemPrefab)
                    {
                        fabricableItems.AddRange(itemPrefab.FabricationRecipes);
                    }
                }
                foreach (MapEntityPrefab mapEntityPrefab in MapEntityPrefab.List)
                {
                    if (mapEntityPrefab is ItemPrefab itemPrefab)
                    {
                        int? minCost = itemPrefab.GetPrices()?.Min(p => p.BuyPrice);
                        int? fabricationCost = null;
                        int? deconstructProductCost = null;

                        var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == itemPrefab);
                        if (fabricationRecipe != null)
                        {
                            foreach (var ingredient in fabricationRecipe.RequiredItems)
                            {
                                int? ingredientPrice = ingredient.ItemPrefab.GetPrices()?.Min(p => p.BuyPrice);
                                if (ingredientPrice.HasValue)
                                {
                                    if (!fabricationCost.HasValue) { fabricationCost = 0; }
                                    float useAmount = ingredient.UseCondition ? ingredient.MinCondition : 1.0f;
                                    fabricationCost += (int)(ingredientPrice.Value * ingredient.Amount * useAmount);
                                }
                            }
                        }

                        foreach (var deconstructItem in itemPrefab.DeconstructItems)
                        {
                            var targetItem = MapEntityPrefab.Find(null, deconstructItem.ItemIdentifier, showErrorMessages: false) as ItemPrefab;
                            if (targetItem == null)
                            {
                                ThrowError("Error in item \"" + itemPrefab.Name + "\" - could not find deconstruct item \"" + deconstructItem.ItemIdentifier + "\"!");
                                continue;
                            }

                            int? deconstructProductPrice = targetItem.GetPrices()?.Min(p => p.BuyPrice);
                            if (deconstructProductPrice.HasValue)
                            {
                                if (!deconstructProductCost.HasValue) { deconstructProductCost = 0; }
                                deconstructProductCost += (int)(deconstructProductPrice * deconstructItem.OutCondition);
                            }

                            if (fabricationRecipe != null)
                            {
                                if (!fabricationRecipe.RequiredItems.Any(r => r.ItemPrefab == targetItem))
                                {
                                    NewMessage("Deconstructing \"" + itemPrefab.Name + "\" produces \"" + deconstructItem.ItemIdentifier + "\", which isn't required in the fabrication recipe of the item.", Color.Orange);
                                }
                            }
                        }

                        if (fabricationCost.HasValue && minCost.HasValue)
                        {
                            if (fabricationCost.Value < minCost * 0.9f)
                            {
                                float ratio = (float)fabricationCost.Value / minCost.Value;
                                Color color = ToolBox.GradientLerp(ratio, Color.Red, Color.Yellow, Color.Green);
                                NewMessage("The fabrication ingredients of \"" + itemPrefab.Name + "\" only cost " + (int)(ratio * 100) + "% of the price of the item. Item price: " + minCost.Value + ", ingredient prices: " + fabricationCost.Value, color);
                            }
                            else if (fabricationCost.Value > minCost * 1.1f)
                            {
                                float ratio = (float)fabricationCost.Value / minCost.Value;
                                Color color = ToolBox.GradientLerp(ratio - 1.0f, Color.Green, Color.Yellow, Color.Red);
                                NewMessage("The fabrication ingredients of \"" + itemPrefab.Name + "\" cost " + (int)(ratio * 100 - 100) + "% more than the price of the item. Item price: " + minCost.Value + ", ingredient prices: " + fabricationCost.Value, color);
                            }
                        }
                        if (deconstructProductCost.HasValue && minCost.HasValue)
                        {
                            if (deconstructProductCost.Value < minCost * 0.8f)
                            {
                                float ratio = (float)deconstructProductCost.Value / minCost.Value;
                                Color color = ToolBox.GradientLerp(ratio, Color.Red, Color.Yellow, Color.Green);
                                NewMessage("The deconstruction output of \"" + itemPrefab.Name + "\" is only worth " + (int)(ratio * 100) + "% of the price of the item. Item price: " + minCost.Value + ", output value: " + deconstructProductCost.Value, color);
                            }
                            else if (deconstructProductCost.Value > minCost * 1.1f)
                            {
                                float ratio = (float)deconstructProductCost.Value / minCost.Value;
                                Color color = ToolBox.GradientLerp(ratio - 1.0f, Color.Green, Color.Yellow, Color.Red);
                                NewMessage("The deconstruction output of \"" + itemPrefab.Name + "\" is worth " + (int)(ratio * 100 - 100) + "% more than the price of the item. Item price: " + minCost.Value + ", output value: " + deconstructProductCost.Value, color);
                            }
                        }
                    }
                }
            }, isCheat: false));

#if DEBUG
            commands.Add(new Command("checkmissingloca", "", (string[] args) =>
            {
                foreach (MapEntityPrefab me in MapEntityPrefab.List)
                {
                    string name = TextManager.Get("entityname." + me.Identifier, returnNull: true);
                    if (!string.IsNullOrEmpty(name)) { continue; }

                    if (me is ItemPrefab itemPrefab)
                    {
                        string nameIdentifier = itemPrefab.ConfigElement?.GetAttributeString("nameidentifier", "");
                        if (nameIdentifier != null)
                        {
                            name = TextManager.Get("entityname." + nameIdentifier, returnNull: true);
                            if (!string.IsNullOrEmpty(name)) { continue; }
                        }
                    }
                    NewMessage("Entity name not translated (" + me.Name + ", " + me.Identifier + ")!", me is ItemPrefab ? Color.Red : Color.Yellow);
                }
            }));

            commands.Add(new Command("spamchatmessages", "", (string[] args) =>
            {
                int msgCount = 1000;
                if (args.Length > 0) int.TryParse(args[0], out msgCount);
                int msgLength = 50;
                if (args.Length > 1) int.TryParse(args[1], out msgLength);

                for (int i = 0; i < msgCount; i++)
                {
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(ToolBox.RandomSeed(msgLength));
                    }
                }
            }));

            commands.Add(new Command("camerasettings", "camerasettings [defaultzoom] [zoomsmoothness] [movesmoothness] [minzoom] [maxzoom]: debug command for testing camera settings. The values default to 1.1, 8.0, 8.0, 0.1 and 2.0.", (string[] args) =>
            {
                float defaultZoom = Screen.Selected.Cam.DefaultZoom;
                if (args.Length > 0) float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out defaultZoom);

                float zoomSmoothness = Screen.Selected.Cam.ZoomSmoothness;
                if (args.Length > 1) float.TryParse(args[1], NumberStyles.Number, CultureInfo.InvariantCulture, out zoomSmoothness);
                float moveSmoothness = Screen.Selected.Cam.MoveSmoothness;
                if (args.Length > 2) float.TryParse(args[2], NumberStyles.Number, CultureInfo.InvariantCulture, out moveSmoothness);

                float minZoom = Screen.Selected.Cam.MinZoom;
                if (args.Length > 3) float.TryParse(args[3], NumberStyles.Number, CultureInfo.InvariantCulture, out minZoom);
                float maxZoom = Screen.Selected.Cam.MaxZoom;
                if (args.Length > 4) float.TryParse(args[4], NumberStyles.Number, CultureInfo.InvariantCulture, out maxZoom);

                Screen.Selected.Cam.DefaultZoom = defaultZoom;
                Screen.Selected.Cam.ZoomSmoothness = zoomSmoothness;
                Screen.Selected.Cam.MoveSmoothness = moveSmoothness;
                Screen.Selected.Cam.MinZoom = minZoom;
                Screen.Selected.Cam.MaxZoom = maxZoom;
            }));

            commands.Add(new Command("waterparams", "waterparams [distortionscalex] [distortionscaley] [distortionstrengthx] [distortionstrengthy] [bluramount]: default 0.5 0.5 0.5 0.5 1", (string[] args) =>
            {
                float distortScaleX = 0.5f, distortScaleY = 0.5f;
                float distortStrengthX = 0.5f, distortStrengthY = 0.5f;
                float blurAmount = 0.0f;
                if (args.Length > 0) float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out distortScaleX);
                if (args.Length > 1) float.TryParse(args[1], NumberStyles.Number, CultureInfo.InvariantCulture, out distortScaleY);
                if (args.Length > 2) float.TryParse(args[2], NumberStyles.Number, CultureInfo.InvariantCulture, out distortStrengthX);
                if (args.Length > 3) float.TryParse(args[3], NumberStyles.Number, CultureInfo.InvariantCulture, out distortStrengthY);
                if (args.Length > 4) float.TryParse(args[4], NumberStyles.Number, CultureInfo.InvariantCulture, out blurAmount);
                WaterRenderer.DistortionScale = new Vector2(distortScaleX, distortScaleY);
                WaterRenderer.DistortionStrength = new Vector2(distortStrengthX, distortStrengthY);
                WaterRenderer.BlurAmount = blurAmount;
            }));


            commands.Add(new Command("refreshrect", "Updates the dimensions of the selected items to match the ones defined in the prefab. Applied only in the subeditor.", (string[] args) =>
            {
                //TODO: maybe do this automatically during loading when possible?
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    if (!MapEntity.SelectedAny)
                    {
                        ThrowError("You have to select item(s) first!");
                    }
                    else
                    {
                        foreach (var mapEntity in MapEntity.SelectedList)
                        {
                            if (mapEntity is Item item)
                            {
                                item.Rect = new Rectangle(item.Rect.X, item.Rect.Y,
                                    (int)(item.Prefab.sprite.size.X * item.Prefab.Scale),
                                    (int)(item.Prefab.sprite.size.Y * item.Prefab.Scale));
                            }
                            else if (mapEntity is Structure structure)
                            {
                                if (!structure.ResizeHorizontal)
                                {
                                    structure.Rect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                        (int)structure.Prefab.ScaledSize.X,
                                        structure.Rect.Height);
                                }
                                if (!structure.ResizeVertical)
                                {
                                    structure.Rect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                        structure.Rect.Width,
                                        (int)structure.Prefab.ScaledSize.Y);
                                }
                            }
                        }
                    }
                }
            }, isCheat: false));
#endif

            commands.Add(new Command("dumptexts", "dumptexts [filepath]: Extracts all the texts from the given text xml and writes them into a file (using the same filename, but with the .txt extension). If the filepath is omitted, the EnglishVanilla.xml file is used.", (string[] args) =>
            {
                string filePath = args.Length > 0 ? args[0] : "Content/Texts/EnglishVanilla.xml";
                var doc = XMLExtensions.TryLoadXml(filePath);
                if (doc?.Root == null) return;
                List<string> lines = new List<string>();
                foreach (XElement element in doc.Root.Elements())
                {
                    lines.Add(element.ElementInnerText());
                }
                File.WriteAllLines(Path.GetFileNameWithoutExtension(filePath) + ".txt", lines);
            },
            () =>
            {
                var files = TextManager.GetTextFiles().Select(f => f.Replace("\\", "/"));
                return new string[][]
                {
                    TextManager.GetTextFiles().Where(f => Path.GetExtension(f)==".xml").ToArray()
                };
            }));

            commands.Add(new Command("loadtexts", "loadtexts [sourcefile] [destinationfile]: Loads all lines of text from a given .txt file and inserts them sequientially into the elements of an xml file. If the file paths are omitted, EnglishVanilla.txt and EnglishVanilla.xml are used.", (string[] args) =>
            {
                string sourcePath = args.Length > 0 ? args[0] : "Content/Texts/EnglishVanilla.txt";
                string destinationPath = args.Length > 1 ? args[1] : "Content/Texts/EnglishVanilla.xml";

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(sourcePath);
                }
                catch (Exception e)
                {
                    ThrowError("Reading the file \"" + sourcePath + "\" failed.", e);
                    return;
                }
                var doc = XMLExtensions.TryLoadXml(destinationPath);
                int i = 0;
                foreach (XElement element in doc.Root.Elements())
                {
                    if (i >= lines.Length)
                    {
                        ThrowError("Error while loading texts to the xml file. The xml has more elements than the number of lines in the text file.");
                        return;
                    }
                    element.Value = lines[i];
                    i++;
                }
                doc.Save(destinationPath);
            },
            () =>
            {
                var files = TextManager.GetTextFiles().Select(f => f.Replace("\\", "/"));
                return new string[][]
                {
                    files.Where(f => Path.GetExtension(f)==".txt").ToArray(),
                    files.Where(f => Path.GetExtension(f)==".xml").ToArray()
                };
            }));

            commands.Add(new Command("updatetextfile", "updatetextfile [sourcefile] [destinationfile]: Inserts all the xml elements that are only present in the source file into the destination file. Can be used to update outdated translation files more easily.", (string[] args) =>
            {
                if (args.Length < 2) return;
                string sourcePath = args[0];
                string destinationPath = args[1];

                var sourceDoc = XMLExtensions.TryLoadXml(sourcePath);
                var destinationDoc = XMLExtensions.TryLoadXml(destinationPath);

                XElement destinationElement = destinationDoc.Root.Elements().First();
                foreach (XElement element in sourceDoc.Root.Elements())
                {
                    if (destinationDoc.Root.Element(element.Name) == null)
                    {
                        element.Value = "!!!!!!!!!!!!!" + element.Value;
                        destinationElement.AddAfterSelf(element);
                    }
                    XNode nextNode = destinationElement.NextNode;
                    while ((!(nextNode is XElement) || nextNode == element) && nextNode != null) nextNode = nextNode.NextNode;
                    destinationElement = nextNode as XElement;
                }
                destinationDoc.Save(destinationPath);
            },
            () =>
            {
                var files = TextManager.GetTextFiles().Where(f => Path.GetExtension(f) == ".xml").Select(f => f.Replace("\\", "/")).ToArray();
                return new string[][]
                {
                    files,
                    files
                };
            }));

            commands.Add(new Command("dumpentitytexts", "dumpentitytexts [filepath]: gets the names and descriptions of all entity prefabs and writes them into a file along with xml tags that can be used in translation files. If the filepath is omitted, the file is written to Content/Texts/EntityTexts.txt", (string[] args) =>
            {
                string filePath = args.Length > 0 ? args[0] : "Content/Texts/EntityTexts.txt";
                List<string> lines = new List<string>();
                foreach (MapEntityPrefab me in MapEntityPrefab.List)
                {
                    lines.Add("<EntityName." + me.Identifier + ">" + me.Name + "</EntityName." + me.Identifier + ">");
                    lines.Add("<EntityDescription." + me.Identifier + ">" + me.Description + "</EntityDescription." + me.Identifier + ">");
                }
                File.WriteAllLines(filePath, lines);
            }));
#if DEBUG
            commands.Add(new Command("checkduplicates", "Checks the given language for duplicate translation keys and writes to file.", (string[] args) =>
            {
                if (args.Length != 1) return;
                TextManager.CheckForDuplicates(args[0]);
            }));

            commands.Add(new Command("writetocsv", "Writes the default language (English) to a .csv file.", (string[] args) =>
            {
                TextManager.WriteToCSV();
                NPCConversation.WriteToCSV();
            }));

            commands.Add(new Command("csvtoxml", "csvtoxml [language] -> Converts .csv localization files in Content/NPCConversations & Content/Texts to .xml for use in-game.", (string[] args) =>
            {
                if (args.Length == 0) return;
                LocalizationCSVtoXML.Convert(args[0]);
            }));

            commands.Add(new Command("guimessagebox", "guimessagebox [msg] -> Creates a message box with the parameter as a message.", (string[] args) =>
            {
                if (args.Length == 0) return;
                var dialog = new GUIMessageBox("Message box", args[0]);
            }));
#endif

            commands.Add(new Command("cleanbuild", "", (string[] args) =>
            {
                GameMain.Config.MusicVolume = 0.5f;
                GameMain.Config.SoundVolume = 0.5f;
                NewMessage("Music and sound volume set to 0.5", Color.Green);

                GameMain.Config.GraphicsWidth = 0;
                GameMain.Config.GraphicsHeight = 0;
                GameMain.Config.WindowMode = WindowMode.Fullscreen;
                NewMessage("Resolution set to 0 x 0 (screen resolution will be used)", Color.Green);
                NewMessage("Fullscreen enabled", Color.Green);

                GameSettings.ShowUserStatisticsPrompt = true;

                GameSettings.VerboseLogging = false;

                if (GameMain.Config.MasterServerUrl != "http://www.undertowgames.com/baromaster")
                {
                    ThrowError("MasterServerUrl \"" + GameMain.Config.MasterServerUrl + "\"!");
                }

                GameMain.Config.SaveNewPlayerConfig();

                var saveFiles = System.IO.Directory.GetFiles(SaveUtil.SaveFolder);

                foreach (string saveFile in saveFiles)
                {
                    System.IO.File.Delete(saveFile);
                    NewMessage("Deleted " + saveFile, Color.Green);
                }

                if (System.IO.Directory.Exists(System.IO.Path.Combine(SaveUtil.SaveFolder, "temp")))
                {
                    System.IO.Directory.Delete(System.IO.Path.Combine(SaveUtil.SaveFolder, "temp"), true);
                    NewMessage("Deleted temp save folder", Color.Green);
                }

                if (System.IO.Directory.Exists(ServerLog.SavePath))
                {
                    var logFiles = System.IO.Directory.GetFiles(ServerLog.SavePath);

                    foreach (string logFile in logFiles)
                    {
                        System.IO.File.Delete(logFile);
                        NewMessage("Deleted " + logFile, Color.Green);
                    }
                }

                if (System.IO.File.Exists("filelist.xml"))
                {
                    System.IO.File.Delete("filelist.xml");
                    NewMessage("Deleted filelist", Color.Green);
                }

                if (System.IO.File.Exists("Data/bannedplayers.txt"))
                {
                    System.IO.File.Delete("Data/bannedplayers.txt");
                    NewMessage("Deleted bannedplayers.txt", Color.Green);
                }

                if (System.IO.File.Exists("Submarines/TutorialSub.sub"))
                {
                    System.IO.File.Delete("Submarines/TutorialSub.sub");

                    NewMessage("Deleted TutorialSub from the submarine folder", Color.Green);
                }

                /*if (System.IO.File.Exists(GameServer.SettingsFile))
                {
                    System.IO.File.Delete(GameServer.SettingsFile);
                    NewMessage("Deleted server settings", Color.Green);
                }

                if (System.IO.File.Exists(GameServer.ClientPermissionsFile))
                {
                    System.IO.File.Delete(GameServer.ClientPermissionsFile);
                    NewMessage("Deleted client permission file", Color.Green);
                }*/

                if (System.IO.File.Exists("crashreport.log"))
                {
                    System.IO.File.Delete("crashreport.log");
                    NewMessage("Deleted crashreport.log", Color.Green);
                }

                if (!System.IO.File.Exists("Content/Map/TutorialSub.sub"))
                {
                    ThrowError("TutorialSub.sub not found!");
                }
            }));
            
            AssignOnClientExecute(
                "giveperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }
                    ShowQuestionPrompt("Permission to grant to client #" + id + "?", (perm) =>
                    {
                        GameMain.Client.SendConsoleCommand("giveperm " + id + " " + perm);
                    });
                }
            );

            AssignOnClientExecute(
                "revokeperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }

                    ShowQuestionPrompt("Permission to revoke from client #" + id + "?", (perm) =>
                    {
                        GameMain.Client.SendConsoleCommand("revokeperm " + id + " " + perm);
                    });
                }
            );

            AssignOnClientExecute(
                "giverank",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    NewMessage("Valid ranks are:", Color.White);
                    foreach (PermissionPreset permissionPreset in PermissionPreset.List)
                    {
                        NewMessage(" - " + permissionPreset.Name, Color.White);
                    }
                    ShowQuestionPrompt("Rank to grant to client #" + id + "?", (rank) =>
                    {
                        GameMain.Client.SendConsoleCommand("giverank " + id + " " + rank);
                    });
                }
            );

            AssignOnClientExecute(
                "givecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    ShowQuestionPrompt("Console command permissions to grant to client #" + id + "? You may enter multiple commands separated with a space.", (commandNames) =>
                    {
                        GameMain.Client.SendConsoleCommand("givecommandperm " + id + " " + commandNames);
                    });
                }
            );

            AssignOnClientExecute(
                "revokecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    ShowQuestionPrompt("Console command permissions to revoke from client #" + id + "? You may enter multiple commands separated with a space.", (commandNames) =>
                    {
                        GameMain.Client.SendConsoleCommand("revokecommandperm " + id + " " + commandNames);
                    });
                }
            );

            AssignOnClientExecute(
                "showperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    if (!int.TryParse(args[0], out int id))
                    {
                        ThrowError("\"" + id + "\" is not a valid client ID.");
                        return;
                    }

                    GameMain.Client.SendConsoleCommand("showperm " + id);
                }
            );

            AssignOnClientExecute(
                "banip",
                (string[] args) =>
                {
                    if (GameMain.Client == null || args.Length == 0) return;
                    ShowQuestionPrompt("Reason for banning the ip \"" + args[0] + "\"?", (reason) =>
                    {
                        ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\")", (duration) =>
                        {
                            TimeSpan? banDuration = null;
                            if (!string.IsNullOrWhiteSpace(duration))
                            {
                                if (!TryParseTimeSpan(duration, out TimeSpan parsedBanDuration))
                                {
                                    ThrowError("\"" + duration + "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                    return;
                                }
                                banDuration = parsedBanDuration;
                            }

                            GameMain.Client.SendConsoleCommand(
                                "banip " +
                                args[0] + " " +
                                (banDuration.HasValue ? banDuration.Value.TotalSeconds.ToString() : "0") + " " +
                                reason);
                        });
                    });
                }
            );

            commands.Add(new Command("unban", "unban [name]: Unban a specific client.", (string[] args) =>
            {
                if (GameMain.Client == null || args.Length == 0) return;
                string clientName = string.Join(" ", args);
                GameMain.Client.UnbanPlayer(clientName, "");
            }));

            commands.Add(new Command("unbanip", "unbanip [ip]: Unban a specific IP.", (string[] args) =>
            {
                if (GameMain.Client == null || args.Length == 0) return;
                GameMain.Client.UnbanPlayer("", args[0]);
            }));

            AssignOnClientExecute(
                "campaigndestination|setcampaigndestination",
                (string[] args) =>
                {
                    var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                    if (campaign == null)
                    {
                        ThrowError("No campaign active!");
                        return;
                    }

                    if (args.Length == 0)
                    {
                        int i = 0;
                        foreach (LocationConnection connection in campaign.Map.CurrentLocation.Connections)
                        {
                            NewMessage("     " + i + ". " + connection.OtherLocation(campaign.Map.CurrentLocation).Name, Color.White);
                            i++;
                        }
                        ShowQuestionPrompt("Select a destination (0 - " + (campaign.Map.CurrentLocation.Connections.Count - 1) + "):", (string selectedDestination) =>
                        {
                            int destinationIndex = -1;
                            if (!int.TryParse(selectedDestination, out destinationIndex)) return;
                            if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                            {
                                NewMessage("Index out of bounds!", Color.Red);
                                return;
                            }
                            GameMain.Client.SendConsoleCommand("campaigndestination " + destinationIndex);
                        });
                    }
                    else
                    {
                        int destinationIndex = -1;
                        if (!int.TryParse(args[0], out destinationIndex)) return;
                        if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                        {
                            NewMessage("Index out of bounds!", Color.Red);
                            return;
                        }
                        GameMain.Client.SendConsoleCommand("campaigndestination " + destinationIndex);
                    }
                }
            );

            commands.Add(new Command("limbscale", "Define the limbscale for the controlled character. Provide id or name if you want to target another character. Note: the changes are not saved!", (string[] args) =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please give the value after the command.");
                    return;
                }
                if (!float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out float value))
                {
                    ThrowError("Failed to parse float value from the arguments");
                    return;
                }
                RagdollParams ragdollParams = character.AnimController.RagdollParams;
                ragdollParams.LimbScale = MathHelper.Clamp(value, RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE);
                var pos = character.WorldPosition;
                character.AnimController.Recreate();
                character.TeleportTo(pos);
            }, isCheat: true));

            commands.Add(new Command("jointscale", "Define the jointscale for the controlled character. Provide id or name if you want to target another character. Note: the changes are not saved!", (string[] args) =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please give the value after the command.");
                    return;
                }
                if (!float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out float value))
                {
                    ThrowError("Failed to parse float value from the arguments");
                    return;
                }
                RagdollParams ragdollParams = character.AnimController.RagdollParams;
                ragdollParams.JointScale = MathHelper.Clamp(value, RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE);
                var pos = character.WorldPosition;
                character.AnimController.Recreate();
                character.TeleportTo(pos);
            }, isCheat: true));

            commands.Add(new Command("ragdollscale", "Rescale the ragdoll of the controlled character. Provide id or name if you want to target another character. Note: the changes are not saved!", (string[] args) =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please give the value after the command.");
                    return;
                }
                if (!float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out float value))
                {
                    ThrowError("Failed to parse float value from the arguments");
                    return;
                }
                RagdollParams ragdollParams = character.AnimController.RagdollParams;
                ragdollParams.LimbScale = MathHelper.Clamp(value, RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE);
                ragdollParams.JointScale = MathHelper.Clamp(value, RagdollParams.MIN_SCALE, RagdollParams.MAX_SCALE);
                var pos = character.WorldPosition;
                character.AnimController.Recreate();
                character.TeleportTo(pos);
            }, isCheat: true));

            commands.Add(new Command("recreateragdoll", "Recreate the ragdoll of the controlled character. Provide id or name if you want to target another character.", (string[] args) =>
            {
                var character = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, true);
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                var pos = character.WorldPosition;
                character.AnimController.Recreate();
                character.TeleportTo(pos);
            }, isCheat: true));

            commands.Add(new Command("resetragdoll", "Reset the ragdoll of the controlled character. Provide id or name if you want to target another character.", (string[] args) =>
            {
                var character = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, true);
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                character.AnimController.ResetRagdoll();
            }, isCheat: true));

            commands.Add(new Command("reloadwearables", "Reloads the sprites of all limbs and wearable sprites (clothing) of the controlled character. Provide id or name if you want to target another character.", args =>
            {
                var character = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, true);
                if (character == null)
                {
                    ThrowError("Not controlling any character or no matching character found with the provided arguments.");
                    return;
                }
                ReloadWearables(character);
            }, isCheat: true));

            commands.Add(new Command("loadwearable", "Force select certain variant for the selected character.", args =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character.");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("No arguments provided! Give an index number for the variant starting from 1.");
                    return;
                }
                if (int.TryParse(args[0], out int variant))
                {
                    ReloadWearables(character, variant);
                }
                
            }, isCheat: true));

            commands.Add(new Command("reloadsprite|reloadsprites", "Reloads the sprites of the selected item(s)/structure(s) (hovering over or selecting in the subeditor) or the controlled character. Can also reload sprites by entity id or by the name attribute (sprite element). Example 1: reloadsprite id itemid. Example 2: reloadsprite name \"Sprite name\"", args =>
            {
                if (Screen.Selected is SpriteEditorScreen)
                {
                    return;
                }
                else if (args.Length > 1)
                {
                    TryDoActionOnSprite(args[0], args[1], s =>
                    {
                        s.ReloadXML();
                        s.ReloadTexture();
                    });
                }
                else if (Screen.Selected is SubEditorScreen)
                {
                    if (!MapEntity.SelectedAny)
                    {
                        ThrowError("You have to select item(s)/structure(s) first!");
                    }
                    MapEntity.SelectedList.ForEach(e =>
                    {
                        if (e.Sprite != null)
                        {
                            e.Sprite.ReloadXML();
                            e.Sprite.ReloadTexture();
                        }
                    });
                }
                else
                {
                    var character = Character.Controlled;
                    if (character == null)
                    {
                        ThrowError("Please provide the mode (name or id) and the value so that I can find the sprite for you!");
                        return;
                    }
                    var item = character.FocusedItem;
                    if (item != null)
                    {
                        item.Sprite.ReloadXML();
                        item.Sprite.ReloadTexture();
                    }
                    else
                    {
                        ReloadWearables(character);
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("flipx", "flipx: mirror the main submarine horizontally", (string[] args) =>
            {
                Submarine.MainSub?.FlipX();
            }, isCheat: true));

            commands.Add(new Command("gender", "Set the gender of the controlled character. Allowed parameters: Male, Female, None.", args =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("No parameters provided!");
                    return;
                }
                if (Enum.TryParse(args[0], true, out Gender gender))
                {
                    character.Info.Gender = gender;
                    character.ReloadHead();
                    foreach (var limb in character.AnimController.Limbs)
                    {
                        if (limb.type != LimbType.Head)
                        {
                            limb.RecreateSprite();
                        }
                        foreach (var wearable in limb.WearingItems)
                        {
                            if (wearable.Gender != Gender.None && wearable.Gender != gender)
                            {
                                wearable.Gender = gender;
                            }
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("race", "Set race of the controlled character. Allowed parameters: White, Black, Asian, None.", args =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("No parameters provided!");
                    return;
                }
                if (Enum.TryParse(args[0], true, out Race race))
                {
                    character.Info.Race = race;
                    character.ReloadHead();
                }
            }, isCheat: true));

            commands.Add(new Command("loadhead|head", "Load head sprite(s). Required argument: head id. Optional arguments: hair index, beard index, moustache index, face attachment index.", args =>
            {
                var character = Character.Controlled;
                if (character == null)
                {
                    ThrowError("Not controlling any character!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("No head id provided!");
                    return;
                }
                if (int.TryParse(args[0], out int id))
                {
                    int hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex;
                    hairIndex = beardIndex = moustacheIndex = faceAttachmentIndex = -1;
                    if (args.Length > 1)
                    {
                        int.TryParse(args[1], out hairIndex);
                    }
                    if (args.Length > 2)
                    {
                        int.TryParse(args[2], out beardIndex);
                    }
                    if (args.Length > 3)
                    {
                        int.TryParse(args[3], out moustacheIndex);
                    }
                    if (args.Length > 4)
                    {
                        int.TryParse(args[4], out faceAttachmentIndex);
                    }
                    character.ReloadHead(id, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
                }
            }, isCheat: true));
        }

        private static void ReloadWearables(Character character, int variant = 0)
        {
            foreach (var limb in character.AnimController.Limbs)
            {
                limb.Sprite?.ReloadTexture();
                limb.DamagedSprite?.ReloadTexture();
                limb.DeformSprite?.Sprite.ReloadTexture();
                foreach (var wearable in limb.WearingItems)
                {
                    if (variant > 0 && wearable.Variant > 0)
                    {
                        wearable.Variant = variant;
                    }
                    wearable.RefreshPath();
                    wearable.Sprite.ReloadXML();
                    wearable.Sprite.ReloadTexture();
                }
                foreach (var wearable in limb.OtherWearables)
                {
                    wearable.RefreshPath();
                    wearable.Sprite.ReloadXML();
                    wearable.Sprite.ReloadTexture();
                }
                if (limb.HuskSprite != null)
                {
                    limb.HuskSprite.Sprite.ReloadXML();
                    limb.HuskSprite.Sprite.ReloadTexture();
                }
            }
        }

        private static bool TryDoActionOnSprite(string firstArg, string secondArg, Action<Sprite> action)
        {
            switch (firstArg)
            {
                case "name":
                    var sprites = Sprite.LoadedSprites.Where(s => s.Name?.ToLowerInvariant() == secondArg.ToLowerInvariant());
                    if (sprites.Any())
                    {
                        foreach (var s in sprites)
                        {
                            action(s);
                        }
                        return true;
                    }
                    else
                    {
                        ThrowError("Cannot find any matching sprites by the name: " + secondArg);
                        return false;
                    }
                case "identifier":
                case "id":
                    sprites = Sprite.LoadedSprites.Where(s => s.EntityID?.ToLowerInvariant() == secondArg.ToLowerInvariant());
                    if (sprites.Any())
                    {
                        foreach (var s in sprites)
                        {
                            action(s);
                        }
                        return true;
                    }
                    else
                    {
                        ThrowError("Cannot find any matching sprites by the id: " + secondArg);
                        return false;
                    }
                default:
                    ThrowError("The first argument must be either 'name' or 'id'");
                    return false;
            }
        }
    }
}

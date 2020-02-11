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
using FarseerPhysics;
using Barotrauma.Extensions;

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
#if USE_STEAM
                    NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
#endif
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
        public static bool Paused = false;
        
        private static GUITextBlock activeQuestionText;
        
        private static GUIFrame frame;
        private static GUIListBox listBox;
        private static GUITextBox textBox;
        private const int maxLength = 1000;

        public static GUITextBox TextBox => textBox;
        
        private static readonly ChatManager chatManager = new ChatManager(true, 64);

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
            textBox.MaxTextLength = maxLength;
            textBox.OnKeyHit += (sender, key) =>
            {
                if (key != Keys.Tab)
                {
                    ResetAutoComplete();
                }
            };
            
            ChatManager.RegisterKeys(textBox, chatManager);
        }

        public static void AddToGUIUpdateList()
        {
            if (isOpen)
            {
                frame.AddToGUIUpdateList();
            }
        }

        public static void Update(float deltaTime)
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

                if (PlayerInput.KeyHit(Keys.Tab))
                {
                     textBox.Text = AutoComplete(textBox.Text, increment: string.IsNullOrEmpty(currentAutoCompletedCommand) ? 0 : 1 );
                }

                if (PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
                {
                    if ((PlayerInput.KeyDown(Keys.C) || PlayerInput.KeyDown(Keys.D) || PlayerInput.KeyDown(Keys.Z)) && activeQuestionCallback != null)
                    {
                        activeQuestionCallback = null;
                        activeQuestionText = null;
                        NewMessage(PlayerInput.KeyDown(Keys.C) ? "^C" : PlayerInput.KeyDown(Keys.D) ? "^D" : "^Z", Color.White, true);
                    }
                }

                if (PlayerInput.KeyHit(Keys.Enter))
                {
                    chatManager.Store(textBox.Text);
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

            chatManager.Clear();
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
            new GUITextBlock(new RectTransform(new Point(150, textContainer.Rect.Height), textContainer.RectTransform),
                command.names[0], textAlignment: Alignment.TopLeft);

            listBox.UpdateScrollBarSize();
            listBox.BarScroll = 1.0f;

            chatManager.Clear();
        }

        private static void AssignOnClientExecute(string names, Action<string[]> onClientExecute)
        {
            Command command = commands.Find(c => c.names.Intersect(names.Split('|')).Count() > 0);
            if (command == null)
            {
                throw new Exception("AssignOnClientExecute failed. Command matching the name(s) \"" + names + "\" not found.");
            }
            else
            {
                command.OnClientExecute = onClientExecute;
                command.RelayToServer = false;
            }
        }

        private static void AssignRelayToServer(string names, bool relay)
        {
            Command command = commands.Find(c => c.names.Intersect(names.Split('|')).Count() > 0);
            if (command == null)
            {
                DebugConsole.Log("Could not assign to relay to server: " + names);
                return;
            }
            command.RelayToServer = relay;
        }

        private static void InitProjectSpecific()
        {
#if WINDOWS
            commands.Add(new Command("copyitemnames", "", (string[] args) =>
            {
                StringBuilder sb = new StringBuilder();
                foreach (ItemPrefab mp in ItemPrefab.Prefabs)
                {
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

            commands.Add(new Command("startlidgrenclient", "", (string[] args) =>
            {
                if (args.Length == 0) return;

                if (GameMain.Client == null)
                {
                    GameMain.Client = new GameClient("Name", args[0], 0);
                }
            }));

            commands.Add(new Command("startsteamp2pclient", "", (string[] args) =>
            {
                if (GameMain.Client == null)
                {
                    GameMain.Client = new GameClient("Name", null, 76561198977850505); //this is juan's alt account, feel free to abuse this one
                }
            }));

            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Enabled cheat commands.", Color.Red);
#if USE_STEAM
                NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
#endif
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
            }, isCheat: true));

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
            commands.Add(new Command("say", "", (string[] args) => { }));
            AssignRelayToServer("say", true);
            commands.Add(new Command("msg", "", (string[] args) => { }));
            AssignRelayToServer("msg", true);
            commands.Add(new Command("setmaxplayers|maxplayers", "", (string[] args) => { }));
            AssignRelayToServer("setmaxplayers", true);
            commands.Add(new Command("setpassword|password", "", (string[] args) => { }));
            AssignRelayToServer("setpassword", true);
            commands.Add(new Command("traitorlist", "", (string[] args) => { }));
            AssignRelayToServer("traitorlist", true);

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

            AssignOnExecute("explosion", (string[] args) =>
            {
                Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float range = 500, force = 10, damage = 50, structureDamage = 10, empStrength = 0.0f;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                if (args.Length > 4) float.TryParse(args[4], out empStrength);
                new Explosion(range, force, damage, structureDamage, empStrength).Explode(explosionPos, null);
            });

            AssignOnExecute("teleportcharacter|teleport", (string[] args) =>
            {
                Character tpCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, false);
                if (tpCharacter == null) return;

                var cam = GameMain.GameScreen.Cam;
                tpCharacter.AnimController.CurrentHull = null;
                tpCharacter.Submarine = null;
                tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition)));
                tpCharacter.AnimController.FindHull(cam.ScreenToWorld(PlayerInput.MousePosition), true);
            });

            AssignOnExecute("spawn|spawncharacter", (string[] args) =>
            {
                SpawnCharacter(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), out string errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            });

            AssignOnExecute("los", (string[] args) =>
             {
                 if (args.None() || !bool.TryParse(args[0], out bool state))
                 {
                     state = !GameMain.LightManager.LosEnabled;
                 }
                 GameMain.LightManager.LosEnabled = state;
                 NewMessage("Line of sight effect " + (GameMain.LightManager.LosEnabled ? "enabled" : "disabled"), Color.White);
             });
            AssignRelayToServer("los", false);

            AssignOnExecute("lighting|lights", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameMain.LightManager.LightingEnabled;
                }
                GameMain.LightManager.LightingEnabled = state;
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
                            return;
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

            commands.Add(new Command("resetentitiesbyidentifier", "resetentitiesbyidentifier [tag/identifier]: Reset items and structures with the given tag/identifier to prefabs. Only applicable in the subeditor.", args =>
            {
                if (args.Length == 0) { return; }
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    bool entityFound = false;
                    foreach (MapEntity entity in MapEntity.mapEntityList)
                    {
                        if (entity is Item item)
                        {
                            if (item.prefab.Identifier != args[0] && !item.Tags.Contains(args[0])) { continue; }
                            item.Reset();
                            if (MapEntity.SelectedList.Contains(item)) { item.CreateEditingHUD(); }
                            entityFound = true;
                        }
                        else if (entity is Structure structure)
                        {
                            if (structure.prefab.Identifier != args[0] && !structure.Tags.Contains(args[0])) { continue; }
                            structure.Reset();
                            if (MapEntity.SelectedList.Contains(structure)) { structure.CreateEditingHUD(); }
                            entityFound = true;
                        }
                        else
                        {
                            continue;
                        }
                        NewMessage($"Reset {entity.Name}.");
                    }
                    if (!entityFound)
                    {
                        if (MapEntity.SelectedList.Count == 0)
                        {
                            NewMessage("No entities selected.");
                            return;
                        }
                    }
                }
            }, () =>
            {
                return new string[][]
                {
                    MapEntityPrefab.List.Select(me => me.Identifier).ToArray()
                };
            }));

            commands.Add(new Command("resetselected", "Reset selected items and structures to prefabs. Only applicable in the subeditor.", args =>
            {
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    if (MapEntity.SelectedList.Count == 0)
                    {
                        NewMessage("No entities selected.");
                        return;
                    }

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
                        else
                        {
                            continue;
                        }
                        NewMessage($"Reset {entity.Name}.");
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
                if (args.Length < 1) { return; }

                GameMain.SubEditorScreen.SetMode(SubEditorScreen.Mode.Default);

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

            commands.Add(new Command("messagebox|guimessagebox", "messagebox [header] [msg] [default/ingame]: Creates a message box.", (string[] args) =>
            {
                var msgBox = new GUIMessageBox(
                    args.Length > 0 ? args[0] : "",
                    args.Length > 1 ? args[1] : "",
                    buttons: new string[] { "OK" },
                    type: args.Length < 3 || args[2] == "default" ? GUIMessageBox.Type.Default : GUIMessageBox.Type.InGame);

                msgBox.Buttons[0].OnClicked = msgBox.Close;
            }));

            AssignOnExecute("debugdraw", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameMain.DebugDraw;
                }
                GameMain.DebugDraw = state;
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
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    fabricableItems.AddRange(itemPrefab.FabricationRecipes);
                }
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
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
            }, isCheat: false));

            commands.Add(new Command("setentityproperties", "setentityproperties [property name] [value]: Sets the value of some property on all selected items/structures in the sub editor.", (string[] args) =>
            {
                if (args.Length != 2 || Screen.Selected != GameMain.SubEditorScreen) { return; }
                foreach (MapEntity me in MapEntity.SelectedList)
                {
                    if (me is ISerializableEntity serializableEntity)
                    {
                        if (serializableEntity.SerializableProperties == null)
                        {
                            continue;
                        }
                        if (!serializableEntity.SerializableProperties.TryGetValue(args[0].ToLowerInvariant(), out SerializableProperty property))
                        {
                            NewMessage("Property \"" + args[0] + "\" not found in the entity \"" + me.ToString() + "\".", Color.Orange);
                            continue;
                        }
                        if (!property.TrySetValue(me, args[1]))
                        {
                            NewMessage("Failed to set the value of \"" + args[0] + "\" to \"" + args[1] + "\" on the entity \"" + me.ToString() + "\".", Color.Orange);
                        }
                    }
                }
            }));

            commands.Add(new Command("checkmissingloca", "", (string[] args) =>
            {
                //key = text tag, value = list of languages the tag is missing from
                Dictionary<string, HashSet<string>> missingTags = new Dictionary<string, HashSet<string>>();
                Dictionary<string, HashSet<string>> tags = new Dictionary<string, HashSet<string>>();
                foreach (string language in TextManager.AvailableLanguages)
                {
                    TextManager.Language = language;
                    tags.Add(language, new HashSet<string>(TextManager.GetAllTagTextPairs().Select(t => t.Key)));
                }

                foreach (string language in TextManager.AvailableLanguages)
                {
                    //check missing mission texts
                    foreach (var missionPrefab in MissionPrefab.List)
                    {
                        string nameIdentifier = "missionname." + missionPrefab.Identifier;
                        if (!tags[language].Contains(nameIdentifier))
                        {
                            if (!missingTags.ContainsKey(nameIdentifier)) { missingTags[nameIdentifier] = new HashSet<string>(); }
                            missingTags[nameIdentifier].Add(language);
                        }
                        string descriptionIdentifier = "missiondescription." + missionPrefab.Identifier;
                        if (!tags[language].Contains(descriptionIdentifier))
                        {
                            if (!missingTags.ContainsKey(descriptionIdentifier)) { missingTags[descriptionIdentifier] = new HashSet<string>(); }
                            missingTags[descriptionIdentifier].Add(language);
                        }
                    }

                    foreach (Submarine sub in Submarine.SavedSubmarines)
                    {
                        string nameIdentifier = "submarine.name." + sub.Name.ToLowerInvariant();
                        if (!tags[language].Contains(nameIdentifier))
                        {
                            if (!missingTags.ContainsKey(nameIdentifier)) { missingTags[nameIdentifier] = new HashSet<string>(); }
                            missingTags[nameIdentifier].Add(language);
                        }
                        string descriptionIdentifier = "submarine.description." + sub.Name.ToLowerInvariant();
                        if (!tags[language].Contains(descriptionIdentifier))
                        {
                            if (!missingTags.ContainsKey(descriptionIdentifier)) { missingTags[descriptionIdentifier] = new HashSet<string>(); }
                            missingTags[descriptionIdentifier].Add(language);
                        }
                    }

                    foreach (AfflictionPrefab affliction in AfflictionPrefab.List)
                    {
                        string nameIdentifier = "afflictionname." + affliction.Identifier;
                        if (!tags[language].Contains(nameIdentifier))
                        {
                            if (!missingTags.ContainsKey(nameIdentifier)) { missingTags[nameIdentifier] = new HashSet<string>(); }
                            missingTags[nameIdentifier].Add(language);
                        }

                        string descriptionIdentifier = "afflictiondescription." + affliction.Identifier;
                        if (!tags[language].Contains(descriptionIdentifier))
                        {
                            if (!missingTags.ContainsKey(descriptionIdentifier)) { missingTags[descriptionIdentifier] = new HashSet<string>(); }
                            missingTags[descriptionIdentifier].Add(language);
                        }
                    }

                    //check missing entity names
                    foreach (MapEntityPrefab me in MapEntityPrefab.List)
                    {
                        string nameIdentifier = "entityname." + me.Identifier;
                        if (tags[language].Contains(nameIdentifier)) { continue; }
                        if (me is ItemPrefab itemPrefab)
                        {
                            nameIdentifier = itemPrefab.ConfigElement?.GetAttributeString("nameidentifier", null) ?? nameIdentifier;
                            if (nameIdentifier != null)
                            {
                                if (tags[language].Contains("entityname." + nameIdentifier)) { continue; }
                            }
                        }

                        if (!missingTags.ContainsKey(nameIdentifier)) { missingTags[nameIdentifier] = new HashSet<string>(); }
                        missingTags[nameIdentifier].Add(language);
                    }
                }

                foreach (string englishTag in tags["English"])
                {
                    foreach (string language in TextManager.AvailableLanguages)
                    {
                        if (language == "English") { continue; }
                        if (!tags[language].Contains(englishTag))
                        {
                            if (!missingTags.ContainsKey(englishTag)) { missingTags[englishTag] = new HashSet<string>(); }
                            missingTags[englishTag].Add(language);
                        }
                    }
                }

                List<string> lines = missingTags.Select(t => "\"" + t.Key + "\"\n    missing from " + string.Join(", ", t.Value)).ToList();

                string filePath = "missingloca.txt";
                File.WriteAllLines(filePath, lines);
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
                TextManager.Language = "English";
            }));

#if DEBUG
            commands.Add(new Command("printreceivertransfers", "", (string[] args) =>
            {
                GameMain.Client.PrintReceiverTransters();
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

            commands.Add(new Command("getprefabinfo", "", (string[] args) =>
            {
                var prefab = MapEntityPrefab.Find(null, args[0]);
                if (prefab != null)
                {
                    DebugConsole.NewMessage(prefab.Name + " " + prefab.Identifier + " " + prefab.GetType().ToString());
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
                                    structure.Rect = structure.DefaultRect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                        (int)structure.Prefab.ScaledSize.X,
                                        structure.Rect.Height);
                                }
                                if (!structure.ResizeVertical)
                                {
                                    structure.Rect = structure.DefaultRect = new Rectangle(structure.Rect.X, structure.Rect.Y,
                                        structure.Rect.Width,
                                        (int)structure.Prefab.ScaledSize.Y);
                                }
                               
                            }
                        }
                    }
                }
            }, isCheat: false));

            commands.Add(new Command("flip", "Flip the currently controlled character.", (string[] args) =>
            {
                Character.Controlled?.AnimController.Flip();
            }, isCheat: false));
            commands.Add(new Command("mirror", "Mirror the currently controlled character.", (string[] args) =>
            {
                (Character.Controlled?.AnimController as FishAnimController)?.Mirror(lerp: false);
            }, isCheat: false));
            commands.Add(new Command("forcetimeout", "Immediately cause the client to time out if one is running.", (string[] args) =>
            {
                GameMain.Client?.ForceTimeOut();
            }, isCheat: false));

#endif

            commands.Add(new Command("dumptexts", "dumptexts [filepath]: Extracts all the texts from the given text xml and writes them into a file (using the same filename, but with the .txt extension). If the filepath is omitted, the EnglishVanilla.xml file is used.", (string[] args) =>
            {
                string filePath = args.Length > 0 ? args[0] : "Content/Texts/EnglishVanilla.xml";
                var doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null) { return; }
                List<string> lines = new List<string>();
                foreach (XElement element in doc.Root.Elements())
                {
                    lines.Add(element.ElementInnerText());
                }
                File.WriteAllLines(Path.GetFileNameWithoutExtension(filePath) + ".txt", lines);
            },
            () =>
            {
                var files = TextManager.GetTextFiles().Select(f => f.CleanUpPath());
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
                if (doc == null) { return; }
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
                var files = TextManager.GetTextFiles().Select(f => f.CleanUpPath());
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

                if (sourceDoc == null || destinationDoc == null) { return; }

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
                var files = TextManager.GetTextFiles().Where(f => Path.GetExtension(f) == ".xml").Select(f => f.CleanUpPath()).ToArray();
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


            commands.Add(new Command("itemcomponentdocumentation", "", (string[] args) =>
            {
                Dictionary<string, string> typeNames = new Dictionary<string, string>
                {
                    { "Single", "Float"},
                    { "Int32", "Integer"},
                    { "Boolean", "True/False"},
                    { "String", "Text"},
                };

                var itemComponentTypes = typeof(ItemComponent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ItemComponent))).ToList();
                itemComponentTypes.Sort((i1, i2) => { return i1.Name.CompareTo(i2.Name); });

                itemComponentTypes.Insert(0, typeof(ItemComponent));
                
                string filePath = args.Length > 0 ? args[0] : "ItemComponentDocumentation.txt";
                List<string> lines = new List<string>();
                foreach (Type t in itemComponentTypes)
                {

                    lines.Add($"[h1]{t.Name}[/h1]");
                    lines.Add("");

                    var properties = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).ToList();//.Cast<System.ComponentModel.PropertyDescriptor>();
                    Type baseType = t.BaseType;
                    while (baseType != null && baseType != typeof(ItemComponent))
                    {
                        properties.AddRange(baseType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
                        baseType = baseType.BaseType;
                    }

                    if (!properties.Any(p => p.GetCustomAttributes(true).Any(a => a is Serialize)))
                    {
                        lines.Add("No editable properties.");
                        lines.Add("");
                        continue;
                    }

                    lines.Add("[table]");
                    lines.Add("  [tr]");

                    lines.Add("    [th]Name[/th]");
                    lines.Add("    [th]Type[/th]");
                    lines.Add("    [th]Default value[/th]");
                    //lines.Add("    [th]Range[/th]");
                    lines.Add("    [th]Description[/th]");

                    lines.Add("  [/tr]");


                    
                    Dictionary<string, SerializableProperty> dictionary = new Dictionary<string, SerializableProperty>();
                    foreach (var property in properties)
                    {
                        object[] attributes = property.GetCustomAttributes(true);
                        Serialize serialize = attributes.FirstOrDefault(a => a is Serialize) as Serialize;
                        if (serialize == null) { continue; }

                        string propertyTypeName = property.PropertyType.Name;
                        if (typeNames.ContainsKey(propertyTypeName))
                        {
                            propertyTypeName = typeNames[propertyTypeName];
                        }
                        else if (property.PropertyType.IsEnum)
                        {
                            List<string> valueNames = new List<string>();
                            foreach (object enumValue in Enum.GetValues(property.PropertyType))
                            {
                                valueNames.Add(enumValue.ToString());
                            }
                            propertyTypeName = string.Join("/", valueNames);
                        }
                        string defaultValueString = serialize.defaultValue?.ToString() ?? "";
                        if (property.PropertyType == typeof(float))
                        {
                            defaultValueString = ((float)serialize.defaultValue).ToString(CultureInfo.InvariantCulture);
                        }

                        lines.Add("  [tr]");

                        lines.Add($"    [td]{property.Name}[/td]");
                        lines.Add($"    [td]{propertyTypeName}[/td]");
                        lines.Add($"    [td]{defaultValueString}[/td]");

                        Editable editable = attributes.FirstOrDefault(a => a is Editable) as Editable;
                        string rangeText = "-";
                        if (editable != null)
                        {
                            if (editable.MinValueFloat > float.MinValue || editable.MaxValueFloat < float.MaxValue)
                            {
                                rangeText = editable.MinValueFloat + "-" + editable.MaxValueFloat;
                            }
                            else if (editable.MinValueInt > int.MinValue || editable.MaxValueInt < int.MaxValue)
                            {
                                rangeText = editable.MinValueInt + "-" + editable.MaxValueInt;
                            }
                        }
                        //lines.Add($"    [td]{rangeText}[/td]");

                        if (!string.IsNullOrEmpty(serialize.Description))
                        {
                            lines.Add($"    [td]{serialize.Description}[/td]");
                        }

                        lines.Add("  [/tr]");
                    }
                    lines.Add("[/table]");
                    lines.Add("");
                }
                File.WriteAllLines(filePath, lines);
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
            }));
#if DEBUG
            commands.Add(new Command("querylobbies", "Queries all SteamP2P lobbies", (args) =>
            {
                Steamworks.Data.LobbyQuery lobbyQuery = Steamworks.SteamMatchmaking.CreateLobbyQuery().FilterDistanceWorldwide();

                Steamworks.Data.Lobby[] lobbies = lobbyQuery.RequestAsync().Result;
                foreach (var lobby in lobbies)
                {
                    DebugConsole.NewMessage(lobby.GetData("name") + ", " + lobby.GetData("lobbyowner"));
                }
            }));

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
                LocalizationCSVtoXML.Convert();
            }));

            commands.Add(new Command("printproperties", "Goes through the currently collected property list for missing localizations and writes them to a file.", (string[] args) =>
            {
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\propertylocalization.txt";
                File.WriteAllLines(path, SerializableEntityEditor.MissingLocalizations);
            }));

            commands.Add(new Command("getproperties", "Goes through the MapEntity prefabs and checks their serializable properties for localization issues.", (string[] args) =>
            {
                if (Screen.Selected != GameMain.SubEditorScreen) return;
                foreach (MapEntityPrefab ep in MapEntityPrefab.List)
                {
                    ep.DebugCreateInstance();
                }

                for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
                {
                    var entity = MapEntity.mapEntityList[i] as ISerializableEntity;
                    if (entity != null)
                    {
                        List<Pair<object, SerializableProperty>> allProperties = new List<Pair<object, SerializableProperty>>();

                        if (entity is Item item)
                        {
                            allProperties.AddRange(item.GetProperties<Editable>());
                            allProperties.AddRange(item.GetProperties<InGameEditable>());
                        }
                        else
                        {
                            var properties = new List<SerializableProperty>();
                            properties.AddRange(SerializableProperty.GetProperties<Editable>(entity));
                            properties.AddRange(SerializableProperty.GetProperties<InGameEditable>(entity));

                            for (int k = 0; k < properties.Count; k++)
                            {
                                allProperties.Add(new Pair<object, SerializableProperty>(entity, properties[k]));
                            }
                        }

                        for (int j = 0; j < allProperties.Count; j++)
                        {
                            var property = allProperties[j].Second;
                            string propertyName = (allProperties[j].First.GetType().Name + "." + property.PropertyInfo.Name).ToLowerInvariant();
                            string displayName = TextManager.Get($"sp.{propertyName}.name", returnNull: true);
                            if (displayName == null)
                            {
                                displayName = property.Name.FormatCamelCaseWithSpaces();

                                Editable editable = property.GetAttribute<Editable>();
                                if (editable != null)
                                {
                                    if (!SerializableEntityEditor.MissingLocalizations.Contains($"sp.{propertyName}.name|{displayName}"))
                                    {
                                        NewMessage("Missing Localization for property: " + propertyName);
                                        SerializableEntityEditor.MissingLocalizations.Add($"sp.{propertyName}.name|{displayName}");
                                        SerializableEntityEditor.MissingLocalizations.Add($"sp.{propertyName}.description|{property.GetAttribute<Serialize>().Description}");
                                    }
                                }
                            }
                        }
                    }
                }
            }));
#endif

            commands.Add(new Command("cleanbuild", "", (string[] args) =>
            {
                GameMain.Config.MusicVolume = 0.5f;
                GameMain.Config.SoundVolume = 0.5f;
                GameMain.Config.DynamicRangeCompressionEnabled = true;
                GameMain.Config.VoipAttenuationEnabled = true;
                NewMessage("Music and sound volume set to 0.5", Color.Green);

                GameMain.Config.GraphicsWidth = 0;
                GameMain.Config.GraphicsHeight = 0;
                GameMain.Config.WindowMode = WindowMode.BorderlessWindowed;
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

            commands.Add(new Command("reloadcorepackage", "", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    if (Screen.Selected == GameMain.GameScreen)
                    {
                        ThrowError("Reloading the core package while in GameScreen WILL break everything; to do it anyway, type 'reloadcorepackage force'");
                        return;
                    }

                    if (Screen.Selected == GameMain.SubEditorScreen)
                    {
                        ThrowError("Reloading the core package while in sub editor WILL break everything; to do it anyway, type 'reloadcorepackage force'");
                        return;
                    }
                }

                if (GameMain.NetworkMember != null)
                {
                    ThrowError("Cannot change content packages while playing online");
                    return;
                }

                GameMain.Config.SelectCorePackage(GameMain.Config.SelectedContentPackages.First(cp => cp.CorePackage), true);
            }));

            commands.Add(new Command("ingamemodswap", "", (string[] args) =>
            {
                ContentPackage.IngameModSwap = !ContentPackage.IngameModSwap;
                if (ContentPackage.IngameModSwap)
                {
                    NewMessage("Enabled ingame mod swapping");
                }
                else
                {
                    NewMessage("Disabled ingame mod swapping");
                }
            }));

            AssignOnClientExecute(
                "giveperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }
                    ShowQuestionPrompt("Permission to grant to client " + args[0] + "?", (perm) =>
                    {
                        GameMain.Client?.SendConsoleCommand("giveperm " + args[0] + " " + perm);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "revokeperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }

                    ShowQuestionPrompt("Permission to revoke from client " + args[0] + "?", (perm) =>
                    {
                        GameMain.Client?.SendConsoleCommand("revokeperm " + args[0] + " " + perm);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "giverank",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    NewMessage("Valid ranks are:", Color.White);
                    foreach (PermissionPreset permissionPreset in PermissionPreset.List)
                    {
                        NewMessage(" - " + permissionPreset.Name, Color.White);
                    }
                    ShowQuestionPrompt("Rank to grant to client " + args[0] + "?", (rank) =>
                    {
                        GameMain.Client?.SendConsoleCommand("giverank " + args[0] + " " + rank);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "givecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    ShowQuestionPrompt("Console command permissions to grant to client " + args[0] + "? You may enter multiple commands separated with a space.", (commandNames) =>
                    {
                        GameMain.Client?.SendConsoleCommand("givecommandperm " + args[0] + " " + commandNames);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "revokecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    ShowQuestionPrompt("Console command permissions to revoke from client " + args[0] + "? You may enter multiple commands separated with a space.", (commandNames) =>
                    {
                        GameMain.Client?.SendConsoleCommand("revokecommandperm " + args[0] + " " + commandNames);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "showperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    GameMain.Client.SendConsoleCommand("showperm " + args[0]);
                }
            );

            AssignOnClientExecute(
                "banendpoint|banip",
                (string[] args) =>
                {
                    if (GameMain.Client == null || args.Length == 0) return;
                    ShowQuestionPrompt("Reason for banning the endpoint \"" + args[0] + "\"? (Enter c to cancel)", (reason) =>
                    {
                        if (reason == "c" || reason == "C") { return; }
                        ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\") (Enter c to cancel)", (duration) =>
                        {
                            if (duration == "c" || duration == "C") { return; }
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

                            GameMain.Client?.SendConsoleCommand(
                                "banendpoint " +
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
                            GameMain.Client?.SendConsoleCommand("campaigndestination " + destinationIndex);
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
                character.AnimController.ResetRagdoll(forceReload: true);
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
                            limb.RecreateSprites();
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
                    foreach (var limb in character.AnimController.Limbs)
                    {
                        if (limb.type != LimbType.Head)
                        {
                            limb.RecreateSprites();
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("head", "Load the head sprite and the wearables (hair etc). Required argument: head id. Optional arguments: hair index, beard index, moustache index, face attachment index.", args =>
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
                    foreach (var limb in character.AnimController.Limbs)
                    {
                        if (limb.type != LimbType.Head)
                        {
                            limb.RecreateSprites();
                        }
                    }
                }
            }, isCheat: true));

            commands.Add(new Command("spawnsub", "spawnsub [subname]: Spawn a submarine at the position of the cursor", (string[] args) =>
            {
                if (GameMain.NetworkMember != null)
                {
                    ThrowError("Cannot spawn additional submarines during a multiplayer session.");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please enter the name of the submarine.");
                    return;
                }
                try
                {
                    Submarine spawnedSub = Submarine.Load(args[0], false);
                    spawnedSub.SetPosition(GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition));
                }
                catch (Exception e)
                {
                    string errorMsg = "Failed to spawn a submarine. Arguments: \"" + string.Join(" ", args) + "\".";
                    ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("DebugConsole.SpawnSubmarine:Error", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + '\n' + e.Message + '\n' + e.StackTrace);
                }
            }, isCheat: true));

            commands.Add(new Command("pause", "Toggles the pause state when playing offline", (string[] args) =>
            {
                if (GameMain.NetworkMember == null)
                {
                    Paused = !Paused;
                    DebugConsole.NewMessage("Game paused: " + Paused);
                }
                else
                {
                    DebugConsole.NewMessage("Cannot pause when a multiplayer session is active.");
                }
            }));

            AssignOnClientExecute("showseed|showlevelseed", (string[] args) =>
            {
                if (Level.Loaded == null)
                {
                    ThrowError("No level loaded.");
                }
                else
                {
                    NewMessage("Level seed: " + Level.Loaded.Seed);
                }
            });
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
                    wearable.ParsePath(true);
                    wearable.Sprite.ReloadXML();
                    wearable.Sprite.ReloadTexture();
                }
                foreach (var wearable in limb.OtherWearables)
                {
                    wearable.ParsePath(true);
                    wearable.Sprite.ReloadXML();
                    wearable.Sprite.ReloadTexture();
                }
                if (limb.HuskSprite != null)
                {
                    limb.HuskSprite.Sprite.ReloadXML();
                    limb.HuskSprite.Sprite.ReloadTexture();
                }
                if (limb.HerpesSprite != null)
                {
                    limb.HerpesSprite.Sprite.ReloadXML();
                    limb.HerpesSprite.Sprite.ReloadTexture();
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

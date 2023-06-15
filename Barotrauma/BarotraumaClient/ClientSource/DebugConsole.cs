﻿using Barotrauma.ClientSource.Settings;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Barotrauma.MapCreatures.Behavior;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using static Barotrauma.FabricationRecipe;

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
                bool allowCheats = GameMain.NetworkMember == null && (GameMain.GameSession?.GameMode is TestGameMode || Screen.Selected is { IsEditor: true });
                if (!allowCheats && !CheatsEnabled && IsCheat)
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
        public static bool IsOpen
        {
            get => isOpen;
            set => isOpen = value;
        }

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
            OpenAL.Alc.SetErrorReasonCallback((string msg) => NewMessage(msg, Color.Orange));

            frame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.45f), GUI.Canvas) { MinSize = new Point(400, 300), AbsoluteOffset = new Point(10, 10) },
                color: new Color(0.4f, 0.4f, 0.4f, 0.8f));

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.01f };

            var toggleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform, Anchor.TopLeft), TextManager.Get("DebugConsoleHelpText"), Color.GreenYellow, GUIStyle.SmallFont, Alignment.CenterLeft, style: null);

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
                frame.AddToGUIUpdateList(order: 1);
            }
        }

        public static void Update(float deltaTime)
        {
            while (queuedMessages.TryDequeue(out var newMsg))
            {
                AddMessage(newMsg);

                if (GameSettings.CurrentConfig.SaveDebugConsoleLogs || GameSettings.CurrentConfig.VerboseLogging)
                {
                    unsavedMessages.Add(newMsg);
                    if (unsavedMessages.Count >= messagesPerFile)
                    {
                        SaveLogs();
                        unsavedMessages.Clear();
                    }
                }
            }

            if (!IsOpen && GUI.KeyboardDispatcher.Subscriber == null)
            {
                foreach (var (key, command) in DebugConsoleMapping.Instance.Bindings)
                {
                    if (key.IsHit())
                    {
                        ExecuteCommand(command);
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
                SoundPlayer.PlayUISound(GUISoundType.Select);
            }

            if (isOpen)
            {
                frame.UpdateManually(deltaTime);

                Character.DisableControls = true;

                if (PlayerInput.KeyHit(Keys.Tab) && !textBox.IsIMEActive)
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
                textBox.Select(ignoreSelectSound: true);
                AddToGUIUpdateList();
            }
            else
            {
                GUI.ForceMouseOn(null);
                textBox.Deselect();
            }
            SoundPlayer.PlayUISound(GUISoundType.Select);
        }

        private static bool IsCommandPermitted(string command, GameClient client)
        {
            switch (command)
            {
                case "kick":
                    return client.HasPermission(ClientPermissions.Kick);
                case "ban":
                case "banip":
                case "banaddress":
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
                case "showperf":
                case "dumptofile":
                case "findentityids":
                case "setfreecamspeed":
                case "togglevoicechatfilters":
                case "bindkey":
                case "savebinds":
                case "unbindkey":
                case "wikiimage_character":
                case "wikiimage_sub":
                    return true;
                default:
                    return client.HasConsoleCommandPermission(command);
            }
        }

        public static void DequeueMessages()
        {
            while (queuedMessages.TryDequeue(out var newMsg))
            {
                if (listBox == null)
                {
                    //don't attempt to add to the listbox if it hasn't been created yet                    
                    Messages.Add(newMsg);
                }
                else
                {
                    AddMessage(newMsg);
                }

                if (GameSettings.CurrentConfig.SaveDebugConsoleLogs || GameSettings.CurrentConfig.VerboseLogging)
                { 
                    unsavedMessages.Add(newMsg); 
                }
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
                if (msg.IsError)
                {
                    var textContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform), style: "InnerFrame", color: Color.White)
                    {
                        CanBeFocused = true,
                        OnSecondaryClicked = (component, data) =>
                        {
                            GUIContextMenu.CreateContextMenu(new ContextMenuOption("editor.copytoclipboard", true, () => { Clipboard.SetText(msg.Text); }));
                            return true;
                        }
                    };
                    var textBlock = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width - 5, 0), textContainer.RectTransform, Anchor.TopLeft) { AbsoluteOffset = new Point(2, 2) },
                        msg.Text, textAlignment: Alignment.TopLeft, font: GUIStyle.SmallFont, wrap: true)
                    {
                        CanBeFocused = false,
                        TextColor = msg.Color
                    };
                    textContainer.RectTransform.NonScaledSize = new Point(textContainer.RectTransform.NonScaledSize.X, textBlock.RectTransform.NonScaledSize.Y + 5);
                    textBlock.SetTextPos();
                }
                else
                {
                    var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                    msg.Text, font: GUIStyle.SmallFont, wrap: true)
                    {
                        CanBeFocused = false,
                        TextColor = msg.Color
                    };
                }
                
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
                command.help, textAlignment: Alignment.TopLeft, font: GUIStyle.SmallFont, wrap: true)
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
            commands.Add(new Command("copyitemnames", "", (string[] args) =>
            {
                StringBuilder sb = new StringBuilder();
                foreach (ItemPrefab mp in ItemPrefab.Prefabs)
                {
                    sb.AppendLine(mp.Name.Value);
                }
                Clipboard.SetText(sb.ToString());
            }));

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
                                    if (option2.ToLowerInvariant() == "y") { GameMain.SubEditorScreen.AutoHull(); }
                                });
                        });
                }
                else
                {
                    ShowQuestionPrompt("The automatic hull generation may not work correctly if your submarine uses curved walls. Do you want to continue? Y/N",
                        (option) => { if (option.ToLowerInvariant() == "y") GameMain.SubEditorScreen.AutoHull(); });
                }
            }));

            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                if (GameMain.GameSession?.Campaign is CampaignMode campaign)
                {
                    campaign.CheatsEnabled = true;
                }
                NewMessage("Enabled cheat commands.", Color.Red);
#if USE_STEAM
                NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
#endif
            }));
            AssignRelayToServer("enablecheats", true);

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
                    Entity.Spawner ??= new EntitySpawner();
                }
                GameMain.GameScreen.Select();
            }));

            commands.Add(new Command("editsubs|subeditor", "editsubs/subeditor: Switch to the Submarine Editor to create or edit submarines.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    var subInfo = new SubmarineInfo(string.Join(" ", args));
                    Submarine.MainSub = Submarine.Load(subInfo, true);
                }
                GameMain.SubEditorScreen.Select(enableAutoSave: Screen.Selected != GameMain.GameScreen);
                Entity.Spawner?.Remove();
                Entity.Spawner = null;
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
            
            commands.Add(new Command("editevents|eventeditor", "editevents/eventeditor: Switch to the Event Editor to edit scripted events.", (string[] args) =>
            {
                GameMain.EventEditorScreen.Select();
            }));

            commands.Add(new Command("editcharacters|charactereditor", "editcharacters/charactereditor: Switch to the Character Editor to edit/create the ragdolls and animations of characters.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.GameScreen)
                {
                    NewMessage("WARNING: Switching between the character editor and the game view may cause odd behaviour or bugs. Use with caution.", Color.Orange);
                }
                GameMain.CharacterEditorScreen.Select();
            }));

            commands.Add(new Command("quickstart", "Starts a singleplayer sandbox", (string[] args) =>
            {
                if (Screen.Selected != GameMain.MainMenuScreen)
                {
                    ThrowError("This command can only be executed from the main menu.");
                    return;
                }

                Identifier subName = (args.Length > 0 ? args[0] : "").ToIdentifier();
                if (subName.IsEmpty)
                {
                    ThrowError("No submarine specified.");
                    return;
                }

                float difficulty = 40;
                if (args.Length > 1)
                {
                    float.TryParse(args[1], out difficulty);
                }

                LevelGenerationParams levelGenerationParams = null;
                if (args.Length > 2)
                {
                    string levelGenerationIdentifier = args[2];
                    levelGenerationParams = LevelGenerationParams.LevelParams.FirstOrDefault(p => p.Identifier == levelGenerationIdentifier);
                }

                if (SubmarineInfo.SavedSubmarines.None(s => s.Name == subName))
                {
                    ThrowError($"Cannot find a sub that matches the name \"{subName}\".");
                    return;
                }

                GameMain.MainMenuScreen.QuickStart(fixedSeed: false, subName, difficulty, levelGenerationParams);

            }, getValidArgs: () => new[] { SubmarineInfo.SavedSubmarines.Select(s => s.Name).Distinct().OrderBy(s => s).ToArray() }));

            commands.Add(new Command("steamnetdebug", "steamnetdebug: Toggles Steamworks networking debug logging.", (string[] args) =>
            {
                SteamManager.SetSteamworksNetworkingDebugLog(!SteamManager.NetworkingDebugLog);
            }));

            commands.Add(new Command("readycheck", "Commence a ready check in multiplayer.", (string[] args) =>
            {
                NewMessage("Ready checks can only be commenced in multiplayer.", Color.Red);
            }));
            
            commands.Add(new Command("bindkey", "bindkey [key] [command]: Binds a key to a command.", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    ThrowError("No key or command specified.");
                    return;
                }

                string keyString = args[0];
                string command = args[1];

                KeyOrMouse key = Enum.TryParse<Keys>(keyString, ignoreCase: true, out var outKey)
                    ? outKey
                    : Enum.TryParse<MouseButton>(keyString, ignoreCase: true, out var outMouseButton)
                        ? outMouseButton
                        : (KeyOrMouse)MouseButton.None;
                
                if (key.Key == Keys.None && key.MouseButton == MouseButton.None)
                {
                    ThrowError($"Invalid key {keyString}.");
                    return;
                }
                    
                DebugConsoleMapping.Instance.Set(key, command);
                NewMessage($"\"{command}\" bound to {key}.", GUIStyle.Green);

                if (GameSettings.CurrentConfig.KeyMap.Bindings.FirstOrDefault(bind => bind.Value.Key != Keys.None && bind.Value.Key == key) is { } existingBind && existingBind.Value != null)
                {
                    AddWarning($"\"{key}\" has already been bound to {existingBind.Key}. The keybind will perform both actions when pressed.");
                }

            }, isCheat: false, getValidArgs: () => new[] { Enum.GetNames(typeof(Keys)), new[] { "\"\"" } }));
            
            commands.Add(new Command("unbindkey", "unbindkey [key]: Unbinds a command.", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    ThrowError("No key specified.");
                    return;
                }

                string keyString = args[0];
                
                KeyOrMouse key = Enum.TryParse<Keys>(keyString, ignoreCase: true, out var outKey)
                    ? outKey
                    : Enum.TryParse<MouseButton>(keyString, ignoreCase: true, out var outMouseButton)
                        ? outMouseButton
                        : (KeyOrMouse)MouseButton.None;

                if (key.Key == Keys.None && key.MouseButton == MouseButton.None)
                {
                    ThrowError($"Invalid key {keyString}.");
                    return;
                }
                DebugConsoleMapping.Instance.Remove(key);
                NewMessage("Keybind unbound.", GUIStyle.Green);
                return;                
            }, isCheat: false, getValidArgs: () => new[] { DebugConsoleMapping.Instance.Bindings.Keys.Select(keys => keys.ToString()).Distinct().OrderBy(k => k).ToArray() }));
            
            commands.Add(new Command("savebinds", "savebinds: Writes current keybinds into the config file.", (string[] args) =>
            {
                ShowQuestionPrompt($"Some keybinds may render the game unusable, are you sure you want to make these keybinds persistent? ({DebugConsoleMapping.Instance.Bindings.Count} keybind(s) assigned) Y/N",
                    (option2) =>
                    {
                        if (option2.ToIdentifier() != "y")
                        {
                            NewMessage("Aborted.", GUIStyle.Red);
                            return;
                        }

                        GameSettings.SaveCurrentConfig();
                    });
            }, isCheat: false));
            
            commands.Add(new Command("togglegrid", "Toggle visual snap grid in sub editor.", (string[] args) =>
            {
                SubEditorScreen.ShouldDrawGrid = !SubEditorScreen.ShouldDrawGrid;
                NewMessage(SubEditorScreen.ShouldDrawGrid ? "Enabled submarine grid." : "Disabled submarine grid.", GUIStyle.Green);
            }));

            commands.Add(new Command("spreadsheetexport", "Export items in format recognized by the spreadsheet importer.", (string[] args) =>
            {
                SpreadsheetExport.Export();
            }));

            commands.Add(new Command("wikiimage_character", "Save an image of the currently controlled character with a transparent background.", (string[] args) =>
            {
                if (Character.Controlled == null) { return; }
                try
                {
                    WikiImage.Create(Character.Controlled);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("The command 'wikiimage_character' failed.", e);
                }
            }));

            commands.Add(new Command("wikiimage_sub", "Save an image of the main submarine with a transparent background.", (string[] args) =>
            {
                if (Submarine.MainSub == null) { return; }
                try
                {
                    MapEntity.SelectedList.Clear();
                    MapEntity.ClearHighlightedEntities();
                    WikiImage.Create(Submarine.MainSub);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("The command 'wikiimage_sub' failed.", e);
                }
            }));

            AssignRelayToServer("kick", false);
            AssignRelayToServer("kickid", false);
            AssignRelayToServer("ban", false);
            AssignRelayToServer("banid", false);
            AssignRelayToServer("dumpids", false);
            AssignRelayToServer("dumptofile", false);
            AssignRelayToServer("findentityids", false);
            AssignRelayToServer("campaigninfo", false);
            AssignRelayToServer("help", false);
            AssignRelayToServer("verboselogging", false);
            AssignRelayToServer("freecam", false);
            AssignRelayToServer("steamnetdebug", false);
            AssignRelayToServer("quickstart", false);
            AssignRelayToServer("togglegrid", false);
            AssignRelayToServer("bindkey", false);
            AssignRelayToServer("unbindkey", false);
            AssignRelayToServer("savebinds", false);
            AssignRelayToServer("spreadsheetexport", false);
#if DEBUG
            AssignRelayToServer("crash", false);
            AssignRelayToServer("showballastflorasprite", false);
            AssignRelayToServer("simulatedlatency", false);
            AssignRelayToServer("simulatedloss", false);
            AssignRelayToServer("simulatedduplicateschance", false);
            AssignRelayToServer("simulatedlongloadingtime", false);
            AssignRelayToServer("storeinfo", false);
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
            AssignRelayToServer("money", true);
            AssignRelayToServer("showmoney", true);
            AssignRelayToServer("setskill", true);
            AssignRelayToServer("readycheck", true);

            AssignRelayToServer("givetalent", true);
            AssignRelayToServer("unlocktalents", true);
            AssignRelayToServer("giveexperience", true);

            AssignOnExecute("control", (string[] args) =>
            {
                if (args.Length < 1) { return; }
                if (GameMain.NetworkMember != null)
                {
                    GameMain.Client?.SendConsoleCommand("control " + string.Join(' ', args[0]));
                    return;
                }
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
                Vector2 explosionPos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float range = 500, force = 10, damage = 50, structureDamage = 20, itemDamage = 100, empStrength = 0.0f, ballastFloraStrength = 50f;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                if (args.Length > 4) float.TryParse(args[4], out itemDamage);
                if (args.Length > 5) float.TryParse(args[5], out empStrength);
                if (args.Length > 6) float.TryParse(args[6], out ballastFloraStrength);
                new Explosion(range, force, damage, structureDamage, itemDamage, empStrength, ballastFloraStrength).Explode(explosionPos, null);
            });

            AssignOnExecute("teleportcharacter|teleport", (string[] args) =>
            {
                Character tpCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, false);
                if (tpCharacter != null)
                {
                    tpCharacter.TeleportTo(GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition));
                }
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
                 NewMessage("Line of sight effect " + (GameMain.LightManager.LosEnabled ? "enabled" : "disabled"), Color.Yellow);
             });
            AssignRelayToServer("los", false);

            AssignOnExecute("lighting|lights", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameMain.LightManager.LightingEnabled;
                }
                GameMain.LightManager.LightingEnabled = state;
                NewMessage("Lighting " + (GameMain.LightManager.LightingEnabled ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignRelayToServer("lighting|lights", false);

            AssignOnExecute("ambientlight", (string[] args) =>
            {
                bool add = string.Equals(args.LastOrDefault(), "add");
                string colorString = string.Join(",", add ? args.SkipLast(1) : args);
                if (colorString.Equals("restore", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Hull hull in Hull.HullList)
                    {
                        if (hull.OriginalAmbientLight != null)
                        {
                            hull.AmbientLight = hull.OriginalAmbientLight.Value;
                            hull.OriginalAmbientLight = null;
                        }
                    }
                    NewMessage("Restored all hull ambient lights", Color.Yellow);
                    return;
                }

                Color color = XMLExtensions.ParseColor(colorString);
                if (Level.Loaded != null)
                {
                    Level.Loaded.GenerationParams.AmbientLightColor = color;
                }
                else
                {
                    GameMain.LightManager.AmbientLight = add ? GameMain.LightManager.AmbientLight.Add(color) : color;
                }

                foreach (Hull hull in Hull.HullList)
                {
                    hull.OriginalAmbientLight ??= hull.AmbientLight;
                    hull.AmbientLight = add ? hull.AmbientLight.Add(color) : color;
                }

                if (add)
                {
                    NewMessage($"Set ambient light color to {color}.", Color.Yellow);
                }
                else
                {
                    NewMessage($"Increased ambient light by {color}.", Color.Yellow);
                }
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

            commands.Add(new Command("listcloudfiles", "Lists all of your files on the Steam Cloud.", args =>
            {
                int i = 0;
                foreach (var file in Steamworks.SteamRemoteStorage.Files)
                {
                    NewMessage($"* {i}: {file.Filename}, {file.Size} bytes", Color.Orange);
                    i++;
                }
                NewMessage($"Bytes remaining: {Steamworks.SteamRemoteStorage.QuotaRemainingBytes}/{Steamworks.SteamRemoteStorage.QuotaBytes}", Color.Yellow);
            }));

            commands.Add(new Command("removefromcloud", "Removes a file from Steam Cloud.", args =>
            {
                if (args.Length < 1) { return; }
                var files = Steamworks.SteamRemoteStorage.Files;
                Steamworks.SteamRemoteStorage.RemoteFile file;
                if (int.TryParse(args[0], out int index) && index>=0 && index<files.Count)
                {
                    file = files[index];
                }
                else
                {
                    file = files.Find(f => f.Filename.Equals(args[0], StringComparison.InvariantCultureIgnoreCase));
                }

                if (!string.IsNullOrEmpty(file.Filename))
                {
                    if (file.Delete())
                    {
                        NewMessage($"Deleting {file.Filename}", Color.Orange);
                    }
                    else
                    {
                        ThrowError($"Failed to delete {file.Filename}");
                    }
                }
            }));

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
                            if (item.Prefab.Identifier != args[0] && !item.Tags.Contains(args[0])) { continue; }
                            item.Reset();
                            if (MapEntity.SelectedList.Contains(item)) { item.CreateEditingHUD(); }
                            entityFound = true;
                        }
                        else if (entity is Structure structure)
                        {
                            if (structure.Prefab.Identifier != args[0] && !structure.Tags.Contains(args[0])) { continue; }
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
                    MapEntityPrefab.List.Select(me => me.Identifier.Value).ToArray()
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
                    buttons: new LocalizedString[] { "OK" },
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
                NewMessage("Debug draw mode " + (GameMain.DebugDraw ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignRelayToServer("debugdraw", false);

            AssignOnExecute("debugdrawlos", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameMain.LightManager.DebugLos;
                }
                GameMain.LightManager.DebugLos = state;
                NewMessage("Los debug draw mode " + (GameMain.LightManager.DebugLos ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignOnExecute("debugwiring", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !ConnectionPanel.DebugWiringMode;
                }
                ConnectionPanel.DebugWiringMode = state;
                NewMessage("Wiring debug mode " + (ConnectionPanel.DebugWiringMode ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignRelayToServer("debugdraw", false);

            AssignOnExecute("devmode", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameMain.DevMode;
                }
                GameMain.DevMode = state;
                if (GameMain.DevMode)
                {
                    GameMain.LightManager.LightingEnabled = false;
                    GameMain.LightManager.LosEnabled = false;
                }
                else
                {
                    GameMain.LightManager.LightingEnabled = true;
                    GameMain.LightManager.LosEnabled = true;
                    GameMain.LightManager.LosAlpha = 1f;
                }
                NewMessage("Dev mode " + (GameMain.DevMode ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignRelayToServer("devmode", false);

            AssignOnExecute("debugdrawlocalization", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !TextManager.DebugDraw;
                }
                TextManager.DebugDraw = state;
                NewMessage("Localization debug draw mode " + (TextManager.DebugDraw ? "enabled" : "disabled"), Color.Yellow);
            });
            AssignRelayToServer("debugdraw", false);

            AssignOnExecute("togglevoicechatfilters", (string[] args) =>
            {
                if (args.None() || !bool.TryParse(args[0], out bool state))
                {
                    state = !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;
                }
                var config = GameSettings.CurrentConfig;
                config.Audio.DisableVoiceChatFilters = state;
                GameSettings.SetCurrentConfig(config);
                NewMessage("Voice chat filters " + (GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters ? "disabled" : "enabled"), Color.Yellow);
            });
            AssignRelayToServer("togglevoicechatfilters", false);

            commands.Add(new Command("fpscounter", "fpscounter: Toggle the FPS counter.", (string[] args) =>
            {
                GameMain.ShowFPS = !GameMain.ShowFPS;
                NewMessage("FPS counter " + (GameMain.DebugDraw ? "enabled" : "disabled"), Color.Yellow);
            }));
            commands.Add(new Command("showperf", "showperf: Toggle performance statistics on/off.", (string[] args) =>
            {
                GameMain.ShowPerf = !GameMain.ShowPerf;
                NewMessage("Performance statistics " + (GameMain.ShowPerf ? "enabled" : "disabled"), Color.Yellow);
            }));

            AssignOnClientExecute("netstats", (string[] args) =>
            {
                if (GameMain.Client == null) return;
                GameMain.Client.ShowNetStats = !GameMain.Client.ShowNetStats;
            });

            commands.Add(new Command("hudlayoutdebugdraw|debugdrawhudlayout", "hudlayoutdebugdraw: Toggle the debug drawing mode of HUD layout areas on/off.", (string[] args) =>
            {
                HUDLayoutSettings.DebugDraw = !HUDLayoutSettings.DebugDraw;
                NewMessage("HUD layout debug draw mode " + (HUDLayoutSettings.DebugDraw ? "enabled" : "disabled"), Color.Yellow);
            }));

            commands.Add(new Command("interactdebugdraw|debugdrawinteract", "interactdebugdraw: Toggle the debug drawing mode of item interaction ranges on/off.", (string[] args) =>
            {
                Character.DebugDrawInteract = !Character.DebugDrawInteract;
                NewMessage("Interact debug draw mode " + (Character.DebugDrawInteract ? "enabled" : "disabled"), Color.Yellow);
            }, isCheat: true));

            AssignOnExecute("togglehud|hud", (string[] args) =>
            {
                GUI.DisableHUD = !GUI.DisableHUD;
                GameMain.Instance.IsMouseVisible = !GameMain.Instance.IsMouseVisible;
                NewMessage(GUI.DisableHUD ? "Disabled HUD" : "Enabled HUD", Color.Yellow);
            });
            AssignRelayToServer("togglehud|hud", false);

            AssignOnExecute("toggleupperhud", (string[] args) =>
            {
                GUI.DisableUpperHUD = !GUI.DisableUpperHUD;
                NewMessage(GUI.DisableUpperHUD ? "Disabled upper HUD" : "Enabled upper HUD", Color.Yellow);
            });
            AssignRelayToServer("toggleupperhud", false);

            AssignOnExecute("toggleitemhighlights", (string[] args) =>
            {
                GUI.DisableItemHighlights = !GUI.DisableItemHighlights;
                NewMessage(GUI.DisableItemHighlights ? "Disabled item highlights" : "Enabled item highlights", Color.Yellow);
            });
            AssignRelayToServer("toggleitemhighlights", false);

            AssignOnExecute("togglecharacternames", (string[] args) =>
            {
                GUI.DisableCharacterNames = !GUI.DisableCharacterNames;
                NewMessage(GUI.DisableCharacterNames ? "Disabled character names" : "Enabled character names", Color.Yellow);
            });
            AssignRelayToServer("togglecharacternames", false);

            AssignOnExecute("followsub", (string[] args) =>
            {
                Camera.FollowSub = !Camera.FollowSub;
                NewMessage(Camera.FollowSub ? "Set the camera to follow the closest submarine" : "Disabled submarine following.", Color.Yellow);
            });
            AssignRelayToServer("followsub", false);

            AssignOnExecute("toggleaitargets|aitargets", (string[] args) =>
            {
                AITarget.ShowAITargets = !AITarget.ShowAITargets;
                NewMessage(AITarget.ShowAITargets ? "Enabled AI target drawing" : "Disabled AI target drawing", Color.Yellow);
            });
            AssignRelayToServer("toggleaitargets|aitargets", false);

            AssignOnExecute("debugai", (string[] args) =>
            {
                HumanAIController.DebugAI = !HumanAIController.DebugAI;
                if (HumanAIController.DebugAI)
                {
                    GameMain.DevMode = true;
                    GameMain.DebugDraw = true;
                    GameMain.LightManager.LightingEnabled = false;
                    GameMain.LightManager.LosEnabled = false;
                }
                else
                {
                    GameMain.DevMode = false;
                    GameMain.DebugDraw = false;
                    GameMain.LightManager.LightingEnabled = true;
                    GameMain.LightManager.LosEnabled = true;
                    GameMain.LightManager.LosAlpha = 1f;
                }
                NewMessage(HumanAIController.DebugAI ? "AI debug info visible" : "AI debug info hidden", Color.Yellow);
            });
            AssignRelayToServer("debugai", false);

            AssignOnExecute("showmonsters", (string[] args) =>
            {
                CreatureMetrics.UnlockAll = true;
                CreatureMetrics.Save();
                NewMessage("All monsters are now visible in the character editor.", Color.Yellow);
                if (Screen.Selected == GameMain.CharacterEditorScreen)
                {
                    GameMain.CharacterEditorScreen.Deselect();
                    GameMain.CharacterEditorScreen.Select();
                }
            });
            AssignRelayToServer("showmonsters", false);

            AssignOnExecute("hidemonsters", (string[] args) =>
            {
                CreatureMetrics.UnlockAll = false;
                CreatureMetrics.Save();
                NewMessage("All monsters that haven't yet been encountered in the game are now hidden in the character editor.", Color.Yellow);
                if (Screen.Selected == GameMain.CharacterEditorScreen)
                {
                    GameMain.CharacterEditorScreen.Deselect();
                    GameMain.CharacterEditorScreen.Select();
                }
            });
            AssignRelayToServer("hidemonsters", false);

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
                    fabricableItems.AddRange(itemPrefab.FabricationRecipes.Values);
                }
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    int? minCost = itemPrefab.GetMinPrice();
                    int? fabricationCost = null;
                    int? deconstructProductCost = null;

                    var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == itemPrefab && f.RequiredItems.Any());
                    if (fabricationRecipe != null)
                    {
                        foreach (var ingredient in fabricationRecipe.RequiredItems)
                        {
                            int? ingredientPrice = ingredient.ItemPrefabs.Min(ip => ip.GetMinPrice());
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
                        if (!(MapEntityPrefab.Find(null, deconstructItem.ItemIdentifier, showErrorMessages: false) is ItemPrefab targetItem))
                        {
                            ThrowError("Error in item \"" + itemPrefab.Name + "\" - could not find deconstruct item \"" + deconstructItem.ItemIdentifier + "\"!");
                            continue;
                        }

                        float avgOutCondition = (deconstructItem.OutConditionMin + deconstructItem.OutConditionMax) / 2;

                        int? deconstructProductPrice = targetItem.GetMinPrice();
                        if (deconstructProductPrice.HasValue)
                        {
                            if (!deconstructProductCost.HasValue) { deconstructProductCost = 0; }
                            deconstructProductCost += (int)(deconstructProductPrice * avgOutCondition);
                        }

                        if (fabricationRecipe != null)
                        {
                            var ingredient = fabricationRecipe.RequiredItems.Find(r => r.ItemPrefabs.Contains(targetItem));

                            if (ingredient == null)
                            {
                                foreach (var requiredItem in fabricationRecipe.RequiredItems)
                                {
                                    foreach (var itemPrefab2 in requiredItem.ItemPrefabs)
                                    {
                                        foreach (var recipe in itemPrefab2.FabricationRecipes.Values)
                                        {
                                            ingredient ??= recipe.RequiredItems.Find(r => r.ItemPrefabs.Contains(targetItem));
                                        }
                                    }
                                }
                            }

                            if (ingredient == null)
                            {
                                NewMessage("Deconstructing \"" + itemPrefab.Name + "\" produces \"" + deconstructItem.ItemIdentifier + "\", which isn't required in the fabrication recipe of the item.", Color.Red);
                            }
                            else if (ingredient.UseCondition && ingredient.MinCondition < avgOutCondition)
                            {
                                NewMessage($"Deconstructing \"{itemPrefab.Name}\" produces more \"{deconstructItem.ItemIdentifier}\", than what's required to fabricate the item (required: {targetItem.Name} {(int)(ingredient.MinCondition * 100)}%, output: {deconstructItem.ItemIdentifier} {(int)(avgOutCondition * 100)}%)", Color.Red);
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

            commands.Add(new Command("analyzeitem", "analyzeitem: Analyzes one item for exploits.", (string[] args) =>
            {
                if (args.Length < 1) { return; }

                List<FabricationRecipe> fabricableItems = new List<FabricationRecipe>();
                foreach (ItemPrefab iPrefab in ItemPrefab.Prefabs)
                {
                    fabricableItems.AddRange(iPrefab.FabricationRecipes.Values);
                }

                string itemNameOrId = args[0].ToLowerInvariant();

                ItemPrefab itemPrefab =
                    (MapEntityPrefab.Find(itemNameOrId, identifier: null, showErrorMessages: false) ??
                    MapEntityPrefab.Find(null, identifier: itemNameOrId.ToIdentifier(), showErrorMessages: false)) as ItemPrefab;

                if (itemPrefab == null)
                {
                    NewMessage("Item not found for analyzing.");
                    return;
                }
                NewMessage("Analyzing item " + itemPrefab.Name + " with base cost " + itemPrefab.DefaultPrice.Price);

                var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == itemPrefab);
                // omega nesting incoming
                if (fabricationRecipe != null)
                {
                    foreach (var priceInfo in itemPrefab.GetSellPricesOver(0))
                    {
                        NewMessage($"    If bought at {GetSeller(priceInfo.Value)} it costs {priceInfo.Value.Price}");
                        int totalPrice = 0;
                        int? totalBestPrice = 0;
                        foreach (var ingredient in fabricationRecipe.RequiredItems)
                        {
                            foreach (ItemPrefab ingredientItemPrefab in ingredient.ItemPrefabs)
                            {
                                int defaultPrice = ingredientItemPrefab.DefaultPrice?.Price ?? 0;
                                NewMessage($"        Its ingredient {ingredientItemPrefab.Name} has base cost {defaultPrice}");
                                totalPrice += defaultPrice;
                                totalBestPrice += ingredientItemPrefab.GetMinPrice();
                                int basePrice = defaultPrice;
                                foreach (var ingredientItemPriceInfo in ingredientItemPrefab.GetBuyPricesUnder())
                                {
                                    if (basePrice > ingredientItemPriceInfo.Value.Price)
                                    {
                                        NewMessage($"            {GetSeller(ingredientItemPriceInfo.Value).CapitaliseFirstInvariant()} sells ingredient {ingredientItemPrefab.Name} for cheaper, {ingredientItemPriceInfo.Value.Price}", Color.Yellow);
                                    }
                                    else
                                    {
                                        NewMessage($"            {GetSeller(ingredientItemPriceInfo.Value).CapitaliseFirstInvariant()} sells ingredient {ingredientItemPrefab.Name} for more, {ingredientItemPriceInfo.Value.Price}", Color.Teal);
                                    }
                                }
                            }
                        }
                        int costDifference = itemPrefab.DefaultPrice.Price - totalPrice;
                        NewMessage($"    Constructing the item from store-bought items provides {costDifference} profit with default values.");

                        if (totalBestPrice.HasValue)
                        {
                            int? bestDifference = priceInfo.Value.Price - totalBestPrice;
                            NewMessage($"    Constructing the item from store-bought items provides {bestDifference} profit with best-case scenario values.");
                        }

                        static string GetSeller(PriceInfo priceInfo) => $"store with identifier \"{priceInfo.StoreIdentifier}\"";
                    }
                }
            },
            () =>
            {
                return new string[][] { ItemPrefab.Prefabs.SelectMany(p => p.Aliases).Concat(ItemPrefab.Prefabs.Select(p => p.Identifier.Value)).ToArray() };
            }, isCheat: false));

            commands.Add(new Command("checkcraftingexploits", "checkcraftingexploits: Finds outright item exploits created by buying store-bought ingredients and constructing them into sellable items.", (string[] args) =>
            {
                List<FabricationRecipe> fabricableItems = new List<FabricationRecipe>();
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    fabricableItems.AddRange(itemPrefab.FabricationRecipes.Values);
                }
                List<Tuple<string, int>> costDifferences = new List<Tuple<string, int>>();

                int maximumAllowedCost = 5;

                if (args.Length > 0)
                {
                    Int32.TryParse(args[0], out maximumAllowedCost);
                }
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    int? defaultCost = itemPrefab.DefaultPrice?.Price;
                    int? fabricationCostStore = null;

                    var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == itemPrefab);
                    if (fabricationRecipe == null)
                    {
                        continue;
                    }

                    bool canBeBought = true;

                    foreach (var ingredient in fabricationRecipe.RequiredItems)
                    {
                        int? ingredientPrice = ingredient.ItemPrefabs.Where(p => p.CanBeBought).Min(ip => ip.DefaultPrice?.Price);
                        if (ingredientPrice.HasValue)
                        {
                            if (!fabricationCostStore.HasValue) { fabricationCostStore = 0; }
                            float useAmount = ingredient.UseCondition ? ingredient.MinCondition : 1.0f;
                            fabricationCostStore += (int)(ingredientPrice.Value * ingredient.Amount * useAmount);
                        }
                        else
                        {
                            canBeBought = false;
                        }
                    }
                    if (fabricationCostStore.HasValue && defaultCost.HasValue && canBeBought)
                    {
                        int costDifference = defaultCost.Value - fabricationCostStore.Value;
                        if (costDifference > maximumAllowedCost || costDifference < 0f)
                        {
                            float ratio = (float)fabricationCostStore.Value / defaultCost.Value;
                            string message = $"Fabricating \"{itemPrefab.Name}\" costs {(int)(ratio * 100)}% of the price of the item, or {costDifference} more. Item price: {defaultCost.Value}, ingredient prices: {fabricationCostStore.Value}";
                            costDifferences.Add(new Tuple<string, int>(message, costDifference));
                        }
                    }
                }

                costDifferences.Sort((x, y) => x.Item2.CompareTo(y.Item2));

                foreach (Tuple<string, int> costDifference in costDifferences)
                {
                    Color color = Color.Yellow;
                    NewMessage(costDifference.Item1, color);
                }
            }, isCheat: false));

            commands.Add(new Command("adjustprice", "adjustprice: Recursively prints out expected price adjustments for items derived from this item.", (string[] args) =>
            {
                List<FabricationRecipe> fabricableItems = new List<FabricationRecipe>();
                foreach (ItemPrefab iP in ItemPrefab.Prefabs)
                {
                    fabricableItems.AddRange(iP.FabricationRecipes.Values);
                }
                if (args.Length < 2)
                {
                    NewMessage("Item or value not defined.");
                    return;
                }
                string itemNameOrId = args[0].ToLowerInvariant();

                ItemPrefab materialPrefab =
                    (MapEntityPrefab.Find(itemNameOrId, identifier: null, showErrorMessages: false) ??
                    MapEntityPrefab.Find(null, identifier: itemNameOrId, showErrorMessages: false)) as ItemPrefab;

                if (materialPrefab == null)
                {
                    NewMessage("Item not found for price adjustment.");
                    return;
                }

                AdjustItemTypes adjustItemType = AdjustItemTypes.NoAdjustment;
                if (args.Length > 2)
                {
                    switch (args[2].ToLowerInvariant())
                    {
                        case "add":
                            adjustItemType = AdjustItemTypes.Additive;
                            break;
                        case "mult":
                            adjustItemType = AdjustItemTypes.Multiplicative;
                            break;
                    }
                }

                if (Int32.TryParse(args[1].ToLowerInvariant(), out int newPrice))
                {
                    Dictionary<ItemPrefab, int> newPrices = new Dictionary<ItemPrefab, int>();
                    PrintItemCosts(newPrices, materialPrefab, fabricableItems, newPrice, true, adjustItemType: adjustItemType);
                    PrintItemCosts(newPrices, materialPrefab, fabricableItems, newPrice, false, adjustItemType: adjustItemType);
                }

            }, isCheat: false));

            commands.Add(new Command("deconstructvalue", "deconstructvalue: Views and compares deconstructed component prices for this item.", (string[] args) =>
            {
                List<FabricationRecipe> fabricableItems = new List<FabricationRecipe>();
                foreach (ItemPrefab iP in ItemPrefab.Prefabs)
                {
                    fabricableItems.AddRange(iP.FabricationRecipes.Values);
                }
                if (args.Length < 1)
                {
                    NewMessage("Item not defined.");
                    return;
                }
                string itemNameOrId = args[0].ToLowerInvariant();

                ItemPrefab parentItem =
                    (MapEntityPrefab.Find(itemNameOrId, identifier: null, showErrorMessages: false) ??
                    MapEntityPrefab.Find(null, identifier: itemNameOrId, showErrorMessages: false)) as ItemPrefab;

                if (parentItem == null)
                {
                    NewMessage("Item not found for price adjustment.");
                    return;
                }

                var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == parentItem);
                int totalValue = 0;
                NewMessage(parentItem.Name + " has the price " + (parentItem.DefaultPrice?.Price ?? 0));
                if (fabricationRecipe != null)
                {
                    NewMessage("    It constructs from:");

                    foreach (RequiredItem requiredItem in fabricationRecipe.RequiredItems)
                    {
                        foreach (ItemPrefab itemPrefab in requiredItem.ItemPrefabs)
                        {
                            int defaultPrice = itemPrefab.DefaultPrice?.Price ?? 0;
                            NewMessage("        " + itemPrefab.Name + " has the price " + defaultPrice);
                            totalValue += defaultPrice;
                        }
                    }
                    NewMessage("Its total value was: " + totalValue);
                    totalValue = 0;
                }
                NewMessage("    The item deconstructs into:");
                foreach (DeconstructItem deconstructItem in parentItem.DeconstructItems)
                {
                    ItemPrefab itemPrefab =
                        (MapEntityPrefab.Find(deconstructItem.ItemIdentifier.Value, identifier: null, showErrorMessages: false) ??
                         MapEntityPrefab.Find(null, identifier: deconstructItem.ItemIdentifier, showErrorMessages: false)) as ItemPrefab;
                    if (itemPrefab == null)
                    {
                        ThrowError($"       Couldn't find deconstruct product \"{deconstructItem.ItemIdentifier}\"!");
                        continue;
                    }

                    int defaultPrice = itemPrefab.DefaultPrice?.Price ?? 0;
                    NewMessage("       " + itemPrefab.Name + " has the price " + defaultPrice);
                    totalValue += defaultPrice;
                }
                NewMessage("Its deconstruct value was: " + totalValue);

            }, isCheat: false));

            commands.Add(new Command("setentityproperties", "setentityproperties [property name] [value]: Sets the value of some property on all selected items/structures in the sub editor.", (string[] args) =>
            {
                if (args.Length != 2 || Screen.Selected != GameMain.SubEditorScreen) { return; }
                foreach (MapEntity me in MapEntity.SelectedList)
                {
                    bool propertyFound = false;
                    if (!(me is ISerializableEntity serializableEntity)) { continue; }                    
                    if (serializableEntity.SerializableProperties == null) { continue; }

                    if (serializableEntity.SerializableProperties.TryGetValue(args[0].ToIdentifier(), out SerializableProperty property))
                    {
                        propertyFound = true;
                        object prevValue = property.GetValue(me);
                        if (property.TrySetValue(me, args[1]))
                        {
                            NewMessage($"Changed the value \"{args[0]}\" from {(prevValue?.ToString() ?? null)} to {args[1]} on entity \"{me.ToString()}\".", Color.LightGreen);
                        }
                        else
                        {
                            NewMessage($"Failed to set the value of \"{args[0]}\" to \"{args[1]}\" on the entity \"{me.ToString()}\".", Color.Orange);
                        }
                    }
                    if (me is Item item)
                    {
                        foreach (ItemComponent ic in item.Components)
                        {
                            ic.SerializableProperties.TryGetValue(args[0].ToIdentifier(), out SerializableProperty componentProperty);
                            if (componentProperty == null) { continue; }
                            propertyFound = true;
                            object prevValue = componentProperty.GetValue(ic);
                            if (componentProperty.TrySetValue(ic, args[1]))
                            {
                                NewMessage($"Changed the value \"{args[0]}\" from {prevValue} to {args[1]} on item \"{me.ToString()}\", component \"{ic.GetType().Name}\".", Color.LightGreen);
                            }
                            else
                            {
                                NewMessage($"Failed to set the value of \"{args[0]}\" to \"{args[1]}\" on the item \"{me.ToString()}\", component \"{ic.GetType().Name}\".", Color.Orange);
                            }
                        }
                    }
                    if (!propertyFound)
                    {
                        NewMessage($"Property \"{args[0]}\" not found in the entity \"{me.ToString()}\".", Color.Orange);
                    }
                }
            },
            () =>
            {
                List<Identifier> propertyList = new List<Identifier>();
                foreach (MapEntity me in MapEntity.SelectedList)
                {
                    if (!(me is ISerializableEntity serializableEntity)) { continue; }
                    if (serializableEntity.SerializableProperties == null) { continue; }
                    propertyList.AddRange(serializableEntity.SerializableProperties.Select(p => p.Key));
                    if (me is Item item)
                    {
                        foreach (ItemComponent ic in item.Components)
                        {
                            propertyList.AddRange(ic.SerializableProperties.Select(p => p.Key));
                        }
                    }
                }

                return new string[][]
                {
                    propertyList.Distinct().Select(i => i.Value).OrderBy(n => n).ToArray(),
                    Array.Empty<string>()
                };
            }));

            commands.Add(new Command("checkmissingloca", "", (string[] args) =>
            {
                void SwapLanguage(LanguageIdentifier language)
                {
                    var config = GameSettings.CurrentConfig;
                    config.Language = language;
                    GameSettings.SetCurrentConfig(config);
                }

                HashSet<string> missingTexts = new HashSet<string>();
                
                //key = text tag, value = list of languages the tag is missing from
                Dictionary<Identifier, HashSet<LanguageIdentifier>> missingTags = new Dictionary<Identifier, HashSet<LanguageIdentifier>>();
                Dictionary<LanguageIdentifier, HashSet<Identifier>> tags = new Dictionary<LanguageIdentifier, HashSet<Identifier>>();
                foreach (LanguageIdentifier language in TextManager.AvailableLanguages)
                {
                    SwapLanguage(language);
                    tags.Add(language, new HashSet<Identifier>(TextManager.GetAllTagTextPairs().Select(t => t.Key)));
                }

                foreach (LanguageIdentifier language in TextManager.AvailableLanguages)
                {
                    //check missing mission texts
                    foreach (var missionPrefab in MissionPrefab.Prefabs)
                    {
                        Identifier missionId = missionPrefab.ConfigElement.GetAttribute("textidentifier") == null ? 
                            missionPrefab.Identifier : 
                            missionPrefab.ConfigElement.GetAttributeIdentifier("textidentifier", Identifier.Empty);
                        
                        if (!tags[language].Contains(missionPrefab.ConfigElement.GetAttributeIdentifier("name", Identifier.Empty)))
                        {
                            addIfMissing($"missionname.{missionId}".ToIdentifier(), language);
                        }

                        if (missionPrefab.Type == MissionType.Combat)
                        {
                            addIfMissing($"MissionDescriptionNeutral.{missionId}".ToIdentifier(), language);
                            addIfMissing($"MissionDescription1.{missionId}".ToIdentifier(), language);
                            addIfMissing($"MissionDescription2.{missionId}".ToIdentifier(), language);
                            addIfMissing($"MissionTeam1.{missionId}".ToIdentifier(), language);
                            addIfMissing($"MissionTeam2.{missionId}".ToIdentifier(), language);
                        }
                        else
                        {
                            if (!tags[language].Contains(missionPrefab.ConfigElement.GetAttributeIdentifier("description", Identifier.Empty)))
                            {
                                addIfMissing($"missiondescription.{missionId}".ToIdentifier(), language);
                            }
                            if (!tags[language].Contains(missionPrefab.ConfigElement.GetAttributeIdentifier("successmessage", Identifier.Empty)))
                            {
                                addIfMissing($"missionsuccess.{missionId}".ToIdentifier(), language);
                            }
                            //only check failure message if there's something defined in the xml (otherwise we just use the generic "missionfailed" text)
                            if (missionPrefab.ConfigElement.GetAttribute("failuremessage") != null &&
                                !tags[language].Contains(missionPrefab.ConfigElement.GetAttributeIdentifier("failuremessage", Identifier.Empty)))
                            {
                                addIfMissing($"missionfailure.{missionId}".ToIdentifier(), language);
                            }
                        }
                        for (int i = 0; i < missionPrefab.Messages.Length; i++)
                        {
                            if (missionPrefab.Messages[i].IsNullOrWhiteSpace() || (missionPrefab.Messages[i] as FallbackLString)?.GetLastFallback() is FallbackLString { PrimaryIsLoaded: false })
                            {
                                addIfMissing($"MissionMessage{i}.{missionId}".ToIdentifier(), language);
                            }
                        }
                    }

                    foreach (Type itemComponentType in typeof(ItemComponent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ItemComponent))))
                    {
                        checkSerializableEntityType(itemComponentType);
                    }
                    checkSerializableEntityType(typeof(Item));
                    checkSerializableEntityType(typeof(Hull));
                    checkSerializableEntityType(typeof(Structure));

                    void checkSerializableEntityType(Type t)
                    {
                        foreach (var property in t.GetProperties())
                        {
                            if (!property.IsDefined(typeof(Editable), false)) { continue; }

                            string propertyTag = $"{property.DeclaringType.Name}.{property.Name}";

                            if (addIfMissingAll(language,
                                propertyTag.ToIdentifier(),
                                property.Name.ToIdentifier(),
                                $"sp.{property.Name}.name".ToIdentifier(),
                                $"sp.{propertyTag}.name".ToIdentifier()) && language == "English".ToLanguageIdentifier())
                            {
                                missingTexts.Add($"<sp.{propertyTag.ToLower()}.name>{property.Name.FormatCamelCaseWithSpaces()}</sp.{propertyTag.ToLower()}.name>");
                            }

                            var description = (property.GetCustomAttributes(true).First(a => a is Serialize) as Serialize).Description;

                            if (addIfMissingAll(language,
                                $"sp.{propertyTag}.description".ToIdentifier(),
                                $"sp.{property.Name}.description".ToIdentifier(),
                                $"{property.Name.ToIdentifier()}.description".ToIdentifier()) && language == "English".ToLanguageIdentifier())
                            {
                                missingTexts.Add($"<sp.{propertyTag.ToLower()}.description>{description}</sp.{propertyTag.ToLower()}.description>");
                            }
                        }
                    }

                    foreach (SubmarineInfo sub in SubmarineInfo.SavedSubmarines)
                    {
                        if (sub.Type != SubmarineType.Player || !sub.IsVanillaSubmarine()) { continue; }

                        addIfMissing($"submarine.name.{sub.Name}".ToIdentifier(), language);
                        addIfMissing(("submarine.description." + sub.Name).ToIdentifier(), language);
                    }

                    foreach (AfflictionPrefab affliction in AfflictionPrefab.List)
                    {
                        if (affliction.ShowIconThreshold > affliction.MaxStrength && 
                            affliction.ShowIconToOthersThreshold > affliction.MaxStrength && 
                            affliction.ShowInHealthScannerThreshold > affliction.MaxStrength)
                        {
                            //hidden affliction, no need for localization
                            continue;
                        }

                        Identifier afflictionId = affliction.TranslationIdentifier;
                        addIfMissing($"afflictionname.{afflictionId}".ToIdentifier(), language);

                        if (affliction.Descriptions.Any())
                        {
                            foreach (var description in affliction.Descriptions)
                            {
                                addIfMissing(description.TextTag, language);
                            }
                        }
                        else
                        {
                            addIfMissing($"afflictiondescription.{afflictionId}".ToIdentifier(), language);
                        }
                    }

                    foreach (var talentTree in TalentTree.JobTalentTrees)
                    {
                        foreach (var talentSubTree in talentTree.TalentSubTrees)
                        {
                            addIfMissing($"talenttree.{talentSubTree.Identifier}".ToIdentifier(), language);
                        }
                    }

                    foreach (var talent in TalentPrefab.TalentPrefabs)
                    {
                        addIfMissing($"talentname.{talent.Identifier}".ToIdentifier(), language);
                    }

                    //check missing entity names
                    foreach (MapEntityPrefab me in MapEntityPrefab.List)
                    {
                        Identifier nameIdentifier = ("entityname." + me.Identifier).ToIdentifier();
                        if (tags[language].Contains(nameIdentifier)) { continue; }
                        if (me.HideInMenus) { continue; }

                        ContentXElement configElement = null;

                        if (me is ItemPrefab itemPrefab)
                        {
                            configElement = itemPrefab.ConfigElement;                        
                        }
                        else if (me is StructurePrefab structurePrefab)
                        {
                            configElement = structurePrefab.ConfigElement;                       
                        }
                        if (configElement != null)
                        {
                            var overrideIdentifier = configElement.GetAttributeIdentifier("nameidentifier", null);
                            if (overrideIdentifier != null && tags[language].Contains("entityname." + overrideIdentifier)) { continue; }
                        }

                        addIfMissing(nameIdentifier, language);
                    }
                }

                foreach (Identifier englishTag in tags[TextManager.DefaultLanguage])
                {
                    foreach (LanguageIdentifier language in TextManager.AvailableLanguages)
                    {
                        if (language == TextManager.DefaultLanguage) { continue; }
                        addIfMissing(englishTag, language);
                    }
                }

                List<string> lines = new List<string>
                {
                    "Missing from English:"
                };

                Dictionary<string, List<string>> missingByLanguages = new Dictionary<string, List<string>>();
                List<string> missingFromEnglish = new List<string>();
                foreach (KeyValuePair<Identifier, HashSet<LanguageIdentifier>> kvp in missingTags)
                {
                    if (kvp.Value.Contains(TextManager.DefaultLanguage))
                    {
                        missingFromEnglish.Add(kvp.Key.Value);
                    }
                    else
                    {
                        string languagesStr = string.Join(", ", kvp.Value.OrderBy(v => v.Value.Value));
                        if (!missingByLanguages.ContainsKey(languagesStr))
                        {
                            missingByLanguages.Add(languagesStr, new List<string>());
                        }
                        missingByLanguages[languagesStr].Add(kvp.Key.Value);
                    }
                }
                foreach (string text in missingFromEnglish.OrderBy(v => v))
                {
                    lines.Add(text);
                }

                foreach (KeyValuePair<string, List<string>> missingByLanguage in missingByLanguages)
                {
                    lines.Add(string.Empty);
                    lines.Add($"Missing from {missingByLanguage.Key}");
                    foreach (string text in missingByLanguage.Value.OrderBy(v => v))
                    {
                        lines.Add(text);
                    }
                }
                
                string filePath = "missingloca.txt";
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, lines);
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
                SwapLanguage(TextManager.DefaultLanguage);

                if (missingTexts.Any())
                {
                    ShowQuestionPrompt("Dump the property names and descriptions missing from English to a new xml file? Y/N",
                        (option) =>
                        {
                            if (option.ToLowerInvariant() == "y")
                            {
                                string path = "newtexts.txt";
                                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                                File.WriteAllLines(path, missingTexts);
                                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                                ToolBox.OpenFileWithShell(Path.GetFullPath(path));
                                SwapLanguage(TextManager.DefaultLanguage);
                            }
                        });
                }
                
                void addIfMissing(Identifier tag, LanguageIdentifier language)
                {
                    if (!tags[language].Contains(tag))
                    {
                        if (!missingTags.ContainsKey(tag)) { missingTags[tag] = new HashSet<LanguageIdentifier>(); }
                        missingTags[tag].Add(language);
                    }
                }
                bool addIfMissingAll(LanguageIdentifier language, params Identifier[] potentialTags)
                {
                    if (!potentialTags.Any(t => tags[language].Contains(t)))
                    {
                        var tag = potentialTags.First();
                        if (!missingTags.ContainsKey(tag)) { missingTags[tag] = new HashSet<LanguageIdentifier>(); }
                        missingTags[tag].Add(language);
                        return true;
                    }
                    return false;
                }
            }));


            commands.Add(new Command("checkduplicateloca", "", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    ThrowError("Please specify a file path.");
                    return;
                }
                XDocument doc1 = XMLExtensions.TryLoadXml(args[0]);
                if (doc1?.Root == null)
                {
                    ThrowError($"Could not load the file \"{args[0]}\"");
                    return;
                }
                List<(string tag, string text)> texts = new List<(string tag, string text)>();

                bool duplicatesFound = false;
                foreach (XElement element in doc1.Root.Elements())
                {
                    string tag = element.Name.ToString();
                    string text = element.ElementInnerText();
                    if (texts.Any(t => t.tag == tag))
                    {
                        ThrowError($"Duplicate tag \"{tag}\".");
                        duplicatesFound = true;
                    }
                }
                if (duplicatesFound)
                {
                    ThrowError($"Aborting, please fix duplicate tags in the file and try again.");
                    return;
                }

                foreach (XElement element in doc1.Root.Elements())
                {
                    string tag = element.Name.ToString();
                    string text = element.ElementInnerText();
                    if (texts.Any(t => t.text == text))
                    {
                        if (tag.StartsWith("sp."))
                        {
                            string[] split = tag.Split('.');
                            if (split.Length > 3)
                            {
                                texts.RemoveAll(t => t.text == text);
                                string newTag = $"sp.{split[2]}.{split[3]}";
                                texts.Add((newTag, text));
                                NewMessage($"Duplicate text \"{tag}\", merging to \"{newTag}\".");
                            }
                            else
                            {
                                NewMessage($"Duplicate text \"{tag}\", using existing one \"{texts.Find(t => t.text == text).tag}\".");
                            }
                        }
                        else
                        {
                            texts.Add((tag, text));
                            ThrowError($"Duplicate text \"{tag}\". Could not determine if the text can be merged with an existing one, please check it manually.");
                        }
                    }
                    else
                    {
                        texts.Add((tag, text));
                    }
                }

                string filePath = "uniquetexts.xml";
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, texts.Select(t => $"<{t.tag}>{t.text}</{t.tag}>"));
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
            }));

            commands.Add(new Command("comparelocafiles", "comparelocafiles [file1] [file2]", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    ThrowError("Please specify two files two compare.");
                    return;
                }

                XDocument doc1 = XMLExtensions.TryLoadXml(args[0]);
                if (doc1?.Root == null)
                {
                    ThrowError($"Could not load the file \"{args[0]}\"");
                    return;
                }
                XDocument doc2 = XMLExtensions.TryLoadXml(args[1]);
                if (doc2?.Root == null)
                {
                    ThrowError($"Could not load the file \"{args[1]}\"");
                    return;
                }

                var content1 = getContent(doc1.Root);
                var language1 = doc1.Root.GetAttributeIdentifier("language", string.Empty);

                var content2 = getContent(doc2.Root);
                var language2 = doc2.Root.GetAttributeIdentifier("language", string.Empty);

                foreach (KeyValuePair<string, string> kvp in content1)
                {
                    if (!content2.ContainsKey(kvp.Key))
                    {
                        ThrowError($"File 2 doesn't contain the text tag \"{kvp.Key}\"");
                    }
                    else if (language1 == language2 && content2[kvp.Key] != kvp.Value)
                    {
                        ThrowError($"Texts for the tag \"{kvp.Key}\" don't match:\n1. {kvp.Value}\n2. {content2[kvp.Key]}");                        
                    }
                }
                foreach (KeyValuePair<string, string> kvp in content2)
                {
                    if (!content1.ContainsKey(kvp.Key))
                    {
                        ThrowError($"File 1 doesn't contain the text tag \"{kvp.Key}\"");
                    }
                }

                static Dictionary<string, string> getContent(XElement element)
                {
                    Dictionary<string, string> content = new Dictionary<string, string>();
                    foreach (XElement subElement in element.Elements())
                    {
                        string key = subElement.Name.ToString().ToLowerInvariant();
                        if (content.ContainsKey(key)) { continue; }
                        content.Add(key, subElement.ElementInnerText());
                    }
                    return content;
                }
            }));

            commands.Add(new Command("eventstats", "", (string[] args) =>
            {
                List<string> debugLines;
                if (args.Length > 0)
                {
                    if (!Enum.TryParse(args[0], ignoreCase: true, out Level.PositionType spawnType))
                    {
                        var enums = Enum.GetNames(typeof(Level.PositionType));
                        ThrowError($"\"{args[0]}\" is not a valid Level.PositionType. Available options are: {string.Join(", ", enums)}");
                        return;
                    }
                    bool fullLog = false;
                    if (args.Length > 1)
                    {
                        bool.TryParse(args[1], out fullLog);
                    }
                    debugLines = EventSet.GetDebugStatistics(filter: monsterEvent => monsterEvent.SpawnPosType.HasFlag(spawnType), fullLog: fullLog);
                }
                else
                {
                    debugLines = EventSet.GetDebugStatistics();
                }
                string filePath = "eventstats.txt";
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, debugLines);
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
            }));

            commands.Add(new Command("setfreecamspeed", "setfreecamspeed [speed]: Set the camera movement speed when not controlling a character. Defaults to 1.", (string[] args) =>
            {
                if (args.Length > 0) 
                { 
                    float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out float speed);
                    Screen.Selected.Cam.FreeCamMoveSpeed = speed;
                }
            }));

#if DEBUG
            commands.Add(new Command("setplanthealth", "setplanthealth [value]: Sets the health of the selected plant in sub editor.", (string[] args) =>
            {
                if (1 > args.Length || Screen.Selected != GameMain.SubEditorScreen) { return; }

                string arg = args[0];

                if (!float.TryParse(arg, out float value))
                {
                    ThrowError($"{arg} is not a valid value.");
                    return;
                }

                foreach (MapEntity me in MapEntity.SelectedList)
                {
                    if (me is Item it)
                    {
                        if (it.GetComponent<Planter>() is { } planter)
                        {
                            foreach (Growable seed in planter.GrowableSeeds.Where(s => s != null))
                            {
                                NewMessage($"Set the health of {seed.Name} to {value} (from {seed.Health})");
                                seed.Health = value;
                            }
                        } 
                        else if (it.GetComponent<Growable>() is { } seed)
                        {
                            NewMessage($"Set the health of {seed.Name} to {value} (from {seed.Health})");
                            seed.Health = value;
                        }
                    }
                }
            }));

            commands.Add(new Command("showballastflorasprite", "", (string[] args) =>
            {
                BallastFloraBehavior.AlwaysShowBallastFloraSprite = !BallastFloraBehavior.AlwaysShowBallastFloraSprite;
                NewMessage("ok", GUIStyle.Green);
            }));

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
                    NewMessage(prefab.Name + " " + prefab.Identifier + " " + prefab.GetType().ToString());
                }
            }));

            commands.Add(new Command("copycharacterinfotoclipboard", "", (string[] args) =>
            {
                if (Character.Controlled?.Info != null)
                {
                    XElement element = Character.Controlled?.Info.Save(null);
                    Clipboard.SetText(element.ToString());
                    DebugConsole.NewMessage($"Copied the characterinfo of {Character.Controlled.Name} to clipboard.");
                }
            }));

            commands.Add(new Command("spawnallitems", "", (string[] args) =>
            {
                var cursorPos = Screen.Selected.Cam?.ScreenToWorld(PlayerInput.MousePosition) ?? Vector2.Zero;
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    Entity.Spawner?.AddItemToSpawnQueue(itemPrefab, cursorPos);
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
                                item.Rect = item.DefaultRect = new Rectangle(item.Rect.X, item.Rect.Y,
                                    (int)(item.Prefab.Sprite.size.X * item.Prefab.Scale),
                                    (int)(item.Prefab.Sprite.size.Y * item.Prefab.Scale));
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
            commands.Add(new Command("bumpitem", "", (string[] args) =>
            {
                float vel = 10.0f;
                if (args.Length > 0)
                {
                    float.TryParse(args[0], NumberStyles.Number, CultureInfo.InvariantCulture, out vel);
                }
                Character.Controlled?.FocusedItem?.body?.ApplyLinearImpulse(Rand.Vector(vel));
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
                doc.SaveSafe(destinationPath);
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
                destinationDoc.SaveSafe(destinationPath);
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
                    lines.Add($"<EntityName.{me.Identifier}>{me.Name}</EntityName.{me.Identifier}>");
                    lines.Add($"<EntityDescription.{me.Identifier}>{me.Description}</EntityDescription.{me.Identifier}>");
                }
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, lines);
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
            }));

            commands.Add(new Command("dumpeventtexts", "dumpeventtexts [filepath]: gets the texts from event files and and writes them into a file along with xml tags that can be used in translation files. If the filepath is omitted, the file is written to Content/Texts/EventTexts.txt", (string[] args) =>
            {
                string filePath = args.Length > 0 ? args[0] : "Content/Texts/EventTexts.txt";
                List<string> lines = new List<string>();
                HashSet<XDocument> docs = new HashSet<XDocument>();
                HashSet<string> textIds = new HashSet<string>();

                foreach (EventPrefab eventPrefab in EventSet.GetAllEventPrefabs())
                {
                    if (eventPrefab.Identifier.IsEmpty) 
                    {
                        continue;
                    }
                    docs.Add(eventPrefab.ConfigElement.Document);
                    getTextsFromElement(eventPrefab.ConfigElement, lines, eventPrefab.Identifier.Value);
                }
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, lines);
                try
                {
                    ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
                }
                catch (Exception e)
                {
                    ThrowError($"Failed to open the file \"{filePath}\".", e);
                }

                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    NewLineOnAttributes = false
                };                
                foreach (XDocument doc in docs)
                {
                    using (var writer = XmlWriter.Create(new System.Uri(doc.BaseUri).LocalPath, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }
                }
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;

                void getTextsFromElement(XElement element, List<string> list, string parentName)
                {
                    string text = element.GetAttributeString("text", null);
                    string textId = $"EventText.{parentName}";
                    if (!string.IsNullOrEmpty(text) && !text.Contains("EventText.", StringComparison.OrdinalIgnoreCase)) 
                    { 
                        list.Add($"<{textId}>{text}</{textId}>");
                        element.SetAttributeValue("text", textId);
                    }

                    int i = 1;
                    foreach (var subElement in element.Elements())
                    {
                        switch (subElement.Name.ToString().ToLowerInvariant())
                        {
                            case "conversationaction":     
                                while (textIds.Contains(parentName+".c"+i))
                                {
                                    i++;
                                }
                                parentName += ".c" + i;           
                                break;
                            case "option":
                                while (textIds.Contains(parentName.Substring(0, parentName.Length - 3) + ".o" + i))
                                {
                                    i++;
                                }
                                parentName = parentName.Substring(0, parentName.Length - 3) + ".o" + i;
                                break;
                        }
                        textIds.Add(parentName);
                        getTextsFromElement(subElement, list, parentName);
                    }
                }
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


                    
                    Dictionary<Identifier, SerializableProperty> dictionary = new Dictionary<Identifier, SerializableProperty>();
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
                        string defaultValueString = serialize.DefaultValue?.ToString() ?? "";
                        if (property.PropertyType == typeof(float))
                        {
                            defaultValueString = ((float)serialize.DefaultValue).ToString(CultureInfo.InvariantCulture);
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
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(filePath, lines);
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
                ToolBox.OpenFileWithShell(Path.GetFullPath(filePath));
            }));

#if DEBUG
            commands.Add(new Command("checkduplicates", "Checks the given language for duplicate translation keys and writes to file.", (string[] args) =>
            {
                if (args.Length != 1) { return; }
                TextManager.CheckForDuplicates(args[0].ToIdentifier().ToLanguageIdentifier());
            }));

            commands.Add(new Command("writetocsv|xmltocsv", "Writes the default language (English) to a .csv file.", (string[] args) =>
            {
                TextManager.WriteToCSV();
                NPCConversation.WriteToCSV();
            }));

            commands.Add(new Command("csvtoxml", "csvtoxml -> Converts .csv localization files Content/Texts/Texts.csv and Content/Texts/NPCConversations.csv to .xml for use in-game.", (string[] args) =>
            {
                ShowQuestionPrompt("Do you want to save the text files to the project folder (../../../BarotraumaShared/Content/Texts/)? If not, they are saved in the current working directory. Y/N",
                    (option1) => 
                    {
                        ShowQuestionPrompt("Do you want to convert the NPC conversations as well? Y/N",
                            (option2) =>
                            {
                                LocalizationCSVtoXML.ConvertMasterLocalizationKit(
                                    option1.ToLowerInvariant() == "y" ? "../../../BarotraumaShared/Content/Texts/" : "Content/Texts",
                                    option1.ToLowerInvariant() == "y" ? "../../../BarotraumaShared/Content/NPCConversations/" : "Content/NPCConversations",
                                    convertConversations: option2.ToLowerInvariant() == "y");
                            });
                    });
            }));

            commands.Add(new Command("printproperties", "Goes through the currently collected property list for missing localizations and writes them to a file.", (string[] args) =>
            {
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\propertylocalization.txt";
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = true;
                File.WriteAllLines(path, SerializableEntityEditor.MissingLocalizations);
                Barotrauma.IO.Validation.SkipValidationInDebugBuilds = false;
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
                        List<(object obj, SerializableProperty property)> allProperties = new List<(object obj, SerializableProperty property)>();

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
                                allProperties.Add((entity, properties[k]));
                            }
                        }

                        for (int j = 0; j < allProperties.Count; j++)
                        {
                            var property = allProperties[j].property;
                            string propertyName = (allProperties[j].obj.GetType().Name + "." + property.PropertyInfo.Name).ToLowerInvariant();
                            LocalizedString displayName = TextManager.Get($"sp.{propertyName}.name");
                            if (displayName.IsNullOrEmpty())
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

                ContentPackageManager.EnabledPackages.ReloadCore();
            }));

#if WINDOWS
            commands.Add(new Command("startdedicatedserver", "", (string[] args) =>
            {
                Process.Start("DedicatedServer.exe");
            }));

            commands.Add(new Command("editserversettings", "", (string[] args) =>
            {
                if (Process.GetProcessesByName("DedicatedServer").Length > 0)
                {
                    NewMessage("Can't be edited if DedicatedServer.exe is already running", Color.Red);
                }
                else
                {
                    Process.Start("notepad.exe", "serversettings.xml");
                }
            }));
#endif

#warning TODO: reimplement?
            /*commands.Add(new Command("ingamemodswap", "", (string[] args) =>
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
            }));*/

            AssignOnClientExecute(
                "giveperm",
                (string[] args) =>
                {
                    if (args.Length < 1) { return; }

                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }
                    ShowQuestionPrompt("Permission to grant to client " + args[0] + "?", (perm) =>
                    {
                        GameMain.Client?.SendConsoleCommand("giveperm \"" + args[0] + "\" " + perm);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "revokeperm",
                (string[] args) =>
                {
                    if (args.Length < 1) { return; }

                    if (args.Length < 2)
                    {
                        NewMessage("Valid permissions are:", Color.White);
                        foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                        {
                            NewMessage(" - " + permission.ToString(), Color.White);
                        }
                    }

                    ShowQuestionPrompt("Permission to revoke from client " + args[0] + "?", (perm) =>
                    {
                        GameMain.Client?.SendConsoleCommand("revokeperm \"" + args[0] + "\" " + perm);
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
                        NewMessage(" - " + permissionPreset.DisplayName, Color.White);
                    }
                    ShowQuestionPrompt("Rank to grant to client " + args[0] + "?", (rank) =>
                    {
                        GameMain.Client?.SendConsoleCommand("giverank \"" + args[0] + "\" " + rank);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "givecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    ShowQuestionPrompt("Console command permissions to grant to client " + args[0] + "? You may enter multiple commands separated with a space or use \"all\" to give the permission to use all console commands.", (commandNames) =>
                    {
                        GameMain.Client?.SendConsoleCommand("givecommandperm \"" + args[0] + "\" " + commandNames);
                    }, args, 1);
                }
            );

            AssignOnClientExecute(
                "revokecommandperm",
                (string[] args) =>
                {
                    if (args.Length < 1) return;

                    ShowQuestionPrompt("Console command permissions to revoke from client " + args[0] + "? You may enter multiple commands separated with a space or use \"all\" to revoke the permission to use any console commands.", (commandNames) =>
                    {
                        GameMain.Client?.SendConsoleCommand("revokecommandperm \"" + args[0] + "\" " + commandNames);
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
                "banaddress|banip",
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
                                "banaddress " +
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
                GameMain.Client.UnbanPlayer(clientName);
            }));

            commands.Add(new Command("unbanaddress", "unbanaddress [endpoint]: Unban a specific endpoint.", (string[] args) =>
            {
                if (GameMain.Client == null || args.Length == 0) return;
                if (Endpoint.Parse(args[0]).TryUnwrap(out var endpoint))
                {
                    GameMain.Client.UnbanPlayer(endpoint);
                }
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

#if DEBUG
            commands.Add(new Command("setcurrentlocationtype", "setcurrentlocationtype [location type]: Change the type of the current location.", (string[] args) =>
            {
                var character = Character.Controlled;
                if (GameMain.GameSession?.Campaign == null)
                {
                    ThrowError("Campaign not active!");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please give the location type after the command.");
                    return;
                }
                var locationType = LocationType.Prefabs.Find(lt => lt.Identifier == args[0]);
                if (locationType == null)
                {
                    ThrowError($"Could not find the location type \"{args[0]}\".");
                    return;
                }
                GameMain.GameSession.Campaign.Map.CurrentLocation.ChangeType(GameMain.GameSession.Campaign, locationType);
            },
            () =>
            {
                return new string[][]
                {
                    LocationType.Prefabs.Select(lt => lt.Identifier.Value).ToArray()
                };
            }));
#endif

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
                if (GameMain.NetworkMember != null)
                {
                    ThrowError("Cannot use the flipx command while playing online.");
                    return;
                }
                if (Submarine.MainSub.SubBody != null) { Submarine.MainSub?.FlipX(); }
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

            commands.Add(new Command("spawnsub", "spawnsub [subname] [is thalamus]: Spawn a submarine at the position of the cursor", (string[] args) =>
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
                    var subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.DisplayName.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                    if (subInfo == null)
                    {
                        ThrowError($"Could not find a submarine with the name \"{args[0]}\".");
                    }
                    else
                    {
                        Submarine spawnedSub = Submarine.Load(subInfo, false);
                        spawnedSub.SetPosition(GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition));
                        if (subInfo.Type == SubmarineType.Wreck)
                        {
                            spawnedSub.MakeWreck();
                            if (args.Length > 1 && bool.TryParse(args[1], out bool isThalamus))
                            {
                                if (isThalamus)
                                {
                                    spawnedSub.CreateWreckAI();
                                }
                                else
                                {
                                    spawnedSub.DisableWreckAI();
                                }
                            }
                            else
                            {
                                spawnedSub.DisableWreckAI();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    string errorMsg = "Failed to spawn a submarine. Arguments: \"" + string.Join(" ", args) + "\".";
                    ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("DebugConsole.SpawnSubmarine:Error", GameAnalyticsManager.ErrorSeverity.Error, errorMsg + '\n' + e.Message + '\n' + e.StackTrace.CleanupStackTrace());
                }
            },
            () =>
            {
                return new string[][]
                {
                    SubmarineInfo.SavedSubmarines.Select(s => s.DisplayName.Value).ToArray()
                };
            },
            isCheat: true));

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
                    NewMessage("Level generation params: " + Level.Loaded.GenerationParams.Identifier);
                    NewMessage("Adjacent locations: " + (Level.Loaded.StartLocation?.Type.Identifier ?? "none".ToIdentifier()) + ", " + (Level.Loaded.StartLocation?.Type.Identifier ?? "none".ToIdentifier()));
                    NewMessage("Mirrored: " + Level.Loaded.Mirrored);
                    NewMessage("Level size: " + Level.Loaded.Size.X + "x" + Level.Loaded.Size.Y);
                    NewMessage("Minimum main path width: " + (Level.Loaded.LevelData?.MinMainPathWidth?.ToString() ?? "unknown"));
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
                    var sprites = Sprite.LoadedSprites.Where(s => s.Name != null && s.Name.Equals(secondArg, StringComparison.OrdinalIgnoreCase));
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
                    sprites = Sprite.LoadedSprites.Where(s => s.EntityIdentifier != null && s.EntityIdentifier == secondArg);
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


        private enum AdjustItemTypes
        {
            NoAdjustment,
            Additive,
            Multiplicative
        }

        private static void PrintItemCosts(Dictionary<ItemPrefab, int> newPrices, ItemPrefab materialPrefab, List<FabricationRecipe> fabricableItems, int newPrice, bool adjustDown, string depth = "", AdjustItemTypes adjustItemType = AdjustItemTypes.NoAdjustment)
        {
            if (newPrice < 1)
            {
                NewMessage(depth + materialPrefab.Name + " cannot be adjusted to this price, because it would become less than 1.");
                return;
            }

            depth += "   ";
            newPrices.TryAdd(materialPrefab, newPrice);

            int componentCost = 0;
            int newComponentCost = 0;

            var fabricationRecipe = fabricableItems.Find(f => f.TargetItem == materialPrefab);

            if (fabricationRecipe != null)
            {
                foreach (RequiredItem requiredItem in fabricationRecipe.RequiredItems)
                {
                    foreach (ItemPrefab itemPrefab in requiredItem.ItemPrefabs)
                    {
                        GetAdjustedPrice(itemPrefab, ref componentCost, ref newComponentCost, newPrices);
                    }
                }
            }
            string componentCostMultiplier = "";
            if (componentCost > 0)
            {
                componentCostMultiplier = $" (Relative difference to component cost {GetComponentCostDifference(materialPrefab.DefaultPrice.Price, componentCost)} => {GetComponentCostDifference(newPrice, newComponentCost)}, or flat profit {(int)(materialPrefab.DefaultPrice.Price - (int)componentCost)} => {newPrice - newComponentCost})";
            }
            string priceAdjustment = "";
            if (newPrice != materialPrefab.DefaultPrice.Price)
            {
                priceAdjustment = ", Suggested price adjustment is " + materialPrefab.DefaultPrice.Price + " => " + newPrice;
            }
            NewMessage(depth + materialPrefab.Name + "(" + materialPrefab.DefaultPrice.Price + ") " + priceAdjustment + componentCostMultiplier);

            if (adjustDown)
            {
                if (componentCost > 0)
                {
                    double newPriceMult = (double)newPrice / (double)(materialPrefab.DefaultPrice.Price);
                    int newPriceDiff = componentCost + newPrice - materialPrefab.DefaultPrice.Price;

                    switch (adjustItemType)
                    {
                        case AdjustItemTypes.Additive:
                            NewMessage(depth + materialPrefab.Name + "'s components should be adjusted " + componentCost + " => " + newPriceDiff);
                            break;
                        case AdjustItemTypes.Multiplicative:
                            NewMessage(depth + materialPrefab.Name + "'s components should be adjusted " + componentCost + " => " + Math.Round(newPriceMult * componentCost));
                            break;
                    }

                    if (fabricationRecipe != null)
                    {
                        foreach (RequiredItem requiredItem in fabricationRecipe.RequiredItems)
                        {
                            foreach (ItemPrefab itemPrefab in requiredItem.ItemPrefabs)
                            {
                                if (itemPrefab.DefaultPrice != null)
                                {
                                    switch (adjustItemType)
                                    {
                                        case AdjustItemTypes.NoAdjustment:
                                            PrintItemCosts(newPrices, itemPrefab, fabricableItems, itemPrefab.DefaultPrice.Price, adjustDown, depth, adjustItemType);
                                            break;
                                        case AdjustItemTypes.Additive:
                                            PrintItemCosts(newPrices, itemPrefab, fabricableItems, itemPrefab.DefaultPrice.Price + (int)((newPrice - materialPrefab.DefaultPrice.Price) / (double)fabricationRecipe.RequiredItems.Length), adjustDown, depth, adjustItemType);
                                            break;
                                        case AdjustItemTypes.Multiplicative:
                                            PrintItemCosts(newPrices, itemPrefab, fabricableItems, (int)(itemPrefab.DefaultPrice.Price * newPriceMult), adjustDown, depth, adjustItemType);
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var fabricationRecipes = fabricableItems.Where(f => f.RequiredItems.Any(x => x.ItemPrefabs.Contains(materialPrefab)));

                foreach (FabricationRecipe fabricationRecipeParent in fabricationRecipes)
                {
                    if (fabricationRecipeParent.TargetItem.DefaultPrice != null)
                    {
                        int targetComponentCost = 0;
                        int newTargetComponentCost = 0;

                        foreach (RequiredItem requiredItem in fabricationRecipeParent.RequiredItems)
                        {
                            foreach (ItemPrefab itemPrefab in requiredItem.ItemPrefabs)
                            {
                                GetAdjustedPrice(itemPrefab, ref targetComponentCost, ref newTargetComponentCost, newPrices);
                            }
                        }
                        switch (adjustItemType)
                        {
                            case AdjustItemTypes.NoAdjustment:
                                PrintItemCosts(newPrices, fabricationRecipeParent.TargetItem, fabricableItems, fabricationRecipeParent.TargetItem.DefaultPrice.Price, adjustDown, depth, adjustItemType);
                                break;
                            case AdjustItemTypes.Additive:
                                PrintItemCosts(newPrices, fabricationRecipeParent.TargetItem, fabricableItems, fabricationRecipeParent.TargetItem.DefaultPrice.Price + newPrice - materialPrefab.DefaultPrice.Price, adjustDown, depth, adjustItemType);
                                break;
                            case AdjustItemTypes.Multiplicative:
                                double maintainedMultiplier = GetComponentCostDifference(fabricationRecipeParent.TargetItem.DefaultPrice.Price, targetComponentCost);
                                PrintItemCosts(newPrices, fabricationRecipeParent.TargetItem, fabricableItems, (int)(newTargetComponentCost * maintainedMultiplier), adjustDown, depth, adjustItemType);
                                break;
                        }
                    }
                }
            }
        }

        private static double GetComponentCostDifference(int itemCost, int componentCost)
        {
            return Math.Round((double)(itemCost / (double)componentCost), 2);
        }

        private static void GetAdjustedPrice(ItemPrefab itemPrefab, ref int componentCost, ref int newComponentCost, Dictionary<ItemPrefab, int> newPrices)
        {
            if (newPrices.TryGetValue(itemPrefab, out int newPrice))
            {
                newComponentCost += newPrice;
            }
            else if (itemPrefab.DefaultPrice != null)
            {
                newComponentCost += itemPrefab.DefaultPrice.Price;
            }
            if (itemPrefab.DefaultPrice != null)
            {
                componentCost += itemPrefab.DefaultPrice.Price;
            }
        }
    }
}

using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        static bool isOpen;

        private static Queue<ColoredText> queuedMessages = new Queue<ColoredText>();

        private static GUITextBlock activeQuestionText;

        public static bool IsOpen
        {
            get
            {
                return isOpen;
            }
        }

        static GUIFrame frame;
        static GUIListBox listBox;
        static GUITextBox textBox;

        public static void Init(GameWindow window)
        {
            int x = 20, y = 20;
            int width = 800, height = 500;

            frame = new GUIFrame(new Rectangle(x, y, width, height), new Color(0.4f, 0.4f, 0.4f, 0.8f));
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            listBox = new GUIListBox(new Rectangle(0, 0, 0, frame.Rect.Height - 40), Color.Black, "", frame);
            //listBox.Color = Color.Black * 0.7f;

            textBox = new GUITextBox(new Rectangle(0, 0, 0, 20), Color.Black, Color.White, Alignment.BottomLeft, Alignment.Left, "", frame);
            textBox.OnTextChanged += (textBox, text) =>
                {
                    ResetAutoComplete();
                    return true;
                };


            NewMessage("Press F3 to open/close the debug console", Color.Cyan);
            NewMessage("Enter \"help\" for a list of available console commands", Color.Cyan);

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

            if (activeQuestionText != null &&
                (listBox.children.Count == 0 || listBox.children[listBox.children.Count - 1] != activeQuestionText))
            {
                listBox.children.Remove(activeQuestionText);
                listBox.children.Add(activeQuestionText);
            }

            if (PlayerInput.KeyHit(Keys.F3))
            {
                isOpen = !isOpen;
                if (isOpen)
                {
                    textBox.Select();
                    AddToGUIUpdateList();
                }
                else
                {
                    GUIComponent.ForceMouseOn(null);
                    textBox.Deselect();
                }
            }

            if (isOpen)
            {
                frame.Update(deltaTime);

                Character.DisableControls = true;

                if (PlayerInput.KeyHit(Keys.Up))
                {
                    textBox.Text = SelectMessage(-1);
                }
                else if (PlayerInput.KeyHit(Keys.Down))
                {
                    textBox.Text = SelectMessage(1);
                }
                else if (PlayerInput.KeyHit(Keys.Tab))
                {
                    textBox.Text = AutoComplete(textBox.Text);
                }

                if (PlayerInput.KeyHit(Keys.Enter))
                {
                    ExecuteCommand(textBox.Text);
                    textBox.Text = "";
                }
            }
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (!isOpen) return;

            frame.Draw(spriteBatch);
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
                case "netstats":
                case "help":
                case "dumpids":
                case "admin":
                case "entitylist":
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
                AddMessage(newMsg);

                if (GameSettings.SaveDebugConsoleLogs) unsavedMessages.Add(newMsg);
            }
        }

        private static void AddMessage(ColoredText msg)
        {
            //listbox not created yet, don't attempt to add
            if (listBox == null) return;

            if (listBox.children.Count > MaxMessages)
            {
                listBox.children.RemoveRange(0, listBox.children.Count - MaxMessages);
            }

            Messages.Add(msg);
            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }

            try
            {
                var textBlock = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width, 0), msg.Text, "", Alignment.TopLeft, Alignment.Left, null, true, GUI.SmallFont);
                textBlock.CanBeFocused = false;
                textBlock.TextColor = msg.Color;

                listBox.AddChild(textBlock);
                listBox.BarScroll = 1.0f;
            }
            catch (Exception e)
            {
                ThrowError("Failed to add a message to the debug console.", e);
            }

            selectedIndex = Messages.Count;
        }

        private static void InitProjectSpecific()
        {
            commands.Add(new Command("autohull", "", (string[] args) =>
            {
                if (Screen.Selected != GameMain.SubEditorScreen) return;

                if (MapEntity.mapEntityList.Any(e => e is Hull || e is Gap))
                {
                    ShowQuestionPrompt("This submarine already has hulls and/or gaps. This command will delete them. Do you want to continue? Y/N",
                        (option) => {
                            if (option.ToLower() == "y") GameMain.SubEditorScreen.AutoHull();
                        });
                }
                else
                {
                    GameMain.SubEditorScreen.AutoHull();
                }
            }));

            commands.Add(new Command("startclient", CommandType.Generic, "", (string[] args) =>
            {
                if (args.Length == 0) return;

                if (GameMain.Client == null)
                {
                    GameMain.NetworkMember = new GameClient("Name");
                    GameMain.Client.ConnectToServer(args[0]);
                }
            }));

            commands.Add(new Command("mainmenuscreen|mainmenu|menu", CommandType.Generic, "mainmenu/menu: Go to the main menu.", (string[] args) =>
            {
                GameMain.GameSession = null;

                List<Character> characters = new List<Character>(Character.CharacterList);
                foreach (Character c in characters)
                {
                    c.Remove();
                }

                GameMain.MainMenuScreen.Select();
            }));

            commands.Add(new Command("gamescreen|game", CommandType.Generic, "gamescreen/game: Go to the \"in-game\" view.", (string[] args) =>
            {
                GameMain.GameScreen.Select();
            }));

            commands.Add(new Command("editsubscreen|editsub|subeditor", CommandType.Generic, "editsub/subeditor: Switch to the submarine editor.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    Submarine.Load(string.Join(" ", args), true);
                }
                GameMain.SubEditorScreen.Select();
            }));

            commands.Add(new Command("editcharacter", CommandType.Debug, "", (string[] args) =>
            {
                GameMain.CharacterEditorScreen.Select();
            }));

            commands.Add(new Command("editparticles", "", (string[] args) =>
            {
                GameMain.ParticleEditorScreen.Select();
            }));

            commands.Add(new Command("control|controlcharacter", CommandType.Character, "control [character name]: Start controlling the specified character.", (string[] args) =>
            {
                if (args.Length < 1) return;

                var character = FindMatchingCharacter(args, true);

                if (character != null)
                {
                    Character.Controlled = character;
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("shake", CommandType.Debug, "", (string[] args) =>
            {
                GameMain.GameScreen.Cam.Shake = 10.0f;
            }));

            commands.Add(new Command("los", CommandType.Render, "los: Toggle the line of sight effect on/off.", (string[] args) =>
            {
                GameMain.LightManager.LosEnabled = !GameMain.LightManager.LosEnabled;
                NewMessage("Line of sight effect " + (GameMain.LightManager.LosEnabled ? "enabled" : "disabled"), Color.White);
            }));

            commands.Add(new Command("lighting|lights", CommandType.Render, "Toggle lighting on/off.", (string[] args) =>
            {
                GameMain.LightManager.LightingEnabled = !GameMain.LightManager.LightingEnabled;
                NewMessage("Lighting " + (GameMain.LightManager.LightingEnabled ? "enabled" : "disabled"), Color.White);
            }));

            commands.Add(new Command("tutorial", CommandType.Generic, "", (string[] args) =>
            {
                TutorialMode.StartTutorial(Tutorials.TutorialType.TutorialTypes[0]);
            }));

            commands.Add(new Command("lobby|lobbyscreen", CommandType.Generic, "", (string[] args) =>
            {
                GameMain.LobbyScreen.Select();
            }));

            commands.Add(new Command("save|savesub", CommandType.Generic, "save [submarine name]: Save the currently loaded submarine using the specified name.", (string[] args) =>
            {
                if (args.Length < 1) return;

                if (GameMain.SubEditorScreen.CharacterMode)
                {
                    GameMain.SubEditorScreen.ToggleCharacterMode();
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

            commands.Add(new Command("load|loadsub", CommandType.Generic, "load [submarine name]: Load a submarine.", (string[] args) =>
            {
                if (args.Length == 0) return;
                Submarine.Load(string.Join(" ", args), true);
            }));

            commands.Add(new Command("cleansub", CommandType.Generic, "", (string[] args) =>
            {
                for (int i = MapEntity.mapEntityList.Count - 1; i >= 0; i--)
                {
                    MapEntity me = MapEntity.mapEntityList[i];

                    if (me.SimPosition.Length() > 2000.0f)
                    {
                        NewMessage("Removed " + me.Name + " (simposition " + me.SimPosition + ")", Color.Orange);
                        MapEntity.mapEntityList.RemoveAt(i);
                    }
                    else if (me.MoveWithLevel)
                    {
                        NewMessage("Removed " + me.Name + " (MoveWithLevel==true)", Color.Orange);
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
            }));

            commands.Add(new Command("debugdraw", CommandType.Render, "debugdraw: Toggle the debug drawing mode on/off.", (string[] args) =>
            {
                GameMain.DebugDraw = !GameMain.DebugDraw;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.DebugDraw)
                    {
                        GameMain.Server.ToggleDebugDrawButton.Text = "DebugDraw: Off";
                        GameMain.Server.ToggleDebugDrawButton.ToolTip = "Turns off debugdraw view information.";
                    }
                    else
                    {
                        GameMain.Server.ToggleDebugDrawButton.Text = "DebugDraw: On";
                        GameMain.Server.ToggleDebugDrawButton.ToolTip = "Turns on debugdraw view information.";
                    }
                }
#endif
                NewMessage("Debug draw mode " + (GameMain.DebugDraw ? "enabled" : "disabled"), Color.White);

            }));

            commands.Add(new Command("togglehud|hud", CommandType.Render, "togglehud/hud: Toggle the character HUD (inventories, icons, buttons, etc) on/off.", (string[] args) =>
            {
                GUI.DisableHUD = !GUI.DisableHUD;
                GameMain.Instance.IsMouseVisible = !GameMain.Instance.IsMouseVisible;

                NewMessage(GUI.DisableHUD ? "Disabled HUD" : "Enabled HUD", Color.White);
            }));

            //Nilmod Disable/Enable rendering - other command
            commands.Add(new Command("togglerenderother|renderother", CommandType.Render, "togglerenderother/renderother: toggles the rendering of particles, lights, los and such.", (string[] args) =>
            {
                GameMain.NilMod.RenderOther = !GameMain.NilMod.RenderOther;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.RenderOther)
                    {
                        GameMain.Server.ToggleRenderOtherButton.Text = "RenderOther: Off";
                        GameMain.Server.ToggleRenderOtherButton.ToolTip = "Turns off particle, lighting, los and other miscellanous rendering.";
                    }
                    else
                    {
                        GameMain.Server.ToggleRenderOtherButton.Text = "RenderOther: On";
                        GameMain.Server.ToggleRenderOtherButton.ToolTip = "Turns on particle, lighting, los and other miscellanous rendering.";
                    }
                }
#endif
                NewMessage(GameMain.NilMod.RenderOther ? "Enabled rendering:Other" : "Disabled rendering:Other", Color.White);
            }));

            //Nilmod Disable/Enable rendering - level command
            commands.Add(new Command("togglerenderlevel|renderlevel", CommandType.Render, "togglerenderlevel/renderlevel: Specifically toggles rendering for the level", (string[] args) =>
            {
                GameMain.NilMod.RenderLevel = !GameMain.NilMod.RenderLevel;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.RenderLevel)
                    {
                        GameMain.Server.ToggleRenderLevelButton.Text = "RenderLevel: Off";
                        GameMain.Server.ToggleRenderLevelButton.ToolTip = "Turns off level rendering, structure rendering will still render the submarine walls unless both are off.";
                    }
                    else
                    {
                        GameMain.Server.ToggleRenderLevelButton.Text = "RenderLevel: On";
                        GameMain.Server.ToggleRenderLevelButton.ToolTip = "Turns on level rendering.";
                    }
                }
#endif
                NewMessage(GameMain.NilMod.RenderLevel ? "Enabled rendering:Level" : "Disabled rendering:Level", Color.White);
            }));

            //Nilmod Disable/Enable rendering - character command
            commands.Add(new Command("togglerendercharacters|togglerendercharacter|rendercharacters|rendercharacter", CommandType.Render, "togglerendercharacter/rendercharacter: Specifically toggles rendering for the level", (string[] args) =>
            {
                GameMain.NilMod.RenderCharacter = !GameMain.NilMod.RenderCharacter;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.RenderCharacter)
                    {
                        GameMain.Server.ToggleRenderCharacterButton.Text = "RenderChar: Off";
                        GameMain.Server.ToggleRenderCharacterButton.ToolTip = "Turns off character rendering.";
                    }
                    else
                    {
                        GameMain.Server.ToggleRenderCharacterButton.Text = "RenderChar: On";
                        GameMain.Server.ToggleRenderCharacterButton.ToolTip = "Turns on character rendering.";
                    }
                }
#endif
                NewMessage(GameMain.NilMod.RenderCharacter ? "Enabled rendering:Character" : "Disabled rendering:Character", Color.White);
            }));

            //Nilmod Disable/Enable rendering - structure command
            commands.Add(new Command("togglerenderstructures|togglerenderstructure|renderstructures|renderstructure", CommandType.Render, "togglerenderstructure/renderstructure: Specifically toggles rendering for the submarine structures", (string[] args) =>
            {
                GameMain.NilMod.RenderStructure = !GameMain.NilMod.RenderStructure;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.RenderStructure)
                    {
                        GameMain.Server.ToggleRenderStructureButton.Text = "RenderStruct: Off";
                        GameMain.Server.ToggleRenderStructureButton.ToolTip = "Turns off rendering of submarine items and background walls, level will still show outer walls, doors, ladders and perhaps stairs..";
                    }
                    else
                    {
                        GameMain.Server.ToggleRenderStructureButton.Text = "RenderStruct: On";
                        GameMain.Server.ToggleRenderStructureButton.ToolTip = "Turns on rendering of all submarine items and background walls.";
                    }
                }
#endif
                NewMessage(GameMain.NilMod.RenderStructure ? "Enabled rendering:Structure" : "Disabled rendering:Structure", Color.White);
            }));

            //Nilmod Disable/Enable particles command
            commands.Add(new Command("toggleparticles|particles", CommandType.Render, "toggleparticles/particles: Toggle the Particle System on/off.", (string[] args) =>
            {
                GameMain.NilMod.DisableParticles = !GameMain.NilMod.DisableParticles;

                NewMessage("Particle System " + (GameMain.NilMod.DisableParticles ? "disabled" : "enabled"), Color.White);
            }));

            commands.Add(new Command("followsub", CommandType.Render, "followsub: Toggle whether the ", (string[] args) =>
            {
                Camera.FollowSub = !Camera.FollowSub;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (Camera.FollowSub)
                    {
                        GameMain.Server.ToggleFollowSubButton.Text = "FollowSub: Off";
                        GameMain.Server.ToggleFollowSubButton.ToolTip = "Stops the camera automatically following submarines.";
                    }
                    else
                    {
                        GameMain.Server.ToggleFollowSubButton.Text = "FollowSub: On";
                        GameMain.Server.ToggleFollowSubButton.ToolTip = "Attaches the camera automatically to begin following submarines again.";
                    }
                }
#endif
                NewMessage(Camera.FollowSub ? "Set the camera to follow the closest submarine" : "Disabled submarine following.", Color.White);
            }));

            commands.Add(new Command("toggleaitargets|aitargets", CommandType.Debug, "toggleaitargets/aitargets: Toggle the visibility of AI targets (= targets that enemies can detect and attack/escape from).", (string[] args) =>
            {
                AITarget.ShowAITargets = !AITarget.ShowAITargets;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (AITarget.ShowAITargets)
                    {
                        GameMain.Server.ToggleAITargetsButton.Text = "AITargets: Off";
                        GameMain.Server.ToggleAITargetsButton.ToolTip = "Turns off AI Targetting range information for Debugdraw mode.";
                    }
                    else
                    {
                        GameMain.Server.ToggleAITargetsButton.Text = "AITargets: On";
                        GameMain.Server.ToggleAITargetsButton.ToolTip = "Turns on AI Targetting range information for Debugdraw mode.";
                    }
                }
#endif
                NewMessage(AITarget.ShowAITargets ? "Enabled AI target drawing" : "Disabled AI target drawing", Color.White);
            }));
#if DEBUG
            commands.Add(new Command("spamchatmessages", CommandType.Debug, "", (string[] args) =>
            {
                int msgCount = 1000;
                if (args.Length > 0) int.TryParse(args[0], out msgCount);
                int msgLength = 50;
                if (args.Length > 1) int.TryParse(args[1], out msgLength);

                for (int i = 0; i < msgCount; i++)
                {
                    if (GameMain.Server != null)
                    {
                        GameMain.Server.SendChatMessage(ToolBox.RandomSeed(msgLength), ChatMessageType.Default);
                    }
                    else if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(ToolBox.RandomSeed(msgLength));
                    }
                }
            }));
#endif
            commands.Add(new Command("cleanbuild", CommandType.Generic, "", (string[] args) =>
            {
                GameMain.Config.MusicVolume = 0.5f;
                GameMain.Config.SoundVolume = 0.5f;
                NewMessage("Music and sound volume set to 0.5", Color.Green);

                GameMain.Config.GraphicsWidth = 0;
                GameMain.Config.GraphicsHeight = 0;
                GameMain.Config.WindowMode = WindowMode.Fullscreen;
                NewMessage("Resolution set to 0 x 0 (screen resolution will be used)", Color.Green);
                NewMessage("Fullscreen enabled", Color.Green);

                GameSettings.VerboseLogging = false;

                if (GameMain.Config.MasterServerUrl != "http://www.undertowgames.com/baromaster")
                {
                    ThrowError("MasterServerUrl \"" + GameMain.Config.MasterServerUrl + "\"!");
                }

                GameMain.Config.Save("config.xml");

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

                if (System.IO.File.Exists(GameServer.SettingsFile))
                {
                    System.IO.File.Delete(GameServer.SettingsFile);
                    NewMessage("Deleted server settings", Color.Green);
                }

                if (System.IO.File.Exists(GameServer.VanillaClientPermissionsFile))
                {
                    System.IO.File.Delete(GameServer.VanillaClientPermissionsFile);
                    NewMessage("Deleted client permission file", Color.Green);
                }

                if (System.IO.File.Exists("crashreport.txt"))
                {
                    System.IO.File.Delete("crashreport.txt");
                    NewMessage("Deleted crashreport.txt", Color.Green);
                }

                if (!System.IO.File.Exists("Content/Map/TutorialSub.sub"))
                {
                    ThrowError("TutorialSub.sub not found!");
                }
            }));

            //Nilmod Spy command
            commands.Add(new Command("spy|spycharacter", CommandType.Character, "spy: View the game from another characters HUD and location.", (string[] args) =>
            {
                if (args.Length < 1) return;

                var character = FindMatchingCharacter(args, false);

                if (character != null)
                {
                    Character.Spied = character;
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("spyid|spyclientid", CommandType.Character, "spy: View the game from another characters HUD and location.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length < 1) return;

                int id = 0;
                int.TryParse(args[0], out id);
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    DebugConsole.NewMessage("Client id \"" + id + "\" not found.", Color.Red);
                    return;
                }

                var character = client.Character;

                if (character != null)
                {
                    Character.Spied = character;
                }
                else
                {
                    DebugConsole.NewMessage("Client " + client.Name + " Does not currently have a character to spy.", Color.Red);
                }
            }));

            commands.Add(new Command("toggledeathchat|toggledeath|deathchat", CommandType.Network, "toggledeathchat: Toggle the visibility of death chat for a living host.", (string[] args) =>
            {
                GameMain.NilMod.ShowDeadChat = !GameMain.NilMod.ShowDeadChat;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.ShowDeadChat)
                    {
                        GameMain.Server.ToggleDeathChat.Text = "DeathChat: Off";
                        GameMain.Server.ToggleDeathChat.ToolTip = "Turns off visibility of the deathchat if you have a living character.";
                    }
                    else
                    {
                        GameMain.Server.ToggleDeathChat.Text = "DeathChat: On";
                        GameMain.Server.ToggleDeathChat.ToolTip = "Turns on visibility of the deathchat if you have a living character";
                    }
                }
#endif
                NewMessage("Death chat visibility " + (GameMain.NilMod.ShowDeadChat ? "enabled" : "disabled"), Color.White);

            }));

            //Nilmod playercount command
            commands.Add(new Command("playercount|countplayers|checkslots|slots", CommandType.Debug, "playercount: Prints details regarding player slot usage of server.", (string[] args) =>
            {
                GameMain.NilMod.DisableParticles = !GameMain.NilMod.DisableParticles;
                NewMessage("There are " + (GameMain.Server.ConnectedClients.Count()) + " connected clients on the server.", Color.Cyan);
                NewMessage("Owners = " + GameMain.NilMod.Owners + "/" + GameMain.NilMod.MaxOwnerSlots, Color.Green);
                NewMessage("Admins = " + GameMain.NilMod.Admins + "/" + GameMain.NilMod.MaxAdminSlots, Color.Green);
                NewMessage("Trusted = " + GameMain.NilMod.Trusted + "/" + GameMain.NilMod.MaxTrustedSlots, Color.Green);
                NewMessage("Spectators = " + GameMain.NilMod.Spectators + "/" + GameMain.NilMod.MaxSpectatorSlots, Color.Green);
                NewMessage("Players = " + GameMain.NilMod.CurrentPlayers + "/" + GameMain.Server.maxPlayers, Color.Green);
                NewMessage("OtherSlotsExcludeSpectators = " + (GameMain.NilMod.OtherSlotsExcludeSpectators ? "No." : "Yes."), Color.Green);
            }));

        }
    }
}

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Steam;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    struct ColoredText
    {
        public string Text;
        public Color Color;
		public bool IsCommand;

        public readonly string Time;

        public ColoredText(string text, Color color, bool isCommand)
        {
            this.Text = text;
            this.Color = color;
			this.IsCommand = isCommand;

            Time = DateTime.Now.ToString();
        }
    }

    static partial class DebugConsole
    {
        public partial class Command
        {
            public readonly string[] names;
            public readonly string help;
            
            public Action<string[]> OnExecute;

            public Func<string[][]> GetValidArgs;

            /// <summary>
            /// Using a command that's considered a cheat disables achievements
            /// </summary>
            public readonly bool IsCheat;

            /// <summary>
            /// Use this constructor to create a command that executes the same action regardless of whether it's executed by a client or the server.
            /// </summary>
            public Command(string name, string help, Action<string[]> onExecute, Func<string[][]> getValidArgs = null, bool isCheat = false)
            {
                names = name.Split('|');
                this.help = help;

                this.OnExecute = onExecute;
                
                this.GetValidArgs = getValidArgs;
                this.IsCheat = isCheat;
            }

            public void Execute(string[] args)
            {
                if (OnExecute == null) return;
                if (!CheatsEnabled && IsCheat)
                {
                    NewMessage("You need to enable cheats using the command \"enablecheats\" before you can use the command \"" + names[0] + "\".", Color.Red);
                    if (Steam.SteamManager.USE_STEAM)
                    {
                        NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                    }
                    return;
                }

                OnExecute(args);
            }
        }

        private static Queue<ColoredText> queuedMessages = new Queue<ColoredText>();

        static partial void ShowHelpMessage(Command command);
        
        const int MaxMessages = 300;

        public static List<ColoredText> Messages = new List<ColoredText>();

        public delegate void QuestionCallback(string answer);
        private static QuestionCallback activeQuestionCallback;

        private static List<Command> commands = new List<Command>();
        public static List<Command> Commands
        {
            get { return commands; }
        }
        
        private static string currentAutoCompletedCommand;
        private static int currentAutoCompletedIndex;

        //used for keeping track of the message entered when pressing up/down
        static int selectedIndex;

        public static bool CheatsEnabled;

        private static List<ColoredText> unsavedMessages = new List<ColoredText>();
        private static int messagesPerFile = 5000;
        public const string SavePath = "ConsoleLogs";

        private static void AssignOnExecute(string names, Action<string[]> onExecute)
        {
            var matchingCommand = commands.Find(c => c.names.Intersect(names.Split('|')).Count() > 0);
            if (matchingCommand == null)
            {
                throw new Exception("AssignOnExecute failed. Command matching the name(s) \""+names+"\" not found.");
            }
            else
            {
                matchingCommand.OnExecute = onExecute;
            }
        }

        static DebugConsole()
        {
#if DEBUG
            CheatsEnabled = true;
#endif

            commands.Add(new Command("help", "", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    foreach (Command c in commands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        ShowHelpMessage(c);
                    }
                }
                else
                {
                    var matchingCommand = commands.Find(c => c.names.Any(name => name == args[0]));
                    if (matchingCommand == null)
                    {
                        NewMessage("Command " + args[0] + " not found.", Color.Red);
                    }
                    else
                    {
                        ShowHelpMessage(matchingCommand);
                    }
                }
            }, 
            () =>
            {
                return new string[][]
                {
                    commands.SelectMany(c => c.names).ToArray(),
                    new string[0]
                };
            }));


            commands.Add(new Command("items|itemlist", "itemlist: List all the item prefabs available for spawning.", (string[] args) =>
            {
                NewMessage("***************", Color.Cyan);
                foreach (MapEntityPrefab ep in MapEntityPrefab.List)
                {
                    var itemPrefab = ep as ItemPrefab;
                    if (itemPrefab == null || itemPrefab.Name == null) continue;
                    string text = $"- {itemPrefab.Name}";
                    if (itemPrefab.Tags.Any())
                    {
                        text += $" ({string.Join(", ", itemPrefab.Tags)})";
                    }
                    if (itemPrefab.AllowedLinks.Any())
                    {
                        text += $", Links: {string.Join(", ", itemPrefab.AllowedLinks)}";
                    }
                    NewMessage(text, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));


            commands.Add(new Command("netstats", "netstats: Toggles the visibility of the network statistics UI.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null) return;
                GameMain.NetworkMember.ShowNetStats = !GameMain.NetworkMember.ShowNetStats;
            }));

            commands.Add(new Command("createfilelist", "", (string[] args) =>
            {
                UpdaterUtil.SaveFileList("filelist.xml");
            }));

            commands.Add(new Command("spawn|spawncharacter", "spawn [creaturename/jobname] [near/inside/outside/cursor]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine). You can also enter the name of a job (e.g. \"Mechanic\") to spawn a character with a specific job and the appropriate equipment.", (string[] args) =>
            {
                SpawnCharacter(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), out string errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            },
            () =>
            {
                List<string> characterFiles = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
                for (int i = 0; i < characterFiles.Count; i++)
                {
                    characterFiles[i] = Path.GetFileNameWithoutExtension(characterFiles[i]).ToLowerInvariant();
                }

                foreach (JobPrefab jobPrefab in JobPrefab.List)
                {
                    characterFiles.Add(jobPrefab.Name);
                }

                return new string[][]
                {
                characterFiles.ToArray(),
                new string[] { "near", "inside", "outside", "cursor" }
                };
            }, isCheat: true));

            commands.Add(new Command("spawnitem", "spawnitem [itemname] [cursor/inventory/cargo/random/[name]]: Spawn an item at the position of the cursor, in the inventory of the controlled character, in the inventory of the client with the given name, or at a random spawnpoint if the last parameter is omitted or \"random\".",
            (string[] args) =>
            {
                try
                {
                    SpawnItem(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), Character.Controlled, out string errorMsg);
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        ThrowError(errorMsg);
                    }
                }
                catch (Exception e)
                {
                    string errorMsg = "Failed to spawn an item. Arguments: \"" + string.Join(" ", args) + "\".";
                    ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("DebugConsole.SpawnItem:Error", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + '\n' + e.Message + '\n' + e.StackTrace);
                }
            },
            () =>
            {
                List<string> itemNames = new List<string>();
                foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
                {
                    if (prefab is ItemPrefab itemPrefab) itemNames.Add(itemPrefab.Name);
                }

                List<string> spawnPosParams = new List<string>() { "cursor", "inventory" };
#if SERVER
                if (GameMain.Server != null) spawnPosParams.AddRange(GameMain.Server.ConnectedClients.Select(c => c.Name));
#endif
                spawnPosParams.AddRange(Character.CharacterList.Where(c => c.Inventory != null).Select(c => c.Name).Distinct());

                return new string[][]
                {
                itemNames.ToArray(),
                spawnPosParams.ToArray()
                };
            }, isCheat: true));
            
            commands.Add(new Command("disablecrewai", "disablecrewai: Disable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled", Color.Red);
            }));

            commands.Add(new Command("enablecrewai", "enablecrewai: Enable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled", Color.Green);
            }, isCheat: true));

            commands.Add(new Command("disableenemyai", "disableenemyai: Disable the AI of the Enemy characters (monsters).", (string[] args) =>
            {
                EnemyAIController.DisableEnemyAI = true;
                NewMessage("Enemy AI disabled", Color.Red);
            }, isCheat: true));

            commands.Add(new Command("enableenemyai", "enableenemyai: Enable the AI of the Enemy characters (monsters).", (string[] args) =>
            {
                EnemyAIController.DisableEnemyAI = false;
                NewMessage("Enemy AI enabled", Color.Green);
            }, isCheat: true));

            commands.Add(new Command("botcount", "botcount [x]: Set the number of bots in the crew in multiplayer.", null));

            commands.Add(new Command("botspawnmode", "botspawnmode [fill/normal]: Set how bots are spawned in the multiplayer.", null));

            commands.Add(new Command("autorestart", "autorestart [true/false]: Enable or disable round auto-restart.", null));

            commands.Add(new Command("autorestartinterval", "autorestartinterval [seconds]: Set how long the server waits between rounds before automatically starting a new one. If set to 0, autorestart is disabled.", null));

            commands.Add(new Command("autorestarttimer", "autorestarttimer [seconds]: Set the current autorestart countdown to the specified value.", null));

            commands.Add(new Command("startwhenclientsready", "startwhenclientsready [true/false]: Enable or disable automatically starting the round when clients are ready to start.", null));

            commands.Add(new Command("giveperm", "giveperm [id]: Grants administrative permissions to the player with the specified client ID.", null));

            commands.Add(new Command("revokeperm", "revokeperm [id]: Revokes administrative permissions to the player with the specified client ID.", null));
            
            commands.Add(new Command("giverank", "giverank [id]: Assigns a specific rank (= a set of administrative permissions) to the player with the specified client ID.", null));

            commands.Add(new Command("givecommandperm", "givecommandperm [id]: Gives the player with the specified client ID the permission to use the specified console commands.", null));

            commands.Add(new Command("revokecommandperm", "revokecommandperm [id]: Revokes permission to use the specified console commands from the player with the specified client ID.", null));
            
            commands.Add(new Command("showperm", "showperm [id]: Shows the current administrative permissions of the client with the specified client ID.", null));
            
            commands.Add(new Command("respawnnow", "respawnnow: Trigger a respawn immediately if there are any clients waiting to respawn.", null));

            commands.Add(new Command("showkarma", "showkarma: Show the current karma values of the players.", null));
            commands.Add(new Command("togglekarma", "togglekarma: Toggle the karma system on/off.", null));
            commands.Add(new Command("resetkarma", "resetkarma [client]: Resets the karma value of the specified client to 100.", null,
            () =>
            {
                if (GameMain.NetworkMember?.ConnectedClients == null) { return null; }
                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));
            commands.Add(new Command("setkarma", "setkarma [client] [0-100]: Sets the karma of the specified client to the specified value.", null,
            () =>
            {
                if (GameMain.NetworkMember?.ConnectedClients == null) { return null; }
                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                    new string[] { "50" }
                };
            }));
            commands.Add(new Command("togglekarmatestmode", "togglekarmatestmode: Toggle the karma test mode on/off. When test mode is enabled, clients get notified when their karma value changes (including the reason for the increase/decrease) and the server doesn't ban clients whose karma decreases below the ban threshold.", null));

            commands.Add(new Command("kick", "kick [name]: Kick a player out of the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;

                string playerName = string.Join(" ", args);

                ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"?", (reason) =>
                {
                    GameMain.NetworkMember.KickPlayer(playerName, reason);
                });
            },
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));

            commands.Add(new Command("kickid", "kickid [id]: Kick the player with the specified client ID out of the server.  You can see the IDs of the clients using the command \"clientlist\".", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;

                int.TryParse(args[0], out int id);
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Reason for kicking \"" + client.Name + "\"?", (reason) =>
                {
                    GameMain.NetworkMember.KickPlayer(client.Name, reason);
                });
            }));

            commands.Add(new Command("ban", "ban [name]: Kick and ban the player from the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;

                string clientName = string.Join(" ", args);
                ShowQuestionPrompt("Reason for banning \"" + clientName + "\"?", (reason) =>
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

                        GameMain.NetworkMember.BanPlayer(clientName, reason, false, banDuration);
                    });
                });
            },
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));
                       
            commands.Add(new Command("banid", "banid [id]: Kick and ban the player with the specified client ID from the server. You can see the IDs of the clients using the command \"clientlist\".", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;

                int.TryParse(args[0], out int id);
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Reason for banning \"" + client.Name + "\"?", (reason) =>
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

                        GameMain.NetworkMember.BanPlayer(client.Name, reason, false, banDuration);
                    });
                });
            }));
            
            commands.Add(new Command("banendpoint|banip", "banendpoint [endpoint]: Ban the IP address/SteamID from the server.", null));
            
            commands.Add(new Command("teleportcharacter|teleport", "teleport [character name]: Teleport the specified character to the position of the cursor. If the name parameter is omitted, the controlled character will be teleported.", (string[] args) =>
            {
                Character tpCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, false);
                if (tpCharacter == null) return;

                var cam = GameMain.GameScreen.Cam;
                tpCharacter.AnimController.CurrentHull = null;
                tpCharacter.Submarine = null;
                tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition)));
                tpCharacter.AnimController.FindHull(cam.ScreenToWorld(PlayerInput.MousePosition), true);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("godmode", "godmode: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                NewMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", Color.White);
            }, isCheat: true));

            commands.Add(new Command("lock", "lock: Lock movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockX = !Submarine.LockX;
                Submarine.LockY = Submarine.LockX;
                NewMessage((Submarine.LockX ? "Submarine movement locked." : "Submarine movement unlocked."), Color.White);
            }, null, true));

            commands.Add(new Command("lockx", "lockx: Lock horizontal movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockX = !Submarine.LockX;
                NewMessage((Submarine.LockX ? "Horizontal submarine movement locked." : "Horizontal submarine movement unlocked."), Color.White);
            }, null, true));

            commands.Add(new Command("locky", "locky: Lock vertical movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockY = !Submarine.LockY;
                NewMessage((Submarine.LockY ? "Vertical submarine movement locked." : "Vertical submarine movement unlocked."), Color.White);
            }, null, true));

            commands.Add(new Command("dumpids", "", (string[] args) =>
            {
                try
                {
                    int count = args.Length == 0 ? 10 : int.Parse(args[0]);
                    Entity.DumpIds(count);
                }
                catch (Exception e)
                {
                    ThrowError("Failed to dump ids", e);
                }
            }));

            commands.Add(new Command("findentityids", "findentityids [entityname]", (string[] args) =>
            {
                if (args.Length == 0) return;
                args[0] = args[0].ToLowerInvariant();
                foreach (MapEntity mapEntity in MapEntity.mapEntityList)
                {
                    if (mapEntity.Name.ToLowerInvariant() == args[0])
                    {
                        ThrowError(mapEntity.ID + ": " + mapEntity.Name.ToString());
                    }
                }
                foreach (Character character in Character.CharacterList)
                {
                    if (character.Name.ToLowerInvariant() == args[0] || character.SpeciesName.ToLowerInvariant() == args[0])
                    {
                        ThrowError(character.ID + ": " + character.Name.ToString());
                    }
                }
            }));

            commands.Add(new Command("giveaffliction", "giveaffliction [affliction name] [affliction strength] [character name]: Add an affliction to a character. If the name parameter is omitted, the affliction is added to the controlled character.", (string[] args) =>
            {
                if (args.Length < 2) return;

                AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.Find(a =>
                    a.Name.ToLowerInvariant() == args[0].ToLowerInvariant() ||
                    a.Identifier.ToLowerInvariant() == args[0].ToLowerInvariant());
                if (afflictionPrefab == null)
                {
                    ThrowError("Affliction \"" + args[0] + "\" not found.");
                    return;
                }

                if (!float.TryParse(args[1], out float afflictionStrength))
                {
                    ThrowError("\"" + args[1] + "\" is not a valid affliction strength.");
                    return;
                }

                Character targetCharacter = (args.Length <= 2) ? Character.Controlled : FindMatchingCharacter(args.Skip(2).ToArray());
                if (targetCharacter != null)
                {
                    targetCharacter.CharacterHealth.ApplyAffliction(targetCharacter.AnimController.MainLimb, afflictionPrefab.Instantiate(afflictionStrength));
                }
            },
            () =>
            {
                return new string[][]
                {
                    AfflictionPrefab.List.Select(a => a.Name).ToArray(),
                    new string[] { "1" },
                    Character.CharacterList.Select(c => c.Name).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("heal", "heal [character name]: Restore the specified character to full health. If the name parameter is omitted, the controlled character will be healed.", (string[] args) =>
            {
                Character healedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                if (healedCharacter != null)
                {
                    healedCharacter.SetAllDamage(0.0f, 0.0f, 0.0f);
                    healedCharacter.Oxygen = 100.0f;
                    healedCharacter.Bloodloss = 0.0f;
                    healedCharacter.SetStun(0.0f, true);
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("revive", "revive [character name]: Bring the specified character back from the dead. If the name parameter is omitted, the controlled character will be revived.", (string[] args) =>
            {
                Character revivedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                if (revivedCharacter == null) return;

                revivedCharacter.Revive();
#if SERVER
                if (GameMain.Server != null)
                {
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        if (c.Character != revivedCharacter) continue;

                        //clients stop controlling the character when it dies, force control back
                        GameMain.Server.SetClientCharacter(c, revivedCharacter);
                        break;
                    }
                }
#endif
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("freeze", "", (string[] args) =>
            {
                if (Character.Controlled != null) Character.Controlled.AnimController.Frozen = !Character.Controlled.AnimController.Frozen;
            }, isCheat: true));

            commands.Add(new Command("ragdoll", "ragdoll [character name]: Force-ragdoll the specified character. If the name parameter is omitted, the controlled character will be ragdolled.", (string[] args) =>
            {
                Character ragdolledCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                if (ragdolledCharacter != null)
                {
                    ragdolledCharacter.IsForceRagdolled = !ragdolledCharacter.IsForceRagdolled;
                }
            },
            () =>
            {
                return new string[][]
                {
        Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("freecamera|freecam", "freecam: Detach the camera from the controlled character.", (string[] args) =>
            {
                Character.Controlled = null;
                GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            }, isCheat: true));

            commands.Add(new Command("eventmanager", "eventmanager: Toggle event manager on/off. No new random events are created when the event manager is disabled.", (string[] args) =>
            {
                if (GameMain.GameSession?.EventManager != null)
                {
                    GameMain.GameSession.EventManager.Enabled = !GameMain.GameSession.EventManager.Enabled;
                    NewMessage(GameMain.GameSession.EventManager.Enabled ? "Event manager on" : "Event manager off", Color.White);
                }
            }, isCheat: true));

            commands.Add(new Command("water|editwater", "water/editwater: Toggle water editing. Allows adding water into rooms by holding the left mouse button and removing it by holding the right mouse button.", (string[] args) =>
            {
                Hull.EditWater = !Hull.EditWater;
                NewMessage(Hull.EditWater ? "Water editing on" : "Water editing off", Color.White);                
            }, isCheat: true));

            commands.Add(new Command("fire|editfire", "fire/editfire: Allows putting up fires by left clicking.", (string[] args) =>
            {
                Hull.EditFire = !Hull.EditFire;
                NewMessage(Hull.EditFire ? "Fire spawning on" : "Fire spawning off", Color.White);                
            }, isCheat: true));

            commands.Add(new Command("explosion", "explosion [range] [force] [damage] [structuredamage] [emp strength]: Creates an explosion at the position of the cursor.", (string[] args) =>
            {
                Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float range = 500, force = 10, damage = 50, structureDamage = 10, empStrength = 0.0f;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                if (args.Length > 4) float.TryParse(args[4], out empStrength);
                new Explosion(range, force, damage, structureDamage, empStrength).Explode(explosionPos, null);
            }, isCheat: true));

            commands.Add(new Command("showseed|showlevelseed", "showseed: Show the seed of the current level.", (string[] args) =>
            {
                if (Level.Loaded == null)
                {
                    ThrowError("No level loaded.");
                }
                else
                {
                    NewMessage("Level seed: " + Level.Loaded.Seed);
                }
            },null));

#if DEBUG
            commands.Add(new Command("crash", "crash: Crashes the game.", (string[] args) =>
            {
                throw new Exception("crash command issued");
            }));

            commands.Add(new Command("teleportsub", "teleportsub [start/end]: Teleport the submarine to the start or end of the level. WARNING: does not take outposts into account, so often leads to physics glitches. Only use for debugging.", (string[] args) =>
            {
                if (Submarine.MainSub == null || Level.Loaded == null) return;

                if (args.Length > 0 && args[0].ToLowerInvariant() == "start")
                {
                    Submarine.MainSub.SetPosition(Level.Loaded.StartPosition - Vector2.UnitY * Submarine.MainSub.Borders.Height);
                }
                else
                {
                    Submarine.MainSub.SetPosition(Level.Loaded.EndPosition - Vector2.UnitY * Submarine.MainSub.Borders.Height);
                }
            }, isCheat: true));

            commands.Add(new Command("waterphysicsparams", "waterphysicsparams [stiffness] [spread] [damping]: defaults 0.02, 0.05, 0.05", (string[] args) =>
            {
                Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float stiffness = 0.02f, spread = 0.05f, damp = 0.01f;
                if (args.Length > 0) float.TryParse(args[0], out stiffness);
                if (args.Length > 1) float.TryParse(args[1], out spread);
                if (args.Length > 2) float.TryParse(args[2], out damp);
                Hull.WaveStiffness = stiffness;
                Hull.WaveSpread = spread;
                Hull.WaveDampening = damp;
            }, null));

            commands.Add(new Command("testlevels", "testlevels", (string[] args) =>
            {
                CoroutineManager.StartCoroutine(TestLevels());
            },
            null));

            IEnumerable<object> TestLevels()
            {
                Submarine selectedSub = null;
                string subName = GameMain.Config.QuickStartSubmarineName;
                if (!string.IsNullOrEmpty(subName))
                {
                    selectedSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name.ToLower() == subName.ToLower());
                }

                int count = 0;
                while (true)
                {
                    var gamesession = new GameSession(
                        Submarine.SavedSubmarines.GetRandom(s => !s.HasTag(SubmarineTag.HideInMenus)),
                        "Data/Saves/test.xml",
                        GameModePreset.List.Find(gm => gm.Identifier == "devsandbox"),
                        missionPrefab: null);
                    string seed = ToolBox.RandomSeed(16);
                    gamesession.StartRound(seed);

                    Rectangle subWorldRect = Submarine.MainSub.Borders;
                    subWorldRect.Location += new Point((int)Submarine.MainSub.WorldPosition.X, (int)Submarine.MainSub.WorldPosition.Y);
                    subWorldRect.Y -= subWorldRect.Height;
                    foreach (var ruin in Level.Loaded.Ruins)
                    {
                        if (ruin.Area.Intersects(subWorldRect))
                        {
                            ThrowError("Ruins intersect with the sub. Seed: " + seed + ", Submarine: " + Submarine.MainSub.Name);
                            yield return CoroutineStatus.Success;
                        }
                    }
                    
                    var levelCells = Level.Loaded.GetCells(
                        Submarine.MainSub.WorldPosition,
                        Math.Max(Submarine.MainSub.Borders.Width / Level.GridCellSize, 2));
                    foreach (var cell in levelCells)
                    {
                        Vector2 minExtents = new Vector2(
                            cell.Edges.Min(e => Math.Min(e.Point1.X, e.Point2.X)),
                            cell.Edges.Min(e => Math.Min(e.Point1.Y, e.Point2.Y)));
                        Vector2 maxExtents = new Vector2(
                            cell.Edges.Max(e => Math.Max(e.Point1.X, e.Point2.X)),
                            cell.Edges.Max(e => Math.Max(e.Point1.Y, e.Point2.Y)));
                        Rectangle cellRect = new Rectangle(
                            (int)minExtents.X, (int)minExtents.Y, 
                            (int)(maxExtents.X - minExtents.X), (int)(maxExtents.Y - minExtents.Y));
                        if (cellRect.Intersects(subWorldRect))
                        {
                            ThrowError("Level cells intersect with the sub. Seed: " + seed + ", Submarine: " + Submarine.MainSub.Name);
                            yield return CoroutineStatus.Success;
                        }
                    }

                    GameMain.GameSession.EndRound("");
                    Submarine.Unload();

                    count++;
                    NewMessage("Level seed " + seed + " ok (test #" + count + ")");
#if CLIENT
                    //dismiss round summary and any other message boxes
                    GUIMessageBox.CloseAll();
#endif
                    yield return CoroutineStatus.Running;
                }
            }
#endif

            commands.Add(new Command("fixitems", "fixitems: Repairs all items and restores them to full condition.", (string[] args) =>
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Condition = it.Prefab.Health;
                }
            }, null, true));

            commands.Add(new Command("fixhulls|fixwalls", "fixwalls/fixhulls: Fixes all walls.", (string[] args) =>
            {
                var walls = new List<Structure>(Structure.WallList);
                foreach (Structure w in walls)
                {
                    try
                    {
                        for (int i = 0; i < w.SectionCount; i++)
                        {
                            w.AddDamage(i, -100000.0f);
                        }
                    }
                    catch (InvalidOperationException e)
                    {
                        string errorMsg = "Error while executing the fixhulls command.\n" + e.StackTrace;
                        GameAnalyticsManager.AddErrorEventOnce("DebugConsole.FixHulls", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    }
                }
            }, null, true));

            commands.Add(new Command("power", "power [temperature]: Immediately sets the temperature of the nuclear reactor to the specified value.", (string[] args) =>
            {
                Item reactorItem = Item.ItemList.Find(i => i.GetComponent<Reactor>() != null);
                if (reactorItem == null) return;

                float power = 1000.0f;
                if (args.Length > 0) float.TryParse(args[0], out power);

                var reactor = reactorItem.GetComponent<Reactor>();
                reactor.TurbineOutput = power / reactor.MaxPowerOutput * 100.0f;
                reactor.FissionRate = power / reactor.MaxPowerOutput * 100.0f;
                reactor.AutoTemp = true;

#if SERVER
                if (GameMain.Server != null)
                {
                    reactorItem.CreateServerEvent(reactor);
                }
#endif
            }, null, true));

            commands.Add(new Command("oxygen|air", "oxygen/air: Replenishes the oxygen levels in every room to 100%.", (string[] args) =>
            {
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                }
            }, null, true));

            commands.Add(new Command("kill", "kill [character]: Immediately kills the specified character.", (string[] args) =>
            {
                Character killedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                killedCharacter?.SetAllDamage(200.0f, 0.0f, 0.0f);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("killmonsters", "killmonsters: Immediately kills all AI-controlled enemies in the level.", (string[] args) =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!(c.AIController is EnemyAIController)) continue;
                    c.SetAllDamage(200.0f, 0.0f, 0.0f);
                }
            }, null, true));

            commands.Add(new Command("setclientcharacter", "setclientcharacter [client name] [character name]: Gives the client control of the specified character.", null,
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("campaigninfo|campaignstatus", "campaigninfo: Display information about the state of the currently active campaign.", (string[] args) =>
            {
                var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                if (campaign == null)
                {
                    ThrowError("No campaign active!");
                    return;
                }

                campaign.LogState();
            }));

            commands.Add(new Command("campaigndestination|setcampaigndestination", "campaigndestination [index]: Set the location to head towards in the currently active campaign.", (string[] args) =>
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
                        Location location = campaign.Map.CurrentLocation.Connections[destinationIndex].OtherLocation(campaign.Map.CurrentLocation);
                        campaign.Map.SelectLocation(location);
                        NewMessage(location.Name + " selected.", Color.White);
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
                    Location location = campaign.Map.CurrentLocation.Connections[destinationIndex].OtherLocation(campaign.Map.CurrentLocation);
                    campaign.Map.SelectLocation(location);
                    NewMessage(location.Name + " selected.", Color.White);
                }
            }));

            commands.Add(new Command("difficulty|leveldifficulty", "difficulty [0-100]: Change the level difficulty setting in the server lobby.", null));

            commands.Add(new Command("verboselogging", "verboselogging: Toggle verbose console logging on/off. When on, additional debug information is written to the debug console.", (string[] args) =>
            {
                GameSettings.VerboseLogging = !GameSettings.VerboseLogging;
                NewMessage((GameSettings.VerboseLogging ? "Enabled" : "Disabled") + " verbose logging.", Color.White);
            }, isCheat: false));


            commands.Add(new Command("calculatehashes", "calculatehashes [content package name]: Show the MD5 hashes of the files in the selected content package. If the name parameter is omitted, the first content package is selected.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    string packageName = string.Join(" ", args).ToLower();
                    var package = GameMain.Config.SelectedContentPackages.FirstOrDefault(p => p.Name.ToLower() == packageName);
                    if (package == null)
                    {
                        ThrowError("Content package \"" + packageName + "\" not found.");
                    }
                    else
                    {
                        package.CalculateHash(logging: true);
                    }
                }
                else
                {
                    GameMain.Config.SelectedContentPackages.First().CalculateHash(logging: true);
                }
            },
            () =>
            {
                return new string[][]
                {
                    GameMain.Config.SelectedContentPackages.Select(cp => cp.Name).ToArray()
                };
            }));

#if DEBUG
            /*TODO: reimplement
            commands.Add(new Command("simulatedlatency", "simulatedlatency [minimumlatencyseconds] [randomlatencyseconds]: applies a simulated latency to network messages. Useful for simulating real network conditions when testing the multiplayer locally.", (string[] args) =>
            {
                if (args.Count() < 2 || (GameMain.NetworkMember == null)) return;
                if (!float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float minimumLatency))
                {
                    ThrowError(args[0] + " is not a valid latency value.");
                    return;
                }
                if (!float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float randomLatency))
                {
                    ThrowError(args[1] + " is not a valid latency value.");
                    return;
                }
#if CLIENT
                if (GameMain.Client != null)
                {
                    GameMain.Client.NetPeerConfiguration.SimulatedMinimumLatency = minimumLatency;
                    GameMain.Client.NetPeerConfiguration.SimulatedRandomLatency = randomLatency;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.NetPeerConfiguration.SimulatedMinimumLatency = minimumLatency;
                    GameMain.Server.NetPeerConfiguration.SimulatedRandomLatency = randomLatency;
                }
#endif
                NewMessage("Set simulated minimum latency to " + minimumLatency + " and random latency to " + randomLatency + ".", Color.White);
            }));
            commands.Add(new Command("simulatedloss", "simulatedloss [lossratio]: applies simulated packet loss to network messages. For example, a value of 0.1 would mean 10% of the packets are dropped. Useful for simulating real network conditions when testing the multiplayer locally.", (string[] args) =>
            {
                if (args.Count() < 1 || (GameMain.NetworkMember == null)) return;
                if (!float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float loss))
                {
                    ThrowError(args[0] + " is not a valid loss ratio.");
                    return;
                }
#if CLIENT
                if (GameMain.Client != null)
                {
                    GameMain.Client.NetPeerConfiguration.SimulatedLoss = loss;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.NetPeerConfiguration.SimulatedLoss = loss;
                }
#endif
                NewMessage("Set simulated packet loss to " + (int)(loss * 100) + "%.", Color.White);
            }));
            commands.Add(new Command("simulatedduplicateschance", "simulatedduplicateschance [duplicateratio]: simulates packet duplication in network messages. For example, a value of 0.1 would mean there's a 10% chance a packet gets sent twice. Useful for simulating real network conditions when testing the multiplayer locally.", (string[] args) =>
            {
                if (args.Count() < 1 || (GameMain.NetworkMember == null)) return;
                if (!float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float duplicates))
                {
                    ThrowError(args[0] + " is not a valid duplicate ratio.");
                    return;
                }
#if CLIENT
                if (GameMain.Client != null)
                {
                    GameMain.Client.NetPeerConfiguration.SimulatedDuplicatesChance = duplicates;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.NetPeerConfiguration.SimulatedDuplicatesChance = duplicates;
                }
#endif
                NewMessage("Set packet duplication to " + (int)(duplicates * 100) + "%.", Color.White);
            }));*/
#endif

            //"dummy commands" that only exist so that the server can give clients permissions to use them
            //TODO: alphabetical order?
            commands.Add(new Command("control", "control [character name]: Start controlling the specified character (client-only).", null, () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));
            commands.Add(new Command("los", "Toggle the line of sight effect on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("lighting|lights", "Toggle lighting on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("ambientlight", "ambientlight [color]: Change the color of the ambient light in the level.", null, isCheat: true));
            commands.Add(new Command("debugdraw", "Toggle the debug drawing mode on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("togglehud|hud", "Toggle the character HUD (inventories, icons, buttons, etc) on/off (client-only).", null));
            commands.Add(new Command("toggleupperhud", "Toggle the upper part of the ingame HUD (chatbox, crewmanager) on/off (client-only).", null));
            commands.Add(new Command("toggleitemhighlights", "Toggle the item highlight effect on/off (client-only).", null));
            commands.Add(new Command("togglecharacternames", "Toggle the names hovering above characters on/off (client-only).", null));
            commands.Add(new Command("followsub", "Toggle whether the camera should follow the nearest submarine (client-only).", null));
            commands.Add(new Command("toggleaitargets|aitargets", "Toggle the visibility of AI targets (= targets that enemies can detect and attack/escape from) (client-only).", null, isCheat: true));
            
            InitProjectSpecific();

            commands.Sort((c1, c2) => c1.names[0].CompareTo(c2.names[0]));
        }

        private static string[] SplitCommand(string command)
        {
            command = command.Trim();

            List<string> commands = new List<string>();
            int escape = 0;
            bool inQuotes = false;
            string piece = "";
            
            for (int i = 0; i < command.Length; i++)
            {
                if (command[i] == '\\')
                {
                    if (escape == 0) escape = 2;
                    else piece += '\\';
                }
                else if (command[i] == '"')
                {
                    if (escape == 0) inQuotes = !inQuotes;
                    else piece += '"';
                }
                else if (command[i] == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrWhiteSpace(piece)) commands.Add(piece);
                    piece = "";
                }
                else if (escape == 0) piece += command[i];

                if (escape > 0) escape--;
            }

            if (!string.IsNullOrWhiteSpace(piece)) commands.Add(piece); //add final piece

            return commands.ToArray();
        }

        public static string AutoComplete(string command, int increment = 1)
        {
            string[] splitCommand = SplitCommand(command);
            string[] args = splitCommand.Skip(1).ToArray();

            //if an argument is given or the last character is a space, attempt to autocomplete the argument
            if (args.Length > 0 || (splitCommand.Length > 0 && command.Last() == ' '))
            {
                Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0]));
                if (matchingCommand == null || matchingCommand.GetValidArgs == null) return command;

                int autoCompletedArgIndex = args.Length > 0 && command.Last() != ' ' ? args.Length - 1 : args.Length;

                //get all valid arguments for the given command
                string[][] allArgs = matchingCommand.GetValidArgs();
                if (allArgs == null || allArgs.GetLength(0) < autoCompletedArgIndex + 1) return command;

                if (string.IsNullOrEmpty(currentAutoCompletedCommand))
                {
                    currentAutoCompletedCommand = autoCompletedArgIndex > args.Length - 1 ? " " : args.Last();
                }

                //find all valid autocompletions for the given argument
                string[] validArgs = allArgs[autoCompletedArgIndex].Where(arg =>
                    currentAutoCompletedCommand.Trim().Length <= arg.Length &&
                    arg.Substring(0, currentAutoCompletedCommand.Trim().Length).ToLower() == currentAutoCompletedCommand.Trim().ToLower()).ToArray();

                if (validArgs.Length == 0) return command;

                currentAutoCompletedIndex = MathUtils.PositiveModulo(currentAutoCompletedIndex + increment, validArgs.Length);
                string autoCompletedArg = validArgs[currentAutoCompletedIndex];

                //add quotation marks to args that contain spaces
                if (autoCompletedArg.Contains(' ')) autoCompletedArg = '"' + autoCompletedArg + '"';
                for (int i = 0; i < splitCommand.Length; i++)
                {
                    if (splitCommand[i].Contains(' ')) splitCommand[i] = '"' + splitCommand[i] + '"';
                }

                return string.Join(" ", autoCompletedArgIndex >= args.Length ? splitCommand : splitCommand.Take(splitCommand.Length - 1)) + " " + autoCompletedArg;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(currentAutoCompletedCommand))
                {
                    currentAutoCompletedCommand = command;
                }

                List<string> matchingCommands = new List<string>();
                foreach (Command c in commands)
                {
                    foreach (string name in c.names)
                    {
                        if (currentAutoCompletedCommand.Length > name.Length) continue;
                        if (currentAutoCompletedCommand == name.Substring(0, currentAutoCompletedCommand.Length))
                        {
                            matchingCommands.Add(name);
                        }
                    }
                }

                if (matchingCommands.Count == 0) return command;
                
                currentAutoCompletedIndex = MathUtils.PositiveModulo(currentAutoCompletedIndex + increment, matchingCommands.Count);
                return matchingCommands[currentAutoCompletedIndex];
            }
        }

        private static string AutoCompleteStr(string str, IEnumerable<string> validStrings)
        {
            if (string.IsNullOrEmpty(str)) return str;
            foreach (string validStr in validStrings)
            {
                if (validStr.Length > str.Length && validStr.Substring(0, str.Length) == str) return validStr;
            }
            return str;
        }

        public static void ResetAutoComplete()
        {
            currentAutoCompletedCommand = "";
            currentAutoCompletedIndex = 0;
        }

        public static string SelectMessage(int direction, string currentText = null)
        {
            if (Messages.Count == 0) return "";

            direction = MathHelper.Clamp(direction, -1, 1);

			int i = 0;
			do
			{
				selectedIndex += direction;
				if (selectedIndex < 0) selectedIndex = Messages.Count - 1;
				selectedIndex = selectedIndex % Messages.Count;
				if (++i >= Messages.Count) break;
			} while (!Messages[selectedIndex].IsCommand || Messages[selectedIndex].Text == currentText);

            return !Messages[selectedIndex].IsCommand ? "" : Messages[selectedIndex].Text;            
        }

        public static void ExecuteCommand(string command)
        {
            if (activeQuestionCallback != null)
            {
#if CLIENT
                activeQuestionText = null;
#endif
                NewMessage(command, Color.White, true);
                //reset the variable before invoking the delegate because the method may need to activate another question
                var temp = activeQuestionCallback;
                activeQuestionCallback = null;
                temp(command);
                return;
            }

            if (string.IsNullOrWhiteSpace(command) || command == "\\" || command == "\n") return;

            string[] splitCommand = SplitCommand(command);
            if (splitCommand.Length == 0)
            {
                ThrowError("Failed to execute command \"" + command + "\"!");
                GameAnalyticsManager.AddErrorEventOnce(
                    "DebugConsole.ExecuteCommand:LengthZero",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Failed to execute command \"" + command + "\"!");
                return;
            }

            if (!splitCommand[0].ToLowerInvariant().Equals("admin"))
            {
                NewMessage(command, Color.White, true);
            }
            
#if CLIENT
            if (GameMain.Client != null)
            {
                if (GameMain.Client.HasConsoleCommandPermission(splitCommand[0].ToLowerInvariant()))
                {
                    Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0].ToLowerInvariant()));

                    //if the command is not defined client-side, we'll relay it anyway because it may be a custom command at the server's side
                    if (matchingCommand == null || matchingCommand.RelayToServer)
                    {
                        GameMain.Client.SendConsoleCommand(command);
                        NewMessage("Server command: " + command, Color.White);
                    }
                    else
                    {
                        matchingCommand.ClientExecute(splitCommand.Skip(1).ToArray());
                    }
                    
                    return;
                }
#if !DEBUG
                if (!IsCommandPermitted(splitCommand[0].ToLowerInvariant(), GameMain.Client))
                {
                    ThrowError("You're not permitted to use the command \"" + splitCommand[0].ToLowerInvariant() + "\"!");
                    return;
                }
#endif
            }
#endif

            bool commandFound = false;
            foreach (Command c in commands)
            {
                if (!c.names.Contains(splitCommand[0].ToLowerInvariant())) continue;                
                c.Execute(splitCommand.Skip(1).ToArray());
                commandFound = true;
                break;                
            }

            if (!commandFound)
            {
                ThrowError("Command \"" + splitCommand[0] + "\" not found.");
            }
        }
        
        private static Character FindMatchingCharacter(string[] args, bool ignoreRemotePlayers = false, Client allowedRemotePlayer = null)
        {
            if (args.Length == 0) return null;

            string characterName;
            if (int.TryParse(args.Last(), out int characterIndex) && args.Length > 1)
            {
                characterName = string.Join(" ", args.Take(args.Length - 1)).ToLowerInvariant();
            }
            else
            {
                characterName = string.Join(" ", args).ToLowerInvariant();
                characterIndex = -1;
            }

            var matchingCharacters = Character.CharacterList.FindAll(c => 
                c.Name.ToLowerInvariant() == characterName &&
                (!c.IsRemotePlayer || !ignoreRemotePlayers || allowedRemotePlayer?.Character == c));

            if (!matchingCharacters.Any())
            {
                NewMessage("Character \""+ characterName + "\" not found", Color.Red);
                return null;
            }

            if (characterIndex == -1)
            {
                if (matchingCharacters.Count > 1)
                {
                    NewMessage(
                        "Found multiple matching characters. " +
                        "Use \"[charactername] [0-" + (matchingCharacters.Count - 1) + "]\" to choose a specific character.",
                        Color.LightGray);
                }
                return matchingCharacters[0];
            }
            else if (characterIndex < 0 || characterIndex >= matchingCharacters.Count)
            {
                ThrowError("Character index out of range. Select an index between 0 and " + (matchingCharacters.Count - 1));
            }
            else
            {
                return matchingCharacters[characterIndex];
            }

            return null;
        }

        private static void SpawnCharacter(string[] args, Vector2 cursorWorldPos, out string errorMsg)
        {
            errorMsg = "";
            if (args.Length == 0) return;

            Character spawnedCharacter = null;

            Vector2 spawnPosition = Vector2.Zero;
            WayPoint spawnPoint = null;

            string characterLowerCase = args[0].ToLowerInvariant();
            JobPrefab job = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == characterLowerCase || jp.Identifier.ToLowerInvariant() == characterLowerCase);
            bool human = job != null || characterLowerCase == "human";

            if (args.Length > 1)
            {
                switch (args[1].ToLowerInvariant())
                {
                    case "inside":
                        spawnPoint = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                        break;
                    case "outside":
                        spawnPoint = WayPoint.GetRandom(SpawnType.Enemy);
                        break;
                    case "near":
                    case "close":
                        float closestDist = -1.0f;
                        foreach (WayPoint wp in WayPoint.WayPointList)
                        {
                            if (wp.Submarine != null) continue;

                            //don't spawn inside hulls
                            if (Hull.FindHull(wp.WorldPosition, null) != null) continue;

                            float dist = Vector2.Distance(wp.WorldPosition, GameMain.GameScreen.Cam.WorldViewCenter);

                            if (closestDist < 0.0f || dist < closestDist)
                            {
                                spawnPoint = wp;
                                closestDist = dist;
                            }
                        }
                        break;
                    case "cursor":
                        spawnPosition = cursorWorldPos;
                        break;
                    default:
                        spawnPoint = WayPoint.GetRandom(human ? SpawnType.Human : SpawnType.Enemy);
                        break;
                }
            }
            else
            {
                spawnPoint = WayPoint.GetRandom(human ? SpawnType.Human : SpawnType.Enemy);
            }

            if (string.IsNullOrWhiteSpace(args[0])) return;

            if (spawnPoint != null) spawnPosition = spawnPoint.WorldPosition;

            if (human)
            {
                CharacterInfo characterInfo = new CharacterInfo(Character.HumanConfigFile, jobPrefab: job);
                spawnedCharacter = Character.Create(characterInfo, spawnPosition, ToolBox.RandomSeed(8));
                if (job != null)
                {
                    spawnedCharacter.GiveJobItems(spawnPoint);
                }

                if (GameMain.GameSession != null)
                {
                    if (GameMain.GameSession.GameMode != null && !GameMain.GameSession.GameMode.IsSinglePlayer)
                    {
                        //TODO: a way to select which team to spawn to?
                        spawnedCharacter.TeamID = Character.Controlled != null ? Character.Controlled.TeamID : Character.TeamType.Team1;
                    }
#if CLIENT
                    GameMain.GameSession.CrewManager.AddCharacter(spawnedCharacter);          
#endif
                }
            }
            else
            {
                IEnumerable<string> characterFiles = GameMain.Instance.GetFilesOfType(ContentType.Character);
                foreach (string characterFile in characterFiles)
                {
                    if (Path.GetFileNameWithoutExtension(characterFile).ToLowerInvariant() == args[0].ToLowerInvariant())
                    {
                        Character.Create(characterFile, spawnPosition, ToolBox.RandomSeed(8));
                        return;
                    }
                }

                errorMsg = "No character matching the name \"" + args[0] + "\" found in the selected content package.";

                //attempt to open the config from the default path (the file may still be present even if it isn't included in the content package)
                string configPath = "Content/Characters/"
                    + args[0].First().ToString().ToUpper() + args[0].Substring(1)
                    + "/" + args[0].ToLower() + ".xml";
                Character.Create(configPath, spawnPosition, ToolBox.RandomSeed(8));
            }
        }

        private static void SpawnItem(string[] args, Vector2 cursorPos, Character controlledCharacter, out string errorMsg)
        {
            errorMsg = "";
            if (args.Length < 1) return;

            Vector2? spawnPos = null;
            Inventory spawnInventory = null;
            
            if (args.Length > 1)
            {
                switch (args.Last())
                {
                    case "cursor":
                        spawnPos = cursorPos;
                        break;
                    case "inventory":
                        spawnInventory = controlledCharacter?.Inventory;
                        break;
                    case "cargo":
                        var wp = WayPoint.GetRandom(SpawnType.Cargo, null, Submarine.MainSub);
                        spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
                        break;
                    case "random":
                        //Dont do a thing, random is basically Human points anyways - its in the help description.
                        break;
                    default:
                        var matchingCharacter = FindMatchingCharacter(args.Skip(1).ToArray());
                        if (matchingCharacter != null){ spawnInventory = matchingCharacter.Inventory; }
                        break;
                }
            }

            string itemName = args[0].ToLowerInvariant();
            if (!(MapEntityPrefab.Find(itemName) is ItemPrefab itemPrefab))
            {
                errorMsg = "Item \"" + itemName + "\" not found!";
                return;
            }

            if ((spawnPos == null || spawnPos == Vector2.Zero) && spawnInventory == null)
            {
                var wp = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
            }

            if (spawnPos != null)
            {
                if (Entity.Spawner == null)
                {
                    new Item(itemPrefab, spawnPos.Value, null);
                }
                else
                {
                    Entity.Spawner?.AddToSpawnQueue(itemPrefab, spawnPos.Value);
                }
            }
            else if (spawnInventory != null)
            {
                if (Entity.Spawner == null)
                {
                    var spawnedItem = new Item(itemPrefab, Vector2.Zero, null);
                    spawnInventory.TryPutItem(spawnedItem, null, spawnedItem.AllowedSlots);
                }
                else
                {
                    Entity.Spawner?.AddToSpawnQueue(itemPrefab, spawnInventory);
                }
            }
        }

        public static void NewMessage(string msg, bool isCommand = false)
        {
            NewMessage(msg, Color.White, isCommand);
        }

        public static void NewMessage(string msg, Color color, bool isCommand = false)
        {
            if (string.IsNullOrEmpty((msg))) return;
            
            var newMsg = new ColoredText(msg, color, isCommand);
            lock (queuedMessages)
            {
                queuedMessages.Enqueue(new ColoredText(msg, color, isCommand));
            }
        }

        public static void ShowQuestionPrompt(string question, QuestionCallback onAnswered)
        {
#if CLIENT
            activeQuestionText = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, 0), listBox.Content.RectTransform),
                "   >>" + question, font: GUI.SmallFont, wrap: true)
            {
                CanBeFocused = false,
                TextColor = Color.Cyan
            };
#else
            NewMessage("   >>" + question, Color.Cyan);
#endif
            activeQuestionCallback += onAnswered;
        }

        private static bool TryParseTimeSpan(string s, out TimeSpan timeSpan)
        {
            timeSpan = new TimeSpan();
            if (string.IsNullOrWhiteSpace(s)) return false;

            string currNum = "";
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                {
                    currNum += c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                else
                {
                    int parsedNum = 0;
                    if (!int.TryParse(currNum, out parsedNum))
                    {
                        return false;
                    }

                    switch (c)
                    {
                        case 'd':
                            timeSpan += new TimeSpan(parsedNum, 0, 0, 0, 0);
                            break;
                        case 'h':
                            timeSpan += new TimeSpan(0, parsedNum, 0, 0, 0);
                            break;
                        case 'm':
                            timeSpan += new TimeSpan(0, 0, parsedNum, 0, 0);
                            break;
                        case 's':
                            timeSpan += new TimeSpan(0, 0, 0, parsedNum, 0);
                            break;
                        default:
                            return false;
                    }

                    currNum = "";
                }
            }

            return true;
        }

        public static Command FindCommand(string commandName)
        {
            commandName = commandName.ToLowerInvariant();
            return commands.Find(c => c.names.Any(n => n.ToLowerInvariant() == commandName));
        }


        public static void Log(string message)
        {
            if (GameSettings.VerboseLogging) NewMessage(message, Color.Gray);
        }

        public static void ThrowError(string error, Exception e = null, bool createMessageBox = false)
        {
            if (e != null)
            {
                error += " {" + e.Message + "}\n" + e.StackTrace;
            }
            System.Diagnostics.Debug.WriteLine(error);
            NewMessage(error, Color.Red);
#if CLIENT
            if (createMessageBox)
            {
                new GUIMessageBox(TextManager.Get("Error"), error);
            }
            else
            {
                isOpen = true;
            }
#endif
        }
        
        public static void SaveLogs()
        {
            if (unsavedMessages.Count == 0) return;
            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception e)
                {
                    ThrowError("Failed to create a folder for debug console logs", e);
                    return;
                }
            }

            string fileName = "DebugConsoleLog_";
#if SERVER
            fileName += "Server_";
#else
            fileName += "Client_";
#endif

            fileName += DateTime.Now.ToShortDateString() + "_" + DateTime.Now.ToShortTimeString();
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                fileName = fileName.Replace(invalidChar.ToString(), "");
            }

            string filePath = Path.Combine(SavePath, fileName);
            if (File.Exists(filePath + ".txt"))
            {
                int fileNum = 2;
                while (File.Exists(filePath + " (" + fileNum + ")"))
                {
                    fileNum++;
                }
                filePath = filePath + " (" + fileNum + ")";
            }

            try
            {
                File.WriteAllLines(filePath + ".txt", unsavedMessages.Select(l => "[" + l.Time + "] " + l.Text));
            }
            catch (Exception e)
            {
                unsavedMessages.Clear();
                ThrowError("Saving debug console log to " + filePath + " failed", e);
            }
        }
    }
}

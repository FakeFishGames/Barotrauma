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
using Barotrauma.IO;
using System.Linq;
using System.Text;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    struct ColoredText
    {
        public string Text;
        public Color Color;
		public bool IsCommand;
        public bool IsError;

        public readonly string Time;

        public ColoredText(string text, Color color, bool isCommand, bool isError)
        {
            this.Text = text;
            this.Color = color;
			this.IsCommand = isCommand;
            this.IsError = isError;

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
#if USE_STEAM
                    NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
#endif
                    return;
                }

                OnExecute(args);
            }

            public override int GetHashCode()
            {
                return names[0].GetHashCode();
            }
        }

        private static readonly Queue<ColoredText> queuedMessages = new Queue<ColoredText>();

        static partial void ShowHelpMessage(Command command);
        
        const int MaxMessages = 300;

        public static List<ColoredText> Messages = new List<ColoredText>();

        public delegate void QuestionCallback(string answer);
        private static QuestionCallback activeQuestionCallback;

        private static readonly List<Command> commands = new List<Command>();
        public static List<Command> Commands
        {
            get { return commands; }
        }
        
        private static string currentAutoCompletedCommand;
        private static int currentAutoCompletedIndex;

        public static bool CheatsEnabled;

        private static readonly List<ColoredText> unsavedMessages = new List<ColoredText>();
        private static readonly int messagesPerFile = 5000;
        public const string SavePath = "ConsoleLogs";

        private static void AssignOnExecute(string names, Action<string[]> onExecute)
        {
            var matchingCommand = commands.Find(c => c.names.Intersect(names.Split('|')).Count() > 0);
            if (matchingCommand == null)
            {
                throw new Exception("AssignOnExecute failed. Command matching the name(s) \"" + names + "\" not found.");
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
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    if (string.IsNullOrEmpty(itemPrefab.Name)) continue;
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

            commands.Add(new Command("spawn|spawncharacter", "spawn [creaturename/jobname] [near/inside/outside/cursor] [team (0-3)]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine). You can also enter the name of a job (e.g. \"Mechanic\") to spawn a character with a specific job and the appropriate equipment.", null,
            () =>
            {
                List<string> characterFiles = GameMain.Instance.GetFilesOfType(ContentType.Character).Select(f => f.Path).ToList();
                for (int i = 0; i < characterFiles.Count; i++)
                {
                    characterFiles[i] = Path.GetFileNameWithoutExtension(characterFiles[i]).ToLowerInvariant();
                }

                foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
                {
                    characterFiles.Add(jobPrefab.Name);
                }

                return new string[][]
                {
                    characterFiles.ToArray(),
                    new string[] { "near", "inside", "outside", "cursor" }
                };
            }, isCheat: true));

            commands.Add(new Command("spawnitem", "spawnitem [itemname/itemidentifier] [cursor/inventory/cargo/random/[name]] [amount]: Spawn an item at the position of the cursor, in the inventory of the controlled character, in the inventory of the client with the given name, or at a random spawnpoint if the last parameter is omitted or \"random\".",
            (string[] args) =>
            {
                try
                {
#if CLIENT
                    SpawnItem(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), Character.Controlled, out string errorMsg);
#elif SERVER
                    SpawnItem(args, Vector2.Zero, null, out string errorMsg);
#endif
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        ThrowError(errorMsg);
                    }
                }
                catch (Exception e)
                {
                    string errorMsg = "Failed to spawn an item. Arguments: \"" + string.Join(" ", args) + "\".";
                    ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("DebugConsole.SpawnItem:Error", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + '\n' + e.Message + '\n' + e.StackTrace.CleanupStackTrace());
                }
            },
            () =>
            {
                List<string> itemNames = new List<string>();
                foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
                {
                    itemNames.Add(itemPrefab.Name);
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
            }, isCheat: true));

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

            commands.Add(new Command("starttraitormissionimmediately", "starttraitormissionimmediately: Skip the initial delay of the traitor mission and start one immediately.", null));

            commands.Add(new Command("botcount", "botcount [x]: Set the number of bots in the crew in multiplayer.", null));

            commands.Add(new Command("botspawnmode", "botspawnmode [fill/normal]: Set how bots are spawned in the multiplayer.", null));

            commands.Add(new Command("killdisconnectedtimer", "killdisconnectedtimer [seconds]: Set the time after which disconnect players' characters get automatically killed.", null));

            commands.Add(new Command("autorestart", "autorestart [true/false]: Enable or disable round auto-restart.", null));

            commands.Add(new Command("autorestartinterval", "autorestartinterval [seconds]: Set how long the server waits between rounds before automatically starting a new one. If set to 0, autorestart is disabled.", null));

            commands.Add(new Command("autorestarttimer", "autorestarttimer [seconds]: Set the current autorestart countdown to the specified value.", null));

            commands.Add(new Command("startwhenclientsready", "startwhenclientsready [true/false]: Enable or disable automatically starting the round when clients are ready to start.", null));

            commands.Add(new Command("giveperm", "giveperm [id/steamid/endpoint/name]: Grants administrative permissions to the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        Enum.GetValues(typeof(ClientPermissions)).Cast<ClientPermissions>().Select(v => v.ToString()).ToArray()
                    };
                }));

            commands.Add(new Command("revokeperm", "revokeperm [id/steamid/endpoint/name]: Revokes administrative permissions from the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        Enum.GetValues(typeof(ClientPermissions)).Cast<ClientPermissions>().Select(v => v.ToString()).ToArray()
                    };
                }));
            
            commands.Add(new Command("giverank", "giverank [id/steamid/endpoint/name]: Assigns a specific rank (= a set of administrative permissions) to the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        PermissionPreset.List.Select(pp => pp.Name).ToArray()
                    };
                }));

            commands.Add(new Command("givecommandperm", "givecommandperm [id/steamid/endpoint/name]: Gives the specified client the permission to use the specified console commands.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        commands.Select(c => c.names[0]).Union(new string[]{ "All" }).ToArray()
                    };
                }));

            commands.Add(new Command("revokecommandperm", "revokecommandperm [id/steamid/endpoint/name]: Revokes permission to use the specified console commands from the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        commands.Select(c => c.names[0]).Union(new string[]{ "All" }).ToArray()
                    };
                }));

            commands.Add(new Command("showperm", "showperm [id/steamid/endpoint/name]: Shows the current administrative permissions of the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                    };
                }));

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
                if (GameMain.NetworkMember == null || args.Length == 0) { return; }

                string playerName = string.Join(" ", args);

                ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"? (Enter c to cancel)", (reason) =>
                {
                    if (reason == "c" || reason == "C") { return; }
                    GameMain.NetworkMember.KickPlayer(playerName, reason);
                });
            },
            () =>
            {
                if (GameMain.NetworkMember == null) { return null; }

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

                ShowQuestionPrompt("Reason for kicking \"" + client.Name + "\"? (Enter c to cancel)", (reason) =>
                {
                    if (reason == "c" || reason == "C") { return; }
                    GameMain.NetworkMember.KickPlayer(client.Name, reason);
                });
            }));

            commands.Add(new Command("ban", "ban [name]: Kick and ban the player from the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;

                string clientName = string.Join(" ", args);
                ShowQuestionPrompt("Reason for banning \"" + clientName + "\"? (Enter c to cancel)", (reason) =>
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

                ShowQuestionPrompt("Reason for banning \"" + client.Name + "\"? (Enter c to cancel)", (reason) =>
                {
                    if (reason == "c" || reason == "C") { return; }
                    ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\") (c to cancel)", (duration) =>
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

                        GameMain.NetworkMember.BanPlayer(client.Name, reason, false, banDuration);
                    });
                });
            }));
            
            commands.Add(new Command("banendpoint|banip", "banendpoint [endpoint]: Ban the IP address/SteamID from the server.", null));
            
            commands.Add(new Command("teleportcharacter|teleport", "teleport [character name]: Teleport the specified character to the position of the cursor. If the name parameter is omitted, the controlled character will be teleported.", null,
            () =>
            {
                return new string[][] { ListCharacterNames() };
            }, isCheat: true));

            commands.Add(new Command("godmode", "godmode [character name]: Toggle character godmode. Makes the targeted character invulnerable to damage. If the name parameter is omitted, the controlled character will receive godmode.",
            (string[] args) =>
            {
                Character targetCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args, false);

                if (targetCharacter == null) { return; }

                targetCharacter.GodMode = !targetCharacter.GodMode;
            },
            () =>
            {
                return new string[][] { ListCharacterNames() };
            }, isCheat: true));

            commands.Add(new Command("godmode_mainsub", "godmode_mainsub: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                NewMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", Color.White);
            }, isCheat: true));

            commands.Add(new Command("growthdelay", "growthdelay: Sets how long it takes for planters to attempt to advance a plant's growth.", (string[] args) =>
            {
                if (args.Length > 0 && float.TryParse(args[0], out float value))
                {
                    Planter.GrowthTickDelay = value;
                    NewMessage($"Growth delay set to {value}.", Color.Green);
                    return;
                }
                NewMessage("Invalid value.", Color.Red);
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
                    Entity.DumpIds(count, args.Length >= 2 ? args[1] : null);
                }
                catch (Exception e)
                {
                    ThrowError("Failed to dump ids", e);
                }
            }));

            commands.Add(new Command("dumptofile", "findentityids [filename]: Outputs the contents of the debug console into a text file in the game folder. If the filename argument is omitted, \"consoleOutput.txt\" is used as the filename.", (string[] args) =>
            {
                string filename = "consoleOutput.txt";
                if (args.Length > 0) { filename = string.Join(" ", args); }

                File.WriteAllLines(filename, Messages.Select(m => m.Text).ToArray());
            }));

            commands.Add(new Command("findentityids", "findentityids [entityname]", (string[] args) =>
            {
                if (args.Length == 0) { return; }
                foreach (MapEntity mapEntity in MapEntity.mapEntityList)
                {
                    if (mapEntity.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase))
                    {
                        ThrowError(mapEntity.ID + ": " + mapEntity.Name.ToString());
                    }
                }
                foreach (Character character in Character.CharacterList)
                {
                    if (character.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase) || character.SpeciesName.Equals(args[0], StringComparison.OrdinalIgnoreCase))
                    {
                        ThrowError(character.ID + ": " + character.Name.ToString());
                    }
                }
            }));

            commands.Add(new Command("giveaffliction", "giveaffliction [affliction name] [affliction strength] [character name] [limb type]: Add an affliction to a character. If the name parameter is omitted, the affliction is added to the controlled character.", (string[] args) =>
            {
                if (args.Length < 2) { return; }

                AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(a =>
                    a.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase) ||
                    a.Identifier.Equals(args[0], StringComparison.OrdinalIgnoreCase));
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

                bool relativeStrength = false;
                if (args.Length > 2)
                {
                    bool.TryParse(args[2], out relativeStrength);
                }

                Character targetCharacter = (relativeStrength || args.Length <= 2) ? Character.Controlled : FindMatchingCharacter(new string[] { args[2] });


                if (targetCharacter != null)
                {
                    Limb targetLimb = targetCharacter.AnimController.MainLimb;
                    if (args.Length > 3)
                    {
                        targetLimb = targetCharacter.AnimController.Limbs.FirstOrDefault(l => l.type.ToString().Equals(args[3], StringComparison.OrdinalIgnoreCase));
                    }
                    if (relativeStrength)
                    {
                        afflictionStrength *= targetCharacter.MaxVitality / afflictionPrefab.MaxStrength;
                    }
                    targetCharacter.CharacterHealth.ApplyAffliction(targetLimb ?? targetCharacter.AnimController.MainLimb, afflictionPrefab.Instantiate(afflictionStrength));
                }
            },
            () =>
            {
                return new string[][]
                {
                    AfflictionPrefab.List.Select(a => a.Name).ToArray(),
                    new string[] { "1" },
                    Character.CharacterList.Select(c => c.Name).ToArray(),
                    Enum.GetNames(typeof(LimbType)).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("heal", "heal [character name] [all]: Restore the specified character to full health. If the name parameter is omitted, the controlled character will be healed. By default only heals common afflictions such as physical damage and blood loss: use the \"all\" argument to heal everything, including poisonings/addictions/etc.", (string[] args) =>
            {
                bool healAll = args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
                Character healedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(healAll ? args.Take(args.Length - 1).ToArray() : args);
                if (healedCharacter != null)
                {
                    healedCharacter.SetAllDamage(0.0f, 0.0f, 0.0f);
                    healedCharacter.Oxygen = 100.0f;
                    healedCharacter.Bloodloss = 0.0f;
                    healedCharacter.SetStun(0.0f, true);
                    if (healAll)
                    {
                        healedCharacter.CharacterHealth.RemoveAllAfflictions();
                    }
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
#if CLIENT
                if (Screen.Selected == GameMain.SubEditorScreen) { return; }
                Character.Controlled = null;
                GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                GameMain.Client?.SendConsoleCommand("freecam");
#endif
            }, isCheat: true));

            commands.Add(new Command("eventmanager", "eventmanager: Toggle event manager on/off. No new random events are created when the event manager is disabled.", (string[] args) =>
            {
                if (GameMain.GameSession?.EventManager != null)
                {
                    GameMain.GameSession.EventManager.Enabled = !GameMain.GameSession.EventManager.Enabled;
                    NewMessage(GameMain.GameSession.EventManager.Enabled ? "Event manager on" : "Event manager off", Color.White);
                }
            }, isCheat: true));
            
            commands.Add(new Command("triggerevent", "triggerevent [identifier]: Created a new event.", (string[] args) =>
            {
                List<EventPrefab> eventPrefabs = EventSet.GetAllEventPrefabs().Where(prefab => !string.IsNullOrWhiteSpace(prefab.Identifier)).ToList();
                if (GameMain.GameSession?.EventManager != null && args.Length > 0)
                {
                    EventPrefab eventPrefab = eventPrefabs.Find(prefab => string.Equals(prefab.Identifier, args[0], StringComparison.InvariantCultureIgnoreCase));

                    if (eventPrefab != null)
                    {
                        var newEvent = eventPrefab.CreateInstance();
                        if (newEvent == null)
                        {
                            NewMessage($"Could not initialize event {args[0]} because level did not meet requirements");
                            return;
                        }
                        GameMain.GameSession.EventManager.ActiveEvents.Add(newEvent);
                        newEvent.Init(true);
                        NewMessage($"Initialized event {eventPrefab.Identifier}", Color.Aqua);
                        return;
                    }

                    NewMessage($"Failed to trigger event because {args[0]} is not a valid event identifier.", Color.Red);
                    return;
                }
                NewMessage("Failed to trigger event", Color.Red);
            }, isCheat: true, getValidArgs: () =>
            {
                List<EventPrefab> eventPrefabs = EventSet.GetAllEventPrefabs().Where(prefab => !string.IsNullOrWhiteSpace(prefab.Identifier)).ToList();
                
                return new[]
                {
                   eventPrefabs.Select(prefab => prefab.Identifier).Distinct().ToArray()
                };
            }));
            
            commands.Add(new Command("setskill", "setskill [all/identifier] [max/level] [character]: Set your skill level.", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    NewMessage($"Missing arguments. Expected at least 2 but got {args.Length} (skill, level, name)", Color.Red);
                    return;
                }

                string skillIdentifier = args[0];
                string levelString = args[1];
                Character character = args.Length >= 3 ? FindMatchingCharacter(args.Skip(2).ToArray(), false) : Character.Controlled;

                if (character?.Info?.Job == null)
                {
                    NewMessage("Character is not valid.", Color.Red);
                    return;
                }

                bool isMax = levelString.Equals("max", StringComparison.OrdinalIgnoreCase);

                if (float.TryParse(levelString, NumberStyles.Number, CultureInfo.InvariantCulture, out float level) || isMax)
                {
                    if (isMax) { level = 100; }
                    if (skillIdentifier.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (Skill skill in character.Info.Job.Skills)
                        {
                            character.Info.SetSkillLevel(skill.Identifier, level);
                        }
                        NewMessage($"Set all {character.Name}'s skills to {level}", Color.Green);
                    }
                    else
                    {
                        character.Info.SetSkillLevel(skillIdentifier, level);
                        NewMessage($"Set {character.Name}'s {skillIdentifier} level to {level}", Color.Green);
                    }
                }
                else
                {
                    NewMessage($"{levelString} is not a valid level. Expected number or \"max\".", Color.Red);
                }
            }, isCheat: true, getValidArgs: () =>
            {
                return new[]
                {
                    Character.Controlled?.Info?.Job?.Skills?.Select(skill => skill.Identifier).ToArray() ?? new string[0],
                    new[]{ "max" },
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray(),
                };
            }));

            commands.Add(new Command("water|editwater", "water/editwater: Toggle water editing. Allows adding water into rooms by holding the left mouse button and removing it by holding the right mouse button.", (string[] args) =>
            {
                Hull.EditWater = !Hull.EditWater;
                NewMessage(Hull.EditWater ? "Water editing on" : "Water editing off", Color.White);
            }, isCheat: true));

            commands.Add(new Command("givetalent", "givetalent [talent] [player]: give the talent to the specified character. If the character argument is omitted, the talent is given to the controlled character.", (string[] args) =>
            {
                if (args.Length == 0) { return; }
                var character = args.Length >= 2 ? FindMatchingCharacter(args.Skip(1).ToArray()) : Character.Controlled;
                if (character != null)
                {
                    TalentPrefab talentPrefab = TalentPrefab.TalentPrefabs.Find(c => 
                        c.Identifier.Equals(args[0], StringComparison.OrdinalIgnoreCase) ||
                        c.DisplayName.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                    if (talentPrefab == null)
                    {
                        ThrowError($"Couldn't find the talent \"{args[0]}\".");
                        return;
                    }
                    character.GiveTalent(talentPrefab);
                    NewMessage($"Gave talent \"{talentPrefab.DisplayName}\" to \"{character.Name}\".");
                }
            },
            () =>
            {
                List<string> talentNames = new List<string>();
                foreach (TalentPrefab talent in TalentPrefab.TalentPrefabs)
                {
                    talentNames.Add(talent.DisplayName);
                }

                return new string[][]
                {
                    talentNames.ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("unlocktalents", "unlocktalents [all/[jobname]] [character]: give the specified character all the talents of the specified class", (string[] args) =>
            {
                var character = args.Length >= 2 ? FindMatchingCharacter(args.Skip(1).ToArray()) : Character.Controlled;
                if (character == null) { return; }

                List<TalentTree> talentTrees = new List<TalentTree>();
                if (args.Length == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    talentTrees.AddRange(TalentTree.JobTalentTrees.Values);
                }
                else
                {
                    var job = JobPrefab.Prefabs.Find(jp => jp.Name != null && jp.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                    if (job == null)
                    {
                        ThrowError($"Failed to find the job \"{args[0]}\".");
                        return;
                    }
                    if (!TalentTree.JobTalentTrees.TryGetValue(job.Identifier, out TalentTree talentTree))
                    {
                        ThrowError($"No talents configured for the job \"{args[0]}\".");
                        return;
                    }
                    talentTrees.Add(talentTree);
                }

                foreach (var talentTree in talentTrees)
                {
                    foreach (var subTree in talentTree.TalentSubTrees)
                    {
                        foreach (var option in subTree.TalentOptionStages)
                        {
                            foreach (var talent in option.Talents)
                            {
                                character.GiveTalent(talent);
                                NewMessage($"Unlocked talent \"{talent.DisplayName}\".");
                            }
                        }
                    }
                }
            },
            () =>
            {
                List<string> availableArgs = new List<string>() { "All" };
                availableArgs.AddRange(JobPrefab.Prefabs.Select(j => j.Name));
                return new string[][]
                {
                    availableArgs.ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("giveexperience", "giveexperience [amount] [character]: Give experience to character.", (string[] args) =>
            {
                if (args.Length < 1)
                {
                    NewMessage($"Missing arguments. Expected at least 1 but got {args.Length} (experience, name)");
                    return;
                }

                string experienceString = args[0];
                var character = FindMatchingCharacter(args.Skip(1).ToArray()) ?? Character.Controlled;

                if (character?.Info == null)
                {
                    NewMessage("Character is not valid.");
                    return;
                }

                if (int.TryParse(experienceString, NumberStyles.Number, CultureInfo.InvariantCulture, out int experience))
                {
                    character.Info.GiveExperience(experience);
                    NewMessage($"Gave {character.Name} {experience} experience");
                }
                else
                {
                    NewMessage($"{experienceString} is not a valid value. Expected number.");
                }
            }, isCheat: true, getValidArgs: () =>
            {
                return new[]
                {
                    new string[] { "100" },
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray(),
                };
            }));

            commands.Add(new Command("fire|editfire", "fire/editfire: Allows putting up fires by left clicking.", (string[] args) =>
            {
                Hull.EditFire = !Hull.EditFire;
                NewMessage(Hull.EditFire ? "Fire spawning on" : "Fire spawning off", Color.White);                
            }, isCheat: true));

            commands.Add(new Command("explosion", "explosion [range] [force] [damage] [structuredamage] [item damage] [emp strength] [ballast flora strength]: Creates an explosion at the position of the cursor.", null, isCheat: true));

            commands.Add(new Command("showseed|showlevelseed", "showseed: Show the seed of the current level.", (string[] args) =>
            {
                if (Level.Loaded == null)
                {
                    ThrowError("No level loaded.");
                }
                else
                {
                    NewMessage("Level seed: " + Level.Loaded.Seed);
                    NewMessage("Level size: " + Level.Loaded.Size.X+"x"+ Level.Loaded.Size.Y);
                    NewMessage("Minimum main path width: " + (Level.Loaded.LevelData?.MinMainPathWidth?.ToString() ?? "unknown"));
                }
            },null));
            
            commands.Add(new Command("teleportsub", "teleportsub [start/end/cursor]: Teleport the submarine to the position of the cursor, or the start or end of the level. WARNING: does not take outposts into account, so often leads to physics glitches. Only use for debugging.", (string[] args) =>
            {
                if (Submarine.MainSub == null || Level.Loaded == null) return;
                if (Level.Loaded.Type == LevelData.LevelType.Outpost)
                {
                    NewMessage("The teleportsub command is unavailable in outpost levels!", Color.Red);
                    return;
                }

                if (args.Length == 0 || args[0].Equals("cursor", StringComparison.OrdinalIgnoreCase))
                {
#if SERVER
                    ThrowError("Cannot teleport the sub to the position of the cursor. Use \"start\" or \"end\", or execute the command as a client.");
#else
                    Submarine.MainSub.SetPosition(Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition));
#endif
                }
                else if (args[0].Equals("start", StringComparison.OrdinalIgnoreCase))
                {
                    Vector2 pos = Level.Loaded.StartPosition;
                    if (Level.Loaded.StartOutpost != null)
                    {
                        pos -= Vector2.UnitY * (Submarine.MainSub.Borders.Height + Level.Loaded.StartOutpost.Borders.Height) / 2;
                    }
                    Submarine.MainSub.SetPosition(pos);
                }
                else
                {
                    Vector2 pos = Level.Loaded.EndPosition;
                    if (Level.Loaded.EndOutpost != null)
                    {
                        pos -= Vector2.UnitY * (Submarine.MainSub.Borders.Height + Level.Loaded.EndOutpost.Borders.Height) / 2;
                    }
                    Submarine.MainSub.SetPosition(pos);
                }
            },
            () =>
            {
                return new string[][]
                {
                    new string[] { "start", "end", "cursor" }
                };
            }, isCheat: true));

#if DEBUG
            commands.Add(new Command("crash", "crash: Crashes the game.", (string[] args) =>
            {
                throw new Exception("crash command issued");
            }));

            commands.Add(new Command("removecharacter", "removecharacter [character name]: Immediately deletes the specified character.", (string[] args) =>
            {
                if (args.Length == 0) { return; }
                Character character = FindMatchingCharacter(args, false);
                if (character == null) { return; }

                Entity.Spawner?.AddToRemoveQueue(character);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("waterphysicsparams", "waterphysicsparams [stiffness] [spread] [damping]: defaults 0.02, 0.05, 0.05", (string[] args) =>
            {
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
                SubmarineInfo selectedSub = null;
                string subName = GameMain.Config.QuickStartSubmarineName;
                if (!string.IsNullOrEmpty(subName))
                {
                    selectedSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                }

                int count = 0;
                while (true)
                {
                    var gamesession = new GameSession(
                        SubmarineInfo.SavedSubmarines.GetRandom(s => s.Type == SubmarineType.Player && !s.HasTag(SubmarineTag.HideInMenus)),
                        GameModePreset.DevSandbox);
                    string seed = ToolBox.RandomSeed(16);
                    gamesession.StartRound(seed);

                    Rectangle subWorldRect = Submarine.MainSub.Borders;
                    subWorldRect.Location += new Point((int)Submarine.MainSub.WorldPosition.X, (int)Submarine.MainSub.WorldPosition.Y);
                    subWorldRect.Y -= subWorldRect.Height;
                    foreach (var ruin in Level.Loaded.Ruins)
                    {
                        if (ruin.Area.Intersects(subWorldRect))
                        {
                            ThrowError("Ruins intersect with the sub. Seed: " + seed + ", Submarine: " + Submarine.MainSub.Info.Name);
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
                            ThrowError("Level cells intersect with the sub. Seed: " + seed + ", Submarine: " + Submarine.MainSub.Info.Name);
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

            commands.Add(new Command("setlocationreputation", "setlocationreputation [value]: Set the reputation in the current location to the specified value.", (string[] args) =>
            {
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    if (args.Length == 0) { return; }
                    if (float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float reputation))
                    {
                        campaign.Map.CurrentLocation.Reputation.SetReputation(reputation);
                    }
                    else
                    {
                        ThrowError($"Could not set location reputation ({args[0]} is not a valid reputation value).");
                    }
                }
                else
                {
                    ThrowError("Could not set location reputation (no active campaign).");
                }
            }, null, true));
            
            commands.Add(new Command("setreputation", "setreputation [faction] [value]: Set the reputation of a cation to the specified value.", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    ThrowError("Insufficient arguments (expected 2)");
                    return;
                }

                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    if (campaign.Factions.FirstOrDefault(f => f.Prefab.Identifier.Equals(args[0], StringComparison.OrdinalIgnoreCase)) is { } faction)
                    {
                        if (float.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float reputation))
                        {
                            faction.Reputation.SetReputation(reputation);
                        }
                        else
                        {
                            ThrowError($"Could not set faction reputation ({args[1]} is not a valid reputation value).");
                        }
                    }
                    else
                    {
                        ThrowError($"Could not set faction reputation (faction {args[0]} not found).");
                    }
                }
                else
                {
                    ThrowError("Could not set faction reputation (no active campaign).");
                }
            }, () =>
            {
                return new[] { FactionPrefab.Prefabs.Select(f => f.Identifier).ToArray() };
            }, true));

            commands.Add(new Command("fixitems", "fixitems: Repairs all items and restores them to full condition.", (string[] args) =>
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Condition = it.MaxCondition;
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
                        string errorMsg = "Error while executing the fixhulls command.\n" + e.StackTrace.CleanupStackTrace();
                        GameAnalyticsManager.AddErrorEventOnce("DebugConsole.FixHulls", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    }
                }
            }, null, true));
            
            commands.Add(new Command("upgradeitem", "upgradeitem [upgrade] [level] [items]: Adds an upgrade to the current targeted item.", args =>
            {
                if (args.Length > 0)
                {
                    int level;
                    if (args.Length > 1)
                    {
                        if (int.TryParse(args[1], out int result))
                        {
                            level = result;
                        }
                        else
                        {
                            ThrowError($"\"{args[1]}\" is not a valid level.");
                            return;
                        }
                        
                    }
                    else
                    {
                        ThrowError("Parameter \"level\" is required.");
                        return;
                    }

                    var upgradePrefab = UpgradePrefab.Find(args[0]);

                    if (upgradePrefab == null)
                    {
                        ThrowError($"Unknown upgrade: {args[0]}.");
                        return;
                    }

                    List<MapEntity> targetItems = new List<MapEntity>();

                    if (upgradePrefab.IsWallUpgrade)
                    {
                        targetItems.AddRange(Submarine.MainSub.GetWalls(true).Cast<MapEntity>());
                    }
                    else
                    {
                        if (args.Length > 2)
                        {
                            targetItems.AddRange(Item.ItemList.Where(item => item.Submarine == Submarine.MainSub).Where(item => item.HasTag(args[2])).Cast<MapEntity>());
                        }
                        else
                        {
                            ThrowError("Argument \"tag\" is required.");
                            return;
                        }
                    }

                    if (!targetItems.Any())
                    {
                        ThrowError("No valid items found.");
                        return;
                    }

                    foreach (MapEntity targetItem in targetItems)
                    {
                        Upgrade existingUpgrade = targetItem.GetUpgrade(args[0]);

                        if (!(targetItem is ISerializableEntity sEntity)) { continue; }

                        var upgrade = new Upgrade(sEntity, upgradePrefab, level);
                        if (targetItem.AddUpgrade(upgrade, true))
                        {
                            if (existingUpgrade == null)
                            {
                                NewMessage($"Added {upgradePrefab.Identifier}:{level} to {sEntity.Name}.", Color.Green);
                                upgrade.ApplyUpgrade(); 
                            }
                            else
                            {
                                NewMessage($"Set {sEntity.Name}'s {upgradePrefab.Identifier} upgrade to level {existingUpgrade.Level}.", Color.Cyan);
                                existingUpgrade.ApplyUpgrade(); 
                            }
                        }
                        else
                        {
                            ThrowError($"{upgrade.Prefab.Identifier} cannot be applied to {sEntity.Name}");
                        }
                    }
                }
                else
                {
                    ThrowError("Parameter \"upgrade\" is required.");
                }
            }, () =>
            {
                return new[]
                {
                    UpgradePrefab.Prefabs.Select(c => c.Identifier).Distinct().ToArray()
                };
            }, true));
            
            commands.Add(new Command("maxupgrades", "maxupgrades [category] [prefab]: Maxes out all upgrades or only specific one if given arguments.", args =>
            {
                UpgradeManager upgradeManager = GameMain.GameSession?.Campaign?.UpgradeManager;
                if (upgradeManager == null)
                {
                    ThrowError("This command can only be used in campaign.");
                    return;
                }
                
                string categoryIdentifier = null;
                string prefabIdentifier = null;

                switch (args.Length)
                {
                    case 1:
                        categoryIdentifier = args[0];
                        break;
                    case 2:
                        categoryIdentifier = args[0];
                        prefabIdentifier = args[1];
                        break;
                }
                
                foreach (UpgradeCategory category in UpgradeCategory.Categories)
                {
                    if (!string.IsNullOrWhiteSpace(categoryIdentifier) && !category.Identifier.Equals(categoryIdentifier, StringComparison.OrdinalIgnoreCase)) { continue; }
                    foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                    {
                        if (!prefab.UpgradeCategories.Contains(category)) { continue; }
                        if (!string.IsNullOrWhiteSpace(prefabIdentifier) && !prefab.Identifier.Equals(prefabIdentifier, StringComparison.OrdinalIgnoreCase)) { continue; }
                        
                        int targetLevel = prefab.MaxLevel - upgradeManager.GetRealUpgradeLevel(prefab, category);
                        for (int i = 0; i < targetLevel; i++)
                        {
                            upgradeManager.PurchaseUpgrade(prefab, category, force: true);
                        }
                        NewMessage($"Upgraded {category.Identifier}.{prefab.Identifier} by {targetLevel} levels.", Color.DarkGreen);
                    }
                }

                NewMessage($"Start a new round to apply the upgrades.", Color.Lime);
            }, () =>
            {
                return new[]
                {
                    UpgradeCategory.Categories.Select(c => c.Identifier).Distinct().ToArray(),
                    UpgradePrefab.Prefabs.Select(c => c.Identifier).Distinct().ToArray()
                };
            }, true));

            commands.Add(new Command("power", "power: Immediately powers up the submarine's nuclear reactor.", (string[] args) =>
            {
                Item reactorItem = Item.ItemList.Find(i => i.GetComponent<Reactor>() != null);
                if (reactorItem == null) { return; }

                var reactor = reactorItem.GetComponent<Reactor>();
                reactor.PowerUpImmediately();
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
            }, isCheat: true));

            commands.Add(new Command("killmonsters", "killmonsters: Immediately kills all AI-controlled enemies in the level.", (string[] args) =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (c.AIController is EnemyAIController enemyAI && enemyAI.PetBehavior == null)
                    {
                        c.SetAllDamage(200.0f, 0.0f, 0.0f);
                    }
                }
                foreach (Hull hull in Hull.hullList)
                {
                    hull.BallastFlora?.Kill();
                }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    sub.WreckAI?.Kill();
                }
            }, null, isCheat: true));

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
                if (!(GameMain.GameSession?.GameMode is CampaignMode campaign))
                {
                    ThrowError("No campaign active!");
                    return;
                }

                campaign.LogState();
            }));

            commands.Add(new Command("campaigndestination|setcampaigndestination", "campaigndestination [index]: Set the location to head towards in the currently active campaign.", (string[] args) =>
            {
                if (!(GameMain.GameSession?.GameMode is CampaignMode campaign))
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

            commands.Add(new Command("togglecampaignteleport", "Toggle on/off teleportation between campaign locations by double clicking on the campaign map.", args =>
            {
                if (GameMain.GameSession?.Campaign == null)
                {
                    ThrowError("No campaign active.");
                    return;
                }
                GameMain.GameSession.Map.AllowDebugTeleport = !GameMain.GameSession.Map.AllowDebugTeleport;
                NewMessage((GameMain.GameSession.Map.AllowDebugTeleport ? "Enabled" : "Disabled") + " teleportation on the campaign map.", Color.White);
            }, isCheat: true));

            commands.Add(new Command("money", "money [amount]: Gives the specified amount of money to the crew when a campaign is active.", args =>
            {
                if (args.Length == 0) { return; }
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    if (int.TryParse(args[0], out int money))
                    {
                        campaign.Money += money;
                    }
                    else
                    {
                        ThrowError($"\"{args[0]}\" is not a valid numeric value.");
                    }
                }
            }, isCheat: true));
            
            commands.Add(new Command("ballastflora", "infectballast [options]: Infect ballasts and control its growth.", args =>
            {
                if (args.Length == 0)
                {
                    ThrowError("No action specified."); 
                    return;
                }

                string primaryAction = args.Length > 0 ? args[0] : "";
                string secondaryArgument = args.Length > 1 ? args[1] : "";

                if (Submarine.MainSub == null)
                {
                    ThrowError("No submarine loaded.");
                    return;
                }

                if (primaryAction.Equals("infect", StringComparison.OrdinalIgnoreCase))
                {
                    List<Pump> pumps = new List<Pump>();
                    foreach (Item item in Submarine.MainSub.GetItems(true))
                    {
                        if (item.CurrentHull != null && item.HasTag("ballast") && item.GetComponent<Pump>() is { } pump)
                        {
                            if (item.CurrentHull.BallastFlora != null) { continue; }
                            pumps.Add(pump);
                        }
                    }
                
                    if (pumps.Any())
                    {
                        BallastFloraPrefab prefab = string.IsNullOrWhiteSpace(secondaryArgument) ? BallastFloraPrefab.Prefabs.First() : BallastFloraPrefab.Find(secondaryArgument);
                        if (prefab == null)
                        {
                            ThrowError($"No such behavior: {secondaryArgument}");
                            return;
                        }

                        Pump random = pumps.GetRandom();
                        random.InfectBallast(prefab.Identifier, allowMultiplePerShip: true);
                        NewMessage($"Infected {random.Name} with {prefab.Identifier} in {random.Item.CurrentHull.DisplayName}.", Color.Green);
                        return;
                    }

                    ThrowError("No available pumps to infect on this submarine.");
                }

                if (primaryAction.Equals("growthwarp", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(secondaryArgument, out int value))
                    {
                        foreach (Hull hull in Hull.hullList.Where(h => h.BallastFlora != null))
                        {
                            BallastFloraBehavior bs = hull.BallastFlora;
                            bs.GrowthWarps = value;
                        }

                        NewMessage("Accelerating growth...", Color.Green);
                        return;
                    }

                    ThrowError($"Invalid integer \"{secondaryArgument}\".");
                }
            }, isCheat: true, getValidArgs: () =>
            {
                string[] primaries = { "infect", "growthwarp" };
                string[] identifiers = BallastFloraPrefab.Prefabs.Select(bfp => bfp.Identifier).Distinct().ToArray();
                return new[] { primaries, identifiers };
            }));

            commands.Add(new Command("setdifficulty|forcedifficulty", "difficulty [0-100]. Leave the parameter empty to disable.", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    Level.ForcedDifficulty = null;
                    NewMessage($"Forced difficulty level disabled.", Color.Green);
                }
                else if (float.TryParse(args[0], out float difficulty))
                {
                    Level.ForcedDifficulty = difficulty;
                    NewMessage($"Set the difficulty level to { Level.ForcedDifficulty }.", Color.Yellow);
                }
            }, isCheat: true));

            commands.Add(new Command("difficulty|leveldifficulty", "difficulty [0-100]: Change the level difficulty setting in the server lobby.", null));
            
            commands.Add(new Command("autoitemplacerdebug|outfitdebug", "autoitemplacerdebug: Toggle automatic item placer debug info on/off. The automatically placed items are listed in the debug console at the start of a round.", (string[] args) =>
            {
                AutoItemPlacer.OutputDebugInfo = !AutoItemPlacer.OutputDebugInfo;
                NewMessage((AutoItemPlacer.OutputDebugInfo ? "Enabled" : "Disabled") + " automatic item placer logging.", Color.White);
            }, isCheat: false));

            commands.Add(new Command("verboselogging", "verboselogging: Toggle verbose console logging on/off. When on, additional debug information is written to the debug console.", (string[] args) =>
            {
                GameSettings.VerboseLogging = !GameSettings.VerboseLogging;
                NewMessage((GameSettings.VerboseLogging ? "Enabled" : "Disabled") + " verbose logging.", Color.White);
            }, isCheat: false));

            commands.Add(new Command("listtasks", "listtasks: Lists all asynchronous tasks currently in the task pool.", (string[] args) => { TaskPool.ListTasks(); }));

            commands.Add(new Command("calculatehashes", "calculatehashes [content package name]: Show the MD5 hashes of the files in the selected content package. If the name parameter is omitted, the first content package is selected.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    string packageName = string.Join(" ", args);
                    var package = GameMain.Config.AllEnabledPackages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
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
                    GameMain.Config.AllEnabledPackages.First().CalculateHash(logging: true);
                }
            },
            () =>
            {
                return new string[][]
                {
                    GameMain.Config.AllEnabledPackages.Select(cp => cp.Name).ToArray()
                };
            }));

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
                    GameMain.Client.SimulatedMinimumLatency = minimumLatency;
                    GameMain.Client.SimulatedRandomLatency = randomLatency;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.SimulatedMinimumLatency = minimumLatency;
                    GameMain.Server.SimulatedRandomLatency = randomLatency;
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
                    GameMain.Client.SimulatedLoss = loss;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.SimulatedLoss = loss;
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
                    GameMain.Client.SimulatedDuplicatesChance = duplicates;
                }
#elif SERVER
                if (GameMain.Server != null)
                {
                    GameMain.Server.SimulatedDuplicatesChance = duplicates;
                }
#endif
                NewMessage("Set packet duplication to " + (int)(duplicates * 100) + "%.", Color.White);
            }));

#if DEBUG
            commands.Add(new Command("storeinfo", "", (string[] args) =>
            {
                if (GameMain.GameSession?.Map?.CurrentLocation is Location location)
                {

                    var msg = "--- Location: " + location.Name + " ---";
                    msg += "\nBalance: " + location.StoreCurrentBalance;
                    msg += "\nPrice modifier: " + location.StorePriceModifier + "%";
                    msg +=  "\nDaily specials:";
                    location.DailySpecials.ForEach(i => msg += "\n   - " + i.Name);
                    msg += "\nRequested goods:";
                    location.RequestedGoods.ForEach(i => msg += "\n   - " + i.Name);
                    NewMessage(msg);
                }
                else
                {
                    NewMessage("No current location set, can't show store info.");
                }
            }));
#endif

            //"dummy commands" that only exist so that the server can give clients permissions to use them
            //TODO: alphabetical order?
            commands.Add(new Command("control", "control [character name]: Start controlling the specified character (client-only).", null, () =>
            {
                return new string[][] { ListCharacterNames() };
            }, isCheat: true));
            commands.Add(new Command("los", "Toggle the line of sight effect on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("lighting|lights", "Toggle lighting on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("ambientlight", "ambientlight [color]: Change the color of the ambient light in the level.", null, isCheat: true));
            commands.Add(new Command("debugdraw", "Toggle the debug drawing mode on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("togglevoicechatfilters", "Toggle the radio/muffle filters in the voice chat (client-only).", null, isCheat: false));
            commands.Add(new Command("togglehud|hud", "Toggle the character HUD (inventories, icons, buttons, etc) on/off (client-only).", null));
            commands.Add(new Command("toggleupperhud", "Toggle the upper part of the ingame HUD (chatbox, crewmanager) on/off (client-only).", null));
            commands.Add(new Command("toggleitemhighlights", "Toggle the item highlight effect on/off (client-only).", null));
            commands.Add(new Command("togglecharacternames", "Toggle the names hovering above characters on/off (client-only).", null));
            commands.Add(new Command("followsub", "Toggle whether the camera should follow the nearest submarine (client-only).", null));
            commands.Add(new Command("toggleaitargets|aitargets", "Toggle the visibility of AI targets (= targets that enemies can detect and attack/escape from) (client-only).", null, isCheat: true));
            commands.Add(new Command("debugai", "Toggle the ai debug mode on/off (works properly only in single player).", null, isCheat: true));

            InitProjectSpecific();

            commands.Sort((c1, c2) => c1.names[0].CompareTo(c2.names[0]));
        }

        public static string AutoComplete(string command, int increment = 1)
        {
            string[] splitCommand = ToolBox.SplitCommand(command);
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

        public static void ResetAutoComplete()
        {
            currentAutoCompletedCommand = "";
            currentAutoCompletedIndex = 0;
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

            if (string.IsNullOrWhiteSpace(command) || command == "\\" || command == "\n") { return; }

            string[] splitCommand = ToolBox.SplitCommand(command);
            if (splitCommand.Length == 0)
            {
                ThrowError("Failed to execute command \"" + command + "\"!");
                GameAnalyticsManager.AddErrorEventOnce(
                    "DebugConsole.ExecuteCommand:LengthZero",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Failed to execute command \"" + command + "\"!");
                return;
            }

            string firstCommand = splitCommand[0].ToLowerInvariant();

            if (!firstCommand.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                NewMessage(command, Color.White, true);
            }

#if CLIENT
            if (GameMain.Client != null)
            {
                Command matchingCommand = commands.Find(c => c.names.Contains(firstCommand));
                if (matchingCommand == null)
                {
                    //if the command is not defined client-side, we'll relay it anyway because it may be a custom command at the server's side
                    GameMain.Client.SendConsoleCommand(command);
                    NewMessage("Server command: " + command, Color.Cyan);
                    return;
                }
                else if (GameMain.Client.HasConsoleCommandPermission(firstCommand))
                {
                    if (matchingCommand.RelayToServer)
                    {
                        GameMain.Client.SendConsoleCommand(command);
                        NewMessage("Server command: " + command, Color.Cyan);
                    }
                    else
                    {
                        matchingCommand.ClientExecute(splitCommand.Skip(1).ToArray());
                    }
                    return;
                }
                if (!IsCommandPermitted(splitCommand[0].ToLowerInvariant(), GameMain.Client))
                {
#if DEBUG
                    AddWarning("You're not permitted to use the command \"{matchingCommand.Name}\". Executing the command anyway because this is a debug build.");
#else
                    ThrowError("You're not permitted to use the command \"" + splitCommand[0].ToLowerInvariant() + "\"!");
                    return;
#endif
                }
            }
#endif

            bool commandFound = false;
            foreach (Command c in commands)
            {
                if (!c.names.Contains(firstCommand)) { continue; }                
                c.Execute(splitCommand.Skip(1).ToArray());
                commandFound = true;
                break;                
            }

            if (!commandFound)
            {
                ThrowError("Command \"" + splitCommand[0] + "\" not found.");
            }
        }

        private static string[] ListCharacterNames() => Character.CharacterList.OrderBy(c => c.IsDead).ThenByDescending(c => c.IsHuman).Select(c => c.Name).Distinct().ToArray();

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
                c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase) &&
                (!c.IsRemotePlayer || !ignoreRemotePlayers || allowedRemotePlayer?.Character == c));

            if (!matchingCharacters.Any())
            {
                NewMessage("Character \""+ characterName + "\" not found", Color.Red);
                return null;
            }

            // Use same sorting as DebugConsole.ListCharacterNames() above
            matchingCharacters = matchingCharacters.OrderBy(c => c.IsDead).ThenByDescending(c => c.IsHuman).ToList();
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
            if (args.Length == 0) { return; }

            Character spawnedCharacter = null;

            Vector2 spawnPosition = Vector2.Zero;
            WayPoint spawnPoint = null;

            string characterLowerCase = args[0].ToLowerInvariant();
            JobPrefab job = null;
            if (!JobPrefab.Prefabs.ContainsKey(characterLowerCase))
            {
                job = JobPrefab.Prefabs.Find(jp => jp.Name != null && jp.Name.Equals(characterLowerCase, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                job = JobPrefab.Prefabs[characterLowerCase];
            }
            bool human = job != null || characterLowerCase == CharacterPrefab.HumanSpeciesName;
            
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

            if (string.IsNullOrWhiteSpace(args[0])) { return; }
            CharacterTeamType teamType = Character.Controlled != null ? Character.Controlled.TeamID : CharacterTeamType.Team1;
            if (args.Length > 2)
            {
                try
                {
                    teamType = (CharacterTeamType)int.Parse(args[2]);
                }
                catch
                {
                    DebugConsole.ThrowError($"\"{args[2]}\" is not a valid team id.");
                }
            }

            if (spawnPoint != null) { spawnPosition = spawnPoint.WorldPosition; }

            if (human)
            {
                var variant = job != null ? Rand.Range(0, job.Variants, Rand.RandSync.Server) : 0;
                CharacterInfo characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: job, variant: variant);
                spawnedCharacter = Character.Create(characterInfo, spawnPosition, ToolBox.RandomSeed(8));
                if (GameMain.GameSession != null)
                {
                    spawnedCharacter.TeamID = teamType;
#if CLIENT
                    GameMain.GameSession.CrewManager.AddCharacter(spawnedCharacter);          
#endif
                }
                spawnedCharacter.GiveJobItems(spawnPoint);
                spawnedCharacter.Info.StartItemsGiven = true;
            }
            else
            {
                if (CharacterPrefab.FindBySpeciesName(args[0]) != null)
                {
                    Character.Create(args[0], spawnPosition, ToolBox.RandomSeed(8));
                }
            }
        }

        private static void SpawnItem(string[] args, Vector2 cursorPos, Character controlledCharacter, out string errorMsg)
        {
            errorMsg = "";
            if (args.Length < 1) return;

            Vector2? spawnPos = null;
            Inventory spawnInventory = null;

            string itemNameOrId = args[0].ToLowerInvariant();
            ItemPrefab itemPrefab =
                (MapEntityPrefab.Find(itemNameOrId, identifier: null, showErrorMessages: false) ??
                MapEntityPrefab.Find(null, identifier: itemNameOrId, showErrorMessages: false)) as ItemPrefab;
            if (itemPrefab == null)
            {
                errorMsg = "Item \"" + itemNameOrId + "\" not found!";
                var matching = ItemPrefab.Prefabs.Find(me => me.Name.ToLowerInvariant().StartsWith(itemNameOrId) && me is ItemPrefab);
                if (matching != null)
                {
                    errorMsg += $" Did you mean \"{matching.Name}\"?";
                    if (matching.Name.Contains(" "))
                    {
                        errorMsg += $" Please note that you should surround multi-word names with quotation marks (e.q. spawnitem \"{matching.Name}\")";
                    }
                }
                return;
            }

            int amount = 1;
            if (args.Length > 1)
            {
                string spawnLocation = args.Last();
                if (args.Length > 2)
                {
                    spawnLocation = args[^2];
                    if (!int.TryParse(args[^1], NumberStyles.Any, CultureInfo.InvariantCulture, out amount)) { amount = 1; }
                    amount = Math.Min(amount, 100);
                }
                
                switch (spawnLocation)
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
            
            if ((spawnPos == null || spawnPos == Vector2.Zero) && spawnInventory == null)
            {
                var wp = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
            }

            for (int i = 0; i < amount; i++)
            {
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
                        onItemSpawned(spawnedItem);
                    }
                    else
                    {
                        Entity.Spawner?.AddToSpawnQueue(itemPrefab, spawnInventory, onSpawned: onItemSpawned);
                    }

                    static void onItemSpawned(Item item)
                    {
                        if (item.ParentInventory?.Owner is Character character)
                        {
                            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                            {
                                wifiComponent.TeamID = character.TeamID;
                            }
                        }
                    }
                }
            }
        }

        public static void NewMessage(string msg, bool isCommand = false)
        {
#if DEBUG
            Console.WriteLine(msg);
#endif
            NewMessage(msg, Color.White, isCommand);
        }

        public static void NewMessage(string msg, Color color, bool isCommand = false, bool isError = false)
        {
            if (string.IsNullOrEmpty(msg)) { return; }
            
            lock (queuedMessages)
            {
                queuedMessages.Enqueue(new ColoredText(msg, color, isCommand, isError));
            }
        }

        public static void ShowQuestionPrompt(string question, QuestionCallback onAnswered, string[] args = null, int argCount = -1)
        {
            if (args != null && args.Length > argCount)
            {
                onAnswered(args[argCount]);
                return;
            }

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
                    if (!int.TryParse(currNum, out int parsedNum) || parsedNum < 0)
                    {
                        return false;
                    }
                    try
                    {
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
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        ThrowError($"{parsedNum} {c} exceeds the maximum supported time span. Using the maximum time span {TimeSpan.MaxValue} instead.");
                        timeSpan = TimeSpan.MaxValue;
                        return true;
                    }
                    currNum = "";
                }
            }

            return true;
        }

        public static Command FindCommand(string commandName) => commands.Find(c => c.names.Any(n => n.Equals(commandName, StringComparison.OrdinalIgnoreCase)));

        public static void Log(string message)
        {
            if (GameSettings.VerboseLogging) NewMessage(message, Color.Gray);
        }

        public static void ThrowError(string error, Exception e = null, bool createMessageBox = false, bool appendStackTrace = false)
        {
            if (e != null)
            {
                error += " {" + e.Message + "}\n";
                if (e.StackTrace != null)
                {
                    error += e.StackTrace.CleanupStackTrace(); 
                }
                if (e.InnerException != null)
                {
                    error += "\n\nInner exception: " + e.InnerException.Message + "\n";
                    if (e.InnerException.StackTrace != null)
                    {
                        error += e.InnerException.StackTrace.CleanupStackTrace(); ;
                    }
                }
            }
            else if (appendStackTrace && Environment.StackTrace != null)
            {
                error += "\n" + Environment.StackTrace.CleanupStackTrace();
            }
            System.Diagnostics.Debug.WriteLine(error);

#if CLIENT
            if (createMessageBox)
            {
                CoroutineManager.StartCoroutine(CreateMessageBox(error));
            }
            else
            {
                isOpen = true;
            }
#endif

            NewMessage(error, Color.Red, isError: true);
        }
        
        public static void AddWarning(string warning)
        {
            System.Diagnostics.Debug.WriteLine(warning);
            NewMessage($"WARNING: {warning}", Color.Yellow);
        }

#if CLIENT
        private static IEnumerable<object> CreateMessageBox(string errorMsg)
        {
            while (GUI.Style == null)
            {
                yield return null;
            }

            new GUIMessageBox(TextManager.Get("Error"), errorMsg);
            yield return CoroutineStatus.Success;
        }
#endif

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

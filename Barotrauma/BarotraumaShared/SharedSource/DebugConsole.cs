using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Barotrauma.IO;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.MapCreatures.Behavior;
using System.Text;


namespace Barotrauma
{
    readonly struct ColoredText
    {
        public readonly string Text;
        public readonly Color Color;
        public readonly bool IsCommand;
        public readonly bool IsError;

        public readonly string Time;

        public ColoredText(string text, Color color, bool isCommand, bool isError)
        {
            this.Text = text;
            this.Color = color;
			this.IsCommand = isCommand;
            this.IsError = isError;

            Time = DateTime.Now.ToString(CultureInfo.InvariantCulture);
        }
    }

    static partial class DebugConsole
    {
        public partial class Command
        {
            public readonly ImmutableArray<Identifier> Names;
            public readonly string Help;
            
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
                Names = name.Split('|').ToIdentifiers().ToImmutableArray();
                this.Help = help;

                this.OnExecute = onExecute;
                
                this.GetValidArgs = getValidArgs;
                this.IsCheat = isCheat;
            }

            public void Execute(string[] args)
            {
                if (OnExecute == null) { return; }

                bool allowCheats = false;
#if CLIENT
                allowCheats = GameMain.NetworkMember == null && (GameMain.GameSession?.GameMode is TestGameMode || Screen.Selected is { IsEditor: true });
#endif
                if (!allowCheats && !CheatsEnabled && IsCheat)
                {
                    NewMessage(
                        $"You need to enable cheats using the command \"enablecheats\" before you can use the command \"{Names.First()}\".", Color.Red);
                    NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                    return;
                }

                OnExecute(args);
            }

            public override int GetHashCode()
            {
                return Names.First().GetHashCode();
            }
        }

        private static readonly ConcurrentQueue<ColoredText> queuedMessages
            = new ConcurrentQueue<ColoredText>();

        public static readonly NamedEvent<ColoredText> MessageHandler = new NamedEvent<ColoredText>();

        public struct ErrorCatcher : IDisposable
        {
            private readonly List<ColoredText> errors;
            private readonly bool wasConsoleOpen;
            private Identifier handlerId;
            public IReadOnlyList<ColoredText> Errors => errors;

            private ErrorCatcher(Identifier handlerId)
            {
                this.handlerId = handlerId;
#if CLIENT
                this.wasConsoleOpen = IsOpen;
#else
                this.wasConsoleOpen = false;
#endif
                this.errors = new List<ColoredText>();

                //create a local variable that can be captured by lambdas
                var errs = this.errors;
                
                MessageHandler.Register(handlerId, msg =>
                {
                    if (!msg.IsError) { return; }
                    errs.Add(msg);
                });
            }
            
            public static ErrorCatcher Create()
                => new ErrorCatcher(ToolBox.RandomSeed(25).ToIdentifier());

            public void Dispose()
            {
                if (handlerId.IsEmpty) { return; }
                MessageHandler.Deregister(handlerId);
                handlerId = Identifier.Empty;
#if CLIENT
                DebugConsole.IsOpen = wasConsoleOpen;
#endif
            }
        }
        
        static partial void ShowHelpMessage(Command command);
        
        const int MaxMessages = 300;

        public static readonly List<ColoredText> Messages = new List<ColoredText>();

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
        private static readonly int messagesPerFile = 800;
        public const string SavePath = "ConsoleLogs";

        private static WeakReference<Character> previousControlledCharacter;  // For SP freecam

        private static void AssignOnExecute(string names, Action<string[]> onExecute)
        {
            var matchingCommand = commands.Find(c => c.Names.Intersect(names.Split('|').ToIdentifiers()).Any());
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
                        if (string.IsNullOrEmpty(c.Help)) continue;
                        ShowHelpMessage(c);
                    }
                }
                else
                {
                    var matchingCommand = commands.Find(c => c.Names.Any(name => name == args[0]));
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
                    commands.SelectMany(c => c.Names).Select(n => n.Value).ToArray(),
                    Array.Empty<string>()
                };
            }));

            void printMapEntityPrefabs<T>(IEnumerable<T> prefabs) where T : MapEntityPrefab
            {
                NewMessage("***************", Color.Cyan);
                foreach (T prefab in prefabs)
                {
                    if (prefab.Name.IsNullOrEmpty()) { continue; }
                    string text = $"- {prefab.Name}";
                    if (prefab.Tags.Any())
                    {
                        text += $" ({string.Join(", ", prefab.Tags)})";
                    }
                    if (prefab.AllowedLinks?.Any() ?? false)
                    {
                        text += $", Links: {string.Join(", ", prefab.AllowedLinks)}";
                    }
                    NewMessage(text, prefab.ContentPackage == ContentPackageManager.VanillaCorePackage ? Color.Cyan : Color.Purple);
                }
                NewMessage("***************", Color.Cyan);
            }

            commands.Add(new Command("items|itemlist", "itemlist: List all the item prefabs available for spawning.", (string[] args) =>
            {
                printMapEntityPrefabs(ItemPrefab.Prefabs);
            }));
            
            commands.Add(new Command("itemassemblies", "itemassemblies: List all the item assemblies available for spawning.", (string[] args) =>
            {
                printMapEntityPrefabs(ItemAssemblyPrefab.Prefabs);
            }));


            commands.Add(new Command("netstats", "netstats: Toggles the visibility of the network statistics UI.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null) return;
                GameMain.NetworkMember.ShowNetStats = !GameMain.NetworkMember.ShowNetStats;
            }));

            commands.Add(new Command("spawn|spawncharacter", "spawn [creaturename/jobname] [near/inside/outside/cursor] [team] [add to crew (true/false)]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine). You can also enter the name of a job (e.g. \"Mechanic\") to spawn a character with a specific job and the appropriate equipment.", null,
            () =>
            {
                string[] creatureAndJobNames =
                    CharacterPrefab.Prefabs.Select(p => p.Identifier.Value)
                    .Concat(JobPrefab.Prefabs.Select(p => p.Identifier.Value))
                    .OrderBy(s => s)
                    .ToArray();

                return new string[][]
                {
                    creatureAndJobNames.ToArray(),
                    new string[] { "near", "inside", "outside", "cursor" },
                    Enum.GetValues<CharacterTeamType>().Select(v => v.ToString()).ToArray(),
                    new string[] { "true", "false" },

                };
            }, isCheat: true));
            
            commands.Add(new Command("give|giveitem", "give|giveitem [itemname/itemidentifier] [amount] [condition]: Spawn an item in the inventory of the controlled character",
            (string[] args) =>
            {
                if (Character.Controlled == null)
                {
                    ThrowError("No character is selected!");
                    return;
                }

                if (args.Length == 0)
                {
                    ThrowError("Please give the name or identifier of the item to spawn.");
                    return;
                }
                
                var modifiedArgs = new List<string>(args);
                modifiedArgs.Insert(1, "inventory");
                TrySpawnItem(modifiedArgs.ToArray());
            },
            getValidArgs: () =>
            {
                return new string[][]
                {
                    GetItemNameOrIdParams().ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("spawnnpc", "spawnnpc [any/npcsetidentifier] [npcidentifier] [near/inside/outside/cursor] [team (0-3)] [add to crew (true/false)]: Spawns an pre-configured NPC at a random spawnpoint. (Use the third parameter to select a specific set of spawnpoints.)", onExecute: null,
            getValidArgs: () =>
            {
                return new string[][]
                {
                    "any".ToEnumerable().Union(NPCSet.Sets.Select(p => p.Identifier.Value).OrderBy(s => s)).ToArray(), // NPC Sets
                    NPCSet.Sets.SelectMany(set => set.Humans).Select(p => p.Identifier.Value).OrderBy(s => s).ToArray(), // NPCs
                    new string[] { "near", "inside", "outside", "cursor" },
                    Enum.GetValues<CharacterTeamType>().Select(v => v.ToString()).ToArray(),
                    new string[] { "true", "false" }
                };
            }, isCheat: true));

            commands.Add(new Command("spawnitem", "spawnitem [itemname/itemidentifier] [cursor/inventory/cargo/random/[name]] [amount] [condition]: Spawn an item at the position of the cursor, in the inventory of the controlled character, in the inventory of the client with the given name, or at a random spawnpoint if the location parameter is omitted or \"random\".",
            (string[] args) =>
            {
                TrySpawnItem(args);
            },
            () =>
            {
                return new string[][]
                {
                    GetItemNameOrIdParams().ToArray(),
                    GetSpawnPosParams().ToArray()
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

            commands.Add(new Command("triggertraitorevent|starttraitoreventimmediately", "triggertraitorevent [eventidentifier]: Skip the initial delay of the traitor events and start one immediately. You can optionally specify which event to start (otherwise a random event is chosen).", null, 
                () =>
            {
                return new string[][]
                {
                    EventPrefab.Prefabs.Where(p => p is TraitorEventPrefab).Select(p => p.Identifier.ToString()).ToArray()
                };
            }));

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
                        PermissionPreset.List.Select(pp => pp.DisplayName.Value).ToArray()
                    };
                }));

            commands.Add(new Command("givecommandperm", "givecommandperm [id/steamid/endpoint/name]: Gives the specified client the permission to use the specified console commands.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        commands.Select(c => c.Names.First().Value).Union(new []{ "All" }).ToArray()
                    };
                }));

            commands.Add(new Command("revokecommandperm", "revokecommandperm [id/steamid/endpoint/name]: Revokes permission to use the specified console commands from the specified client.", null,
                () =>
                {
                    if (GameMain.NetworkMember == null) return null;

                    return new string[][]
                    {
                        GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                        commands.Select(c => c.Names.First().Value).Union(new []{ "All" }).ToArray()
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
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.SessionId == id);
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

                        GameMain.NetworkMember.BanPlayer(clientName, reason, banDuration);
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
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.SessionId == id);
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

                        GameMain.NetworkMember.BanPlayer(client.Name, reason, banDuration);
                    });
                });
            }));
            
            commands.Add(new Command("banaddress|banip", "banaddress [endpoint]: Ban the IP address/SteamID from the server.", null));
            
            commands.Add(new Command("teleportcharacter|teleport", "teleport [character name] [location]: Teleport the specified character to a location , or the position of the cursor if location is omitted. If the name parameter is omitted, the controlled character will be teleported.", 
            onExecute: null,
            getValidArgs:() =>
            {
                return new string[][]
                {
                    ListCharacterNames(includeMeArgument: Character.Controlled != null, includeCrewArgument: true),
                    ListAvailableLocations()
                };
            }, isCheat: true));
            
            commands.Add(new Command("monstersignoreplayer", "Toggle if monsters should ignore the player character (and their equipment) when targeting.",
            onExecute: (string[] args) =>
            {
                ToggleEnemyAITargetingRestrictions(EnemyTargetingRestrictions.PlayerCharacters);
            },
            getValidArgs: null,
            isCheat: true));
            
            commands.Add(new Command("monstersignoresub", "Toggle if monsters should ignore the player submarines when targeting.",
            onExecute: (string[] args) =>
            {
                ToggleEnemyAITargetingRestrictions(EnemyTargetingRestrictions.PlayerSubmarines);
            },
            getValidArgs: null,
            isCheat: true));
            
            commands.Add(new Command("monstersrestoretargets", "Remove any targeting restrictions from monsters.",
            onExecute: (string[] args) =>
            {
                ToggleEnemyAITargetingRestrictions(EnemyTargetingRestrictions.None);
            },
            getValidArgs: null,
            isCheat: true));
            
            commands.Add(new Command("monstertargetingrestrictions", "monstertargetingrestrictions [restrictions]: Set targeting restrictions for all monsters. Supports multiple options comma-separated: 'monsterargetingrestrictions PlayerCharacters,PlayerSubmarines'. Use 'None' to remove all restrictions.",
            onExecute:(string[] args) =>
            {
                if (args.Length == 0)
                {
                    // use the set function to keep log consistent
                    ToggleEnemyAITargetingRestrictions(EnemyAIController.TargetingRestrictions);
                    return;
                }
                
                // try parse the complete flags from first arg
                if (Enum.TryParse<EnemyTargetingRestrictions>(args[0], ignoreCase: true, out var restrictions))
                {
                    ToggleEnemyAITargetingRestrictions(restrictions);
                }
                else
                {
                    NewMessage($"Failed to parse argument '{args[0]}'", Color.Red);
                }
            },
            getValidArgs: () =>
            {
                return new string[][]
                {
                    Enum.GetNames(typeof(EnemyTargetingRestrictions))
                };
            },
            isCheat: true));

            commands.Add(new Command("listlocations|locations", "listlocations: List all the locations in the level: subs, outposts, ruins, caves.", 
            onExecute:(string[] args) =>
            {
                var availableLocations = ListAvailableLocations();
                NewMessage("***************", Color.Cyan);
                foreach (var location in availableLocations)
                {
                    NewMessage(location, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));

            commands.Add(new Command("godmode", "godmode [character name] [remove afflictions (true/false)]: Toggle character godmode. Makes the targeted character invulnerable to damage. If the name parameter is omitted, the controlled character will receive godmode.",
            (string[] args) =>
            {
                bool? godmodeStateOnFirstCharacter = null;
                HandleCommandForCrewOrSingleCharacter(args, ToggleGodMode);
                void ToggleGodMode(Character targetCharacter)
                {
                    if (args.Length > 1 && bool.TryParse(args[1], out bool removeafflictions))
                    {
                        if (removeafflictions) { targetCharacter.CharacterHealth.RemoveAllAfflictions(); }
                    }
                    targetCharacter.GodMode = godmodeStateOnFirstCharacter ?? !targetCharacter.GodMode;
                    godmodeStateOnFirstCharacter = targetCharacter.GodMode;
                    NewMessage((targetCharacter.GodMode ? "Enabled godmode on " : "Disabled godmode on ") + targetCharacter.Name,
                       targetCharacter.GodMode ? Color.LimeGreen : Color.Gray);
                }
            },
            () =>
            {
                return new string[][] { ListCharacterNames(includeMeArgument: Character.Controlled != null, includeCrewArgument: true) };
            }, isCheat: true));

            commands.Add(new Command("godmode_mainsub", "godmode_mainsub: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) { return; }

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
                foreach (MapEntity mapEntity in MapEntity.MapEntityList)
                {
                    if (mapEntity.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase))
                    {
                        ThrowError(mapEntity.ID + ": " + mapEntity.Name.ToString());
                    }
                }
                foreach (Character character in Character.CharacterList)
                {
                    if (character.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase) || character.SpeciesName == args[0])
                    {
                        ThrowError(character.ID + ": " + character.Name.ToString());
                    }
                }
            }));

            commands.Add(new Command("giveaffliction", "giveaffliction [affliction name] [affliction strength] [character name] [limb type] [use relative strength]: Add an affliction to a character. If the name parameter is omitted, the affliction is added to the controlled character.", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    if (args.Length == 1)
                    {
                        ThrowError("Must give a strength value!"); 
                    }
                    return;
                }
                string affliction = args[0];
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(a => a.Identifier == affliction);
                if (afflictionPrefab == null)
                {
                    afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(a => a.Name.Equals(affliction, StringComparison.OrdinalIgnoreCase));
                }
                if (afflictionPrefab == null)
                {
                    ThrowError("Affliction \"" + affliction + "\" not found.");
                    return;
                }
                if (!float.TryParse(args[1], out float afflictionStrength))
                {
                    ThrowError("\"" + args[1] + "\" is not a valid affliction strength.");
                    return;
                }
                bool relativeStrength = false;
                if (args.Length > 4)
                {
                    bool.TryParse(args[4], out relativeStrength);
                }
                Character targetCharacter = args.Length <= 2 ? Character.Controlled : FindMatchingCharacter(args.Skip(2).ToArray());
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
                    AfflictionPrefab.Prefabs.Select(a => a.Name.Value).ToArray().Concat(AfflictionPrefab.Prefabs.Select(a => a.Identifier.Value)).ToArray(),
                    new string[] { "1" },
                    ListCharacterNames(),
                    Enum.GetNames(typeof(LimbType)).ToArray()
                };
            }, isCheat: true));
            
            commands.Add(new Command("healme", "healme [all]: Restore controlled character to full health. By default only heals common afflictions such as physical damage and blood loss: use the \"all\" argument to heal everything, including poisonings/addictions/etc.", (string[] args) =>
                {
                    bool healAll = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
                    if (Character.Controlled != null)
                    {
                        HealCharacter(Character.Controlled, healAll);
                    }
                },
                () =>
                {
                    return new string[][]
                    {
                        new string[] { "all" }
                    };
                }, isCheat: true));

            commands.Add(new Command("heal", "heal [character name] [all]: Restore the specified character to full health. If the name parameter is omitted, the controlled character will be healed. By default only heals common afflictions such as physical damage and blood loss: use the \"all\" argument to heal everything, including poisonings/addictions/etc.", (string[] args) =>
            {
                bool healAll = args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
                HandleCommandForCrewOrSingleCharacter(args, (Character targetCharacter) => HealCharacter(targetCharacter, healAll));
            },
            () =>
            {
                return new string[][]
                {
                    ListCharacterNames(includeMeArgument: true, includeCrewArgument: true),
                    new string[] { "all" }
                };
            }, isCheat: true));


            commands.Add(new Command("listsuitabletreatments", "listsuitabletreatments [character name]: List which items are the most suitable for treating the specified character. Useful for debugging medic AI.", (string[] args) =>
            {
                Character character = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                if (character != null)
                {
                    Dictionary<Identifier, float> treatments = new Dictionary<Identifier, float>();
                        character.CharacterHealth.GetSuitableTreatments(treatments, user: null,
                        checkTreatmentThreshold: true,
                        checkTreatmentSuggestionThreshold: false);
                    foreach (var treatment in treatments.OrderByDescending(t => t.Value))
                    {
                        Color color = Color.White;
#if CLIENT
                        color = ToolBox.GradientLerp(
                            MathUtils.InverseLerp(-1000, 1000, treatment.Value),
                            Color.Red, Color.Yellow, Color.White, Color.LightGreen);
#endif
                        NewMessage((int)treatment.Value + ": " + treatment.Key, color);

                    }
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("revive", "revive [character name]: Bring the specified character back from the dead. If the name parameter is omitted, the controlled character will be revived.", (string[] args) =>
            {
                Character revivedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                if (revivedCharacter == null) { return; }

                revivedCharacter.Revive();
#if SERVER
                if (GameMain.Server != null)
                {
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        if (c.Character != revivedCharacter) { continue; }

                        // If killed in ironman mode, the character has been wiped from the save mid-round, so its
                        // original data needs to be restored to the save file (without making a backup of the dead character)
                        if (GameMain.Server?.ServerSettings is { IronmanModeActive: true } && GameMain.GameSession?.Campaign is MultiPlayerCampaign mpCampaign)
                        {
                            if (mpCampaign.RestoreSingleCharacterFromBackup(c) is CharacterCampaignData characterToRestore)
                            {
                                characterToRestore.CharacterInfo.PermanentlyDead = false;
                                mpCampaign.SaveSingleCharacter(characterToRestore, skipBackup: true);
                            }
                        }

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
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
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
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("freecamera|freecam", "freecam: Detach the camera from the controlled character.", (string[] args) =>
            {
#if CLIENT
                if (Screen.Selected == GameMain.SubEditorScreen) { return; }

                if (GameMain.Client == null)
                {
                    if (Character.Controlled == null)
                    {
                        // Exiting freecam - try to return to previous character
                        Character prevCharacter = null;
                        if (previousControlledCharacter != null && previousControlledCharacter.TryGetTarget(out prevCharacter) && 
                            prevCharacter != null && !prevCharacter.IsDead && !prevCharacter.Removed)
                        {
                            Character.Controlled = prevCharacter;
                            NewMessage("Exiting freecam mode", Color.Yellow);
                        }
                        else
                        {
                            NewMessage("Could not regain control of the previous character (dead or removed).", Color.Red);
                        }
                    }
                    else
                    {
                        // Entering freecam - store current character ID
                        previousControlledCharacter = new WeakReference<Character>(Character.Controlled);
                        Character.Controlled = null;
                        GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                        NewMessage("Entering freecam mode", Color.Yellow);
                    }
                }
                else
                {
                    GameMain.Client?.SendConsoleCommand("freecam");
                }
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
            
            commands.Add(new Command("triggerevent", "triggerevent [identifier]: Trigger an event based on identifier.", (string[] args) =>
            {
                List<EventPrefab> allEventPrefabsWithId = EventSet.GetAllEventPrefabs().Where(prefab => prefab.Identifier != Identifier.Empty).ToList();
                if (GameMain.GameSession?.EventManager != null && args.Length > 0)
                {
                    string eventPrefabId = args[0];
                    if (eventPrefabId == "all")
                    {
                        foreach (var eventPrefab in allEventPrefabsWithId.Where(e => e.EventType == typeof(ScriptedEvent)))
                        {
                            var newEvent = eventPrefab.CreateInstance(GameMain.GameSession.EventManager.RandomSeed);
                            if (newEvent == null)
                            {
                                NewMessage($"Could not initialize event {eventPrefabId} because level did not meet requirements");
                                return;
                            }
                            GameMain.GameSession.EventManager.ActivateEvent(newEvent);
                        }
                    }
                    else
                    {
                        EventPrefab eventPrefab = allEventPrefabsWithId.Find(prefab => prefab.Identifier == eventPrefabId);
                        if (eventPrefab is TraitorEventPrefab)
                        {
                            ThrowError($"{eventPrefab.Identifier} is a traitor event. You need to use the 'triggertraitorevent' command to start it.");
                            return;
                        }
                        else if (eventPrefab != null)
                        {
                            var newEvent = eventPrefab.CreateInstance(GameMain.GameSession.EventManager.RandomSeed);
                            if (newEvent == null)
                            {
                                NewMessage($"Could not initialize event {eventPrefabId} because level did not meet requirements");
                                return;
                            }
                            GameMain.GameSession.EventManager.ActivateEvent(newEvent);
                            NewMessage($"Initialized event {eventPrefab.Identifier}", Color.Aqua);
                            return;
                        }
                        else
                        {
                        NewMessage($"Failed to trigger event because {eventPrefabId} is not a valid event identifier.", Color.Red);
                        return;
                        }
                    }
                }
                NewMessage("Failed to trigger event", Color.Red);
            }, isCheat: true, getValidArgs: () =>
            {
                List<EventPrefab> eventPrefabs = EventSet.GetAllEventPrefabs().Where(prefab => prefab.Identifier != Identifier.Empty).ToList();
                
                return new[]
                {
                   eventPrefabs.Select(prefab => prefab.Identifier).Distinct().Select(id => id.Value).ToArray()
                };
            }));            
            
            commands.Add(new Command("debugevent", "debugevent [identifier]: outputs debug info about a specific event that's currently active. Mainly intended for debugging events in multiplayer: in single player, the same information is available by enabling debugdraw.", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    ThrowError($"Please specify the identifier of the event you want to debug.");
                    return;
                }

                if (GameMain.GameSession?.EventManager is EventManager eventManager)
                {
                    var ev = eventManager.ActiveEvents.FirstOrDefault(ev => ev.Prefab?.Identifier == args[0]);
                    if (ev == null)
                    {
                        ThrowError($"Event \"{args[0]}\" not found.");
                    }
                    else
                    {
                        string info = ev.GetDebugInfo();
#if SERVER
                        //strip rich text tags
                        RichTextData.GetRichTextData(info, out info);
#endif
                        NewMessage(info);
                    }
                }
            }, isCheat: true, getValidArgs: () =>
            {
                IEnumerable<EventPrefab> eventPrefabs;
                if (GameMain.GameSession?.EventManager == null || GameMain.GameSession.EventManager.ActiveEvents.None())
                {
                    eventPrefabs = EventSet.GetAllEventPrefabs().Where(prefab => prefab.Identifier != Identifier.Empty);
                }
                else
                {
                    eventPrefabs = GameMain.GameSession.EventManager.ActiveEvents.Select(e => e.Prefab);
                }
                return new[]
                {
                    eventPrefabs.Select(ev => ev.Identifier.ToString()).ToArray() ?? Array.Empty<string>()
                };
            }));

            commands.Add(new Command("unlockmission", "unlockmission [identifier/tag]: Unlocks a mission in a random adjacent level.", (string[] args) =>
            {
                if (GameMain.GameSession?.GameMode is not CampaignMode campaign)
                {
                    ThrowError("The unlockmission command is only usable in the campaign mode.");
                    return;
                }
                if (args.Length == 0)
                {
                    ThrowError("Please enter the identifier or a tag of the mission you want to unlock.");
                    return;
                }
                var currentLocation = campaign.Map.CurrentLocation;
                if (MissionPrefab.Prefabs.Any(p => p.Identifier == args[0]))
                {
                    currentLocation.UnlockMissionByIdentifier(args[0].ToIdentifier());
                }
                else
                {
                    currentLocation.UnlockMissionByTag(args[0].ToIdentifier());
                }
                if (campaign is MultiPlayerCampaign mpCampaign)
                {
                    mpCampaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
                }
            }, isCheat: true, getValidArgs: () =>
            {
                return new[]
                {
                   MissionPrefab.Prefabs.Select(p => p.Identifier.ToString()).ToArray()
                };
            }));

            commands.Add(new Command("setcampaignmetadata", "setcampaignmetadata [identifier] [value]: Sets the specified campaign metadata value.", (string[] args) =>
            {
                if (!(GameMain.GameSession?.GameMode is CampaignMode campaign))
                {
                    ThrowError("The setcampaignmetadata command is only usable in the campaign mode.");
                    return;
                }
                if (args.Length < 2)
                {
                    ThrowError("Please specify an identifier and a value.");
                    return;
                }
                if (float.TryParse(args[1], out float floatVal))
                {
                    SetDataAction.PerformOperation(campaign.CampaignMetadata, args[0].ToIdentifier(), floatVal, SetDataAction.OperationType.Set);
                }
                else
                {
                    SetDataAction.PerformOperation(campaign.CampaignMetadata, args[0].ToIdentifier(), args[1], SetDataAction.OperationType.Set);
                }

            }, isCheat: true));

            commands.Add(new Command("setskill", "setskill [all/identifier] [max/level] [character]: Set your skill level.", (string[] args) =>
            {
                if (args.Length < 2)
                {
                    NewMessage($"Missing arguments. Expected at least 2 but got {args.Length} (skill, level, name)", Color.Red);
                    return;
                }

                Identifier skillIdentifier = args[0].ToIdentifier();
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
                    if (skillIdentifier == "all")
                    {
                        foreach (Skill skill in character.Info.Job.GetSkills())
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
                    Character.Controlled?.Info?.Job?.GetSkills()?.Select(skill => skill.Identifier.Value).ToArray() ?? Array.Empty<string>(),
                    new[]{ "max" },
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray(),
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
                        c.Identifier == args[0] ||
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
                    talentNames.Add(talent.DisplayName.Value);
                }

                return new string[][]
                {
                    talentNames.Select(id => id).ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("unlocktalents", "unlocktalents [all/[jobname]] [character]: give the specified character all the talents of the specified class", (string[] args) =>
            {
                var character = args.Length >= 2 ? FindMatchingCharacter(args.Skip(1).ToArray()) : Character.Controlled;
                if (character == null) { return; }

                List<TalentTree> talentTrees = new List<TalentTree>();
                if (args.Length == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    talentTrees.AddRange(TalentTree.JobTalentTrees);
                }
                else
                {
                    var job = JobPrefab.Prefabs.Find(jp => jp.Name != null && jp.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                    if (job == null)
                    {
                        ThrowError($"Failed to find the job \"{args[0]}\".");
                        return;
                    }
                    if (!TalentTree.JobTalentTrees.TryGet(job.Identifier, out TalentTree talentTree))
                    {
                        ThrowError($"No talents configured for the job \"{args[0]}\".");
                        return;
                    }
                    talentTrees.Add(talentTree);
                }

                foreach (var talentTree in talentTrees)
                {
                    foreach (var talentId in talentTree.AllTalentIdentifiers)
                    {
                        character.GiveTalent(talentId);
                        NewMessage($"Unlocked talent \"{talentId}\".");                        
                    }
                }
            },
            () =>
            {
                List<string> availableArgs = new List<string>() { "All" };
                availableArgs.AddRange(JobPrefab.Prefabs.Select(j => j.Name.Value));
                return new string[][]
                {
                    availableArgs.ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
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
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray(),
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
                    NewMessage("Level generation params: " + Level.Loaded.GenerationParams.Identifier);
                    NewMessage("Adjacent locations: " + (Level.Loaded.StartLocation?.Type.Identifier ?? "none".ToIdentifier()) + ", " + (Level.Loaded.StartLocation?.Type.Identifier ?? "none".ToIdentifier()));
                    NewMessage("Mirrored: " + Level.Loaded.Mirrored);
                    NewMessage("Level size: " + Level.Loaded.Size.X + "x" + Level.Loaded.Size.Y);
                    NewMessage("Minimum main path width: " + (Level.Loaded.LevelData?.MinMainPathWidth?.ToString() ?? "unknown"));
                }
            },null));
            
            commands.Add(new Command("teleportsub", "teleportsub [start/end/endoutpost/cursor]: Teleport the submarine to the position of the cursor, or the start or end of the level. The 'endoutpost' argument also automatically docks the sub with the outpost at the end of the level. WARNING: does not take outposts into account, so often leads to physics glitches. Only use for debugging.", 
            onExecute:(string[] args) =>
            {
                if (Submarine.MainSub == null) { return; }

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
                    if (Level.Loaded == null)
                    {
                        NewMessage("Can't teleport the sub to the start of the level (no level loaded).", Color.Red);
                        return;
                    }
                    Vector2 pos = Level.Loaded.StartPosition;
                    if (Level.Loaded.StartOutpost != null)
                    {
                        pos -= Vector2.UnitY * (Submarine.MainSub.Borders.Height + Level.Loaded.StartOutpost.Borders.Height) / 2;
                    }
                    Submarine.MainSub.SetPosition(pos);
                }
                else if (args[0].Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    if (Level.Loaded == null)
                    {
                        NewMessage("Can't teleport the sub to the end of the level (no level loaded).", Color.Red);
                        return;
                    }
                    Vector2 pos = Level.Loaded.EndPosition;
                    if (Level.Loaded.EndOutpost != null)
                    {
                        pos -= Vector2.UnitY * (Submarine.MainSub.Borders.Height + Level.Loaded.EndOutpost.Borders.Height) / 2;
                    }
                    Submarine.MainSub.SetPosition(pos);
                }                
                else if (args[0].Equals("endoutpost", StringComparison.OrdinalIgnoreCase))
                {
                    if (Level.Loaded?.EndOutpost == null)
                    {
                        NewMessage("Can't teleport the sub to the end outpost (no outpost at the end of the level).", Color.Red);
                        return;
                    }
                    Submarine.MainSub.SetPosition(Level.Loaded.EndExitPosition - Vector2.UnitY * Submarine.MainSub.Borders.Height);
                    var submarineDockingPort = DockingPort.List.FirstOrDefault(d => d.Item.Submarine == Submarine.MainSub);
                    var outpostDockingPort = DockingPort.List.FirstOrDefault(d => d.Item.Submarine == Level.Loaded.EndOutpost);
                    if (submarineDockingPort != null && outpostDockingPort != null)
                    {
                        submarineDockingPort.Dock(outpostDockingPort);
                    }
                }
            },
            getValidArgs:() =>
            {
                return new string[][]
                {
                    new string[] { "start", "end", "endoutpost", "cursor" }
                };
            }, isCheat: true));

#if DEBUG
            commands.Add(new Command("crash", "crash: Crashes the game.", (string[] args) =>
            {
                throw new Exception("crash command issued");
            }));

            commands.Add(new Command("listeditableproperties", "", (string[] args) =>
            {
                StringBuilder sb = new StringBuilder();
                string filename;
#if CLIENT
                filename = "ItemComponent properties (client).txt";
                sb.AppendLine("Client-side ItemComponent properties:");
#else
                filename = "ItemComponent properties (server).txt";
                sb.AppendLine("Server-side ItemComponent properties:");
#endif
                var itemComponents = typeof(ItemComponent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ItemComponent)));
                foreach (var ic in itemComponents.OrderBy(ic => ic.Name))
                {
                    sb.AppendLine(ic.Name+":");
                    foreach (var prop in ic.GetProperties())
                    {
                        if (prop.DeclaringType != ic) { continue; }
                        if (prop.GetCustomAttributes(inherit: false).OfType<Editable>().Any())
                        {
                            sb.AppendLine(prop.Name);
                        }
                    }
                }
                File.WriteAllText(filename, sb.ToString());
            }));

            commands.Add(new Command("fastforward", "fastforward [seconds]: Fast forwards the game by x seconds. Note that large numbers may cause a long freeze.", (string[] args) =>
            {
                float seconds = 0;
                if (args.Length > 0) { float.TryParse(args[0], out seconds); }
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                for (int i = 0; i < seconds * Timing.FixedUpdateRate; i++)
                {
                    Screen.Selected?.Update(Timing.Step);
                }
                sw.Stop();
                NewMessage($"Fast-forwarded by {seconds} seconds (took {sw.ElapsedMilliseconds / 1000.0f} s).");
            }));

            commands.Add(new Command("removecharacter", "removecharacter [character name]: Immediately deletes the specified character.", (string[] args) =>
            {
                if (args.Length == 0) { return; }
                Character character = FindMatchingCharacter(args, false);
                if (character == null) { return; }

                Entity.Spawner?.AddEntityToRemoveQueue(character);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("replaceitem", "replaceitem [item name (index)] [new item]: Replaces the specified item with another one.", (string[] args) =>
            {
                if (args.Length < 2) { return; }

                string itemName = args[0];
                int itemIndex = 0;
                string newItemName = args[1];
                if (args.Length == 3)
                {
                    if (!int.TryParse(args[1], out itemIndex))
                    {
                        ThrowError($"Failed to parse the argument {args[1]} as an index. Please give the arguments either in the format [old_item] [new_item] or [old_item] [index] [new_item]");
                        return;
                    }
                    newItemName = args[2];
                }

                var oldItem = Item.ItemList.FindAll(it => it.Name == args[0]).ElementAtOrDefault(itemIndex);
                if (oldItem == null)
                {
                    ThrowError($"Could not find an item with the name {args[0]} (index {itemIndex}).");
                    return;
                }
                if ((MapEntityPrefab.FindByIdentifier(args[1].ToIdentifier()) ?? MapEntityPrefab.FindByName(args[1])) is not ItemPrefab newItem)
                {
                    ThrowError($"Could not find an item with the name or identifier {args[1]}.");
                    return;
                }
                oldItem.ReplaceWithLinkedItems(newItem);
                NewMessage($"Replaced {oldItem.Name} with {newItem.Name}.");
            },
            () =>
            {
                return new string[][]
                {
                    Item.ItemList.Select(it => it.Name).Distinct().ToArray(),
                    ItemPrefab.Prefabs.Select(it => it.Name.Value).Distinct().ToArray(),
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

            commands.Add(new Command("testmaps", "testmaps [amount]: generates campaign maps and checks whether there are any errors or exceptions. If the amount argument is omitted, the command will keep testing maps until it's cancelled.", (string[] args) =>
            {
                if (args.Length > 0 && int.TryParse(args[0], out int amount)) 
                {
                    CoroutineManager.StartCoroutine(TestMaps(amount: amount));
                }
                else
                {
                    CoroutineManager.StartCoroutine(TestMaps());
                }
            },
            null));

            commands.Add(new Command("testmap", "testmap [seed]: generates a campaign map and checks whether there are any errors or exceptions.", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    ThrowError("Please provide the seed of the map to test.");
                    return;
                }
                CoroutineManager.StartCoroutine(TestMaps(fixedSeed: args[0], amount: 1));
            },
            null));

            IEnumerable<CoroutineStatus> TestMaps(string fixedSeed = null, int? amount = null)
            {
                int count = 0;
#if CLIENT
                while (!PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.C))
#else
                while (!input.Equals("c", StringComparison.OrdinalIgnoreCase))
#endif
                {
                    using var errorCatcher = DebugConsole.ErrorCatcher.Create();
                    {
                        string seed = fixedSeed ?? ToolBox.RandomSeed(16);
                        Map map = new Map(campaign: MultiPlayerCampaign.StartNew(seed, new CampaignSettings()), seed: seed);

                        //check path to the first end location, because there are no normal connections between the end locations
                        var lastLocation = map.EndLocations[0];
                        int endDistance = Map.GetDistanceToClosestLocationOrConnection(map.StartLocation, maxDistance: int.MaxValue, criteria: (Location location) => location == lastLocation);
                        if (endDistance == int.MaxValue)
                        {
                            ThrowError($"No path to the end of the map found. Seed: {seed}");
                        }

                        if (map.Locations.None(l => l.Type.Identifier == "outpost" && map.GetZoneIndex(l.MapPosition.X) == 1))
                        {
                            ThrowError($"No outpost in the first zone of the map. Seed: {seed}");
                        }

                        if (errorCatcher.Errors.Any())
                        {
                            ThrowError($"Error(s) found when generating a level. Seed: {seed}");
                            yield return CoroutineStatus.Success;
                        }

                        count++;
                        NewMessage($"Map seed {seed} ok (test #{count}). Press C to abort.");

                        map.Remove();

                        if (amount.HasValue && count >= amount)
                        {
                            NewMessage("Testing finished successfully.");
                            break;
                        }
                    }

                    yield return CoroutineStatus.Running;
                }
            }

            commands.Add(new Command("testlevels", "testlevels [amount]: generates levels and checks whether there are any errors or exceptions. If the amount argument is omitted, the command will keep testing levels until it's cancelled.", (string[] args) =>
            {
                if (args.Length > 0 && int.TryParse(args[0], out int amount))
                {
                    CoroutineManager.StartCoroutine(TestLevels(amount: amount));
                }
                else
                {
                    CoroutineManager.StartCoroutine(TestLevels());
                }
            },
            null));

            commands.Add(new Command("testlevel", "testlevel [seed]: generates a levels and checks whether there are any errors or exceptions.", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    ThrowError("Please provide the seed of the level to test.");
                    return;
                }
                CoroutineManager.StartCoroutine(TestLevels(fixedSeed: args[0], amount: 1));
            },
            null));

            IEnumerable<CoroutineStatus> TestLevels(string fixedSeed = null, int? amount = null)
            {
                SubmarineInfo selectedSub = null;
                Identifier subName = GameSettings.CurrentConfig.QuickStartSub;
                if (subName != Identifier.Empty)
                {
                    selectedSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                }

                int count = 0;
#if CLIENT
                while (!PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.C))
#else
                while (!input.Equals("c", StringComparison.OrdinalIgnoreCase))
#endif
                {
                    var gamesession = new GameSession(
                        SubmarineInfo.SavedSubmarines.GetRandomUnsynced(s => s.Type == SubmarineType.Player && !s.HasTag(SubmarineTag.HideInMenus)),
                        Option.None,
                        GameModePreset.DevSandbox ?? GameModePreset.Sandbox);
                    string seed = fixedSeed ?? ToolBox.RandomSeed(16);
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

                    if (Level.Loaded.StartOutpost != null &&
                        Level.Loaded.StartOutpost.Info.OutpostTags.Contains("PvPOutpost".ToIdentifier()))
                    {
                        ThrowError("Chose a PvP outpost for the start of the level. This is probably not intentional, unless there's a PvP outpost that's also intended to be used in normal levels?");
                    }
                    if (Level.Loaded.EndOutpost != null &&
                        Level.Loaded.EndOutpost.Info.OutpostTags.Contains("PvPOutpost".ToIdentifier()))
                    {
                        ThrowError("Chose a PvP outpost for the end of the level. This is probably not intentional, unless there's a PvP outpost that's also intended to be used in normal levels?");
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
                    NewMessage("Level seed " + seed + " ok (test #" + count + "). Press C to abort.");
#if CLIENT
                    //dismiss round summary and any other message boxes
                    GUIMessageBox.CloseAll();
#endif
                    if (amount.HasValue && count >= amount) 
                    {
                        NewMessage("Testing finished successfully.");
                        break;
                    }

                    yield return CoroutineStatus.Running;
                }
            }
#endif

                commands.Add(new Command("showreputation", "showreputation: List the current reputation values.", (string[] args) =>
            {
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    NewMessage("Reputation:");
                    foreach (var faction in campaign.Factions)
                    {
                        NewMessage($" - {faction.Prefab.Name}: {faction.Reputation.Value}");
                    }
                }
                else
                {
                    ThrowError("Could not show reputation (no active campaign).");
                }
            }, null));

            commands.Add(new Command("setlocationreputation", "setlocationreputation [value]: Set the reputation in the current location to the specified value.", (string[] args) =>
            {
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    if (args.Length == 0) { return; }
                    if (float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float reputation))
                    {
                        campaign.Map.CurrentLocation.Reputation?.SetReputation(reputation);
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
                    if (campaign.Factions.FirstOrDefault(f => f.Prefab.Identifier == args[0]) is { } faction)
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
                return new[]
                {
                    FactionPrefab.Prefabs.Select(static f => f.Identifier.Value).ToArray(),
                    GameMain.GameSession?.Campaign?.Factions.Select(static f => f.Prefab.Identifier.ToString()).ToArray() ?? Array.Empty<string>()
                };
            }, true));

            commands.Add(new Command("fixitems", "fixitems: Repairs all items and restores them to full condition.", (string[] args) =>
            {
                foreach (Item it in Item.ItemList)
                {
                    if (it.GetComponent<GeneticMaterial>() != null) { continue; }
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
                        GameAnalyticsManager.AddErrorEventOnce("DebugConsole.FixHulls", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    }
                }
            }, null, true));

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
                    if (!string.IsNullOrWhiteSpace(categoryIdentifier) && category.Identifier != categoryIdentifier) { continue; }
                    foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                    {
                        if (!prefab.UpgradeCategories.Contains(category)) { continue; }
                        if (!string.IsNullOrWhiteSpace(prefabIdentifier) && prefab.Identifier != prefabIdentifier) { continue; }
                        
                        int targetLevel = prefab.GetMaxLevelForCurrentSub() - upgradeManager.GetRealUpgradeLevel(prefab, category);
                        for (int i = 0; i < targetLevel; i++)
                        {
                            upgradeManager.TryPurchaseUpgrade(prefab, category, force: true);
                        }
                        NewMessage($"Upgraded {category.Identifier}.{prefab.Identifier} by {targetLevel} levels.", Color.DarkGreen);
                    }
                }

                NewMessage($"Start a new round to apply the upgrades.", Color.Lime);
            }, () =>
            {
                return new[]
                {
                    UpgradeCategory.Categories.Select(c => c.Identifier).Distinct().Select(i => i.Value).ToArray(),
                    UpgradePrefab.Prefabs.Select(c => c.Identifier).Distinct().Select(i => i.Value).ToArray()
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
                foreach (Hull hull in Hull.HullList)
                {
                    hull.OxygenPercentage = 100.0f;
                }
            }, null, true));

            commands.Add(new Command("kill", "kill [character]: Immediately kills the specified character.", (string[] args) =>
            {
                Character killedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(args);
                killedCharacter?.Kill(CauseOfDeathType.Unknown, causeOfDeathAffliction: null);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
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
                foreach (Hull hull in Hull.HullList)
                {
                    hull.BallastFlora?.Kill();
                }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    sub.WreckAI?.Kill();
                }
            }, null, isCheat: true));

            commands.Add(new Command("killall", "killall: Immediately kills all characters in the level.", args =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    c.Kill(CauseOfDeathType.Unknown, null);
                    NewMessage($"Killed {c.DisplayName}.");
                }
            }, null, isCheat: true));

            commands.Add(new Command("despawnnow", "despawnnow [character]: Immediately despawns the specified dead character. If the character argument is omitted, all dead characters are despawned.", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    foreach (Character c in Character.CharacterList.Where(c => c.IsDead).ToList())
                    {
                        c.DespawnNow();
                    }
                }
                else
                {
                    Character character = FindMatchingCharacter(args);
                    character?.DespawnNow();
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Where(c => c.IsDead).Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }, isCheat: true));

            commands.Add(new Command("setclientcharacter", "setclientcharacter [client name] [character name]: Gives the client control of the specified character.", null,
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
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
                        NewMessage("     " + i + ". " + connection.OtherLocation(campaign.Map.CurrentLocation).DisplayName, Color.White);
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
                        NewMessage(location.DisplayName + " selected.", Color.White);
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
                    NewMessage(location.DisplayName + " selected.", Color.White);
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

            commands.Add(new Command("money", "money [amount] [character]: Gives the specified amount of money to the crew when a campaign is active.", args =>
            {
                if (args.Length == 0) { return; }

                if (!(GameMain.GameSession?.GameMode is CampaignMode campaign)) { return; }
                Character targetCharacter = null;

                if (args.Length >= 2)
                {
                    targetCharacter = FindMatchingCharacter(args.Skip(1).ToArray());
                }

                if (int.TryParse(args[0], out int money))
                {
                    Wallet wallet = targetCharacter is null || GameMain.IsSingleplayer ? campaign.Bank : targetCharacter.Wallet;
                    wallet.Give(money);
                    GameAnalyticsManager.AddMoneyGainedEvent(money, GameAnalyticsManager.MoneySource.Cheat, "console");
                }
                else
                {
                    ThrowError($"\"{args[0]}\" is not a valid numeric value.");
                }
            }, isCheat: true, getValidArgs: () => new []
            {
                new []{ string.Empty },
                Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
            }));

            commands.Add(new Command("showmoney", "showmoney: Shows the amount of money in everyones wallet.", args =>
            {
                if (!(GameMain.GameSession?.GameMode is CampaignMode campaign))
                {
                    ThrowError("No campaign active!");
                    return;
                }

                NewMessage($"Bank: {campaign.Bank.Balance}");
            }, isCheat: true));

            commands.Add(new Command("skipeventcooldown", "skipeventcooldown: Skips the currently active event cooldown and triggers pending monster spawns immediately.", args =>
            {
                GameMain.GameSession?.EventManager?.SkipEventCooldown();
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
                        if (item.CurrentHull != null && item.HasTag(Tags.Ballast) && item.GetComponent<Pump>() is { } pump)
                        {
                            if (item.CurrentHull.BallastFlora != null) { continue; }
                            pumps.Add(pump);
                        }
                    }
                
                    if (pumps.Any())
                    {
                        BallastFloraPrefab prefab = string.IsNullOrWhiteSpace(secondaryArgument) ? BallastFloraPrefab.Prefabs.First() : BallastFloraPrefab.Find(secondaryArgument.ToIdentifier());
                        if (prefab == null)
                        {
                            ThrowError($"No such behavior: {secondaryArgument}");
                            return;
                        }

                        Pump random = pumps.GetRandomUnsynced();
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
                        foreach (Hull hull in Hull.HullList.Where(h => h.BallastFlora != null))
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
                string[] identifiers = BallastFloraPrefab.Prefabs.Select(bfp => bfp.Identifier).Distinct().Select(i => i.Value).ToArray();
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
                var config = GameSettings.CurrentConfig;
                config.VerboseLogging = !GameSettings.CurrentConfig.VerboseLogging;
                GameSettings.SetCurrentConfig(config);
                NewMessage((GameSettings.CurrentConfig.VerboseLogging ? "Enabled" : "Disabled") + " verbose logging.", Color.White);
            }, isCheat: false));

            commands.Add(new Command("listtasks", "listtasks: Lists all asynchronous tasks currently in the task pool.", (string[] args) => { TaskPool.ListTasks(line => DebugConsole.NewMessage(line)); }));
            
            commands.Add(new Command("listcoroutines", "listcoroutines: Lists all coroutines currently running.", (string[] args) => { CoroutineManager.ListCoroutines(); }));

            commands.Add(new Command("calculatehashes", "calculatehashes [content package name]: Show the MD5 hashes of the files in the selected content package. If the name parameter is omitted, the first content package is selected.", (string[] args) =>
            {
                if (args.Length > 0)
                {
                    string packageName = string.Join(" ", args);
                    var package = ContentPackageManager.EnabledPackages.All.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
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
                    ContentPackageManager.EnabledPackages.Core.CalculateHash(logging: true);
                }
            },
            () =>
            {
                return new string[][]
                {
                    ContentPackageManager.EnabledPackages.All.Select(cp => cp.Name).ToArray()
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
                if (GameMain.NetworkMember != null)
                {
                    GameMain.NetworkMember.SimulatedMinimumLatency = minimumLatency;
                    GameMain.NetworkMember.SimulatedRandomLatency = randomLatency;
                }
                NewMessage("Set simulated minimum latency to " + minimumLatency.ToString(CultureInfo.InvariantCulture) + " and random latency to " + randomLatency.ToString(CultureInfo.InvariantCulture) + ".", Color.White);
            }));

            commands.Add(new Command("simulatedloss", "simulatedloss [lossratio]: applies simulated packet loss to network messages. For example, a value of 0.1 would mean 10% of the packets are dropped. Useful for simulating real network conditions when testing the multiplayer locally.", (string[] args) =>
            {
                if (args.Count() < 1 || (GameMain.NetworkMember == null)) return;
                if (!float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float loss))
                {
                    ThrowError(args[0] + " is not a valid loss ratio.");
                    return;
                }
                if (GameMain.NetworkMember != null)
                {
                    GameMain.NetworkMember.SimulatedLoss = loss;
                }
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
                if (GameMain.NetworkMember != null)
                {
                    GameMain.NetworkMember.SimulatedDuplicatesChance = duplicates;
                }
                NewMessage("Set packet duplication to " + (int)(duplicates * 100) + "%.", Color.White);
            }));

#if DEBUG
            commands.Add(new Command("debugvoip", "Toggle the server writing VOIP into audio files.", null, isCheat: false));

            commands.Add(new Command("simulatedlongloadingtime", "simulatedlongloadingtime [minimum loading time]: forces loading a round to take at least the specified amount of seconds.", (string[] args) =>
            {
                if (args.Count() < 1 || (GameMain.NetworkMember == null)) return;
                if (!float.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float time))
                {
                    ThrowError(args[0] + " is not a valid duration ratio.");
                    return;
                }
                GameSession.MinimumLoadingTime = time;                
                NewMessage("Set minimum loading time to " + time + " seconds.", Color.White);
            }));


            commands.Add(new Command("resetcharacternetstate", "resetcharacternetstate [character name]: A debug-only command that resets a character's network state, intended for diagnosing character syncing issues.", null,
            () =>
            {
                if (GameMain.NetworkMember == null) { return null; }
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().OrderBy(n => n).ToArray()
                };
            }));

            commands.Add(new Command("storeinfo", "", (string[] args) =>
            {
                if (GameMain.GameSession?.Map?.CurrentLocation is Location location)
                {
                    if (location.Stores != null)
                    {
                        var msg = "--- Location: " + location.DisplayName + " ---";
                        foreach (var store in location.Stores)
                        {
                            msg += $"\nStore identifier: {store.Value.Identifier}";
                            msg += $"\nBalance: {store.Value.Balance}";
                            msg += $"\nPrice modifier: {store.Value.PriceModifier}%";
                            msg += "\nDaily specials:";
                            store.Value.DailySpecials.ForEach(i => msg += $"\n   - {i.Name}");
                            msg += "\nRequested goods:";
                            store.Value.RequestedGoods.ForEach(i => msg += $"\n   - {i.Name}");
                            
                        }
                        NewMessage(msg);
                    }
                    else
                    {
                        NewMessage($"No stores at {location}, can't show store info.");
                    }
                }
                else
                {
                    NewMessage("No current location set, can't show store info.");
                }
            }));
#endif

            commands.Add(new Command("startitems|startitemset", "start item set identifier", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    ThrowError($"No start item set identifier defined!");
                    return;
                }
                AutoItemPlacer.DefaultStartItemSet = args[0].ToIdentifier();
                NewMessage($"Start item set changed to \"{AutoItemPlacer.DefaultStartItemSet}\"");
            }, isCheat: false));

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
            commands.Add(new Command("debugwiring", "Toggle the wiring debug mode on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("debugdrawlocalization", "Toggle the localization debug drawing mode on/off (client-only). Colors all text that hasn't been fetched from a localization file magenta, making it easier to spot hard-coded or missing texts.", null, isCheat: false));
            commands.Add(new Command("debugdrawlos", "Toggle the los debug drawing mode on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("togglevoicechatfilters", "Toggle the radio/muffle filters in the voice chat (client-only).", null, isCheat: false));
            commands.Add(new Command("togglehud|hud", "Toggle the character HUD (inventories, icons, buttons, etc) on/off (client-only).", null));
            commands.Add(new Command("toggleupperhud", "Toggle the upper part of the ingame HUD (chatbox, crewmanager) on/off (client-only).", null));
            commands.Add(new Command("toggleitemhighlights", "Toggle the item highlight effect on/off (client-only).", null));
            commands.Add(new Command("togglecharacternames", "Toggle the names hovering above characters on/off (client-only).", null));
            commands.Add(new Command("followsub", "Toggle whether the camera should follow the nearest submarine (client-only).", null));
            commands.Add(new Command("toggleaitargets|aitargets", "Toggle the visibility of AI targets (= targets that enemies can detect and attack/escape from) (client-only).", null, isCheat: true));
            commands.Add(new Command("debugai", "Toggle the ai debug mode on/off (works properly only in single player).", null, isCheat: true));
            commands.Add(new Command("devmode", "Toggle the dev mode on/off (client-only).", null, isCheat: true));
            commands.Add(new Command("showmonsters", "Permanently unlocks all the monsters in the character editor. Use \"hidemonsters\" to undo.", null, isCheat: true));
            commands.Add(new Command("hidemonsters", "Permanently hides in the character editor all the monsters that haven't been encountered in the game. Use \"showmonsters\" to undo.", null, isCheat: true));
            commands.Add(new Command("loslightingfreecam", "Toggles line of sight effect, lighting, and enables freecam mode. (client-only)", null, isCheat: true));

            InitProjectSpecific();

            commands.Sort((c1, c2) => c1.Names.First().CompareTo(c2.Names.First()));
        }

        private static void HealCharacter(Character healedCharacter, bool healAll, Client targetClient = null)
        {
            healedCharacter.SetAllDamage(0.0f, 0.0f, 0.0f);
            healedCharacter.Oxygen = 100.0f;
            healedCharacter.Bloodloss = 0.0f;
            healedCharacter.SetStun(0.0f, true);
            if (healAll)
            {
                healedCharacter.CharacterHealth.RemoveAllAfflictions();
            }

            string characterNameText = healedCharacter == Character.Controlled ? $"{healedCharacter.Name} (you)" : healedCharacter.Name;
            string text = healAll ? $"Healed {characterNameText}: all afflictions" : $"Healed {characterNameText}: damage and common afflictions";
            NewMessage(text, Color.Yellow);
#if SERVER
            if (targetClient != null)
            {
                GameMain.Server.SendConsoleMessage(text, targetClient);
            }
#endif
        }

        public static string AutoComplete(string command, int increment = 1)
        {
            string[] splitCommand = ToolBox.SplitCommand(command);
            string[] args = splitCommand.Skip(1).ToArray();

            //if an argument is given or the last character is a space, attempt to autocomplete the argument
            if (args.Length > 0 || (splitCommand.Length > 0 && command.Last() == ' '))
            {
                Command matchingCommand = commands.Find(c => c.Names.Contains(splitCommand[0].ToIdentifier()));
                if (matchingCommand?.GetValidArgs == null) { return command; }

                int autoCompletedArgIndex = args.Length > 0 && command.Last() != ' ' ? args.Length - 1 : args.Length;

                //get all valid arguments for the given command
                string[][] allArgs = matchingCommand.GetValidArgs();
                if (allArgs == null || allArgs.GetLength(0) < autoCompletedArgIndex + 1) { return command; }

                if (string.IsNullOrEmpty(currentAutoCompletedCommand))
                {
                    currentAutoCompletedCommand = autoCompletedArgIndex > args.Length - 1 ? " " : args.Last();
                }

                //find all valid autocompletions for the given argument
                string[] validArgs = allArgs[autoCompletedArgIndex].Where(arg =>
                    currentAutoCompletedCommand.Trim().Length <= arg.Length &&
                    arg.Substring(0, currentAutoCompletedCommand.Trim().Length).ToLower() == currentAutoCompletedCommand.Trim().ToLower()).ToArray();
                
                // add all completions that contain the current argument, to the end of the list
                validArgs = validArgs.Concat(allArgs[autoCompletedArgIndex].Where(arg => 
                    arg.ToLower().Contains(currentAutoCompletedCommand.Trim().ToLower()) && 
                    !validArgs.Contains(arg))).ToArray();

                if (validArgs.Length == 0) { return command; }

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

                List<Identifier> matchingCommands = new List<Identifier>();
                foreach (Command c in commands)
                {
                    foreach (var name in c.Names)
                    {
                        if (currentAutoCompletedCommand.Length > name.Value.Length) { continue; }
                        if (name.StartsWith(currentAutoCompletedCommand))
                        {
                            matchingCommands.Add(name);
                        }
                    }
                }

                if (matchingCommands.Count == 0) return command;
                
                currentAutoCompletedIndex = MathUtils.PositiveModulo(currentAutoCompletedIndex + increment, matchingCommands.Count);
                return matchingCommands[currentAutoCompletedIndex].Value;
            }
        }

        public static void ResetAutoComplete()
        {
            currentAutoCompletedCommand = "";
            currentAutoCompletedIndex = 0;
        }

        /// <summary>
        /// Executes the specific command or commands
        /// </summary>
        /// <param name="inputtedCommands">Command, or multiple commands separated by newlines.</param>  
        public static void ExecuteCommand(string inputtedCommands)
        {
            if (string.IsNullOrWhiteSpace(inputtedCommands) || inputtedCommands == "\\" || inputtedCommands == "\n") { return; }

            string[] commandsToExecute = inputtedCommands.Split("\n");
            foreach (string command in commandsToExecute)
            {
                if (activeQuestionCallback != null)
                {
#if CLIENT
                    activeQuestionText = null;
#endif
                    NewCommand(command);
                    //reset the variable before invoking the delegate because the method may need to activate another question
                    var temp = activeQuestionCallback;
                    activeQuestionCallback = null;
                    temp(command);
                    return;
                }

                if (string.IsNullOrWhiteSpace(command) || command == "\\") { return; }

                string[] splitCommand = ToolBox.SplitCommand(command);
                if (splitCommand.Length == 0)
                {
                    ThrowError("Failed to execute command \"" + command + "\"!");
                    GameAnalyticsManager.AddErrorEventOnce(
                        "DebugConsole.ExecuteCommand:LengthZero",
                        GameAnalyticsManager.ErrorSeverity.Error,
                        "Failed to execute command \"" + command + "\"!");
                    return;
                }

                Identifier firstCommand = splitCommand[0].ToIdentifier();

                if (firstCommand != "admin")
                {
                    NewCommand(command);
                }

#if CLIENT
                if (GameMain.Client != null)
                {
                    Command matchingCommand = commands.Find(c => c.Names.Contains(firstCommand));
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
                    if (!IsCommandPermitted(firstCommand, GameMain.Client))
                    {
#if DEBUG
                        AddWarning($"You're not permitted to use the command \"{firstCommand}\". Executing the command anyway because this is a debug build.");
#else
                    ThrowError($"You're not permitted to use the command \"{firstCommand}\"!");
                    return;
#endif
                    }
                }
#endif

                bool commandFound = false;
                foreach (Command c in commands)
                {
                    if (!c.Names.Contains(firstCommand)) { continue; }
                    c.Execute(splitCommand.Skip(1).ToArray());
                    commandFound = true;
                    break;
                }

                if (!commandFound)
                {
                    ThrowError("Command \"" + splitCommand[0] + "\" not found.");
                }
            }
        }

        private static string[] ListAvailableLocations()
        {
            List<string> locationNames = new();
            foreach (var submarine in Submarine.Loaded)
            {
                locationNames.Add(submarine.Info.Name);
            }

            if (Level.Loaded != null)
            {
                foreach (var cave in Level.Loaded.Caves)
                {
                    string caveName = cave.CaveGenerationParams.Name;
                    // add index in case there are duplicate names
                    int index = 1;
                    while (locationNames.Contains($"{caveName}_{index}"))
                    {
                        index++;
                    }
                    locationNames.Add($"{caveName}_{index}");
                }
            }

            if (Submarine.MainSub != null) { locationNames.Add("mainsub"); }
            locationNames.Add("cursor");

            return locationNames.ToArray();
        }
        
        private static bool TryFindTeleportPosition(string locationName, out Vector2 teleportPosition)
        {
            if (Submarine.MainSub is Submarine mainSub && string.Equals(locationName, "mainsub", StringComparison.InvariantCultureIgnoreCase))
            {
                var randomWaypoint = GetRandomWaypoint(mainSub.GetWaypoints(alsoFromConnectedSubs:false));
                if (randomWaypoint != null)
                {
                    teleportPosition = randomWaypoint.WorldPosition;
                    return true;
                }
                LogError("No waypoints found in the main sub!");
            }
            
            foreach (var submarine in Submarine.Loaded)
            {
                if (string.Equals(submarine.Info.Name, locationName, StringComparison.InvariantCultureIgnoreCase))
                {
                    var randomWaypoint = GetRandomWaypoint(submarine.GetWaypoints(alsoFromConnectedSubs:false));
                    if (randomWaypoint != null)
                    {
                        teleportPosition = randomWaypoint.WorldPosition;
                        return true;
                    }
                    LogError($"No waypoints found in sub {submarine.Info.Name}!");
                }
            }

            if (Level.Loaded is Level loadedLevel)
            {
                (string locationNameNoIndex, int locationIndex) = SplitIndex(locationName);
                int caveIndex = 1;
                foreach (var cave in loadedLevel.Caves)
                {
                    if (string.Equals(cave.CaveGenerationParams.Name, locationNameNoIndex, StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (caveIndex != locationIndex)
                        {
                            caveIndex++;
                            continue;
                        }
                        
                        var randomWaypoint = GetRandomWaypoint(cave.Tunnels.GetRandom(Rand.RandSync.Unsynced).WayPoints);
                        if (randomWaypoint != null)
                        {
                            teleportPosition = randomWaypoint.WorldPosition;
                            return true;
                        }
                        LogError($"No waypoints found in cave {cave.CaveGenerationParams.Name}!");
                    }
                }
            }
            teleportPosition = Vector2.Zero;
            return false;
            
            WayPoint GetRandomWaypoint(IReadOnlyList<WayPoint> waypoints)
            {
                if (waypoints.None())
                {
                    return null;
                }
                
                if (waypoints.Any(point => point.SpawnType == SpawnType.Human))
                {
                    return waypoints.GetRandom(point => point.SpawnType == SpawnType.Human, Rand.RandSync.Unsynced);
                }
                
                if (waypoints.Any(point => point.SpawnType == SpawnType.Path))
                {
                    return waypoints.GetRandom(point => point.SpawnType == SpawnType.Path, Rand.RandSync.Unsynced);
                }
                
                return waypoints.GetRandom(Rand.RandSync.Unsynced);
            }
            
            (string, int) SplitIndex(string caveName)
            { 
                string[] splitName = caveName.Split('_');
                if (splitName.Length == 1)
                {
                    return (splitName[0], -1);
                }
                else
                {
                    return (splitName[0], int.Parse(splitName[1]));
                }
            }
        }
        
        
        private static TFile GetSubmarineFile<TFile>(string submarineName) where TFile : BaseSubFile
        {
            List<TFile> submarineFiles = GetContentFiles<TFile>();
            
            foreach (var file in submarineFiles)
            {
                var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(i => i.FilePath == file.Path.Value);
                if (matchingSub != null && string.Equals(matchingSub.Name, submarineName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return file;
                }
            }
            
            return null;
        }
        
        private static List<TFile> GetContentFiles<TFile>() where TFile : ContentFile
        {
            var contentFiles = ContentPackageManager.EnabledPackages.All
                .SelectMany(p => p.GetFiles<TFile>())
                .ToList();
            
            return contentFiles;
        }
        
        private static List<TFile> GetSubmarineFiles<TFile>() where TFile : BaseSubFile
        {
            var submarineFiles = GetContentFiles<TFile>()
                .OrderBy(f => f.UintIdentifier).ToList();
            
            return submarineFiles;
        }
        
        private static ContentFile GetContentFile(string path)
        {
            var contentFiles = GetContentFiles<ContentFile>();
            return contentFiles.FirstOrDefault(file => string.Equals(file.Path.Value, path, StringComparison.InvariantCultureIgnoreCase));
        }
        
        private static string[] ListContentFilePaths()
        {
            List<string> contentFilePaths = new();
            
            var contentFiles = GetContentFiles<ContentFile>();
            foreach (var contentFile in contentFiles)
            {
                contentFilePaths.Add(contentFile.Path.Value);
            }
            
            return contentFilePaths.ToArray();
        }
        
        private static string[] ListSubmarineFileNames<TFile>() where TFile : BaseSubFile
        {
            List<string> submarineFileNames = new List<string>();
            
            var submarineFiles = GetSubmarineFiles<TFile>();
            
            foreach (var file in submarineFiles)
            {
                var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(i => i.FilePath == file.Path.Value);
                if (matchingSub != null)
                {
                    submarineFileNames.Add(matchingSub.Name);
                }
            }
            
            return submarineFileNames.ToArray();
        }

        private static IOrderedEnumerable<Character> SortSpawnedSpecies(IEnumerable<Character> characterList) => characterList.OrderBy(c => c.IsDead).ThenByDescending(c => c.IsHuman).ThenBy(c => c.Name);

        private static string[] ListCharacterNames(bool includeMeArgument = false, bool includeCrewArgument = false) => 
            GetCharacterNames(includeMeArgument, includeCrewArgument);

        private static string[] GetCharacterNames(bool includeMeArgument = false, bool includeCrewArgument = false)
        {
            var characterNames = new List<string>();
            if (includeMeArgument) { characterNames.Add("/me"); }
            if (includeCrewArgument) { characterNames.Add("/crew"); }
            characterNames.AddRange(SortSpawnedSpecies(Character.CharacterList).Select(c => c.Name));
            return characterNames.ToArray();
        }

        private static string[] GetSpawnedSpeciesNames() => SortSpawnedSpecies(Character.CharacterList).Select(c => c.SpeciesName.Value).Distinct().ToArray();

        private static IEnumerable<Character> FindMatchingSpecies(string[] args)
        {
            if (args.Length == 0) { return Array.Empty<Character>(); }
            string speciesName = args[0].ToLowerInvariant();
            return FindMatchingSpecies(speciesName);
        }
        
        private static IEnumerable<Character> FindMatchingSpecies(string speciesName) => Character.CharacterList.FindAll(c => c.SpeciesName.Value.Equals(speciesName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Checks if the arguments specify a specific character, or if they target the crew, and executes the specified action on them.
        /// </summary>
        private static void HandleCommandForCrewOrSingleCharacter(string[] args, Action<Character> action, Client targetClient = null)
        {
            if (args.Length > 0 && args.First() == "/crew")
            {
                foreach (var crewCharacter in GameSession.GetSessionCrewCharacters(CharacterType.Both))
                {
                    action(crewCharacter);
                }
            }
            else
            {
                Character targetCharacter = (args.Length == 0 || args.First() == "/me") ? 
                    targetClient?.Character ?? Character.Controlled : 
                    FindMatchingCharacter(args, false);
                if (targetCharacter == null) { return; }
                action(targetCharacter);
            }
        }

        private static Character FindMatchingCharacter(string[] args, bool ignoreRemotePlayers = false, Client allowedRemotePlayer = null, bool botsOnly = false)
        {
            if (args.Length == 0) { return null; }
            
            List<Character> matchingCharacters = null;
            string characterName = null;
            int characterIndex = -1;
            foreach (string arg in args)
            {
                if (arg == "/me")
                {
                    return allowedRemotePlayer?.Character ?? Character.Controlled;
                }
                // try to parse the character name from all the arguments.
                if (matchingCharacters == null || matchingCharacters.None())
                {
                    string possibleCharacterName = arg?.ToLowerInvariant();
                    matchingCharacters = Character.CharacterList.FindAll(c => 
                        c.Name.Equals(possibleCharacterName, StringComparison.OrdinalIgnoreCase) &&
                        (!c.IsRemotePlayer || !ignoreRemotePlayers || allowedRemotePlayer?.Character == c));
                
                    if (botsOnly)
                    {
                        matchingCharacters = matchingCharacters.FindAll(c => c is AICharacter);
                    }
                    if (matchingCharacters.Any())
                    {
                        characterName = possibleCharacterName;
                    }
                }
                else if (characterName != null && int.TryParse(arg, out int possibleIndex))
                {
                    // If we've already found the character name, let's seek for the index.
                    characterIndex = possibleIndex;
                }
            }

            if (matchingCharacters == null || matchingCharacters.None())
            {
                NewMessage("No matching character found!", Color.Red);
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
        
        private static void TeleportCharacter(Vector2 cursorWorldPos, Character controlledCharacter, string[] args)
        {
            if (Screen.Selected != GameMain.GameScreen)
            {
                NewMessage("Cannot teleport a character in the menu or the editor screens.", color: Color.Yellow);
                return;
            }
            
            Character targetCharacter = controlledCharacter;
            Vector2 worldPosition = cursorWorldPos;
            string locationNameArgument = "";
            string firstArgument = args.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            if (args.Length > 0)
            {
                string lastArgument = args.Last();
                // First seek the matching character.
                if (firstArgument is not ("/me" or "/crew"))
                {
                    var availableLocations = ListAvailableLocations();
                    if (args.Length > 1 || availableLocations.None(locationName => string.Equals(locationName, lastArgument, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Try to find a matching character, if there's more than one argument or if the last argument is not a valid location argument.
                        // If there's only one argument, and it's a valid location argument, we shouldn't try to parse a target character from it.
                        targetCharacter = FindMatchingCharacter(args, ignoreRemotePlayers: false);   
                    }
                }
                // Then seek the possible location argument.
                if (args.Count() > 1)
                {
                    if (targetCharacter == null || !targetCharacter.Name.Equals(lastArgument, StringComparison.OrdinalIgnoreCase) && 
                        !int.TryParse(lastArgument, out _))
                    {
                        locationNameArgument = lastArgument;
                    }
                }
            }
            if (firstArgument == "/crew")
            {
                foreach (var crewCharacter in GameSession.GetSessionCrewCharacters(CharacterType.Both))
                {
                    TeleportSpecificCharacter(crewCharacter, locationNameArgument, worldPosition);
                }
            }
            else
            {
                TeleportSpecificCharacter(targetCharacter, locationNameArgument, worldPosition);
            }
        }

        private static void TeleportSpecificCharacter(Character targetCharacter, string locationNameArgument, Vector2 defaultWorldPosition)
        {
            Vector2 worldPosition = defaultWorldPosition;
            if (!string.IsNullOrWhiteSpace(locationNameArgument) && !string.Equals(locationNameArgument, "cursor", comparisonType: StringComparison.InvariantCultureIgnoreCase))
            {
                if (TryFindTeleportPosition(locationNameArgument, out Vector2 teleportPosition))
                {
                    worldPosition = teleportPosition;
                }
                else
                {
                    ThrowError($"No teleport position for location \"{locationNameArgument}\" was found.");
                    return;
                }
            }

            if (targetCharacter != null)
            {
                targetCharacter.TeleportTo(worldPosition);
                targetCharacter.AnimController.BodyInRest = false;
            }
            else
            {
                NewMessage("Invalid arguments", color: Color.Yellow);
            }
        }

        /// <param name="usePreConfiguredNPC">Should we spawn a preconfigured NPC from an <see cref="NPCSet"/>? If so, the first 2 arguments are expected to be the identifier of the NPC set and the identifier of the NPC.</param>
        private static void SpawnCharacter(string[] args, Vector2 cursorWorldPos, bool usePreConfiguredNPC = false)
        {
            int characterArgumentCount = 1;
            if (usePreConfiguredNPC)
            {
                //two arguments required for NPCs, identifier of the NPC set and identifier of the NPC.
                characterArgumentCount = 2;
            }

            if (args.Length < characterArgumentCount) { return; }
            for (int i = 0; i < characterArgumentCount; i++)
            {
                if (string.IsNullOrWhiteSpace(args[i])) { return; }
            }

            JobPrefab job = null;
            bool isHuman = true;
            if (!usePreConfiguredNPC)
            {
                string characterLowerCase = args[0].ToLowerInvariant();
                if (!JobPrefab.Prefabs.ContainsKey(characterLowerCase))
                {
                    job = JobPrefab.Prefabs.Find(jp => jp.Name != null && jp.Name.Equals(characterLowerCase, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    job = JobPrefab.Prefabs[characterLowerCase];
                }
                isHuman = job != null || characterLowerCase == CharacterPrefab.HumanSpeciesName;
            }

            ParseOptionalArgs(out Vector2 spawnPosition, out WayPoint spawnPoint, out CharacterTeamType? teamType, out bool addToCrew);

            if (usePreConfiguredNPC)
            {
                Identifier npcSetIdentifier = args[0].ToIdentifier();
                Identifier humanPrefabIdentifier = args[1].ToIdentifier();
                HumanPrefab humanPrefab =
                    npcSetIdentifier == "any" ?
                        NPCSet.Sets.SelectMany(set => set.Humans).FirstOrDefault(human => human.Identifier == humanPrefabIdentifier) :
                        NPCSet.Get(npcSetIdentifier, humanPrefabIdentifier);
                if (humanPrefab != null)
                {
                    Entity.Spawner.AddCharacterToSpawnQueue(CharacterPrefab.HumanSpeciesName, spawnPosition, humanPrefab.CreateCharacterInfo(), onSpawn: newCharacter =>
                    {
                        newCharacter.HumanPrefab = humanPrefab;
                        SetTeamAndCrew(newCharacter);
                        humanPrefab.GiveItems(newCharacter, newCharacter.Submarine, spawnPoint);
                        humanPrefab.InitializeCharacter(newCharacter);
#if SERVER
                        newCharacter.LoadTalents();
                        GameMain.NetworkMember.CreateEntityEvent(newCharacter, new Character.UpdateTalentsEventData());
#endif
                    });
                }
            }
            else if (isHuman)
            {
                int variant = job != null ? Rand.Range(0, job.Variants, Rand.RandSync.ServerAndClient) : 0;
                CharacterInfo characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: job, variant: variant);
                Entity.Spawner.AddCharacterToSpawnQueue(CharacterPrefab.HumanSpeciesName, spawnPosition, characterInfo, onSpawn: newCharacter =>
                {
                    SetTeamAndCrew(newCharacter);
                    newCharacter.GiveJobItems(isPvPMode: GameMain.GameSession?.GameMode is PvPMode, spawnPoint);
                    newCharacter.GiveIdCardTags(spawnPoint);
                    newCharacter.Info.StartItemsGiven = true;
                });
            }
            else if (CharacterPrefab.FindBySpeciesName(args[0].ToIdentifier()) is { } prefab)
            {
                Entity.Spawner.AddCharacterToSpawnQueue(args[0].ToIdentifier(), spawnPosition, prefab.HasCharacterInfo ? new CharacterInfo(prefab.Identifier) : null, onSpawn: SetTeamAndCrew);
            }

            void SetTeamAndCrew(Character newCharacter)
            {
                if (teamType.HasValue)
                {
                    newCharacter.TeamID = teamType.Value;
                }
                else if (isHuman)
                {
                    newCharacter.TeamID = Character.Controlled?.TeamID ?? CharacterTeamType.Team1;
                }
                if (addToCrew)
                {
                    GameMain.GameSession?.CrewManager.AddCharacter(newCharacter);
                }
            }

            void ParseOptionalArgs(out Vector2 spawnPosition, out WayPoint spawnPoint, out CharacterTeamType? teamType, out bool addToCrew)
            {
                spawnPosition = Vector2.Zero;
                spawnPoint = null;
                teamType = null;

                int argIndex = characterArgumentCount;
                if (args.Length > argIndex)
                {
                    switch (args[argIndex].ToLowerInvariant())
                    {
                        case "inside":
                            spawnPoint = WayPoint.GetRandom(SpawnType.Human, job, Submarine.MainSub);
                            break;
                        case "outside":
                            spawnPoint = WayPoint.GetRandom(SpawnType.Enemy);
                            break;
                        case "near":
                        case "close":
                            float closestDist = -1f;
                            foreach (WayPoint wp in WayPoint.WayPointList)
                            {
                                if (wp.Submarine != null) { continue; }

                                // Don't spawn inside hulls
                                if (Hull.FindHull(wp.WorldPosition, null) != null) { continue; }

                                float dist = Vector2.Distance(wp.WorldPosition, GameMain.GameScreen.Cam.WorldViewCenter);

                                if (closestDist < 0f || dist < closestDist)
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
                            spawnPoint = WayPoint.GetRandom(isHuman ? SpawnType.Human : SpawnType.Enemy);
                            break;
                    }
                }
                else
                {
                    spawnPoint = WayPoint.GetRandom(isHuman ? SpawnType.Human : SpawnType.Enemy);
                }
                if (spawnPoint != null)
                {
                    spawnPosition = spawnPoint.WorldPosition;
                }

                argIndex++;
                if (args.Length > argIndex)
                {
                    if (int.TryParse(args[argIndex], out int teamID) && teamID is >= 0 and <= 3)
                    {
                        teamType = (CharacterTeamType)teamID;
                    }
                    else if (Enum.TryParse(args[argIndex], ignoreCase: true, out CharacterTeamType parsedTeamType))
                    {
                        teamType = parsedTeamType;
                    }
                    else
                    {
                        ThrowError($"\"{args[argIndex]}\" is not a valid team id.");
                    }
                }

                argIndex++;
                addToCrew = isHuman;
                if (args.Length > argIndex)
                {
                    if (bool.TryParse(args[argIndex], out bool result))
                    {
                        addToCrew = result;
                    }
                    else
                    {
                        ThrowError($"Could not parse the \"add to crew\" argument ({args[argIndex]}). Defaulting to {addToCrew}.");
                    }
                }
            }
        }

        private static IEnumerable<string> GetSpawnPosParams()
        {
            yield return "cursor";
            yield return "inventory";

#if SERVER
            if (GameMain.Server != null)
            {
                foreach (var clientName in GameMain.Server.ConnectedClients.Select(c => c.Name))
                {
                    yield return clientName;
                }
            }
#endif

            foreach (var characterName in Character.CharacterList.Where(c => c.Inventory != null).Select(c => c.Name).Distinct())
            {
                yield return characterName;
            }
        }

        private static IEnumerable<string> GetItemNameOrIdParams()
        {
            HashSet<string> seen = new HashSet<string>();

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (seen.Add(itemPrefab.Name.Value))
                {
                    yield return itemPrefab.Name.Value;
                }
            }
            
            seen.Clear();
            
            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (seen.Add(itemPrefab.Identifier.Value))
                {
                    yield return itemPrefab.Identifier.Value;
                }
            }
        }

        private static void TrySpawnItem(string[] args)
        {
            try
            {
#if CLIENT
                SpawnItem(args, Screen.Selected.Cam?.ScreenToWorld(PlayerInput.MousePosition) ?? PlayerInput.MousePosition, Character.Controlled, out string errorMsg);
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
                GameAnalyticsManager.AddErrorEventOnce("DebugConsole.SpawnItem:Error", GameAnalyticsManager.ErrorSeverity.Error, errorMsg + '\n' + e.Message + '\n' + e.StackTrace.CleanupStackTrace());
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
                (MapEntityPrefab.FindByName(itemNameOrId) ??
                MapEntityPrefab.FindByIdentifier(itemNameOrId.ToIdentifier())) as ItemPrefab;
            if (itemPrefab == null)
            {
                errorMsg = "Item \"" + itemNameOrId + "\" not found!";
                var matching = ItemPrefab.Prefabs.Find(me => me.Name.StartsWith(itemNameOrId, StringComparison.OrdinalIgnoreCase) && me is ItemPrefab);
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

            bool TryGetSpawnPosParam(out string spawnLocation, out int spawnLocationIndex)
            {
                var allSpawnPosParams = GetSpawnPosParams();
                spawnLocation = args.FirstOrDefault(s => allSpawnPosParams.Contains(s));
                spawnLocationIndex = spawnLocation != null ? args.IndexOf(spawnLocation) : -1;

                return spawnLocation != null;
            }

            int amount = 1;
            int conditionPrc = 100;
            
            if (TryGetSpawnPosParam(out string spawnLocation, out int spawnLocationIndex))
            {
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
                        var matchingCharacter = FindMatchingCharacter(args.Skip(1).Take(1).ToArray());
                        if (matchingCharacter != null){ spawnInventory = matchingCharacter.Inventory; }
                        break;
                }

                if (args.Length > spawnLocationIndex + 1)
                {
                    if (!int.TryParse(args[spawnLocationIndex + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out amount)) { amount = 1; }
                    amount = Math.Min(amount, 100);
                }
                
                if (args.Length > spawnLocationIndex + 2)
                {
                    if (!int.TryParse(args[^1], NumberStyles.Any, CultureInfo.InvariantCulture, out conditionPrc)) { conditionPrc = 100; }
                }
            }
            
            float itemCondition = itemPrefab.Health * Math.Clamp(conditionPrc / 100f, 0f, 1f);
            
            if ((spawnPos == null || spawnPos == Vector2.Zero) && spawnInventory == null)
            {
                var wp = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
            }

            for (int i = 0; i < amount; i++)
            {
                if (spawnPos != null)
                {
                    if (Entity.Spawner == null || Entity.Spawner.Removed)
                    {
                        new Item(itemPrefab, spawnPos.Value, null);
                    }
                    else
                    {
                        Entity.Spawner?.AddItemToSpawnQueue(itemPrefab, spawnPos.Value, condition: itemCondition);
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
                        Entity.Spawner?.AddItemToSpawnQueue(itemPrefab, spawnInventory, onSpawned: onItemSpawned);
                    }

                    void onItemSpawned(Item item)
                    {
                        if (item.ParentInventory?.Owner is Character character)
                        {
                            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                            {
                                wifiComponent.TeamID = character.TeamID;
                            }
                        }

                        item.Condition = item.Health * Math.Clamp(conditionPrc / 100f, 0f, 1f);
                    }
                }
            }
        }

        /// <summary>
        /// Throws the error in debug builds. In non-debug builds, logs it instead.
        /// Use for handling non-critical errors that shouldn't go unnoticed in debug builds (like warnings might), but which don't break the game and thus doesn't have to open the console.
        /// </summary>
        public static void AddSafeError(string error)
        {
#if DEBUG
            DebugConsole.ThrowError(error);
#else
            DebugConsole.LogError(error);
#endif
        }

        public static void LogError(string msg, Color? color = null, ContentPackage contentPackage = null)
        {
            msg = AddContentPackageInfoToMessage(msg, contentPackage);
            color ??= Color.Red;
            NewMessage(msg, color.Value, isCommand: false, isError: true);
        }

        public static void NewCommand(string command, Color? color = null)
        {
            color ??= Color.White;
            NewMessage(command, color.Value, isCommand: true, isError: false);
        }

        public static void NewMessage(LocalizedString msg, Color? color = null, bool debugOnly = false)
            => NewMessage(msg.Value, color, debugOnly);

        public static void NewMessage(string msg, Color? color = null, bool debugOnly = false)
        {
            color ??= Color.White;
            if (debugOnly)
            {
#if DEBUG
                NewMessage(msg, color.Value, isCommand: false, isError: false);
#endif
            }
            else
            {
                NewMessage(msg, color.Value, isCommand: false, isError: false);
            }
#if DEBUG && CLIENT
            Console.WriteLine(msg);
#endif
        }

        private static void NewMessage(string msg, Color color, bool isCommand, bool isError)
        {
            if (string.IsNullOrEmpty(msg)) { return; }

            var newMsg = new ColoredText(msg, color, isCommand, isError);
            queuedMessages.Enqueue(newMsg);
            MessageHandler.Invoke(newMsg);
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
                "   >>" + question, font: GUIStyle.SmallFont, wrap: true)
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

        public static Command FindCommand(string commandName) => commands.Find(c => c.Names.Contains(commandName.ToIdentifier()));

        public static void Log(LocalizedString message) => Log(message?.Value);
        
        public static void Log(string message)
        {
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                NewMessage(message, Color.Gray);
            }
        }

        public static void ThrowErrorLocalized(LocalizedString error, Exception e = null, ContentPackage contentPackage = null, bool createMessageBox = false, bool appendStackTrace = false)
        {
            ThrowError(error.Value, e, contentPackage, createMessageBox, appendStackTrace);
        }

        public static void ThrowError(string error, Exception e = null, ContentPackage contentPackage = null, bool createMessageBox = false, bool appendStackTrace = false)
        {
            error = AddContentPackageInfoToMessage(error, contentPackage);
#if CLIENT
            SteamTimelineManager.OnError(error, e);
#endif
            if (e != null)
            {
                error += " {" + e.Message + "}\n";
                if (e.StackTrace != null)
                {
                    error += e.StackTrace.CleanupStackTrace(); 
                }
                if (e.InnerException != null)
                {
                    var innermost = e.GetInnermost();
                    error += "\n\nInner exception: " + innermost.Message + "\n";
                    if (innermost.StackTrace != null)
                    {
                        error += innermost.StackTrace.CleanupStackTrace();
                    }
                }
            }
            else if (appendStackTrace && Environment.StackTrace != null)
            {
                error += "\n" + Environment.StackTrace.CleanupStackTrace();
            }
            System.Diagnostics.Debug.WriteLine($"ThrowError: {error}");

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

            LogError(error);
        }

        public static void ThrowErrorAndLogToGA(string gaIdentifier, string errorMsg)
        {
            ThrowError(errorMsg);
            GameAnalyticsManager.AddErrorEventOnce(
                gaIdentifier,
                GameAnalyticsManager.ErrorSeverity.Error,
                errorMsg);
        }

        private static readonly HashSet<string> loggedErrorIdentifiers = new HashSet<string>();
        /// <summary>
        /// Log the error message, but only if an error with the same identifier hasn't been thrown yet during this session.
        /// </summary>
        public static void ThrowErrorOnce(string identifier, string errorMsg, Exception e = null)
        {
            if (loggedErrorIdentifiers.Contains(identifier)) { return; }
            ThrowError(errorMsg, e);
            loggedErrorIdentifiers.Add(identifier);
        }

        public static void AddWarning(string warning, ContentPackage contentPackage = null)
        {
            warning = AddContentPackageInfoToMessage($"WARNING: {warning}", contentPackage);
            System.Diagnostics.Debug.WriteLine(warning);
            NewMessage(warning, Color.Yellow);
        }

        private static string AddContentPackageInfoToMessage(string message, ContentPackage contentPackage)
        {
            if (contentPackage == null) { return message; }
#if CLIENT
            string color = XMLExtensions.ToStringHex(Color.MediumPurple);
            return $"‖color:{color}‖[{contentPackage.Name}]‖color:end‖ {message}";
#else
            return $"[{contentPackage.Name}] {message}";
#endif
        }

#if CLIENT
        private static IEnumerable<CoroutineStatus> CreateMessageBox(string errorMsg)
        {
            new GUIMessageBox(TextManager.Get("Error"), errorMsg, minSize: new Point(GUI.IntScale(700), GUI.IntScale(500)));
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
                    Directory.CreateDirectory(SavePath, catchUnauthorizedAccessExceptions: false);
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
            var invalidChars = Path.GetInvalidFileNameCharsCrossPlatform();
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
                File.WriteAllLines(filePath + ".txt", unsavedMessages.Select(l => "[" + l.Time + "] " + l.Text), catchUnauthorizedAccessExceptions: false);
            }
            catch (Exception e)
            {
                unsavedMessages.Clear();
                ThrowError("Saving debug console log to " + filePath + " failed", e);
            }
        }
        
        private static void ToggleEnemyAITargetingRestrictions(EnemyTargetingRestrictions restrictions)
        {
            if (restrictions == EnemyTargetingRestrictions.None)
            {
                // If restriction is None, clear all restrictions
                EnemyAIController.TargetingRestrictions = EnemyTargetingRestrictions.None;
            }
            else
            {
                // Toggle the restriction
                if (EnemyAIController.TargetingRestrictions.HasFlag(restrictions))
                {
                    // If the restriction is already set, remove it
                    EnemyAIController.TargetingRestrictions &= ~restrictions;
                }
                else
                {
                    // If the restriction is not set, add it
                    EnemyAIController.TargetingRestrictions |= restrictions;
                }
            }

            NewMessage($"Monster targeting restrictions is now '{EnemyAIController.TargetingRestrictions}'", Color.Yellow);
        }

        public static void DeactivateCheats()
        {
#if CLIENT
            GameMain.DebugDraw = false;
            GameMain.LightManager.LightingEnabled = true;
            Character.DebugDrawInteract = false;
#endif
            Hull.EditWater = false;
            Hull.EditFire = false;
            EnemyAIController.DisableEnemyAI = false;
            HumanAIController.DisableCrewAI = false;
        }
    }
}

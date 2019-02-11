using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    enum SelectionMode
    {
        Manual = 0, Random = 1, Vote = 2
    }

    enum YesNoMaybe
    {
        No = 0, Maybe = 1, Yes = 2
    }

    enum BotSpawnMode
    {
        Normal, Fill
    }

    partial class GameServer : NetworkMember, ISerializableEntity
    {
        private class SavedClientPermission
        {
            public readonly string IP;
            public readonly ulong SteamID;
            public readonly string Name;
            public List<DebugConsole.Command> PermittedCommands;

            public ClientPermissions Permissions;

            public SavedClientPermission(string name, string ip, ClientPermissions permissions, List<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.IP = ip;

                this.Permissions = permissions;
                this.PermittedCommands = permittedCommands;
            }
            public SavedClientPermission(string name, ulong steamID, ClientPermissions permissions, List<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.SteamID = steamID;

                this.Permissions = permissions;
                this.PermittedCommands = permittedCommands;
            }
        }

        public const string SettingsFile = "serversettings.xml";
        public static readonly string PermissionPresetFile = "Data" + Path.DirectorySeparatorChar + "permissionpresets.xml";
        public static readonly string ClientPermissionsFile = "Data" + Path.DirectorySeparatorChar + "clientpermissions.xml";

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public Dictionary<ItemPrefab, int> extraCargo;

        public bool ShowNetStats;

        private TimeSpan refreshMasterInterval = new TimeSpan(0, 0, 30);
        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);

        private SelectionMode subSelectionMode, modeSelectionMode;

        private float selectedLevelDifficulty;

        private bool registeredToMaster;

        private WhiteList whitelist;
        private BanList banList;

        private string password;

        public float AutoRestartTimer;

        private bool autoRestart;

        private bool isPublic;

        private int maxPlayers;

        private bool randomPreferences;

        private List<SavedClientPermission> clientPermissions = new List<SavedClientPermission>();

        [Serialize(true, true)]
        public bool RandomizeSeed
        {
            get;
            set;
        }

        [Serialize(300.0f, true)]
        public float RespawnInterval
        {
            get;
            private set;
        }

        [Serialize(180.0f, true)]
        public float MaxTransportTime
        {
            get;
            private set;
        }

        [Serialize(0.2f, true)]
        public float MinRespawnRatio
        {
            get;
            private set;
        }
        
        [Serialize(60.0f, true)]
        public float AutoRestartInterval
        {
            get;
            set;
        }

        [Serialize(false, true)]
        public bool StartWhenClientsReady
        {
            get;
            private set;
        }

        [Serialize(0.8f, true)]
        public float StartWhenClientsReadyRatio
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool AllowSpectating
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool EndRoundAtLevelEnd
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool SaveServerLogs
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool AllowRagdollButton
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool AllowFileTransfers
        {
            get;
            private set;
        }

        [Serialize(800, true)]
        private int LinesPerLogFile
        {
            get
            {
                return ServerLog.LinesPerFile;
            }
            set
            {
                ServerLog.LinesPerFile = value;
            }
        }

        public bool AutoRestart
        {
            get { return autoRestart; }
            set
            {
                autoRestart = value;

                AutoRestartTimer = autoRestart ? AutoRestartInterval : 0.0f;
            }
        }

        [Serialize(true, true)]
        public bool AllowRespawn
        {
            get;
            set;
        }
        
        [Serialize(0, true)]
        public int BotCount
        {
            get;
            set;
        }

        [Serialize(16, true)]
        public int MaxBotCount
        {
            get;
            set;
        }
        
        public BotSpawnMode BotSpawnMode
        {
            get;
            set;
        }

        public float SelectedLevelDifficulty
        {
            get { return selectedLevelDifficulty; }
            set { selectedLevelDifficulty = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        [Serialize(true, true)]
        public bool AllowDisguises
        {
            get;
            set;
        }

        public YesNoMaybe TraitorsEnabled
        {
            get;
            set;
        }

        public SelectionMode SubSelectionMode
        {
            get { return subSelectionMode; }
        }

        public SelectionMode ModeSelectionMode
        {
            get { return modeSelectionMode; }
        }

        public BanList BanList
        {
            get { return banList; }
        }

        [Serialize(true, true)]
        public bool AllowVoteKick
        {
            get;
            private set;
        }

        [Serialize(0.6f, true)]
        public float EndVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(0.6f, true)]
        public float KickVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(30.0f, true)]
        public float KillDisconnectedTime
        {
            get;
            private set;
        }

        [Serialize(120.0f, true)]
        public float KickAFKTime
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool TraitorUseRatio
        {
            get;
            private set;
        }

        [Serialize(0.2f, true)]
        public float TraitorRatio
        {
            get;
            private set;
        }

        [Serialize(false, true)]
        public bool KarmaEnabled
        {
            get;
            set;
        }

        [Serialize(false, true)]
        public bool RandomPreferences
        {
            get;
            set;
        }
        
        [Serialize("sandbox", true)]
        public string GameModeIdentifier
        {
            get;
            set;
        }

        [Serialize("Random", true)]
        public string MissionType
        {
            get;
            set;
        }
        
        public int MaxPlayers
        {
            get { return maxPlayers; }
        }

        public List<MissionType> AllowedRandomMissionTypes
        {
            get;
            set;
        }

        [Serialize(60f, true)]
        public float AutoBanTime
        {
            get;
            private set;
        }

        [Serialize(360f, true)]
        public float MaxAutoBanTime
        {
            get;
            private set;
        }

        /// <summary>
        /// A list of int pairs that represent the ranges of UTF-16 codes allowed in client names
        /// </summary>
        public List<Pair<int, int>> AllowedClientNameChars
        {
            get;
            private set;
        } = new List<Pair<int, int>>();

        private void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            SerializableProperty.SerializeProperties(this, doc.Root, true);

            doc.Root.SetAttributeValue("name", name);
            doc.Root.SetAttributeValue("public", isPublic);
            doc.Root.SetAttributeValue("port", NetPeerConfiguration.Port);
            if (Steam.SteamManager.USE_STEAM) doc.Root.SetAttributeValue("queryport", QueryPort);
            doc.Root.SetAttributeValue("maxplayers", maxPlayers);
            doc.Root.SetAttributeValue("enableupnp", NetPeerConfiguration.EnableUPnP);

            doc.Root.SetAttributeValue("autorestart", autoRestart);

            doc.Root.SetAttributeValue("SubSelection", subSelectionMode.ToString());
            doc.Root.SetAttributeValue("ModeSelection", modeSelectionMode.ToString());
            doc.Root.SetAttributeValue("LevelDifficulty", ((int)selectedLevelDifficulty).ToString());
            doc.Root.SetAttributeValue("TraitorsEnabled", TraitorsEnabled.ToString());

            /*doc.Root.SetAttributeValue("BotCount", BotCount);
            doc.Root.SetAttributeValue("MaxBotCount", MaxBotCount);*/
            doc.Root.SetAttributeValue("BotSpawnMode", BotSpawnMode.ToString());
            
            doc.Root.SetAttributeValue("AllowedRandomMissionTypes", string.Join(",", AllowedRandomMissionTypes));

            doc.Root.SetAttributeValue("AllowedClientNameChars", string.Join(",", AllowedClientNameChars.Select(c => c.First + "-" + c.Second)));

#if SERVER
            doc.Root.SetAttributeValue("password", password);
#endif

            if (GameMain.NetLobbyScreen != null
#if CLIENT
                && GameMain.NetLobbyScreen.ServerMessage != null
#endif
                )
            {
                doc.Root.SetAttributeValue("ServerMessage", GameMain.NetLobbyScreen.ServerMessageText);
            }

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineOnAttributes = true;

            using (var writer = XmlWriter.Create(SettingsFile, settings))
            {
                doc.Save(writer);
            }
        }

        private void LoadSettings()
        {
            XDocument doc = null;
            if (File.Exists(SettingsFile))
            {
                doc = XMLExtensions.TryLoadXml(SettingsFile);
            }

            if (doc == null || doc.Root == null)
            {
                doc = new XDocument(new XElement("serversettings"));
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);

            AutoRestart = doc.Root.GetAttributeBool("autorestart", false);
#if CLIENT
            if (autoRestart)
            {
                GameMain.NetLobbyScreen.SetAutoRestart(autoRestart, AutoRestartInterval);
            }
#endif

            subSelectionMode = SelectionMode.Manual;
            Enum.TryParse(doc.Root.GetAttributeString("SubSelection", "Manual"), out subSelectionMode);
            Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;

            modeSelectionMode = SelectionMode.Manual;
            Enum.TryParse(doc.Root.GetAttributeString("ModeSelection", "Manual"), out modeSelectionMode);
            Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;

            selectedLevelDifficulty = doc.Root.GetAttributeFloat("LevelDifficulty", 20.0f);
            GameMain.NetLobbyScreen.SetLevelDifficulty(selectedLevelDifficulty);

            var traitorsEnabled = TraitorsEnabled;
            Enum.TryParse(doc.Root.GetAttributeString("TraitorsEnabled", "No"), out traitorsEnabled);
            TraitorsEnabled = traitorsEnabled;
            GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);
            
            var botSpawnMode = BotSpawnMode.Fill;
            Enum.TryParse(doc.Root.GetAttributeString("BotSpawnMode", "Fill"), out botSpawnMode);
            BotSpawnMode = botSpawnMode;

            //"65-90", "97-122", "48-59" = upper and lower case english alphabet and numbers
            string[] allowedClientNameCharsStr = doc.Root.GetAttributeStringArray("AllowedClientNameChars", new string[] { "32-33", "65-90", "97-122", "48-59" });
            foreach (string allowedClientNameCharRange in allowedClientNameCharsStr)
            {
                string[] splitRange = allowedClientNameCharRange.Split('-');
                if (splitRange.Length == 0 || splitRange.Length > 2)
                {
                    DebugConsole.ThrowError("Error in server settings - "+ allowedClientNameCharRange+" is not a valid range for characters allowed in client names.");
                    continue;
                }

                int min = -1;
                if (!int.TryParse(splitRange[0], out min))
                {
                    DebugConsole.ThrowError("Error in server settings - " + allowedClientNameCharRange + " is not a valid range for characters allowed in client names.");
                    continue;
                }
                int max = min;
                if (splitRange.Length == 2)
                {
                    if (!int.TryParse(splitRange[1], out max))
                    {
                        DebugConsole.ThrowError("Error in server settings - " + allowedClientNameCharRange + " is not a valid range for characters allowed in client names.");
                        continue;
                    }
                }

                if (min > -1 && max > -1) AllowedClientNameChars.Add(new Pair<int, int>(min, max));
            }

            AllowedRandomMissionTypes = new List<MissionType>();
            string[] allowedMissionTypeNames = doc.Root.GetAttributeStringArray(
                "AllowedRandomMissionTypes", Enum.GetValues(typeof(MissionType)).Cast<MissionType>().Select(m => m.ToString()).ToArray());
            foreach (string missionTypeName in allowedMissionTypeNames)
            {
                if (Enum.TryParse(missionTypeName, out MissionType missionType))
                {
                    if (missionType == Barotrauma.MissionType.None) continue;
                    AllowedRandomMissionTypes.Add(missionType);
                }
            }

            if (GameMain.NetLobbyScreen != null
#if CLIENT
                && GameMain.NetLobbyScreen.ServerMessage != null
#endif
                )
            {
#if SERVER
                GameMain.NetLobbyScreen.ServerName = doc.Root.GetAttributeString("name", "");
                GameMain.NetLobbyScreen.SelectedModeIdentifier = GameModeIdentifier;
                GameMain.NetLobbyScreen.MissionTypeName = MissionType;
#endif
                GameMain.NetLobbyScreen.ServerMessageText = doc.Root.GetAttributeString("ServerMessage", "");
            }

            GameMain.NetLobbyScreen.SetBotSpawnMode(BotSpawnMode);
            GameMain.NetLobbyScreen.SetBotCount(BotCount);

#if CLIENT
            showLogButton.Visible = SaveServerLogs;
#endif

            List<string> monsterNames = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
            for (int i = 0; i < monsterNames.Count; i++)
            {
                monsterNames[i] = Path.GetFileName(Path.GetDirectoryName(monsterNames[i]));
            }
            monsterEnabled = new Dictionary<string, bool>();
            foreach (string s in monsterNames)
            {
                if (!monsterEnabled.ContainsKey(s)) monsterEnabled.Add(s, true);
            }
            extraCargo = new Dictionary<ItemPrefab, int>();

            AutoBanTime = doc.Root.GetAttributeFloat("autobantime", 60);
            MaxAutoBanTime = doc.Root.GetAttributeFloat("maxautobantime", 360);
        }

        public void LoadClientPermissions()
        {
            clientPermissions.Clear();

            if (!File.Exists(ClientPermissionsFile))
            {
                if (File.Exists("Data/clientpermissions.txt"))
                {
                    LoadClientPermissionsOld("Data/clientpermissions.txt");
                }
                return;
            }

            XDocument doc = XMLExtensions.TryLoadXml(ClientPermissionsFile);
            foreach (XElement clientElement in doc.Root.Elements())
            {
                string clientName = clientElement.GetAttributeString("name", "");
                string clientIP = clientElement.GetAttributeString("ip", "");
                string steamIdStr = clientElement.GetAttributeString("steamid", "");

                if (string.IsNullOrWhiteSpace(clientName))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have a name and an IP address.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(clientIP) && string.IsNullOrWhiteSpace(steamIdStr))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have an IP address or a Steam ID.");
                    continue;
                }

                string permissionsStr = clientElement.GetAttributeString("permissions", "");
                ClientPermissions permissions = ClientPermissions.None;
                if (permissionsStr.ToLowerInvariant() == "all")
                {
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        permissions |= permission;
                    }
                }
                else if (!Enum.TryParse(permissionsStr, out permissions))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + permissionsStr + "\" is not a valid client permission.");
                    continue;
                }

                List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
                if (permissions.HasFlag(ClientPermissions.ConsoleCommands))
                {
                    foreach (XElement commandElement in clientElement.Elements())
                    {
                        if (commandElement.Name.ToString().ToLowerInvariant() != "command") continue;

                        string commandName = commandElement.GetAttributeString("name", "");
                        DebugConsole.Command command = DebugConsole.FindCommand(commandName);
                        if (command == null)
                        {
                            DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + commandName + "\" is not a valid console command.");
                            continue;
                        }

                        permittedCommands.Add(command);
                    }
                }

                if (!string.IsNullOrEmpty(steamIdStr))
                {
                    if (ulong.TryParse(steamIdStr, out ulong steamID))
                    {
                        clientPermissions.Add(new SavedClientPermission(clientName, steamID, permissions, permittedCommands));
                    }
                    else
                    {
                        DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + steamIdStr + "\" is not a valid Steam ID.");
                        continue;
                    }
                }
                else
                {
                    clientPermissions.Add(new SavedClientPermission(clientName, clientIP, permissions, permittedCommands));
                }
            }
        }

        /// <summary>
        /// Method for loading old .txt client permission files to provide backwards compatibility
        /// </summary>
        private void LoadClientPermissionsOld(string file)
        {
            if (!File.Exists(file)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to open client permission file " + ClientPermissionsFile, e);
                return;
            }

            clientPermissions.Clear();

            foreach (string line in lines)
            {
                string[] separatedLine = line.Split('|');
                if (separatedLine.Length < 3) continue;

                string name = string.Join("|", separatedLine.Take(separatedLine.Length - 2));
                string ip = separatedLine[separatedLine.Length - 2];

                ClientPermissions permissions = ClientPermissions.None;
                if (Enum.TryParse(separatedLine.Last(), out permissions))
                {
                    clientPermissions.Add(new SavedClientPermission(name, ip, permissions, new List<DebugConsole.Command>()));
                }
            }            
        }
        
        public void SaveClientPermissions()
        {
            //delete old client permission file
            if (File.Exists("Data/clientpermissions.txt"))
            {
                File.Delete("Data/clientpermissions.txt");
            }

            Log("Saving client permissions", ServerLog.MessageType.ServerMessage);

            XDocument doc = new XDocument(new XElement("ClientPermissions"));

            foreach (SavedClientPermission clientPermission in clientPermissions)
            {
                XElement clientElement = new XElement("Client", 
                    new XAttribute("name", clientPermission.Name),
                    new XAttribute("permissions", clientPermission.Permissions.ToString()));

                if (clientPermission.SteamID > 0)
                {
                    clientElement.Add(new XAttribute("steamid", clientPermission.SteamID));
                }
                else
                {
                    clientElement.Add(new XAttribute("ip", clientPermission.IP));
                }

                if (clientPermission.Permissions.HasFlag(ClientPermissions.ConsoleCommands))
                {
                    foreach (DebugConsole.Command command in clientPermission.PermittedCommands)
                    {
                        clientElement.Add(new XElement("command", new XAttribute("name", command.names[0])));
                    }
                }

                doc.Root.Add(clientElement);
            }

            try
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.NewLineOnAttributes = true;

                using (var writer = XmlWriter.Create(ClientPermissionsFile, settings))
                {
                    doc.Save(writer);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving client permissions to " + ClientPermissionsFile + " failed", e);
            }
        }
    }
}

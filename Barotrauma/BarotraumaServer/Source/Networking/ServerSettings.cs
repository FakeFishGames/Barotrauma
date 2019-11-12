using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    partial class ServerSettings
    {
        public static readonly string ClientPermissionsFile = "Data" + Path.DirectorySeparatorChar + "clientpermissions.xml";

        partial void InitProjSpecific()
        {
            LoadSettings();
            LoadClientPermissions();
        }

        private void WriteNetProperties(IWriteMessage outMsg)
        {
            outMsg.Write((UInt16)netProperties.Keys.Count);
            foreach (UInt32 key in netProperties.Keys)
            {
                outMsg.Write(key);
                netProperties[key].Write(outMsg);
            }
        }

        public void ServerAdminWrite(IWriteMessage outMsg, Client c)
        {
            //outMsg.Write(isPublic);
            //outMsg.Write(EnableUPnP);
            //outMsg.WritePadBits();
            //outMsg.Write((UInt16)QueryPort);

            WriteNetProperties(outMsg);
            WriteMonsterEnabled(outMsg);
            BanList.ServerAdminWrite(outMsg, c);
            Whitelist.ServerAdminWrite(outMsg, c);
        }

        public void ServerWrite(IWriteMessage outMsg, Client c)
        {
            outMsg.Write(ServerName);
            outMsg.Write(ServerMessageText);
            outMsg.Write((byte)MaxPlayers);
            outMsg.Write(HasPassword);
            outMsg.Write(isPublic);
            outMsg.WritePadBits();
            outMsg.WriteRangedInteger(TickRate, 1, 60);

            WriteExtraCargo(outMsg);

            Voting.ServerWrite(outMsg);

            if (c.HasPermission(Networking.ClientPermissions.ManageSettings))
            {
                outMsg.Write(true);
                outMsg.WritePadBits();

                ServerAdminWrite(outMsg, c);
            }
            else
            {
                outMsg.Write(false);
                outMsg.WritePadBits();
            }
        }

        public void ServerRead(IReadMessage incMsg, Client c)
        {
            if (!c.HasPermission(Networking.ClientPermissions.ManageSettings)) return;

            NetFlags flags = (NetFlags)incMsg.ReadByte();

            bool changed = false;
            
            if (flags.HasFlag(NetFlags.Name))
            {
                string serverName = incMsg.ReadString();
                if (ServerName != serverName) changed = true;
                ServerName = serverName;
            }
            
            if (flags.HasFlag(NetFlags.Message))
            {
                string serverMessageText = incMsg.ReadString();
                if (ServerMessageText != serverMessageText) changed = true;
                ServerMessageText = serverMessageText;
            }
                        
            if (flags.HasFlag(NetFlags.Properties))
            {
                changed |= ReadExtraCargo(incMsg);

                UInt32 count = incMsg.ReadUInt32();

                for (int i = 0; i < count; i++)
                {
                    UInt32 key = incMsg.ReadUInt32();

                    if (netProperties.ContainsKey(key))
                    {
                        object prevValue = netProperties[key].Value;
                        netProperties[key].Read(incMsg);
                        if (!netProperties[key].PropEquals(prevValue, netProperties[key]))
                        {
                            GameServer.Log(c.Name + " changed " + netProperties[key].Name + " to " + netProperties[key].Value.ToString(), ServerLog.MessageType.ServerMessage);
                        }
                        changed = true;
                    }
                    else
                    {
                        UInt32 size = incMsg.ReadVariableUInt32();
                        incMsg.BitPosition += (int)(8 * size);
                    }
                }

                bool changedMonsterSettings = incMsg.ReadBoolean(); incMsg.ReadPadBits();
                changed |= changedMonsterSettings;
                if (changedMonsterSettings) ReadMonsterEnabled(incMsg);
                changed |= BanList.ServerAdminRead(incMsg, c);
                changed |= Whitelist.ServerAdminRead(incMsg, c);
            }

            if (flags.HasFlag(NetFlags.Misc))
            {
                int orBits = incMsg.ReadRangedInteger(0, (int)Barotrauma.MissionType.All) & (int)Barotrauma.MissionType.All;
                int andBits = incMsg.ReadRangedInteger(0, (int)Barotrauma.MissionType.All) & (int)Barotrauma.MissionType.All;
                GameMain.NetLobbyScreen.MissionType = (Barotrauma.MissionType)(((int)GameMain.NetLobbyScreen.MissionType | orBits) & andBits);
                
                int traitorSetting = (int)TraitorsEnabled + incMsg.ReadByte() - 1;
                if (traitorSetting < 0) traitorSetting = 2;
                if (traitorSetting > 2) traitorSetting = 0;
                TraitorsEnabled = (YesNoMaybe)traitorSetting;

                int botCount = BotCount + incMsg.ReadByte() - 1;
                if (botCount < 0) botCount = MaxBotCount;
                if (botCount > MaxBotCount) botCount = 0;
                BotCount = botCount;

                int botSpawnMode = (int)BotSpawnMode + incMsg.ReadByte() - 1;
                if (botSpawnMode < 0) botSpawnMode = 1;
                if (botSpawnMode > 1) botSpawnMode = 0;
                BotSpawnMode = (BotSpawnMode)botSpawnMode;

                float levelDifficulty = incMsg.ReadSingle();
                if (levelDifficulty >= 0.0f) SelectedLevelDifficulty = levelDifficulty;

                UseRespawnShuttle = incMsg.ReadBoolean();

                bool changedAutoRestart = incMsg.ReadBoolean();
                bool autoRestart = incMsg.ReadBoolean();
                if (changedAutoRestart)
                {
                    AutoRestart = autoRestart;
                }

                changed |= true;
            }

            if (flags.HasFlag(NetFlags.LevelSeed))
            {
                GameMain.NetLobbyScreen.LevelSeed = incMsg.ReadString();
                changed |= true;
            }

            if (changed)
            {
                if (KarmaPreset == "custom")
                {
                    GameMain.NetworkMember?.KarmaManager?.SaveCustomPreset();
                    GameMain.NetworkMember?.KarmaManager?.Save();
                }
                GameMain.NetLobbyScreen.LastUpdateID++;
            }
        }

        public void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            SerializableProperty.SerializeProperties(this, doc.Root, true);

            doc.Root.SetAttributeValue("name", ServerName);
            doc.Root.SetAttributeValue("public", isPublic);
            doc.Root.SetAttributeValue("port", Port);
#if USE_STEAM
            doc.Root.SetAttributeValue("queryport", QueryPort);
#endif
            doc.Root.SetAttributeValue("maxplayers", maxPlayers);
            doc.Root.SetAttributeValue("enableupnp", EnableUPnP);

            doc.Root.SetAttributeValue("autorestart", autoRestart);
            
            doc.Root.SetAttributeValue("LevelDifficulty", ((int)selectedLevelDifficulty).ToString());
            
            doc.Root.SetAttributeValue("AllowedRandomMissionTypes", string.Join(",", AllowedRandomMissionTypes));
            doc.Root.SetAttributeValue("AllowedClientNameChars", string.Join(",", AllowedClientNameChars.Select(c => c.First + "-" + c.Second)));
            
            doc.Root.SetAttributeValue("ServerMessage", ServerMessageText);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            using (var writer = XmlWriter.Create(SettingsFile, settings))
            {
                doc.Save(writer);
            }

            if (KarmaPreset == "custom")
            {
                GameMain.Server?.KarmaManager?.SaveCustomPreset();
            }
            GameMain.Server?.KarmaManager?.Save();
        }

        private void LoadSettings()
        {
            XDocument doc = null;
            if (File.Exists(SettingsFile))
            {
                doc = XMLExtensions.TryLoadXml(SettingsFile);
            }

            if (doc == null)
            {
                doc = new XDocument(new XElement("serversettings"));
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);

            AutoRestart = doc.Root.GetAttributeBool("autorestart", false);
                        
            Voting.AllowSubVoting = SubSelectionMode == SelectionMode.Vote;            
            Voting.AllowModeVoting = ModeSelectionMode == SelectionMode.Vote;

            selectedLevelDifficulty = doc.Root.GetAttributeFloat("LevelDifficulty", 20.0f);
            GameMain.NetLobbyScreen.SetLevelDifficulty(selectedLevelDifficulty);
            
            GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);

            string[] defaultAllowedClientNameChars = 
                new string[] {
                    "32-33",
                    "38-46",
                    "48-57",
                    "65-90",
                    "91",
                    "93",
                    "95-122",
                    "192-255",
                    "384-591",
                    "1024-1279",
                    "19968-40959","13312-19903","131072-15043983","15043985-173791","173824-178207","178208-183983","63744-64255","194560-195103" //CJK
                };

            string[] allowedClientNameCharsStr = doc.Root.GetAttributeStringArray("AllowedClientNameChars", defaultAllowedClientNameChars);
            if (doc.Root.GetAttributeString("AllowedClientNameChars", "") == "65-90,97-122,48-59")
            {
                allowedClientNameCharsStr = defaultAllowedClientNameChars;
            }

            foreach (string allowedClientNameCharRange in allowedClientNameCharsStr)
            {
                string[] splitRange = allowedClientNameCharRange.Split('-');
                if (splitRange.Length == 0 || splitRange.Length > 2)
                {
                    DebugConsole.ThrowError("Error in server settings - " + allowedClientNameCharRange + " is not a valid range for characters allowed in client names.");
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

                if (min > -1 && max > -1) { AllowedClientNameChars.Add(new Pair<int, int>(min, max)); }
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

            ServerName = doc.Root.GetAttributeString("name", "");
            ServerMessageText = doc.Root.GetAttributeString("ServerMessage", "");
            
            GameMain.NetLobbyScreen.SelectedModeIdentifier = GameModeIdentifier;
            //handle Random as the mission type, which is no longer a valid setting
            //MissionType.All offers equivalent functionality
            if (MissionType == "Random") { MissionType = "All"; }
            GameMain.NetLobbyScreen.MissionTypeName = MissionType;

            GameMain.NetLobbyScreen.SetBotSpawnMode(BotSpawnMode);
            GameMain.NetLobbyScreen.SetBotCount(BotCount);

            List<string> monsterNames = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
            for (int i = 0; i < monsterNames.Count; i++)
            {
                monsterNames[i] = Path.GetFileName(Path.GetDirectoryName(monsterNames[i]));
            }
            MonsterEnabled = new Dictionary<string, bool>();
            foreach (string s in monsterNames)
            {
                if (!MonsterEnabled.ContainsKey(s)) MonsterEnabled.Add(s, true);
            }
        }

        public void LoadClientPermissions()
        {
            ClientPermissions.Clear();

            if (!File.Exists(ClientPermissionsFile))
            {
                if (File.Exists("Data/clientpermissions.txt"))
                {
                    LoadClientPermissionsOld("Data/clientpermissions.txt");
                }
                return;
            }

            XDocument doc = XMLExtensions.TryLoadXml(ClientPermissionsFile);
            if (doc == null) { return; }
            foreach (XElement clientElement in doc.Root.Elements())
            {
                string clientName = clientElement.GetAttributeString("name", "");
                string clientEndPoint = clientElement.GetAttributeString("endpoint", null) ?? clientElement.GetAttributeString("ip", "");
                string steamIdStr = clientElement.GetAttributeString("steamid", "");

                if (string.IsNullOrWhiteSpace(clientName))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have a name and an IP address.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(clientEndPoint) && string.IsNullOrWhiteSpace(steamIdStr))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have an IP address or a Steam ID.");
                    continue;
                }

                ClientPermissions permissions = Networking.ClientPermissions.None;
                List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();

                if (clientElement.Attribute("preset") == null)
                {
                    string permissionsStr = clientElement.GetAttributeString("permissions", "");
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

                    if (permissions.HasFlag(Networking.ClientPermissions.ConsoleCommands))
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
                }
                else
                {
                    string presetName = clientElement.GetAttributeString("preset", "");
                    PermissionPreset preset = PermissionPreset.List.Find(p => p.Name == presetName);
                    if (preset == null)
                    {
                        DebugConsole.ThrowError("Failed to restore saved permissions to the client \"" + clientName + "\". Permission preset \"" + presetName + "\" not found.");
                        return;
                    }
                    else
                    {
                        permissions = preset.Permissions;
                        permittedCommands = preset.PermittedCommands.ToList();
                    }
                }

                if (!string.IsNullOrEmpty(steamIdStr))
                {
                    if (ulong.TryParse(steamIdStr, out ulong steamID))
                    {
                        ClientPermissions.Add(new SavedClientPermission(clientName, steamID, permissions, permittedCommands));
                    }
                    else
                    {
                        DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + steamIdStr + "\" is not a valid Steam ID.");
                        continue;
                    }
                }
                else
                {
                    ClientPermissions.Add(new SavedClientPermission(clientName, clientEndPoint, permissions, permittedCommands));
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

            ClientPermissions.Clear();

            foreach (string line in lines)
            {
                string[] separatedLine = line.Split('|');
                if (separatedLine.Length < 3) continue;

                string name = string.Join("|", separatedLine.Take(separatedLine.Length - 2));
                string ip = separatedLine[separatedLine.Length - 2];

                ClientPermissions permissions = Networking.ClientPermissions.None;
                if (Enum.TryParse(separatedLine.Last(), out permissions))
                {
                    ClientPermissions.Add(new SavedClientPermission(name, ip, permissions, new List<DebugConsole.Command>()));
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
            
            GameServer.Log("Saving client permissions", ServerLog.MessageType.ServerMessage);

            XDocument doc = new XDocument(new XElement("ClientPermissions"));

            foreach (SavedClientPermission clientPermission in ClientPermissions)
            {
                var matchingPreset = PermissionPreset.List.Find(p => p.MatchesPermissions(clientPermission.Permissions, clientPermission.PermittedCommands));
                if (matchingPreset != null && matchingPreset.Name == "None")
                {
                    continue;
                }

                XElement clientElement = new XElement("Client",
                    new XAttribute("name", clientPermission.Name));

                if (clientPermission.SteamID > 0)
                {
                    clientElement.Add(new XAttribute("steamid", clientPermission.SteamID));
                }
                else
                {
                    clientElement.Add(new XAttribute("endpoint", clientPermission.EndPoint));
                }

                if (matchingPreset == null)
                {
                    clientElement.Add(new XAttribute("permissions", clientPermission.Permissions.ToString()));
                    if (clientPermission.Permissions.HasFlag(Networking.ClientPermissions.ConsoleCommands))
                    {
                        foreach (DebugConsole.Command command in clientPermission.PermittedCommands)
                        {
                            clientElement.Add(new XElement("command", new XAttribute("name", command.names[0])));
                        }
                    }
                }
                else
                {
                    clientElement.Add(new XAttribute("preset", matchingPreset.Name));
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

using Lidgren.Network;
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

        private void WriteNetProperties(NetBuffer outMsg)
        {
            outMsg.Write((UInt16)netProperties.Keys.Count);
            foreach (UInt32 key in netProperties.Keys)
            {
                outMsg.Write(key);
                netProperties[key].Write(outMsg);
            }
        }

        public void ServerAdminWrite(NetBuffer outMsg, Client c)
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

        public void ServerWrite(NetBuffer outMsg,Client c)
        {
            outMsg.Write(ServerName);
            outMsg.Write(ServerMessageText);
            outMsg.WriteRangedInteger(1, 60, TickRate);

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
        
        public void ServerRead(NetIncomingMessage incMsg,Client c)
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
                        netProperties[key].Read(incMsg);
                        GameServer.Log(c.Name + " changed " + netProperties[key].Name + " to " + netProperties[key].Value.ToString(), ServerLog.MessageType.ServerMessage);
                        changed = true;
                    }
                    else
                    {
                        UInt32 size = incMsg.ReadVariableUInt32();
                        incMsg.Position += 8 * size;
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
                int missionType = GameMain.NetLobbyScreen.MissionTypeIndex + incMsg.ReadByte() - 1;
                while (missionType < 0) missionType += Enum.GetValues(typeof(MissionType)).Length;
                while (missionType >= Enum.GetValues(typeof(MissionType)).Length) missionType -= Enum.GetValues(typeof(MissionType)).Length;
                GameMain.NetLobbyScreen.MissionTypeIndex = missionType;
                
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

                float levelDifficulty = incMsg.ReadFloat();
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

            if (changed) GameMain.NetLobbyScreen.LastUpdateID++;
        }

        public void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            SerializableProperty.SerializeProperties(this, doc.Root, true);

            doc.Root.SetAttributeValue("name", ServerName);
            doc.Root.SetAttributeValue("public", isPublic);
            doc.Root.SetAttributeValue("port", GameMain.Server.NetPeerConfiguration.Port);
            if (Steam.SteamManager.USE_STEAM) doc.Root.SetAttributeValue("queryport", QueryPort);
            doc.Root.SetAttributeValue("maxplayers", maxPlayers);
            doc.Root.SetAttributeValue("enableupnp", GameMain.Server.NetPeerConfiguration.EnableUPnP);

            doc.Root.SetAttributeValue("autorestart", autoRestart);

            doc.Root.SetAttributeValue("SubSelection", SubSelectionMode.ToString());
            doc.Root.SetAttributeValue("ModeSelection", ModeSelectionMode.ToString());
            doc.Root.SetAttributeValue("LevelDifficulty", ((int)selectedLevelDifficulty).ToString());
            doc.Root.SetAttributeValue("TraitorsEnabled", TraitorsEnabled.ToString());

            /*doc.Root.SetAttributeValue("BotCount", BotCount);
            doc.Root.SetAttributeValue("MaxBotCount", MaxBotCount);*/
            doc.Root.SetAttributeValue("BotSpawnMode", BotSpawnMode.ToString());

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

            if (doc == null || doc.Root == null)
            {
                doc = new XDocument(new XElement("serversettings"));
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);

            AutoRestart = doc.Root.GetAttributeBool("autorestart", false);
                        
            Voting.AllowSubVoting = SubSelectionMode == SelectionMode.Vote;            
            Voting.AllowModeVoting = ModeSelectionMode == SelectionMode.Vote;

            selectedLevelDifficulty = doc.Root.GetAttributeFloat("LevelDifficulty", 20.0f);
            GameMain.NetLobbyScreen.SetLevelDifficulty(selectedLevelDifficulty);

            var traitorsEnabled = TraitorsEnabled;
            Enum.TryParse(doc.Root.GetAttributeString("TraitorsEnabled", "No"), out traitorsEnabled);
            TraitorsEnabled = traitorsEnabled;
            GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);

            var botSpawnMode = BotSpawnMode.Normal;
            Enum.TryParse(doc.Root.GetAttributeString("BotSpawnMode", "Normal"), out botSpawnMode);
            BotSpawnMode = botSpawnMode;

            //"65-90", "97-122", "48-59" = upper and lower case english alphabet and numbers
            string[] allowedClientNameCharsStr = doc.Root.GetAttributeStringArray("AllowedClientNameChars", new string[] { "65-90", "97-122", "48-59" });
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

            ServerName = doc.Root.GetAttributeString("name", "");
            ServerMessageText = doc.Root.GetAttributeString("ServerMessage", "");
            
            GameMain.NetLobbyScreen.SelectedModeIdentifier = GameModeIdentifier;
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
                    ClientPermissions.Add(new SavedClientPermission(clientName, clientIP, permissions, permittedCommands));
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
                    clientElement.Add(new XAttribute("ip", clientPermission.IP));
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

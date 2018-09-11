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
        public const string SettingsFile = "serversettings.xml";
        public static readonly string PermissionPresetFile = "Data" + Path.DirectorySeparatorChar + "permissionpresets.xml";
        public static readonly string ClientPermissionsFile = "Data" + Path.DirectorySeparatorChar + "clientpermissions.xml";

        partial class NetPropertyData
        {
            public void Write(NetOutgoingMessage msg)
            {
                switch (typeString)
                {
                    case "float":
                        msg.Write((byte)4);
                        msg.Write((float)property.GetValue());
                        break;
                    case "vector2":
                        msg.Write((byte)8);
                        msg.Write(((Vector2)property.GetValue()).X);
                        msg.Write(((Vector2)property.GetValue()).Y);
                        break;
                    case "vector3":
                        msg.Write((byte)12);
                        msg.Write(((Vector3)property.GetValue()).X);
                        msg.Write(((Vector3)property.GetValue()).Y);
                        msg.Write(((Vector3)property.GetValue()).Z);
                        break;
                    case "vector4":
                        msg.Write((byte)16);
                        msg.Write(((Vector4)property.GetValue()).X);
                        msg.Write(((Vector4)property.GetValue()).Y);
                        msg.Write(((Vector4)property.GetValue()).Z);
                        msg.Write(((Vector4)property.GetValue()).W);
                        break;
                    case "color":
                        msg.Write((byte)4);
                        msg.Write(((Color)property.GetValue()).R);
                        msg.Write(((Color)property.GetValue()).G);
                        msg.Write(((Color)property.GetValue()).B);
                        msg.Write(((Color)property.GetValue()).A);
                        break;
                    case "rectangle":
                        msg.Write((byte)16);
                        msg.Write(((Rectangle)property.GetValue()).X);
                        msg.Write(((Rectangle)property.GetValue()).Y);
                        msg.Write(((Rectangle)property.GetValue()).Width);
                        msg.Write(((Rectangle)property.GetValue()).Height);
                        break;
                    default:
                        string strVal = property.GetValue().ToString();

                        //the length of a string can take a variable amount of bytes
                        //so we calculate how many they would be here
                        int headerLength = 1;
                        int strLen = strVal.Length;
                        while (strLen >= 0x80)
                        {
                            headerLength++;
                            strLen >>= 7;
                        }
                        msg.Write((byte)(strVal.Length + headerLength));
                        msg.Write(strVal);
                        break;
                }
            }
        }

        partial void InitProjSpecific()
        {
            LoadSettings();

            PermissionPreset.LoadAll(PermissionPresetFile);
            LoadClientPermissions();
        }

        public void ServerWrite(NetOutgoingMessage outMsg,Client c)
        {
            SharedWrite(outMsg);

            if (c.HasPermission(Networking.ClientPermissions.ManageSettings))
            {
                outMsg.Write(true);
                outMsg.Write(isPublic);
                outMsg.Write(EnableUPnP);
                outMsg.WritePadBits();
                outMsg.Write(QueryPort);

                Voting.ServerWrite(outMsg);

                foreach (UInt32 key in netProperties.Keys)
                {
                    outMsg.Write(key);
                    netProperties[key].Write(outMsg);
                }
            }
            else
            {
                outMsg.Write(false);
            }
        }

        public void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            SerializableProperty.SerializeProperties(this, doc.Root, true);

            doc.Root.SetAttributeValue("name", ServerName);
            doc.Root.SetAttributeValue("public", isPublic);
            doc.Root.SetAttributeValue("port", Port);
            if (Steam.SteamManager.USE_STEAM) doc.Root.SetAttributeValue("queryport", QueryPort);
            doc.Root.SetAttributeValue("maxplayers", maxPlayers);
            doc.Root.SetAttributeValue("enableupnp", EnableUPnP);

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

            doc.Root.SetAttributeValue("password", password);
            
            doc.Root.SetAttributeValue("ServerMessage", ServerMessageText);
        
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
            
#if SERVER
            GameMain.NetLobbyScreen.SelectedModeName = GameMode;
            GameMain.NetLobbyScreen.MissionTypeName = MissionType;
#endif

            GameMain.NetLobbyScreen.SetBotSpawnMode(BotSpawnMode);
            GameMain.NetLobbyScreen.SetBotCount(BotCount);

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

                string permissionsStr = clientElement.GetAttributeString("permissions", "");
                if (!Enum.TryParse(permissionsStr, out ClientPermissions permissions))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + permissionsStr + "\" is not a valid client permission.");
                    continue;
                }

                List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
                if (permissions.HasFlag(Barotrauma.Networking.ClientPermissions.ConsoleCommands))
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

                ClientPermissions permissions = Barotrauma.Networking.ClientPermissions.None;
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

#if SERVER
            GameServer.Log("Saving client permissions", ServerLog.MessageType.ServerMessage);
#endif

            XDocument doc = new XDocument(new XElement("ClientPermissions"));

            foreach (SavedClientPermission clientPermission in ClientPermissions)
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

                if (clientPermission.Permissions.HasFlag(Barotrauma.Networking.ClientPermissions.ConsoleCommands))
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
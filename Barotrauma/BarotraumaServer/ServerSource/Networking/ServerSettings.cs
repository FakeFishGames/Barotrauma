using Barotrauma.Extensions;
using Barotrauma.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    partial class ServerSettings
    {
        partial class NetPropertyData
        {
            private object lastSyncedValue;
            public UInt16 LastUpdateID { get; private set; }

            public void SyncValue()
            {
                if (!PropEquals(lastSyncedValue, Value))
                {
                    LastUpdateID = GameMain.NetLobbyScreen.LastUpdateID;
                    lastSyncedValue = Value;
                }
            }
            public void ForceUpdate()
            {
                LastUpdateID = GameMain.NetLobbyScreen.LastUpdateID++;
            }
        }
        
        public static readonly string ClientPermissionsFile = "Data" + Path.DirectorySeparatorChar + "clientpermissions.xml";
        public static readonly char SubmarineSeparatorChar = '|';

        public readonly Dictionary<NetFlags, UInt16> LastUpdateIdForFlag
            = ((NetFlags[])Enum.GetValues(typeof(NetFlags)))
                .Select(f => (f, (ushort)1))
                .ToDictionary();

        public void UpdateFlag(NetFlags flag)
            => LastUpdateIdForFlag[flag] = (UInt16)(GameMain.NetLobbyScreen.LastUpdateID + 1);

        public NetFlags UnsentFlags()
            => LastUpdateIdForFlag.Keys
                .Where(k => NetIdUtils.IdMoreRecent(LastUpdateIdForFlag[k], GameMain.NetLobbyScreen.LastUpdateID))
                .Aggregate(NetFlags.None, (f1, f2) => f1 | f2);

        private bool IsFlagRequired(Client c, NetFlags flag)
            => NetIdUtils.IdMoreRecent(LastUpdateIdForFlag[flag], c.LastRecvLobbyUpdate);
        
        public NetFlags GetRequiredFlags(Client c)
            => LastUpdateIdForFlag.Keys
                .Where(k => IsFlagRequired(c, k))
                .Aggregate(NetFlags.None, (f1, f2) => f1 | f2);

        partial void InitProjSpecific()
        {
            LoadSettings();
            LoadClientPermissions();
        }

        public void ForcePropertyUpdate()
        {
            UpdateFlag(NetFlags.Properties);
            foreach (NetPropertyData property in netProperties.Values)
            {
                property.ForceUpdate();
            }
        }

        private void WriteNetProperties(IWriteMessage outMsg, Client c)
        {
            foreach (UInt32 key in netProperties.Keys)
            {
                var property = netProperties[key];
                property.SyncValue();
                if (NetIdUtils.IdMoreRecent(property.LastUpdateID, c.LastRecvLobbyUpdate))
                {
                    outMsg.WriteUInt32(key);
                    netProperties[key].Write(outMsg);
                }
            }
            outMsg.WriteUInt32((UInt32)0);
        }

        public void ServerAdminWrite(IWriteMessage outMsg, Client c)
        {
            c.LastSentServerSettingsUpdate = LastUpdateIdForFlag[NetFlags.Properties];
            WriteNetProperties(outMsg, c);
            WriteMonsterEnabled(outMsg);
            BanList.ServerAdminWrite(outMsg, c);
        }

        public void ServerWrite(IWriteMessage outMsg, Client c)
        {
            NetFlags requiredFlags = GetRequiredFlags(c);
            outMsg.WriteByte((byte)requiredFlags);
            if (requiredFlags.HasFlag(NetFlags.Name))
            {
                outMsg.WriteString(ServerName);
            }

            if (requiredFlags.HasFlag(NetFlags.Message))
            {
                outMsg.WriteString(ServerMessageText);
            }
            outMsg.WriteByte((byte)PlayStyle);
            outMsg.WriteByte((byte)MaxPlayers);
            outMsg.WriteBoolean(HasPassword);
            outMsg.WriteBoolean(IsPublic);
            outMsg.WriteBoolean(AllowFileTransfers);
            outMsg.WritePadBits();
            outMsg.WriteRangedInteger(TickRate, 1, 60);

            if (requiredFlags.HasFlag(NetFlags.Properties))
            {
                WriteExtraCargo(outMsg);
            }

            if (requiredFlags.HasFlag(NetFlags.HiddenSubs))
            {
                WriteHiddenSubs(outMsg);
            }

            if (c.HasPermission(Networking.ClientPermissions.ManageSettings)
                && NetIdUtils.IdMoreRecent(
                    newID: LastUpdateIdForFlag[NetFlags.Properties],
                    oldID: c.LastRecvServerSettingsUpdate))
            {
                outMsg.WriteBoolean(true);
                outMsg.WritePadBits();

                ServerAdminWrite(outMsg, c);
            }
            else
            {
                outMsg.WriteBoolean(false);
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
                if (ServerName != serverName) { changed = true; }
                ServerName = serverName;
            }
            
            if (flags.HasFlag(NetFlags.Message))
            {
                string serverMessageText = incMsg.ReadString();
                if (ServerMessageText != serverMessageText) { changed = true; }
                ServerMessageText = serverMessageText;
            }
                        
            if (flags.HasFlag(NetFlags.Properties))
            {
                bool propertiesChanged = ReadExtraCargo(incMsg);

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
                            GameServer.Log(
                                NetworkMember.ClientLogName(c)
                                + $" changed {netProperties[key].Name}"
                                + $" to {netProperties[key].Value}",
                                ServerLog.MessageType.ServerMessage);
                        }
                        propertiesChanged = true;
                    }
                    else
                    {
                        UInt32 size = incMsg.ReadVariableUInt32();
                        incMsg.BitPosition += (int)(8 * size);
                    }
                }

                bool changedMonsterSettings = incMsg.ReadBoolean(); incMsg.ReadPadBits();
                propertiesChanged |= changedMonsterSettings;
                if (changedMonsterSettings) { ReadMonsterEnabled(incMsg); }
                propertiesChanged |= BanList.ServerAdminRead(incMsg, c);

                if (propertiesChanged)
                {
                    UpdateFlag(NetFlags.Properties);
                }
                changed |= propertiesChanged;
            }

            if (flags.HasFlag(NetFlags.HiddenSubs))
            {
                ReadHiddenSubs(incMsg);
                changed |= true;
                UpdateFlag(NetFlags.HiddenSubs);
            }
            
            if (flags.HasFlag(NetFlags.Misc))
            {
                int orBits = incMsg.ReadRangedInteger(0, (int)Barotrauma.MissionType.All) & (int)Barotrauma.MissionType.All;
                int andBits = incMsg.ReadRangedInteger(0, (int)Barotrauma.MissionType.All) & (int)Barotrauma.MissionType.All;
                GameMain.NetLobbyScreen.MissionType = (MissionType)(((int)GameMain.NetLobbyScreen.MissionType | orBits) & andBits);
                
                int traitorSetting = (int)TraitorsEnabled + incMsg.ReadByte() - 1;
                if (traitorSetting < 0) { traitorSetting = 2; }
                if (traitorSetting > 2) { traitorSetting = 0; }
                TraitorsEnabled = (YesNoMaybe)traitorSetting;

                int botCount = BotCount + incMsg.ReadByte() - 1;
                if (botCount < 0) { botCount = MaxBotCount; }
                if (botCount > MaxBotCount) { botCount = 0; }
                BotCount = botCount;

                int botSpawnMode = (int)BotSpawnMode + incMsg.ReadByte() - 1;
                if (botSpawnMode < 0) { botSpawnMode = 1; }
                if (botSpawnMode > 1) { botSpawnMode = 0; }
                BotSpawnMode = (BotSpawnMode)botSpawnMode;

                float levelDifficulty = incMsg.ReadSingle();
                if (levelDifficulty >= 0.0f) { SelectedLevelDifficulty = levelDifficulty; }

                bool changedUseRespawnShuttle = incMsg.ReadBoolean();
                bool useRespawnShuttle = incMsg.ReadBoolean();
                if (changedUseRespawnShuttle)
                {
                    UseRespawnShuttle = useRespawnShuttle;
                }

                bool changedAutoRestart = incMsg.ReadBoolean();
                bool autoRestart = incMsg.ReadBoolean();
                if (changedAutoRestart)
                {
                    AutoRestart = autoRestart;
                }

                changed |= true;
                UpdateFlag(NetFlags.Misc);
            }

            if (flags.HasFlag(NetFlags.LevelSeed))
            {
                GameMain.NetLobbyScreen.LevelSeed = incMsg.ReadString();
                changed |= true;
                UpdateFlag(NetFlags.LevelSeed);
            }

            if (changed)
            {
                if (KarmaPreset == "custom")
                {
                    GameMain.NetworkMember?.KarmaManager?.SaveCustomPreset();
                    GameMain.NetworkMember?.KarmaManager?.Save();
                }
                SaveSettings();
                GameMain.NetLobbyScreen.LastUpdateID++;
            }
        }

        public void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            doc.Root.SetAttributeValue("name", ServerName);
            doc.Root.SetAttributeValue("port", Port);
#if USE_STEAM
            doc.Root.SetAttributeValue("queryport", QueryPort);
#endif
            doc.Root.SetAttributeValue("password", password ?? "");

            doc.Root.SetAttributeValue("enableupnp", EnableUPnP);
            doc.Root.SetAttributeValue("autorestart", autoRestart);

            doc.Root.SetAttributeValue("LevelDifficulty", ((int)selectedLevelDifficulty).ToString());

            doc.Root.SetAttributeValue("ServerMessage", ServerMessageText);

            doc.Root.SetAttributeValue("HiddenSubs", string.Join(",", HiddenSubs));

            doc.Root.SetAttributeValue("AllowedRandomMissionTypes", string.Join(",", AllowedRandomMissionTypes));
            doc.Root.SetAttributeValue("AllowedClientNameChars", string.Join(",", AllowedClientNameChars.Select(c => $"{c.Start}-{c.End}")));

            SerializableProperty.SerializeProperties(this, doc.Root, true);
            doc.Root.Add(CampaignSettings.Save());

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            using (var writer = XmlWriter.Create(SettingsFile, settings))
            {
                doc.SaveSafe(writer);
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

            if (string.IsNullOrEmpty(doc.Root.GetAttributeString("losmode", "")))
            {
                LosMode = GameSettings.CurrentConfig.Graphics.LosMode;
            }
            if (string.IsNullOrEmpty(doc.Root.GetAttributeString("language", "")))
            {
                Language = ServerLanguageOptions.PickLanguage(GameSettings.CurrentConfig.Language);
            }

            AutoRestart = doc.Root.GetAttributeBool("autorestart", false);
                        
            AllowSubVoting = SubSelectionMode == SelectionMode.Vote;            
            AllowModeVoting = ModeSelectionMode == SelectionMode.Vote;

            selectedLevelDifficulty = doc.Root.GetAttributeFloat("LevelDifficulty", 20.0f);
            GameMain.NetLobbyScreen.SetLevelDifficulty(selectedLevelDifficulty);
            
            GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);

            HiddenSubs.UnionWith(doc.Root.GetAttributeStringArray("HiddenSubs", Array.Empty<string>()));
            if (HiddenSubs.Any())
            {
                UpdateFlag(NetFlags.HiddenSubs);
            }

            SelectedSubmarine = SelectNonHiddenSubmarine(SelectedSubmarine);

            string[] defaultAllowedClientNameChars = 
                new string[] 
                {
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
                    "19968-21327","21329-40959","13312-19903","131072-173791","173824-178207","178208-183983","63744-64255","194560-195103" //CJK
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

                if (min > max)
                {
                    //swap min and max
                    (min, max) = (max, min);
                }
                if (min > -1 && max > -1) { AllowedClientNameChars.Add(new Range<int>(min, max)); }
            }

            AllowedRandomMissionTypes = new List<MissionType>();
            string[] allowedMissionTypeNames = doc.Root.GetAttributeStringArray(
                "AllowedRandomMissionTypes", Enum.GetValues(typeof(MissionType)).Cast<MissionType>().Select(m => m.ToString()).ToArray());
            foreach (string missionTypeName in allowedMissionTypeNames)
            {
                if (Enum.TryParse(missionTypeName, out MissionType missionType))
                {
                    if (missionType == Barotrauma.MissionType.None) { continue; }
                    if (MissionPrefab.HiddenMissionClasses.Contains(missionType)) { continue; }
                    AllowedRandomMissionTypes.Add(missionType);
                }
            }

            ServerName = doc.Root.GetAttributeString("name", "");
            if (ServerName.Length > NetConfig.ServerNameMaxLength) { ServerName = ServerName.Substring(0, NetConfig.ServerNameMaxLength); }
            ServerMessageText = doc.Root.GetAttributeString("ServerMessage", "");

            GameMain.NetLobbyScreen.SelectedModeIdentifier = GameModeIdentifier;
            //handle Random as the mission type, which is no longer a valid setting
            //MissionType.All offers equivalent functionality
            if (MissionType == "Random") { MissionType = "All"; }
            GameMain.NetLobbyScreen.MissionTypeName = MissionType;

            GameMain.NetLobbyScreen.SetBotSpawnMode(BotSpawnMode);
            GameMain.NetLobbyScreen.SetBotCount(BotCount);

            MonsterEnabled ??= CharacterPrefab.Prefabs.Select(p => (p.Identifier, true)).ToDictionary();

            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name.ToIdentifier() == nameof(Barotrauma.CampaignSettings))
                {
                    CampaignSettings = new CampaignSettings(element);
                }
            }
        }

        public string SelectNonHiddenSubmarine(string current = null)
        {
            current ??= GameMain.NetLobbyScreen.SelectedSub.Name;
            if (HiddenSubs.Contains(current))
            {
                var candidates
                    = GameMain.NetLobbyScreen.GetSubList().Where(s => !HiddenSubs.Contains(s.Name)).ToArray();
                if (candidates.Any())
                {
                    GameMain.NetLobbyScreen.SelectedSub = candidates.GetRandom(Rand.RandSync.Unsynced);
                    return GameMain.NetLobbyScreen.SelectedSub.Name;
                }
                else
                {
                    HiddenSubs.Remove(current);
                    return current;
                }
            }
            return current;
        }
        
        public void LoadClientPermissions()
        {
            ClientPermissions.Clear();

            if (!File.Exists(ClientPermissionsFile)) { return; }

            XDocument doc = XMLExtensions.TryLoadXml(ClientPermissionsFile);
            if (doc == null) { return; }
            foreach (XElement clientElement in doc.Root.Elements())
            {
                string clientName = clientElement.GetAttributeString("name", "");
                string addressStr = clientElement.GetAttributeString("address", null)
                                    ?? clientElement.GetAttributeString("endpoint", null)
                                    ?? clientElement.GetAttributeString("ip", "");
                string accountIdStr = clientElement.GetAttributeString("accountid", null)
                                   ?? clientElement.GetAttributeString("steamid", "");

                if (string.IsNullOrWhiteSpace(clientName))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have a name.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(addressStr) && string.IsNullOrWhiteSpace(accountIdStr))
                {
                    DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - all clients must have an endpoint or a Steam ID.");
                    continue;
                }

                ClientPermissions permissions = Networking.ClientPermissions.None;
                HashSet<DebugConsole.Command> permittedCommands = new HashSet<DebugConsole.Command>();

                if (clientElement.Attribute("preset") == null)
                {
                    string permissionsStr = clientElement.GetAttributeString("permissions", "");
                    if (permissionsStr.Equals("all", StringComparison.OrdinalIgnoreCase))
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
                        permittedCommands = preset.PermittedCommands.ToHashSet();
                    }
                }

                if (permissions.HasFlag(Networking.ClientPermissions.ConsoleCommands))
                {
                    foreach (XElement commandElement in clientElement.Elements())
                    {
                        if (!commandElement.Name.ToString().Equals("command", StringComparison.OrdinalIgnoreCase)) { continue; }

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

                if (!string.IsNullOrEmpty(accountIdStr))
                {
                    if (AccountId.Parse(accountIdStr).TryUnwrap(out var accountId))
                    {
                        ClientPermissions.Add(new SavedClientPermission(clientName, accountId, permissions, permittedCommands));
                    }
                    else
                    {
                        DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + accountIdStr + "\" is not a valid account ID.");
                    }
                }
                else
                {
                    if (Address.Parse(addressStr).TryUnwrap(out var address))
                    {
                        ClientPermissions.Add(new SavedClientPermission(clientName, address, permissions, permittedCommands));
                    }
                    else
                    {
                        DebugConsole.ThrowError("Error in " + ClientPermissionsFile + " - \"" + addressStr + "\" is not a valid endpoint.");
                    }
                }
            }
        }

        public void SaveClientPermissions()
        {
            GameServer.Log("Saving client permissions", ServerLog.MessageType.ServerMessage);

            XDocument doc = new XDocument(new XElement("ClientPermissions"));

            foreach (SavedClientPermission clientPermission in ClientPermissions)
            {
                var matchingPreset = PermissionPreset.List.Find(p => p.MatchesPermissions(clientPermission.Permissions, clientPermission.PermittedCommands));
                #warning TODO: this is broken because of localization
                if (matchingPreset != null && matchingPreset.Name == "None")
                {
                    continue;
                }

                XElement clientElement = new XElement("Client",
                    new XAttribute("name", clientPermission.Name));

                clientElement.Add(clientPermission.AddressOrAccountId.TryGet(out AccountId accountId)
                    ? new XAttribute("accountid", accountId.StringRepresentation)
                    : new XAttribute("address", ((Address)clientPermission.AddressOrAccountId).StringRepresentation));

                clientElement.Add(matchingPreset == null
                    ? new XAttribute("permissions", clientPermission.Permissions.ToString())
                    : new XAttribute("preset", matchingPreset.Name));
                
                if (clientPermission.Permissions.HasFlag(Networking.ClientPermissions.ConsoleCommands))
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
                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                settings.Indent = true;
                settings.NewLineOnAttributes = true;

                using (var writer = XmlWriter.Create(ClientPermissionsFile, settings))
                {
                    doc.SaveSafe(writer);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving client permissions to " + ClientPermissionsFile + " failed", e);
            }
        }
    }
}

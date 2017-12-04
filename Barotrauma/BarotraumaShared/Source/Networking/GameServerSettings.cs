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

    partial class GameServer : NetworkMember, ISerializableEntity
    {
        private class SavedClientPermission
        {
            public readonly string IP;
            public readonly string Name;

            public ClientPermissions Permissions;

            public SavedClientPermission(string name, string ip, ClientPermissions permissions)
            {
                this.Name = name;
                this.IP = ip;

                this.Permissions = permissions;
            }
        }

        public const string SettingsFile = "serversettings.xml";
        public static readonly string ClientPermissionsFile = "Data" + Path.DirectorySeparatorChar + "clientpermissions.txt";

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }
        
        public Dictionary<string, int> extraCargo;

        public bool ShowNetStats;
        //Nil Mod Diagnostics
        public bool ShowLagDiagnostics;

        private TimeSpan refreshMasterInterval = new TimeSpan(0, 0, 30);
        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);

        private SelectionMode subSelectionMode, modeSelectionMode;
        
        private bool registeredToMaster;

        private WhiteList whitelist;
        private BanList banList;

        private string password;

        private string adminAuthPass = "";
        public string AdminAuthPass
        {
            set
            {
                DebugConsole.NewMessage("Admin auth pass changed!",Color.Yellow);
                adminAuthPass = "";
                if (value.Length > 0)
                {
                    adminAuthPass = Encoding.UTF8.GetString(Lidgren.Network.NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(value)));
                }
            }
        }

        public float AutoRestartTimer;
        
        private bool autoRestart;

        private bool isPublic;

        private int maxPlayers;

        private List<SavedClientPermission> clientPermissions = new List<SavedClientPermission>();
        
        [Serialize(true, true)]
        public bool RandomizeSeed
        {
            get;
            private set;
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
                return log.LinesPerFile;
            }
            set
            {
                log.LinesPerFile = value;
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

        private void SaveSettings()
        {
            XDocument doc = new XDocument(new XElement("serversettings"));

            SerializableProperty.SerializeProperties(this, doc.Root, true);
            
            doc.Root.SetAttributeValue("name", name);
            doc.Root.SetAttributeValue("public", isPublic);
            doc.Root.SetAttributeValue("port", config.Port);
            doc.Root.SetAttributeValue("maxplayers", maxPlayers);
            doc.Root.SetAttributeValue("enableupnp", config.EnableUPnP);

            doc.Root.SetAttributeValue("autorestart", autoRestart);

            doc.Root.SetAttributeValue("SubSelection", subSelectionMode.ToString());
            doc.Root.SetAttributeValue("ModeSelection", modeSelectionMode.ToString());
            
            doc.Root.SetAttributeValue("TraitorsEnabled", TraitorsEnabled.ToString());

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
            Enum.TryParse<SelectionMode>(doc.Root.GetAttributeString("SubSelection", "Manual"), out subSelectionMode);
            Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;

            modeSelectionMode = SelectionMode.Manual;
            Enum.TryParse<SelectionMode>(doc.Root.GetAttributeString("ModeSelection", "Manual"), out modeSelectionMode);
            Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;

            var traitorsEnabled = TraitorsEnabled;
            Enum.TryParse<YesNoMaybe>(doc.Root.GetAttributeString("TraitorsEnabled", "No"), out traitorsEnabled);
            TraitorsEnabled = traitorsEnabled;
            GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);
            
            if (GameMain.NetLobbyScreen != null
#if CLIENT
                && GameMain.NetLobbyScreen.ServerMessage != null
#endif
                )
            {
                GameMain.NetLobbyScreen.ServerMessageText = doc.Root.GetAttributeString("ServerMessage", "");
            }

#if CLIENT
            showLogButton.Visible = SaveServerLogs;
#endif

            List<string> monsterNames = Directory.GetDirectories("Content/Characters").ToList();
            for (int i = 0; i < monsterNames.Count; i++)
            {
                monsterNames[i] = monsterNames[i].Replace("Content/Characters", "").Replace("/", "").Replace("\\", "");
            }
            monsterEnabled = new Dictionary<string, bool>();
            foreach (string s in monsterNames)
            {
                monsterEnabled.Add(s, true);
            }
            extraCargo = new Dictionary<string, int>();
        }

        public void LoadClientPermissions()
        {
            if (!File.Exists(ClientPermissionsFile)) return;
            
            string[] lines;
            try
            {
                lines = File.ReadAllLines(ClientPermissionsFile);
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

                string name = String.Join("|", separatedLine.Take(separatedLine.Length - 2));
                string ip = separatedLine[separatedLine.Length - 2];

                ClientPermissions permissions = ClientPermissions.None;
                if (Enum.TryParse<ClientPermissions>(separatedLine.Last(), out permissions))
                {
                    clientPermissions.Add(new SavedClientPermission(name, ip, permissions));
                }
            }            
        }
        
        public void SaveClientPermissions()
        {
            GameServer.Log("Saving client permissions", ServerLog.MessageType.ServerMessage);

            List<string> lines = new List<string>();

            foreach (SavedClientPermission clientPermission in clientPermissions)
            {
                lines.Add(clientPermission.Name + "|" + clientPermission.IP+"|"+clientPermission.Permissions.ToString());
            }

            try
            {
                File.WriteAllLines(ClientPermissionsFile, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving client permissions to " + ClientPermissionsFile + " failed", e);
            }
        }
    }
}
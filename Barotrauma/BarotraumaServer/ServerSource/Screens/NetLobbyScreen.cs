using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private SubmarineInfo selectedSub;
        private SubmarineInfo selectedShuttle;

        public SubmarineInfo SelectedSub
        {
            get { return selectedSub; }
            set
            {
                selectedSub = value;
                lastUpdateID++;
                if (GameMain.NetworkMember?.ServerSettings != null)
                {
                    GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
                }
            }
        }
        public SubmarineInfo SelectedShuttle
        {
            get { return selectedShuttle; }
            set { selectedShuttle = value; lastUpdateID++; }
        }

        public List<SubmarineInfo> CampaignSubmarines
        {
            get
            {
                return campaignSubmarines;
            }
            set
            {
                campaignSubmarines = value;
                lastUpdateID++;
                if (GameMain.NetworkMember?.ServerSettings != null)
                {
                    GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
                }
            }
        }

        private List<SubmarineInfo> campaignSubmarines;       

        public void AddCampaignSubmarine(SubmarineInfo sub)
        {
            if (!campaignSubmarines.Contains(sub))
            {
                campaignSubmarines.Add(sub);
            }
            else
            {
                return;
            }

            lastUpdateID++;
            if (GameMain.NetworkMember?.ServerSettings != null)
            {
                GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
            }
        }

        public void RemoveCampaignSubmarine(SubmarineInfo sub)
        {
            if (campaignSubmarines.Contains(sub))
            {
                campaignSubmarines.Remove(sub);
            }
            else
            {
                return;
            }

            lastUpdateID++;
            if (GameMain.NetworkMember?.ServerSettings != null)
            {
                GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
            }
        }

        public GameModePreset[] GameModes { get; }

        private int selectedModeIndex;
        public int SelectedModeIndex
        {
            get { return selectedModeIndex; }
            set
            {
                lastUpdateID++;
                selectedModeIndex = MathHelper.Clamp(value, 0, GameModes.Length - 1);
                if (SelectedMode != GameModePreset.MultiPlayerCampaign && GameMain.GameSession?.GameMode is CampaignMode && Selected == this)
                {
                    GameMain.GameSession = null;
                }
                if (GameMain.NetworkMember?.ServerSettings != null)
                {
                    GameMain.NetworkMember.ServerSettings.GameModeIdentifier = SelectedModeIdentifier;
                    GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
                }
            }
        }

        public string SelectedModeIdentifier
        {
            get { return GameModes[SelectedModeIndex].Identifier; }
            set
            {
                for (int i = 0; i < GameModes.Length; i++)
                {
                    if (GameModes[i].Identifier.ToLower() == value.ToLower())
                    {
                        SelectedModeIndex = i;
                        break;
                    }
                }
                if (GameMain.NetworkMember?.ServerSettings != null)
                {
                    GameMain.NetworkMember.ServerSettings.GameModeIdentifier = SelectedModeIdentifier;
                    GameMain.NetworkMember.ServerSettings.ServerDetailsChanged = true;
                }
            }
        }

        public GameModePreset SelectedMode
        {
            get { return GameModes[SelectedModeIndex]; }
        }

        private MissionType missionType;
        public MissionType MissionType
        {
            get { return missionType; }
            set
            {
                lastUpdateID++;
                missionType = value;
                if (GameMain.NetworkMember?.ServerSettings != null)
                {
                    GameMain.NetworkMember.ServerSettings.MissionType = missionType.ToString();
                }
            }
        }

        public string MissionTypeName
        {
            get { return missionType.ToString(); }
            set
            {
                Enum.TryParse(value, out MissionType type);
                MissionType = type;
            }
        }

        public void ChangeServerName(string n)
        {
            GameMain.Server.ServerSettings.ServerName = n; lastUpdateID++;
        }

        public void ChangeServerMessage(string m)
        {
            GameMain.Server.ServerSettings.ServerMessageText = m; lastUpdateID++;
        }
        
        public List<JobPrefab> JobPreferences
        {
            get
            {
                return null;
            }
        }

        public NetLobbyScreen()
        {
            LevelSeed = ToolBox.RandomSeed(8);

            subs = SubmarineInfo.SavedSubmarines.Where(s => s.Type == SubmarineType.Player && !s.HasTag(SubmarineTag.HideInMenus)).ToList();

            if (subs == null || subs.Count() == 0)
            {
                throw new Exception("No submarines are available.");
            }

            selectedSub = subs.FirstOrDefault(s => !s.HasTag(SubmarineTag.Shuttle));
            if (selectedSub == null)
            {
                //no subs available, use a shuttle
                DebugConsole.ThrowError("No full-size submarines available - choosing a shuttle as the main submarine.");
                selectedSub = subs[0];
            }

            selectedShuttle = subs.First(s => s.HasTag(SubmarineTag.Shuttle));
            if (selectedShuttle == null)
            {
                //no shuttles available, use a sub
                DebugConsole.ThrowError("No shuttles available - choosing a full-size submarine as the shuttle.");
                selectedShuttle = subs[0];
            }

            DebugConsole.NewMessage("Selected sub: " + SelectedSub.Name, Color.White);
            DebugConsole.NewMessage("Selected shuttle: " + SelectedShuttle.Name, Color.White);

            GameModes = GameModePreset.List.ToArray();
        }
        
        private List<SubmarineInfo> subs;
        public List<SubmarineInfo> GetSubList()
        {
            return subs;
        }

        public string LevelSeed
        {
            get
            {
                return levelSeed;
            }
            set
            {
                if (levelSeed == value) return;

                lastUpdateID++;
                levelSeed = value;
                LocationType.Random(new MTRandom(ToolBox.StringToInt(levelSeed))); //call to sync up with clients
            }
        }
        
        public void ToggleCampaignMode(bool enabled)
        {
            for (int i = 0; i < GameModes.Length; i++)
            {
                if ((GameModes[i] == GameModePreset.MultiPlayerCampaign) == enabled)
                {
                    selectedModeIndex = i;
                    break;
                }
            }

            lastUpdateID++;
        }

        public override void Select()
        {
            base.Select();
            GameMain.Server.ServerSettings.Voting.ResetVotes(GameMain.Server.ConnectedClients);
            if (SelectedMode != GameModePreset.MultiPlayerCampaign && GameMain.GameSession?.GameMode is CampaignMode && Selected == this)
            {
                GameMain.GameSession = null;
            }
        }

        public void RandomizeSettings()
        {
            if (GameMain.Server.ServerSettings.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);

            if (GameMain.Server.ServerSettings.SubSelectionMode == SelectionMode.Random)
            {
                var nonShuttles = SubmarineInfo.SavedSubmarines.Where(c => !c.HasTag(SubmarineTag.Shuttle) && !c.HasTag(SubmarineTag.HideInMenus)).ToList();
                SelectedSub = nonShuttles[Rand.Range(0, nonShuttles.Count)];
            }
            if (GameMain.Server.ServerSettings.ModeSelectionMode == SelectionMode.Random)
            {
                var allowedGameModes = Array.FindAll(GameModes, m => !m.IsSinglePlayer && m != GameModePreset.MultiPlayerCampaign);
                SelectedModeIdentifier = allowedGameModes[Rand.Range(0, allowedGameModes.Length)].Identifier;
            }
        }
    }
}

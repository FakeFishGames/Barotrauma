using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private Submarine selectedSub;
        private Submarine selectedShuttle;
        private bool usingShuttle = true;

        public Submarine SelectedSub
        {
            get { return selectedSub; }
            set { selectedSub = value; lastUpdateID++; }
        }
        public Submarine SelectedShuttle
        {
            get { return selectedShuttle; }
            set { selectedShuttle = value; lastUpdateID++; }
        }
        public bool UsingShuttle
        {
            get { return usingShuttle; }
            set { usingShuttle = value; lastUpdateID++; }
        }

        private GameModePreset[] gameModes;
        public GameModePreset[] GameModes
        {
            get { return gameModes; }
        }

        private int selectedModeIndex;
        public int SelectedModeIndex
        {
            get { return selectedModeIndex; }
            set
            {
                lastUpdateID++;
                selectedModeIndex = MathHelper.Clamp(value, 0, gameModes.Length - 1);
            }
        }

        public string SelectedModeIdentifier
        {
            get { return gameModes[SelectedModeIndex].Identifier; }
            set
            {
                for (int i = 0; i < gameModes.Length; i++)
                {
                    if (gameModes[i].Identifier.ToLower() == value.ToLower())
                    {
                        SelectedModeIndex = i;
                        break;
                    }
                }
            }
        }

        public GameModePreset SelectedMode
        {
            get { return gameModes[SelectedModeIndex]; }
        }

        private int missionTypeIndex;
        public int MissionTypeIndex
        {
            get { return missionTypeIndex; }
            set
            {
                lastUpdateID++;
                missionTypeIndex = MathHelper.Clamp(value, 0, Enum.GetValues(typeof(MissionType)).Length - 1);
            }
        }

        public string MissionTypeName
        {
            get { return ((MissionType)missionTypeIndex).ToString(); }
            set
            {
                if (Enum.TryParse(value, out MissionType missionType))
                {
                    missionTypeIndex = (int)missionType;
                }
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

            subs = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList();

            if (subs == null || subs.Count() == 0)
            {
                throw new Exception("No submarines are available.");
            }

            selectedSub = subs.First(s => !s.HasTag(SubmarineTag.Shuttle));
            selectedShuttle = subs.First(s => s.HasTag(SubmarineTag.Shuttle));

            DebugConsole.NewMessage("Selected sub: " + SelectedSub.Name, Color.White);
            DebugConsole.NewMessage("Selected shuttle: " + SelectedShuttle.Name, Color.White);

            gameModes = GameModePreset.List.ToArray();
        }
        
        private List<Submarine> subs;
        public List<Submarine> GetSubList()
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
                LocationType.Random(levelSeed); //call to sync up with clients
            }
        }
        
        public void ToggleCampaignMode(bool enabled)
        {
            for (int i = 0; i < GameModes.Length; i++)
            {
                if ((GameModes[i].Identifier == "multiplayercampaign") == enabled)
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
        }

        public void RandomizeSettings()
        {
            if (GameMain.Server.ServerSettings.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);

            if (GameMain.Server.ServerSettings.SubSelectionMode == SelectionMode.Random)
            {
                var nonShuttles = Submarine.SavedSubmarines.Where(c => !c.HasTag(SubmarineTag.Shuttle) && !c.HasTag(SubmarineTag.HideInMenus)).ToList();
                SelectedSub = nonShuttles[Rand.Range(0, nonShuttles.Count)];
            }
            if (GameMain.Server.ServerSettings.ModeSelectionMode == SelectionMode.Random)
            {
                var allowedGameModes = Array.FindAll(gameModes, m => !m.IsSinglePlayer && m.Identifier != "multiplayercampaign");
                SelectedModeIdentifier = allowedGameModes[Rand.Range(0, allowedGameModes.Length)].Identifier;
            }
        }
    }
}

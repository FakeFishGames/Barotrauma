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

        public string SelectedModeName
        {
            get { return gameModes[SelectedModeIndex].Name; }
            set
            {
                for (int i = 0; i < gameModes.Count(); i++)
                {
                    if (gameModes[i].Name.ToLower() == value.ToLower())
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

        public string ServerMessageText;

        private int missionTypeIndex;
        public int MissionTypeIndex
        {
            get { return missionTypeIndex; }
            set {
                lastUpdateID++;
                missionTypeIndex = Math.Max(0, Math.Min(Mission.MissionTypes.Count()-1, value));
            }
        }

        public string MissionTypeName
        {
            get { return Mission.MissionTypes[MissionTypeIndex]; }
            set
            {
                for (int i = 0; i < Mission.MissionTypes.Count(); i++)
                {
                    if (Mission.MissionTypes[i].ToLower() == value.ToLower())
                    {
                        MissionTypeIndex = i;
                        break;
                    }
                }
            }
        }
        
        public void ChangeServerName(string n)
        {
            ServerName = n; lastUpdateID++;
        }

        public void ChangeServerMessage(string m)
        {
            ServerMessageText = m; lastUpdateID++;
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

            if (subs == null || subs.Count()==0)
            {
                throw new Exception("No submarines are available.");
            }

            selectedSub = subs.First(s => !s.HasTag(SubmarineTag.Shuttle));
            selectedShuttle = subs.First(s => s.HasTag(SubmarineTag.Shuttle));

            DebugConsole.NewMessage("Selected sub: " + SelectedSub.Name, Color.White);
            DebugConsole.NewMessage("Selected shuttle: " + SelectedShuttle.Name, Color.White);

            gameModes = GameModePreset.list.ToArray();
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

        public bool StartButtonEnabled
        {
            get { return true; }
            set { /* do nothing */ }
        }

        public void ToggleCampaignMode(bool enabled)
        {
            for (int i = 0; i < GameModes.Length; i++)
            {
                if ((GameModes[i].Name == "Campaign") == enabled)
                {
                    selectedModeIndex = i;
                    break;
                }
            }

            lastUpdateID++;
        }
    }
}

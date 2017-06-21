using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        public Submarine SelectedSub;
        public Submarine SelectedShuttle;

        private GameModePreset[] GameModes;
        public int SelectedModeIndex;
        public GameModePreset SelectedMode
        {
            get { return GameModes[SelectedModeIndex]; }
        }

        public string ServerMessageText;

        public int MissionTypeIndex;

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

            SelectedSub = subs[0];
            SelectedShuttle = subs[0]; //TODO: don't use the same sub as a shuttle by default

            DebugConsole.NewMessage("Selected sub: " + SelectedSub.Name, Color.White);
            DebugConsole.NewMessage("Selected shuttle: " + SelectedShuttle.Name, Color.White);

            GameModes = GameModePreset.list.ToArray();
        }

        public override void Select()
        {

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

                levelSeed = value;
                LocationType.Random(levelSeed); //call to sync up with clients
            }
        }

        public bool StartButtonEnabled
        {
            get { return true; }
            set { /* do nothing */ }
        }
    }
}

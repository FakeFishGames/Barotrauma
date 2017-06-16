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

        private List<Submarine> subs = new List<Submarine>();
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
            }
        }

        public bool StartButtonEnabled
        {
            get { return true; }
            set { /* do nothing */ }
        }
    }
}

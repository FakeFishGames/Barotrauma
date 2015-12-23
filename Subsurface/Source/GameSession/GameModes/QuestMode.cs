using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class MissionMode : GameMode
    {
        Mission mission;

        public override Mission Mission
        {
            get
            {
                return mission;
            }
        }

        public MissionMode(GameModePreset preset)
            : base(preset)
        {
            Location[] locations = new Location[2];

            Random rand = new Random(ToolBox.StringToInt(GameMain.NetLobbyScreen.LevelSeed));

            for (int i = 0; i < 2; i++)
            {
                locations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f));
            }
            mission = Mission.LoadRandom(locations, rand);
        }

        public override void Start()
        {
            base.Start();

            new GUIMessageBox(mission.Name, mission.Description, 400, 400);

            //quest.Start(Level.Loaded);
        }

        public override void End(string endMessage = "")
        {
            //quest.End();

            base.End(endMessage);
        }
    }
}

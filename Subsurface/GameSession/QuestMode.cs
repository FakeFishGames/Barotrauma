using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class QuestMode : GameMode
    {
        Quest quest;

        public override Quest Quest
        {
            get
            {
                return quest;
            }
        }

        public QuestMode(GameModePreset preset)
            : base(preset)
        {
            Location[] locations = new Location[2];

            Random rand = new Random(Game1.NetLobbyScreen.LevelSeed.GetHashCode());

            for (int i = 0; i < 2; i++)
            {
                locations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f));
            }
            quest = Quest.LoadRandom(locations, rand);
        }

        public override void Start(TimeSpan duration)
        {
            base.Start(duration);

            new GUIMessageBox(quest.Name, quest.Description, 400, 400);

            quest.Start(Level.Loaded);
        }

        public override void End(string endMessage = "")
        {
            quest.End();

            base.End(endMessage);
        }
    }
}

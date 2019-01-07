using System;
using System.Collections.Generic;
using System.Reflection;

namespace Barotrauma
{
    class GameModePreset
    {
        public static List<GameModePreset> list = new List<GameModePreset>();

        public ConstructorInfo Constructor;
        public string Name;

        public bool IsSinglePlayer
        {
            get;
            private set;
        }

        //are clients allowed to vote for this gamemode
        public bool Votable
        {
            get;
            private set;
        }

        public string Description
        {
            get;
            private set;
        }

        public GameModePreset(string name, Type type, bool isSinglePlayer = false, bool votable = true)
        {
            this.Name = name;

            Constructor = type.GetConstructor(new Type[] { typeof(GameModePreset), typeof(object) });

            IsSinglePlayer = isSinglePlayer;
            Votable = votable;

            list.Add(this);
        }

        public GameMode Instantiate(object param)
        {
            object[] lobject = new object[] { this, param };
            return (GameMode)Constructor.Invoke(lobject);
        }

        public static void Init()
        {
#if CLIENT
            new GameModePreset("Single Player", typeof(SinglePlayerCampaign), true);
            new GameModePreset("Tutorial", typeof(TutorialMode), true);
            new GameModePreset("SPSandbox", typeof(GameMode), true)
            {
                Description = "Single player sandbox mode for debugging."
            };
#endif

            new GameModePreset("Sandbox", typeof(GameMode), false)
            {
                Description = "A game mode with no specific objectives."
            };

            new GameModePreset("Mission", typeof(MissionMode), false)
            {
                Description = "The crew must work together to complete a specific task, such as retrieving "
                + "an alien artifact or killing a creature that's terrorizing nearby outposts. The game ends "
                + "when the task is completed or everyone in the crew has died."
            };

            new GameModePreset("Campaign", typeof(MultiPlayerCampaign), false, false);
        }
    }
}

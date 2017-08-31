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
        public bool IsSinglePlayer;

        public string Description;

        public GameModePreset(string name, Type type, bool isSinglePlayer = false)
        {
            this.Name = name;

            Constructor = type.GetConstructor(new Type[] { typeof(GameModePreset), typeof(object) });

            IsSinglePlayer = isSinglePlayer;

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
#endif

            var mode = new GameModePreset("SandBox", typeof(GameMode), false);
            mode.Description = "A game mode with no specific objectives.";
            
            mode = new GameModePreset("Mission", typeof(MissionMode), false);
            mode.Description = "The crew must work together to complete a specific task, such as retrieving "
                + "an alien artifact or killing a creature that's terrorizing nearby outposts. The game ends "
                + "when the task is completed or everyone in the crew has died.";

            new GameModePreset("Campaign", typeof(MultiplayerCampaign), false);
        }
    }
}

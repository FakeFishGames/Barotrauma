using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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
            //Constructor = constructor;


            Constructor = type.GetConstructor(new Type[] { typeof(GameModePreset) });

            IsSinglePlayer = isSinglePlayer;

            list.Add(this);
        }

        public GameMode Instantiate()
        {
            object[] lobject = new object[] { this };
            return (GameMode)Constructor.Invoke(lobject);
        }

        public static void Init()
        {
            new GameModePreset("Single Player", typeof(SinglePlayerMode), true);
            new GameModePreset("Tutorial", typeof(TutorialMode), true);

            var mode = new GameModePreset("SandBox", typeof(GameMode), false);
            mode.Description = "A game mode with no specific objectives.";

            //mode = new GameModePreset("Traitor", typeof(TraitorMode), false);
            //mode.Description = "One of the players is selected as a traitor and given a secret objective. "
            //    + "The rest of the crew will win if they reach the end of the level or kill the traitor "
            //    + "before the objective is completed.";

            mode = new GameModePreset("Mission", typeof(MissionMode), false);
            mode.Description = "The crew must work together to complete a specific task, such as retrieving "
                + "an alien artifact or killing a creature that's terrorizing nearby outposts. The game ends "
                + "when the task is completed or everyone in the crew has died.";
        }
    }
}

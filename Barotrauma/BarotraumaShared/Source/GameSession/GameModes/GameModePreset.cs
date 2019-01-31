using System;
using System.Collections.Generic;
using System.Reflection;

namespace Barotrauma
{
    class GameModePreset
    {
        public static List<GameModePreset> List = new List<GameModePreset>();
        
        public ConstructorInfo Constructor
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public string Identifier
        {
            get;
            private set;
        }

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

        public GameModePreset(string identifier, Type type, bool isSinglePlayer = false, bool votable = true)
        {
            Name = TextManager.Get("GameMode." + identifier);
            Identifier = identifier;

            Constructor = type.GetConstructor(new Type[] { typeof(GameModePreset), typeof(object) });

            IsSinglePlayer = isSinglePlayer;
            Votable = votable;

            List.Add(this);
        }

        public GameMode Instantiate(object param)
        {
            object[] lobject = new object[] { this, param };
            return (GameMode)Constructor.Invoke(lobject);
        }

        public static void Init()
        {
#if CLIENT
            new GameModePreset("singleplayercampaign", typeof(SinglePlayerCampaign), true);
            new GameModePreset("tutorial", typeof(TutorialMode), true);
            new GameModePreset("devsandbox", typeof(GameMode), true)
            {
                Description = "Single player sandbox mode for debugging."
            };
#endif
            new GameModePreset("sandbox", typeof(GameMode), false)
            {
                Description = "A game mode with no specific objectives."
            };

            new GameModePreset("mission", typeof(MissionMode), false)
            {
                Description = "The crew must work together to complete a specific task, such as retrieving "
                + "an alien artifact or killing a creature that's terrorizing nearby outposts. The game ends "
                + "when the task is completed or everyone in the crew has died."
            };

            //new GameModePreset("multiplayercampaign", typeof(MultiPlayerCampaign), false, false);
        }
    }
}

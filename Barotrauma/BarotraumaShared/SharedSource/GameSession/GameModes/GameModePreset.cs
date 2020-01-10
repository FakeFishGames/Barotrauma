using System;
using System.Collections.Generic;
using System.Reflection;

namespace Barotrauma
{
    class GameModePreset
    {
        public static List<GameModePreset> List = new List<GameModePreset>();

        public readonly ConstructorInfo Constructor;

        public readonly string Name;
        public readonly string Description;

        public readonly string Identifier;

        public readonly bool IsSinglePlayer;

        //are clients allowed to vote for this gamemode
        public readonly bool Votable;

        public GameModePreset(string identifier, Type type, bool isSinglePlayer = false, bool votable = true)
        {
            Name = TextManager.Get("GameMode." + identifier);
            Description = TextManager.Get("GameModeDescription." + identifier, returnNull: true) ?? "";
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
            new GameModePreset("devsandbox", typeof(GameMode), true);
#endif
            new GameModePreset("sandbox", typeof(GameMode), false);
            new GameModePreset("mission", typeof(MissionMode), false);
            new GameModePreset("multiplayercampaign", typeof(MultiPlayerCampaign), false, false);
        }
    }
}

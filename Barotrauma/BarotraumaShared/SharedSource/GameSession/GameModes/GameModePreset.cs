using System;
using System.Collections.Generic;
using System.Reflection;

namespace Barotrauma
{
    class GameModePreset
    {
        public static List<GameModePreset> List = new List<GameModePreset>();

        public static GameModePreset SinglePlayerCampaign;
        public static GameModePreset MultiPlayerCampaign;
        public static GameModePreset Tutorial;
        public static GameModePreset Mission;
        public static GameModePreset TestMode;
        public static GameModePreset Sandbox;
        public static GameModePreset DevSandbox;

        public readonly Type GameModeType;

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

            GameModeType = type;

            IsSinglePlayer = isSinglePlayer;
            Votable = votable;

            List.Add(this);
        }

        public static void Init()
        {
#if CLIENT
            Tutorial = new GameModePreset("tutorial", typeof(TutorialMode), true);
            DevSandbox = new GameModePreset("devsandbox", typeof(GameMode), true);
            SinglePlayerCampaign = new GameModePreset("singleplayercampaign", typeof(SinglePlayerCampaign), true);
            TestMode = new GameModePreset("testmode", typeof(TestGameMode), true);
#endif
            Sandbox = new GameModePreset("sandbox", typeof(GameMode), false);
            Mission = new GameModePreset("mission", typeof(MissionMode), false);
            MultiPlayerCampaign = new GameModePreset("multiplayercampaign", typeof(MultiPlayerCampaign), false, false);
        }
    }
}

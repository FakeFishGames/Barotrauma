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
        public static GameModePreset PvP;
        public static GameModePreset TestMode;
        public static GameModePreset Sandbox;
        public static GameModePreset DevSandbox;

        public readonly Type GameModeType;

        public readonly LocalizedString Name;
        public readonly LocalizedString Description;

        public readonly Identifier Identifier;

        public readonly bool IsSinglePlayer;

        //are clients allowed to vote for this gamemode
        public readonly bool Votable;

        public GameModePreset(Identifier identifier, Type type, bool isSinglePlayer = false, bool votable = true)
        {
            Name = TextManager.Get("GameMode." + identifier);
            Description = TextManager.Get("GameModeDescription." + identifier).Fallback("");
            Identifier = identifier;

            GameModeType = type;

            IsSinglePlayer = isSinglePlayer;
            Votable = votable;

            List.Add(this);
        }

        public static void Init()
        {
#if CLIENT
            Tutorial = new GameModePreset("tutorial".ToIdentifier(), typeof(TutorialMode), isSinglePlayer: true);
            DevSandbox = new GameModePreset("devsandbox".ToIdentifier(), typeof(GameMode), isSinglePlayer: true);
            SinglePlayerCampaign = new GameModePreset("singleplayercampaign".ToIdentifier(), typeof(SinglePlayerCampaign), isSinglePlayer: true);
            TestMode = new GameModePreset("testmode".ToIdentifier(), typeof(TestGameMode), isSinglePlayer: true);
#endif
            Sandbox = new GameModePreset("sandbox".ToIdentifier(), typeof(GameMode), isSinglePlayer: false);
            Mission = new GameModePreset("mission".ToIdentifier(), typeof(CoOpMode), isSinglePlayer: false);
            PvP = new GameModePreset("pvp".ToIdentifier(), typeof(PvPMode), isSinglePlayer: false);
            MultiPlayerCampaign = new GameModePreset("multiplayercampaign".ToIdentifier(), typeof(MultiPlayerCampaign), isSinglePlayer: false);
        }
    }
}

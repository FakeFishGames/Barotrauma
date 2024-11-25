using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;

namespace Barotrauma
{
    [NetworkSerialize]
    public readonly record struct RoundStartWarningData(float RoundStartsAnywaysTimeInSeconds, string Team1Sub, ImmutableArray<uint> Team1IncompatiblePerks, string Team2Sub, ImmutableArray<uint> Team2IncompatiblePerks) : INetSerializableStruct;

    partial class NetLobbyScreen : Screen
    {
        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get
            {
#if SERVER
                if (GameMain.Server != null && lastUpdateID < 1) lastUpdateID++;
#endif
                return lastUpdateID;
            }
            set { lastUpdateID = value; }
        }
        
        private string levelSeed = "";

        public void SetLevelDifficulty(float difficulty)
        {
            difficulty = MathHelper.Clamp(difficulty, 0.0f, 100.0f);
#if SERVER
            if (GameMain.Server != null)
            {
                GameMain.Server.ServerSettings.SelectedLevelDifficulty = difficulty;
                lastUpdateID++;
            }
#elif CLIENT
            levelDifficultySlider.BarScroll = difficulty / 100.0f;
            levelDifficultySlider.OnMoved(levelDifficultySlider, levelDifficultySlider.BarScroll);
#endif
        }

        public void SetBotCount(int botCount)
        {
#if SERVER
            if (GameMain.Server != null)
            {
                if (botCount < 0) botCount = GameMain.Server.ServerSettings.MaxBotCount;
                if (botCount > GameMain.Server.ServerSettings.MaxBotCount) botCount = 0;

                GameMain.Server.ServerSettings.BotCount = botCount;
                lastUpdateID++;
            }
#endif
        }

        public void SetBotSpawnMode(BotSpawnMode botSpawnMode)
        {
#if SERVER
            if (GameMain.Server != null)
            {
                GameMain.Server.ServerSettings.BotSpawnMode = botSpawnMode;
                lastUpdateID++;
            }
#endif
        }

        public void SetTraitorProbability(float probability)
        {
            if (GameMain.NetworkMember != null)
            {
                GameMain.NetworkMember.ServerSettings.TraitorProbability = probability;
            }
        }

        public void SetTraitorDangerLevel(int dangerLevel)
        {
            if (GameMain.NetworkMember != null)
            {
                GameMain.NetworkMember.ServerSettings.TraitorDangerLevel = dangerLevel;
            }
#if SERVER
            if (GameMain.Server != null) { GameMain.Server.ServerSettings.TraitorDangerLevel = dangerLevel; }
#elif CLIENT
            SetTraitorDangerIndicators(dangerLevel);
#endif
        }
    }
}

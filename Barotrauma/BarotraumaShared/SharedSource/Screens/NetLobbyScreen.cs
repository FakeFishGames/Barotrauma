﻿using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
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
#endif
#if CLIENT
            levelDifficultyScrollBar.BarScroll = difficulty / 100.0f;
            levelDifficultyScrollBar.OnMoved(levelDifficultyScrollBar, levelDifficultyScrollBar.BarScroll);
#endif
        }

        public void ToggleTraitorsEnabled(int dir)
        {
#if SERVER
            if (GameMain.Server == null) return;

            lastUpdateID++;
            
            int index = (int)GameMain.Server.ServerSettings.TraitorsEnabled + dir;
            if (index < 0) index = 2;
            if (index > 2) index = 0;

            SetTraitorsEnabled((YesNoMaybe)index);
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
#if CLIENT
            botCountText.Text = botCount.ToString();
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
#if CLIENT

            botSpawnModeText.Text = TextManager.Get(botSpawnMode.ToString());
            botSpawnModeText.ToolTip = TextManager.Get($"botspawnmode.{botSpawnMode}.tooltip") + "\n\n" + TextManager.Get("botspawn.campaignnote");
            foreach (var btn in botSpawnModeButtons)
            {
                btn.ToolTip = botSpawnModeText.ToolTip;
            }
#endif
        }

        public void SetTraitorsEnabled(YesNoMaybe enabled)
        {
#if SERVER
            if (GameMain.Server != null) GameMain.Server.ServerSettings.TraitorsEnabled = enabled;
#endif
#if CLIENT
            traitorProbabilityText.Text = TextManager.Get(enabled.ToString());
#endif
        }
    }
}

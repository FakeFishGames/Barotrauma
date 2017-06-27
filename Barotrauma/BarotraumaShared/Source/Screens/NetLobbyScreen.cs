using System;
using Barotrauma.Networking;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        public bool IsServer;
        public string ServerName = "Server";

        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get { if (GameMain.Server != null && lastUpdateID < 1) lastUpdateID++; return lastUpdateID; }
            set { if (GameMain.Server != null) return; lastUpdateID = value; }
        }

        //for guitextblock delegate
        public string GetServerName()
        {
            return ServerName;
        }
        
        private string levelSeed = "";
        
        private float autoRestartTimer;

        public string AutoRestartText()
        {
            if (GameMain.Server != null)
            {
                if (!GameMain.Server.AutoRestart) return "";
                return "Restarting in " + ToolBox.SecondsToReadableTime(Math.Max(GameMain.Server.AutoRestartTimer, 0));
            }

            if (autoRestartTimer == 0.0f) return "";            
            return "Restarting in " + ToolBox.SecondsToReadableTime(Math.Max(autoRestartTimer, 0));
        }

        public void ToggleTraitorsEnabled(int dir)
        {
            if (GameMain.Server == null) return;

            lastUpdateID++;
            
            int index = (int)GameMain.Server.TraitorsEnabled + dir;
            if (index < 0) index = 2;
            if (index > 2) index = 0;

            SetTraitorsEnabled((YesNoMaybe)index);
        }

        public void SetTraitorsEnabled(YesNoMaybe enabled)
        {
            if (GameMain.Server != null) GameMain.Server.TraitorsEnabled = enabled;
#if CLIENT
            (traitorProbabilityText as GUITextBlock).Text = enabled.ToString();
#endif
        }
    }
}

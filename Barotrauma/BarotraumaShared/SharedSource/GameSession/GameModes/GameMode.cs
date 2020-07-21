using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class GameMode
    {
        public static List<GameModePreset> PresetList = new List<GameModePreset>();

        protected DateTime startTime;
                
        protected GameModePreset preset;
                
        public CrewManager CrewManager
        {
            get { return GameMain.GameSession?.CrewManager; }
        }

        public virtual Mission Mission
        {
            get { return null; }
        }

        public bool IsSinglePlayer
        {
            get { return preset.IsSinglePlayer; }
        }

        public string Name
        {
            get { return preset.Name; }
        }

        public virtual bool Paused
        {
            get { return false; }
        }

        public GameModePreset Preset
        {
            get { return preset; }
        }

        public GameMode(GameModePreset preset)
        {
            this.preset = preset;
        }

        public virtual void Start()
        {
            startTime = DateTime.Now;
        }

        public virtual void ShowStartMessage() { }
        
        public virtual void AddToGUIUpdateList()
        {
#if CLIENT
            GameMain.GameSession?.CrewManager.AddToGUIUpdateList();
#endif
        }

        public virtual void Update(float deltaTime)
        {
            CrewManager?.Update(deltaTime);
        }

        public virtual void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
        }

        public virtual void Remove() { }
    }
}

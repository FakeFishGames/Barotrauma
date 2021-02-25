using System;
using System.Collections.Generic;
using System.Linq;

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

        public virtual IEnumerable<Mission> Missions
        {
            get { return Enumerable.Empty<Mission>(); }
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

        public virtual void UpdateWhilePaused(float deltaTime) { }

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

        public virtual void AddExtraMissions(LevelData levelData) { }
        
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

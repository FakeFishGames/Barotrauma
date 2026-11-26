using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public partial class GameMode
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

        public LocalizedString Name
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
            GCHelper.RegisterForWatch(this, notes: $"GameMode: {preset.Name}");
        }

        public virtual void Start()
        {
            startTime = DateTime.Now;
        }

        public virtual void ShowStartMessage() { }

        public virtual void AddExtraMissions(LevelData levelData) { }
        
        public void AddToGUIUpdateList()
        {
#if CLIENT
            GameMain.GameSession?.CrewManager.AddToGUIUpdateList();
#endif
            AddToGUIUpdateListGameModeSpecific();
            foreach (var hook in PluginData.GetHooks<PluginGameModeAddToGUIUpdateListDelegate>())
            {
                hook(this);
            }
        }


        protected virtual void AddToGUIUpdateListGameModeSpecific()
        {
        }


        public void Update(float deltaTime)
        {
            CrewManager?.Update(deltaTime);
            UpdateGameModeSpecific(deltaTime);
            foreach (var hook in PluginData.GetHooks<PluginGameModeUpdateDelegate>())
            {
                hook(this, deltaTime);
            }
        }

        protected virtual void UpdateGameModeSpecific(float deltaTime)
        {
        }

        public virtual void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
        }

        public virtual void Remove() { }
    }
}

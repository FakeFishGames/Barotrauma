using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class TestGameMode : GameMode
    {
        public Action OnRoundEnd;

        public TestGameMode(GameModePreset preset) : base(preset)
        {
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: jobPrefab, variant: variant));
                }
            }
        }

        public override void Start()
        {
            base.Start();
            
            CrewManager.InitSinglePlayerRound();
        }
        
        public override void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            OnRoundEnd?.Invoke();
        }
    }
}

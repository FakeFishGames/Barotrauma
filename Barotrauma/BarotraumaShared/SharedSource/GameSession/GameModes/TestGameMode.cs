using System.Linq;

namespace Barotrauma
{
    partial class TestGameMode : GameMode
    {
        /// <summary>
        /// Whether this test was started from a collaborative editing session.
        /// </summary>
        public bool IsCollaborativeTest { get; set; }

        public TestGameMode(GameModePreset preset) : base(preset)
        {
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs.OrderBy(p => p.Identifier))
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: jobPrefab, variant: variant));
                }
            }
        }
    }
}
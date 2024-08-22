using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class Skill
    {
        public readonly Identifier Identifier;

        public const float MaximumSkill = 100.0f;

        private float level;

        /// <summary>
        /// The highest skill level during the round (before any death penalties were applied)
        /// </summary>
        public float HighestLevelDuringRound { get; private set; }

        public float Level
        {
            get { return level; }
            set 
            {
                HighestLevelDuringRound = MathHelper.Max(value, HighestLevelDuringRound);
                level = value; 
            }
        }

        public LocalizedString DisplayName { get; private set; }

        public void IncreaseSkill(float value, bool increasePastMax)
        {
            Level = MathHelper.Clamp(level + value, 0.0f, increasePastMax ? SkillSettings.Current.MaximumSkillWithTalents : MaximumSkill);
        }

        private readonly Identifier iconJobId;

        public Sprite Icon => !iconJobId.IsEmpty && JobPrefab.Prefabs.TryGet(iconJobId, out var jobPrefab)
            ? jobPrefab.Icon
            : null;

        public readonly float PriceMultiplier = 1.0f;

        public Skill(SkillPrefab prefab, Rand.RandSync randSync)
        {
            Identifier = prefab.Identifier;
            Level = Rand.Range(prefab.LevelRange.Start, prefab.LevelRange.End, randSync);
            iconJobId = GetIconJobId();
            PriceMultiplier = prefab.PriceMultiplier;
            DisplayName = TextManager.Get("SkillName." + Identifier);
        }

        public Skill(Identifier identifier, float level)
        {
            Identifier = identifier;
            Level = level;
            iconJobId = GetIconJobId();
            DisplayName = TextManager.Get("SkillName." + Identifier);
        }

        private Identifier GetIconJobId()
        {
            Identifier jobId = Identifier.Empty;
            if (Identifier == "electrical")
            {
                jobId = "engineer".ToIdentifier();
            }
            else if (Identifier == "helm")
            {
                jobId = "captain".ToIdentifier();
            }
            else if (Identifier == "mechanical")
            {
                jobId = "mechanic".ToIdentifier();
            }
            else if (Identifier == "medical")
            {
                jobId = "medicaldoctor".ToIdentifier();
            }
            else if (Identifier == "weapons")
            {
                jobId = "securityofficer".ToIdentifier();
            }

            return jobId;
        }
    }
}

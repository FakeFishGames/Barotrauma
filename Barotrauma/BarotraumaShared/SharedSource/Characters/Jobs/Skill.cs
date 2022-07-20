using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class Skill
    {
        public readonly Identifier Identifier;

        public const float MaximumSkill = 100.0f;

        private float level;

        public float Level
        {
            get { return level; }
            set { level = value; }
        }

        public void IncreaseSkill(float value, bool increasePastMax)
        {
            level = MathHelper.Clamp(level + value, 0.0f, increasePastMax ? SkillSettings.Current.MaximumSkillWithTalents : MaximumSkill);
        }

        private Identifier iconJobId;

        public Sprite Icon => !iconJobId.IsEmpty && JobPrefab.Prefabs.TryGet(iconJobId, out var jobPrefab)
            ? jobPrefab.Icon
            : null;

        public readonly float PriceMultiplier = 1.0f;

        public Skill(SkillPrefab prefab, Rand.RandSync randSync)
        {
            Identifier = prefab.Identifier;
            level = Rand.Range(prefab.LevelRange.Start, prefab.LevelRange.End, randSync);
            iconJobId = GetIconJobId();
            PriceMultiplier = prefab.PriceMultiplier;
        }

        public Skill(Identifier identifier, float level)
        {
            Identifier = identifier;
            this.level = level;
            iconJobId = GetIconJobId();
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

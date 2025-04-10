using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class Skill
    {
        public readonly Identifier Identifier;

        /// <summary>
        /// The "normal" maximum skill level. It's possible to go above this with certain talents, see <see cref="SkillSettings.MaximumSkillWithTalents"/>.
        /// </summary>
        public const float DefaultMaximumSkill = 100.0f;

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

        /// <summary>
        /// Increase the skill level by a value. Handles clamping the level above the maximum. 
        /// Note that if the skill level is already above maximum (if it for example has been set by console commands), it's allowed to stay at that level, but not to increase further.
        /// </summary>
        /// <param name="value">How much to increase the skill.</param>
        /// <param name="canIncreasePastDefaultMaximumSkill">Can the skill level increase above <see cref="DefaultMaximumSkill"/>, or can it go all the way to <see cref="SkillSettings.MaximumSkillWithTalents"/>?</param>
        public void IncreaseSkill(float value, bool canIncreasePastDefaultMaximumSkill)
        {
            float currentMaximum = canIncreasePastDefaultMaximumSkill ? SkillSettings.Current.MaximumSkillWithTalents : DefaultMaximumSkill;
            if (Level > currentMaximum && value > 0)
            {
                //level above max already (set with console commands?), don't allow increasing it further and don't clamp it below max either
                return;
            }
            Level = MathHelper.Clamp(level + value, 0.0f, currentMaximum);
        }

        private readonly Identifier iconJobId;

        public Sprite Icon => !iconJobId.IsEmpty && JobPrefab.Prefabs.TryGet(iconJobId, out var jobPrefab)
            ? jobPrefab.Icon
            : null;

        public readonly float PriceMultiplier = 1.0f;

        public Skill(SkillPrefab prefab, bool isPvP, Rand.RandSync randSync)
        {
            Identifier = prefab.Identifier;

            var levelRange = prefab.GetLevelRange(isPvP);
            Level = Rand.Range(levelRange.Start, levelRange.End, randSync);
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

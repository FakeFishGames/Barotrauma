using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor.Resources
{
    /// <summary>
    /// Resource that corresponds to amount of static items in the outpost.
    /// </summary>
    internal class NPCResource : ReadonlyResource
    {
        /// <summary>
        /// Identifiers of the jobs that add up to this resource.
        /// </summary>
        public ImmutableHashSet<Identifier> AcceptedJobs { get; set; } = [];

        [Serialize(1f, IsPropertySaveable.Yes, description: "How much the amount of the resource is increased by an NPC with a matching job.")]
        public float AmountAddedByJob { get; set; }

        [Serialize("", IsPropertySaveable.No, description: "Identifier of a skill on the NPCs that adds up to this resource.")]
        public Identifier RelevantSkill { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Minimum skill level before the skill starts affecting the resource.")]
        public float MinSkillAmount { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How much the amount of the resource is increased by an NPC with MinSkillAmount on the specified skill.")]
        public float AmountAddedBySkillMin { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How much the amount of the resource is increased by an NPC with 100 on the specified skill.")]
        public float AmountAddedBySkillMax { get; set; }

        private float amount = -1;

        public NPCResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
        {
            AcceptedJobs = prefab.OriginalElement.GetAttributeIdentifierImmutableHashSet(nameof(AcceptedJobs), []);
        }

        protected override void OnOutpostEnteredResourceSpecific(Submarine outpost, PersistentOutpostData outpostData)
        {
            RefreshAmount(outpost, outpostData);
        }

        protected override void OnOutpostExitedResourceSpecific(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs)
        {
            RefreshAmount(outpost, outpostData);
        }

        protected override float GetAmountResourceSpecific()
        {
            return amount;
        }

        public void RefreshAmount(Submarine outpost, PersistentOutpostData outpostData)
        {
            amount = 0f;
            if (outpost?.Info == null) { return; } 

            var outpostNpcs = outpost.Info.GetOutpostNPCs();
            foreach (Character npc in outpostNpcs)
            {
                if (!npc.JobIdentifier.IsEmpty && AcceptedJobs.Contains(npc.JobIdentifier))
                {
                    amount += AmountAddedByJob;
                }
                if (!RelevantSkill.IsEmpty)
                {
                    float skillLevel = npc.GetSkillLevel(RelevantSkill);
                    if (skillLevel < MinSkillAmount) { continue; }
                    float skillFactor = MathUtils.InverseLerp(MinSkillAmount, 100.0f, skillLevel);
                    amount += MathHelper.Lerp(AmountAddedBySkillMin, AmountAddedBySkillMax, skillFactor);
                }
            }
        }
    }
}
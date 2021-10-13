using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityUnlockTree : CharacterAbility
    {
        public CharacterAbilityUnlockTree(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (!addingFirstTime) { return; }
            if (!TalentTree.JobTalentTrees.TryGetValue(Character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return; }

            var subTree = talentTree.TalentSubTrees.Find(t => t.TalentOptionStages.Any(ts => ts.Talents.Contains(CharacterTalent.Prefab)));
            if (subTree != null)
            {
                foreach (var talentOption in subTree.TalentOptionStages)
                {
                    foreach (var talent in talentOption.Talents)
                    {
                        if (talent == CharacterTalent.Prefab) { continue; }
                        Character.GiveTalent(talent);
                    }
                }
            }
        }
    }
}

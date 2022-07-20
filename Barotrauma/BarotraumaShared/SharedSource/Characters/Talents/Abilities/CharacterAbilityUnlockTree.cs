using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityUnlockTree : CharacterAbility
    {
        public CharacterAbilityUnlockTree(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (!TalentTree.JobTalentTrees.TryGet(Character.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return; }

            var subTree = talentTree.TalentSubTrees.Find(t => t.TalentOptionStages.Any(ts => ts.Talents.Contains(CharacterTalent.Prefab)));
            if (subTree == null) { return; }
            
            subTree.ForceUnlock = true;
            if (!addingFirstTime) { return; }

            foreach (var talentOption in subTree.TalentOptionStages)
            {
                foreach (var talent in talentOption.Talents)
                {
                    if (talent == CharacterTalent.Prefab) { continue; }
                    if (Character.GiveTalent(talent))
                    {
                        Character.Info.AdditionalTalentPoints++;
                    }
                }
            }            
        }
    }
}

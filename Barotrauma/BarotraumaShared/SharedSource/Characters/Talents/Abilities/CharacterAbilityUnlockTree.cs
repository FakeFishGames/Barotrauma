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

            var subTree = talentTree.TalentSubTrees.Find(t => t.AllTalentIdentifiers.Contains(CharacterTalent.Prefab.Identifier));
            if (subTree == null) { return; }
            
            subTree.ForceUnlock = true;
            if (!addingFirstTime) { return; }

            foreach (var talentId in subTree.AllTalentIdentifiers)
            {
                if (talentId == CharacterTalent.Prefab.Identifier) { continue; }
                if (Character.GiveTalent(talentId))
                {
                    Character.Info.AdditionalTalentPoints++;
                }                
            }            
        }
    }
}

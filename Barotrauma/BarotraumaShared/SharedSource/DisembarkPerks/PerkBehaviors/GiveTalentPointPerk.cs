using System.Collections.Generic;

namespace Barotrauma.PerkBehaviors
{
    internal class GiveTalentPointPerk : PerkBase
    {
        [Serialize(0, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        public GiveTalentPointPerk(ContentXElement element, DisembarkPerkPrefab prefab) : base(element, prefab) { }

        public override void ApplyOnRoundStart(IReadOnlyCollection<Character> teamCharacters, Submarine teamSubmarine)
        {
            foreach (Character character in teamCharacters)
            {
                character.Info.AdditionalTalentPoints += Amount;
            }
        }
    }
}
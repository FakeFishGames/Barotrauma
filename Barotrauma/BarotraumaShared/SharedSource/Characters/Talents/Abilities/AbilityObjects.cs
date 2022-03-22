using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    abstract class AbilityObject
    {
        // kept as blank for now, as we are using a composition and only using this object to enforce parameter types
    }

    class AbilityCharacter : AbilityObject, IAbilityCharacter
    {
        public AbilityCharacter(Character character)
        {
            Character = character;
        }
        public Character Character { get; set; }
    }

}

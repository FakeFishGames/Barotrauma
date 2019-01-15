using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Location
    {
        private HireManager hireManager;

        public void RemoveHireableCharacter(CharacterInfo character)
        {
            if (!Type.HasHireableCharacters)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - the location has no hireable characters.\n" + Environment.StackTrace);
                return;
            }
            if (hireManager == null)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - hire manager has not been instantiated.\n" + Environment.StackTrace);
                return;
            }

            hireManager.RemoveCharacter(character);
        }

        public IEnumerable<CharacterInfo> GetHireableCharacters()
        {
            if (!Type.HasHireableCharacters)
            {
                return Enumerable.Empty<CharacterInfo>();
            }

            if (hireManager == null)
            {
                hireManager = new HireManager();
            }
            if (!hireManager.AvailableCharacters.Any())
            {
                hireManager.GenerateCharacters(location: this, amount: HireManager.MaxAvailableCharacters);
            }
            return hireManager.AvailableCharacters;
        }

        partial void RemoveProjSpecific()
        {
            hireManager?.Remove();
        }
    }
}

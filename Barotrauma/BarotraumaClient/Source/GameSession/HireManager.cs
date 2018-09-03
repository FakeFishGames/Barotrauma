using System.Collections.Generic;

namespace Barotrauma
{
    class HireManager
    {
        public List<CharacterInfo> availableCharacters;

        public const int MaxAvailableCharacters = 10;

        public HireManager()
        {
            availableCharacters = new List<CharacterInfo>();
        }

        public void GenerateCharacters(Location location, int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                JobPrefab job = location.Type.GetRandomHireable();
                if (job == null) return;

                availableCharacters.Add(new CharacterInfo(Character.HumanConfigFile, "", Gender.None, job));
            }
        }
    }
}

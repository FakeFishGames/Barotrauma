using System.Collections.Generic;

namespace Barotrauma
{
    class HireManager
    {
        private List<CharacterInfo> availableCharacters;
        public IEnumerable<CharacterInfo> AvailableCharacters
        {
            get { return availableCharacters; }
        }

        public const int MaxAvailableCharacters = 10;

        public HireManager()
        {
            availableCharacters = new List<CharacterInfo>();
        }

        public void RemoveCharacter(CharacterInfo character)
        {
            availableCharacters.Remove(character);
        }

        public void GenerateCharacters(Location location, int amount)
        {
            availableCharacters.ForEach(c => c.Remove());
            availableCharacters.Clear();
            for (int i = 0; i < amount; i++)
            {
                JobPrefab job = location.Type.GetRandomHireable();
                if (job == null) { return; }

                availableCharacters.Add(new CharacterInfo(Character.HumanSpeciesName, "", job));
            }
        }

        public void Remove()
        {
            availableCharacters.ForEach(c => c.Remove());
            availableCharacters.Clear();
        }
    }
}

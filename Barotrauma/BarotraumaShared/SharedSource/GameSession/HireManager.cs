using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class HireManager
    {
        public List<CharacterInfo> AvailableCharacters { get; set; }
        public List<CharacterInfo> PendingHires = new List<CharacterInfo>();

        public const int MaxAvailableCharacters = 10;

        public HireManager()
        {
            AvailableCharacters = new List<CharacterInfo>();
        }

        public void RemoveCharacter(CharacterInfo character)
        {
            AvailableCharacters.Remove(character);
        }

        public void GenerateCharacters(Location location, int amount)
        {
            AvailableCharacters.ForEach(c => c.Remove());
            AvailableCharacters.Clear();
            for (int i = 0; i < amount; i++)
            {
                JobPrefab job = location.Type.GetRandomHireable();
                if (job == null) { return; }

                var variant = Rand.Range(0, job.Variants, Rand.RandSync.ServerAndClient);
                AvailableCharacters.Add(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: job, variant: variant));
            }
        }

        public void Remove()
        {
            AvailableCharacters.ForEach(c => c.Remove());
            AvailableCharacters.Clear();
        }

        public void RenameCharacter(CharacterInfo characterInfo, string newName)
        {
            if (characterInfo == null || string.IsNullOrEmpty(newName)) { return; }
            AvailableCharacters.FirstOrDefault(ci => ci == characterInfo)?.Rename(newName);
            PendingHires.FirstOrDefault(ci => ci == characterInfo)?.Rename(newName);
        }
    }
}

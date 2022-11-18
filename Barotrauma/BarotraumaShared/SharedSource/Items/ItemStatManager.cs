#nullable enable

using System;
using System.Collections.Generic;

namespace Barotrauma
{
    internal sealed class ItemStatManager
    {
        private Item item;

        public ItemStatManager(Item item)
        {
            this.item = item;
        }

        [NetworkSerialize]
        public readonly record struct TalentStatIdentifier(ItemTalentStats Stat, Identifier TalentIdentifier, UInt32 CharacterID) : INetSerializableStruct;

        private readonly Dictionary<TalentStatIdentifier, float> talentStats = new();

        public void ApplyStat(ItemTalentStats stat, float value, CharacterTalent talent)
        {
            if (talent.Character?.ID is not { } characterId ||
                talent.Prefab?.Identifier is not { } talentIdentifier)
            {
                return;
            }

            TalentStatIdentifier identifier = new TalentStatIdentifier(stat, talentIdentifier, characterId);
            talentStats[identifier] = value;

#if SERVER
            if (GameMain.NetworkMember is { IsServer: true } server)
            {
                server.CreateEntityEvent(item, new Item.SetItemStatEventData(talentStats));
            }
#endif
        }

        // Used for getting the value value from network packet
        public void ApplyStat(TalentStatIdentifier identifier, float value)
        {
            talentStats[identifier] = value;
        }

        public float GetAdjustedValue(ItemTalentStats stat, float originalValue)
        {
            float total = originalValue;
            foreach (var (key, value) in talentStats)
            {
                if (key.Stat == stat)
                {
                    total *= value;
                }
            }

            return total;
        }
    }
}
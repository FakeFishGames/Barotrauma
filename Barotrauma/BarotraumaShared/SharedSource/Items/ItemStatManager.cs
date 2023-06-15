#nullable enable

using System;
using System.Collections.Generic;

namespace Barotrauma
{
    [NetworkSerialize]
    internal readonly record struct TalentStatIdentifier(ItemTalentStats Stat, Identifier TalentIdentifier, Option<UInt32> UniqueCharacterId) : INetSerializableStruct
    {
        /// <summary>
        /// Stackable identifiers feature a unique ID to allow multiple stats applied by the same talent from different characters to coexist.
        /// </summary>
        public static TalentStatIdentifier CreateStackable(ItemTalentStats stat, Identifier talentIdentifier, UInt32 characterId)
            => new(stat, talentIdentifier, Option<UInt32>.Some(characterId));

        /// <summary>
        /// Unstackable identifiers do not have a unique ID causing them to be identical to other stats applied by the same talent from different characters and thus only one of them will be applied.
        /// <see cref="ItemStatManager.ApplyStat"/> will always use the highest value for unstackable stats.
        /// </summary>
        public static TalentStatIdentifier CreateUnstackable(ItemTalentStats stat, Identifier talentIdentifier)
            => new(stat, talentIdentifier, Option.None);
    }

    internal sealed class ItemStatManager
    {
        private readonly Dictionary<TalentStatIdentifier, float> talentStats = new();
        private readonly Item item;

        public ItemStatManager(Item item) => this.item = item;

        public void ApplyStat(ItemTalentStats stat, bool stackable, float value, CharacterTalent talent)
        {
            if (talent.Character?.ID is not { } characterId ||
                talent.Prefab?.Identifier is not { } talentIdentifier) { return; }

            var identifier = stackable
                ? TalentStatIdentifier.CreateStackable(stat, talentIdentifier, characterId)
                : TalentStatIdentifier.CreateUnstackable(stat, talentIdentifier);

            if (!stackable)
            {
                if (talentStats.TryGetValue(identifier, out float existingValue))
                {
                    // Always use the highest value for non-stackable stats
                    if (existingValue > value) { return; }
                }
            }

            talentStats[identifier] = value;

#if SERVER
            if (GameMain.NetworkMember is { IsServer: true } server)
            {
                server.CreateEntityEvent(item, new Item.SetItemStatEventData(talentStats));
            }
#endif
        }

        /// <summary>
        /// Used for setting the value value from network packet; bypassing all validity checks.
        /// </summary>
        public void ApplyStatDirect(TalentStatIdentifier identifier, float value) => talentStats[identifier] = value;

        public float GetAdjustedValue(ItemTalentStats stat, float originalValue)
        {
            float total = originalValue;

            foreach (var (key, value) in talentStats)
            {
                if (key.Stat != stat) { continue; }
                total *= value;
            }

            return total;
        }
    }
}
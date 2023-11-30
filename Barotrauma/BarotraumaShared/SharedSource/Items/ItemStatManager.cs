#nullable enable

using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    [NetworkSerialize]
    internal readonly record struct TalentStatIdentifier(ItemTalentStats Stat, Identifier TalentIdentifier, Option<UInt32> UniqueCharacterId, bool Save) : INetSerializableStruct
    {
        /// <summary>
        /// Stackable identifiers feature a unique ID to allow multiple stats applied by the same talent from different characters to coexist.
        /// </summary>
        public static TalentStatIdentifier CreateStackable(ItemTalentStats stat, Identifier talentIdentifier, UInt32 characterId)
            => new(stat, talentIdentifier, Option<UInt32>.Some(characterId), Save: false);

        /// <summary>
        /// Unstackable identifiers do not have a unique ID causing them to be identical to other stats applied by the same talent from different characters and thus only one of them will be applied.
        /// <see cref="ItemStatManager.ApplyStat"/> will always use the highest value for unstackable stats.
        /// </summary>
        public static TalentStatIdentifier CreateUnstackable(ItemTalentStats stat, Identifier talentIdentifier, bool Save)
            => new(stat, talentIdentifier, Option.None, Save);

        public XElement Serialize()
            => new XElement("Stat",
                new XAttribute("type", Stat),
                new XAttribute("talent", TalentIdentifier));

        public static Option<TalentStatIdentifier> TryLoadFromXML(XElement element)
        {
            var stat = element.GetAttributeEnum("type", ItemTalentStats.None);
            var talentIdentifier = element.GetAttributeIdentifier("talent", Identifier.Empty);

            if (stat == ItemTalentStats.None || talentIdentifier == Identifier.Empty)
            {
                var error = $"Failed to load talent stat identifier from XML {element}";
                DebugConsole.ThrowError(error);
                GameAnalyticsManager.AddErrorEventOnce("ItemStatManager.TryLoadFromXML:Invalid", GameAnalyticsManager.ErrorSeverity.Error, error);
                return Option.None;
            }

            return Option.Some(CreateUnstackable(stat, talentIdentifier, true));
        }
    }

    internal sealed class ItemStatManager
    {
        private readonly Dictionary<TalentStatIdentifier, float> talentStats = new();
        private readonly Item item;

        public ItemStatManager(Item item) => this.item = item;

        public void ApplyStat(ItemTalentStats stat, bool stackable, bool save, float value, CharacterTalent talent)
        {
            if (talent.Character?.ID is not { } characterId ||
                talent.Prefab?.Identifier is not { } talentIdentifier) { return; }

            var identifier = stackable
                ? TalentStatIdentifier.CreateStackable(stat, talentIdentifier, characterId)
                : TalentStatIdentifier.CreateUnstackable(stat, talentIdentifier, save);

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

        public void Save(XElement parent)
        {
            var element = new XElement("itemstats");

            foreach (var (key, value) in talentStats)
            {
                if (!key.Save) { continue; }

                var statElement = key.Serialize();
                statElement.Add(new XAttribute("value", value));

                element.Add(statElement);
            }

            parent.Add(element);
        }

        public void Load(XElement element)
        {
            foreach (XElement statElement in element.Elements())
            {
                if (!TalentStatIdentifier.TryLoadFromXML(statElement).TryUnwrap(out var identifier)) { continue; }

                var value = statElement.GetAttributeFloat("value", 0f);

                ApplyStatDirect(identifier, value);
            }
        }

        /// <summary>
        /// Used for setting the value value from network packet; bypassing all validity checks.
        /// </summary>
        public void ApplyStatDirect(TalentStatIdentifier identifier, float value)
            => talentStats[identifier] = value;

        /// <summary>
        /// Adjusts the value by multiplying it with the value of the talent stat
        /// </summary>
        public float GetAdjustedValueMultiplicative(ItemTalentStats stat, float originalValue)
        {
            float total = originalValue;

            foreach (var (key, value) in talentStats)
            {
                if (key.Stat != stat) { continue; }
                total *= value;
            }

            return total;
        }

        /// <summary>
        /// Adjusts the value by adding the value of the talent stat instead of multiplying it
        /// </summary>
        public float GetAdjustedValueAdditive(ItemTalentStats stat, float originalValue)
        {
            float total = originalValue;

            foreach (var (key, value) in talentStats)
            {
                if (key.Stat != stat) { continue; }
                total += value;
            }

            return total;
        }
    }
}
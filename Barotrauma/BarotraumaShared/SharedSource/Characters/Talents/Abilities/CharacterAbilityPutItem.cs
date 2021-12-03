using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityPutItem : CharacterAbility
    {
        private readonly string itemIdentifier;
        private readonly int amount;
        public override bool AppliesEffectOnIntervalUpdate => true;
        public CharacterAbilityPutItem(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            itemIdentifier = abilityElement.GetAttributeString("itemidentifier", "");
            amount = abilityElement.GetAttributeInt("amount", 1);
            if (string.IsNullOrEmpty(itemIdentifier))
            {
                DebugConsole.ThrowError($"Error in talent \"{characterAbilityGroup.CharacterTalent.DebugIdentifier}\" - itemIdentifier not defined.");
            }
        }

        protected override void ApplyEffect()
        {
            if (string.IsNullOrEmpty(itemIdentifier))
            {
                DebugConsole.ThrowError("Cannot put item in inventory - itemIdentifier not defined.");
                return;
            }

            ItemPrefab itemPrefab = ItemPrefab.Find(null, itemIdentifier);
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Cannot put item in inventory - item prefab " + itemIdentifier + " not found.");
                return;
            }
            for (int i = 0; i < amount; i++)
            {
                if (GameMain.GameSession?.RoundEnding ?? true)
                {
                    Item item = new Item(itemPrefab, Character.WorldPosition, Character.Submarine);
                    if (!Character.Inventory.TryPutItem(item, Character))
                    {
                        foreach (Item containedItem in Character.Inventory.AllItemsMod)
                        {
                            if (containedItem.OwnInventory?.TryPutItem(item, Character) ?? false) { break; }
                        }
                    }
                }
                else
                {
                    Entity.Spawner.AddToSpawnQueue(itemPrefab, Character.Inventory);
                }
            }
        }
    }
}

using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract partial class AIObjective
    {
        public static Color ObjectiveIconColor => Color.LightGray;

        public static Sprite GetSprite(Identifier identifier, Identifier option, Entity targetEntity)
        {
            if (identifier == Identifier.Empty)
            {
                return null;
            }
            if (OrderPrefab.Prefabs.ContainsKey(identifier))
            {
                OrderPrefab orderPrefab = OrderPrefab.Prefabs[identifier];
                if (option != Identifier.Empty && orderPrefab.OptionSprites.TryGetValue(option, out var optionSprite))
                {
                    return optionSprite;
                }
                if (targetEntity is Item targetItem && targetItem.Prefab.MinimapIcon != null)
                {
                    return targetItem.Prefab.MinimapIcon;
                }
                return orderPrefab.SymbolSprite;
            }
            return GUIStyle.GetComponentStyle($"{identifier}objectiveicon")?.GetDefaultSprite();
        }

        public Sprite GetSprite()
        {
            return GetSprite(Identifier, Option, (this as AIObjectiveOperateItem)?.OperateTarget);
        }
    }
}

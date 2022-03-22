using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract partial class AIObjective
    {
        public static Color ObjectiveIconColor => Color.LightGray;

        public static Sprite GetSprite(string identifier, string option, Entity targetEntity)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }
            identifier = identifier.RemoveWhitespace();
            if (Order.Prefabs.TryGetValue(identifier, out Order orderPrefab))
            {
                if (!string.IsNullOrEmpty(option) && orderPrefab.OptionSprites.TryGetValue(option, out var optionSprite))
                {
                    return optionSprite;
                }
                if (targetEntity is Item targetItem && targetItem.Prefab.MinimapIcon != null)
                {
                    return targetItem.Prefab.MinimapIcon;
                }
                return orderPrefab.SymbolSprite;
            }
            return GUI.Style.GetComponentStyle($"{identifier}objectiveicon")?.GetDefaultSprite();
        }

        public Sprite GetSprite()
        {
            return GetSprite(Identifier, Option, (this as AIObjectiveOperateItem)?.OperateTarget);
        }
    }
}

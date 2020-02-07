using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public class GUIColorSettings
    {
        // Inventory
        public static readonly Color InventorySlotColor = new Color(27, 140, 132);
        public static readonly Color InventorySlotEquippedColor = new Color(211, 227, 217);
        public static readonly Color EquipmentSlotEmptyColor = new Color(152, 148, 128);
        public static readonly Color EquipmentSlotColor = new Color(225, 211, 189);
        public static readonly Color EquipmentSlotIconColor = new Color(99, 70, 64);

        // Health HUD
        public static readonly Color BuffColorLow = Color.LightGreen;
        public static readonly Color BuffColorMedium = Color.Green;
        public static readonly Color BuffColorHigh = Color.DarkGreen;

        public static readonly Color DebuffColorLow = Color.DarkSalmon;
        public static readonly Color DebuffColorMedium = Color.Red;
        public static readonly Color DebuffColorHigh = Color.DarkRed;

        public static readonly Color HealthBarColorLow = Color.Red;
        public static readonly Color HealthBarColorMedium = Color.Orange;
        public static readonly Color HealthBarColorHigh = new Color(78, 114, 88);
    }
}

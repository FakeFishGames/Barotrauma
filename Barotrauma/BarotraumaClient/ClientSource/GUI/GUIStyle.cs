using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public static class GUIStyle
    {
        public readonly static ImmutableDictionary<Identifier, GUIFont> Fonts;
        public readonly static ImmutableDictionary<Identifier, GUISprite> Sprites;
        public readonly static ImmutableDictionary<Identifier, GUISpriteSheet> SpriteSheets;
        public readonly static ImmutableDictionary<Identifier, GUIColor> Colors;
        static GUIStyle()
        {
            var guiClassProperties = typeof(GUIStyle).GetFields(BindingFlags.Public | BindingFlags.Static);

            ImmutableDictionary<Identifier, T> getPropertiesOfType<T>() where T : class
            {
                return guiClassProperties
                    .Where(p => p.FieldType == typeof(T))
                    .Select(p => (p.Name.ToIdentifier(), p.GetValue(null) as T))
                    .ToImmutableDictionary();
            }

            Fonts = getPropertiesOfType<GUIFont>();
            Sprites = getPropertiesOfType<GUISprite>();
            SpriteSheets = getPropertiesOfType<GUISpriteSheet>();
            Colors = getPropertiesOfType<GUIColor>();
        }

        public readonly static PrefabCollection<GUIComponentStyle> ComponentStyles = new PrefabCollection<GUIComponentStyle>();

        public readonly static GUIFont Font = new GUIFont("Font");
        public readonly static GUIFont UnscaledSmallFont = new GUIFont("UnscaledSmallFont");
        public readonly static GUIFont SmallFont = new GUIFont("SmallFont");
        public readonly static GUIFont LargeFont = new GUIFont("LargeFont");
        public readonly static GUIFont SubHeadingFont = new GUIFont("SubHeadingFont");
        public readonly static GUIFont DigitalFont = new GUIFont("DigitalFont");
        public readonly static GUIFont HotkeyFont = new GUIFont("HotkeyFont");
        public readonly static GUIFont MonospacedFont = new GUIFont("MonospacedFont");

        public readonly static GUICursor CursorSprite = new GUICursor("Cursor");

        public readonly static GUISprite SubmarineLocationIcon = new GUISprite("SubmarineLocationIcon");
        public readonly static GUISprite Arrow = new GUISprite("Arrow");
        public readonly static GUISprite SpeechBubbleIcon = new GUISprite("SpeechBubbleIcon");
        public readonly static GUISprite BrokenIcon = new GUISprite("BrokenIcon");
        public readonly static GUISprite YouAreHereCircle = new GUISprite("YouAreHereCircle");

        public readonly static GUISprite Radiation = new GUISprite("Radiation");
        public readonly static GUISpriteSheet RadiationAnimSpriteSheet = new GUISpriteSheet("RadiationAnimSpriteSheet");

        public readonly static GUISpriteSheet SavingIndicator = new GUISpriteSheet("SavingIndicator");
        public readonly static GUISpriteSheet GenericThrobber = new GUISpriteSheet("GenericThrobber");

        public readonly static GUISprite UIGlow = new GUISprite("UIGlow");
        public readonly static GUISprite TalentGlow = new GUISprite("TalentGlow");
        public readonly static GUISprite PingCircle = new GUISprite("PingCircle");
        public readonly static GUISprite UIGlowCircular = new GUISprite("UIGlowCircular");
        public readonly static GUISprite UIGlowSolidCircular = new GUISprite("UIGlowSolidCircular");
        public readonly static GUISprite UIThermalGlow = new GUISprite("UIGlowSolidCircular");
        public readonly static GUISprite ButtonPulse = new GUISprite("ButtonPulse");
        public readonly static GUISprite WalletPortraitBG = new GUISprite("WalletPortraitBG");
        public readonly static GUISprite CrewWalletIconSmall = new GUISprite("CrewWalletIconSmall");

        public readonly static GUISprite EndRoundButtonPulse = new GUISprite("EndRoundButtonPulse");

        public readonly static GUISpriteSheet FocusIndicator = new GUISpriteSheet("FocusIndicator");
        
        public readonly static GUISprite IconOverflowIndicator = new GUISprite("IconOverflowIndicator");

        /// <summary>
        /// General green color used for elements whose colors are set from code
        /// </summary>
        public readonly static GUIColor Green = new GUIColor("Green");

        /// <summary>
        /// General red color used for elements whose colors are set from code
        /// </summary>
        public readonly static GUIColor Orange = new GUIColor("Orange");

        /// <summary>
        /// General red color used for elements whose colors are set from code
        /// </summary>
        public readonly static GUIColor Red = new GUIColor("Red");

        /// <summary>
        /// General blue color used for elements whose colors are set from code
        /// </summary>
        public readonly static GUIColor Blue = new GUIColor("Blue");

        /// <summary>
        /// General yellow color used for elements whose colors are set from code
        /// </summary>
        public readonly static GUIColor Yellow = new GUIColor("Yellow");

        /// <summary>
        /// Color to display the name of modded servers in the server list.
        /// </summary>
        public readonly static GUIColor ModdedServerColor = new GUIColor("ModdedServerColor");

        public readonly static GUIColor ColorInventoryEmpty = new GUIColor("ColorInventoryEmpty");
        public readonly static GUIColor ColorInventoryHalf = new GUIColor("ColorInventoryHalf");
        public readonly static GUIColor ColorInventoryFull = new GUIColor("ColorInventoryFull");
        public readonly static GUIColor ColorInventoryBackground = new GUIColor("ColorInventoryBackground");
        public readonly static GUIColor ColorInventoryEmptyOverlay = new GUIColor("ColorInventoryEmptyOverlay");

        public readonly static GUIColor TextColorNormal = new GUIColor("TextColorNormal");
        public readonly static GUIColor TextColorBright = new GUIColor("TextColorBright");
        public readonly static GUIColor TextColorDark = new GUIColor("TextColorDark");
        public readonly static GUIColor TextColorDim = new GUIColor("TextColorDim");

        public readonly static GUIColor ItemQualityColorPoor = new GUIColor("ItemQualityColorPoor");
        public readonly static GUIColor ItemQualityColorNormal = new GUIColor("ItemQualityColorNormal");
        public readonly static GUIColor ItemQualityColorGood = new GUIColor("ItemQualityColorGood");
        public readonly static GUIColor ItemQualityColorExcellent = new GUIColor("ItemQualityColorExcellent");
        public readonly static GUIColor ItemQualityColorMasterwork = new GUIColor("ItemQualityColorMasterwork");
            
        public readonly static GUIColor ColorReputationVeryLow = new GUIColor("ColorReputationVeryLow");
        public readonly static GUIColor ColorReputationLow = new GUIColor("ColorReputationLow");
        public readonly static GUIColor ColorReputationNeutral = new GUIColor("ColorReputationNeutral");
        public readonly static GUIColor ColorReputationHigh = new GUIColor("ColorReputationHigh");
        public readonly static GUIColor ColorReputationVeryHigh = new GUIColor("ColorReputationVeryHigh");

        // Inventory
        public readonly static GUIColor EquipmentSlotIconColor = new GUIColor("EquipmentSlotIconColor");

        // Health HUD
        public readonly static GUIColor BuffColorLow = new GUIColor("BuffColorLow");
        public readonly static GUIColor BuffColorMedium = new GUIColor("BuffColorMedium");
        public readonly static GUIColor BuffColorHigh = new GUIColor("BuffColorHigh");

        public readonly static GUIColor DebuffColorLow = new GUIColor("DebuffColorLow");
        public readonly static GUIColor DebuffColorMedium = new GUIColor("DebuffColorMedium");
        public readonly static GUIColor DebuffColorHigh = new GUIColor("DebuffColorHigh");

        public readonly static GUIColor HealthBarColorLow = new GUIColor("HealthBarColorLow");
        public readonly static GUIColor HealthBarColorMedium = new GUIColor("HealthBarColorMedium");
        public readonly static GUIColor HealthBarColorHigh = new GUIColor("HealthBarColorHigh");
        public readonly static GUIColor HealthBarColorPoisoned = new GUIColor("HealthBarColorPoisoned");

        public static Point ItemFrameMargin 
        {
            get 
            { 
                Point size = new Point(50, 56).Multiply(GUI.SlicedSpriteScale);

                var style = GetComponentStyle("ItemUI"); 
                var sprite = style?.Sprites[GUIComponent.ComponentState.None].First();
                if (sprite != null)
                {
                    size.X = Math.Min(sprite.Slices[0].Width + sprite.Slices[2].Width, size.X);
                    size.Y = Math.Min(sprite.Slices[0].Height + sprite.Slices[6].Height, size.Y);
                }
                return size;
            } 
        }

        public static Point ItemFrameOffset => new Point(0, 3).Multiply(GUI.SlicedSpriteScale);

        public static GUIComponentStyle GetComponentStyle(string styleName)
        {
            return GetComponentStyle(styleName.ToIdentifier());
        }

        public static GUIComponentStyle GetComponentStyle(Identifier identifier)
            => ComponentStyles.TryGet(identifier, out var style) ? style : null;

        public static void Apply(GUIComponent targetComponent, string styleName = "", GUIComponent parent = null)
        {
            Apply(targetComponent, styleName.ToIdentifier(), parent);
        }

        public static void Apply(GUIComponent targetComponent, Identifier styleName, GUIComponent parent = null)
        {
            GUIComponentStyle componentStyle;
            if (parent != null)
            {
                GUIComponentStyle parentStyle = parent.Style;

                if (parentStyle == null)
                {
                    Identifier parentStyleName = parent.GetType().Name.ToIdentifier();

                    if (!ComponentStyles.ContainsKey(parentStyleName))
                    {
                        DebugConsole.ThrowError($"Couldn't find a GUI style \"{parentStyleName}\"");
                        return;
                    }
                    parentStyle = ComponentStyles[parentStyleName];
                }
                Identifier childStyleName = styleName.IsEmpty ? targetComponent.GetType().Name.ToIdentifier() : styleName;
                parentStyle.ChildStyles.TryGetValue(childStyleName, out componentStyle);
            }
            else
            {
                Identifier styleIdentifier = styleName.ToIdentifier();
                if (styleIdentifier == Identifier.Empty)
                {
                    styleIdentifier = targetComponent.GetType().Name.ToIdentifier();
                }
                if (!ComponentStyles.ContainsKey(styleIdentifier))
                {
                    DebugConsole.ThrowError($"Couldn't find a GUI style \"{styleIdentifier}\"");
                    return;
                }
                componentStyle = ComponentStyles[styleIdentifier];
            }

            targetComponent.ApplyStyle(componentStyle);
        }

        public static GUIColor GetQualityColor(int quality)
        {
            switch (quality)
            {
                case 1:
                    return ItemQualityColorGood;
                case 2:
                    return ItemQualityColorExcellent;
                case 3:
                    return ItemQualityColorMasterwork;
                case -1:
                    return ItemQualityColorPoor;
                default:
                    return ItemQualityColorNormal;
            }
        }

        public static void RecalculateFonts()
        {
            foreach (var font in Fonts.Values)
            {
                font.Prefabs.ForEach(p => p.LoadFont());
            }
        }

        public static void RecalculateSizeRestrictions()
        {
            foreach (var componentStyle in ComponentStyles)
            {
                componentStyle.RefreshSize();
            }
        }
    }
}

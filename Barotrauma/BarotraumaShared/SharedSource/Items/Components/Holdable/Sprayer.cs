using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Sprayer : RangedWeapon
    {
        [Serialize(0.0f, IsPropertySaveable.No, description: "The distance at which the item can spray walls.")]
        public float Range { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "How fast the item changes the color of the walls.")]
        public float SprayStrength { get; set; }

        public readonly ImmutableDictionary<Identifier, Color> LiquidColors;
        public ItemContainer LiquidContainer { get; private set; }

        public Sprayer(Item item, ContentXElement element) : base(item, element)
        {
            item.IsShootable = true;
            item.RequireAimToUse = true;

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "paintcolors":
                        {
                            var liquidColors = new Dictionary<Identifier, Color>();
                            foreach (XElement paintElement in subElement.Elements())
                            {
                                Identifier paintName = paintElement.GetAttributeIdentifier("paintitem", Identifier.Empty);
                                Color paintColor = paintElement.GetAttributeColor("color", Color.Transparent);

                                if (paintName != string.Empty)
                                {
                                    liquidColors.Add(paintName, paintColor);
                                }
                            }
                            LiquidColors = liquidColors.ToImmutableDictionary();
                        }
                        break;
                }
            }
            InitProjSpecific(element);
        }

        public override void OnItemLoaded()
        {
            LiquidContainer = item.GetComponent<ItemContainer>();
        }

        partial void InitProjSpecific(ContentXElement element);

#if SERVER
        public override bool Use(float deltaTime, Character character = null)
        {
            return character != null || character.Removed;
        }
#endif

    }
}

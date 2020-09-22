using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Sprayer : RangedWeapon
    {
        [Serialize(0.0f, false, description: "The distance at which the item can spray walls.")]
        public float Range { get; set; }

        [Serialize(1.0f, false, description: "How fast the item changes the color of the walls.")]
        public float SprayStrength { get; set; }

        private readonly Dictionary<string, Color> liquidColors;
        private ItemContainer liquidContainer;

        public Sprayer(Item item, XElement element) : base(item, element)
        {
            item.IsShootable = true;
            item.RequireAimToUse = true;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "paintcolors":
                        {
                            liquidColors = new Dictionary<string, Color>();
                            foreach (XElement paintElement in subElement.Elements())
                            {
                                string paintName = paintElement.GetAttributeString("paintitem", string.Empty);
                                Color paintColor = paintElement.GetAttributeColor("color", Color.Transparent);

                                if (paintName != string.Empty)
                                {
                                    liquidColors.Add(paintName, paintColor);
                                }
                            }
                        }
                        break;
                }
            }
            InitProjSpecific(element);
        }

        public override void OnItemLoaded()
        {
            liquidContainer = item.GetComponent<ItemContainer>();
        }

        partial void InitProjSpecific(XElement element);

#if SERVER
        public override bool Use(float deltaTime, Character character = null)
        {
            return character != null || character.Removed;
        }
#endif

    }
}

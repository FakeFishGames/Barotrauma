using Barotrauma.Networking;
using System.Xml.Linq;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma.Items.Components
{
    class NameTag : ItemComponent
    {
        [InGameEditable, Serialize("", false, description: "Name written on the tag.", alwaysUseInstanceValues: true)]
        public string WrittenName { get; set; }

        public NameTag(Item item, XElement element) : base(item, element)
        {
            AllowInGameEditing = true;
            DrawHudWhenEquipped = true;
            item.EditableWhenEquipped = true;
        }
    }
}
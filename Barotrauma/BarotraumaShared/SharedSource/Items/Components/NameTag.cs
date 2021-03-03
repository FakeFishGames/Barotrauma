using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class NameTag : ItemComponent
    {
        [InGameEditable(MaxLength = 32), Serialize("", false, description: "Name written on the tag.", alwaysUseInstanceValues: true)]
        public string WrittenName { get; set; }

        public NameTag(Item item, XElement element) : base(item, element)
        {
            AllowInGameEditing = true;
            DrawHudWhenEquipped = true;
            item.EditableWhenEquipped = true;
        }
    }
}
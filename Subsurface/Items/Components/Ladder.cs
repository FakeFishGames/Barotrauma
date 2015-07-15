using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Ladder : ItemComponent
    {

        public Ladder(Item item, XElement element)
            : base(item, element)
        {
        }

        public override bool Select(Character character = null)
        {
            if (character == null) return false;

            character.AnimController.Anim = AnimController.Animation.Climbing;
            //picker.SelectedConstruction = item;

            return true;
        }
    }
}

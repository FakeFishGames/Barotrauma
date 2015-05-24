using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Ladder : ItemComponent
    {

        public Ladder(Item item, XElement element)
            : base(item, element)
        {
        }

        public override bool Pick(Character picker = null)
        {
            if (picker == null) return false;

            picker.animController.anim = AnimController.Animation.Climbing;
            //picker.SelectedConstruction = item;

            return true;
        }
    }
}

using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Ladder : ItemComponent
    {

        public Ladder(Item item, XElement element)
            : base(item, element)
        {
        }

        public override bool Select(Character character)
        {
            if (character == null || character.LockHands || character.Removed || !(character.AnimController is HumanoidAnimController)) return false;

            character.AnimController.Anim = AnimController.Animation.Climbing;
            return true;
        }
    }
}

using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Ladder : ItemComponent
    {
        public static List<Ladder> List { get; } = new List<Ladder>();

        public Ladder(Item item, XElement element)
            : base(item, element)
        {
            List.Add(this);
        }

        public override bool Select(Character character)
        {
            if (character == null || character.LockHands || character.Removed || !(character.AnimController is HumanoidAnimController)) return false;

            character.AnimController.Anim = AnimController.Animation.Climbing;
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            List.Remove(this);
        }
    }
}

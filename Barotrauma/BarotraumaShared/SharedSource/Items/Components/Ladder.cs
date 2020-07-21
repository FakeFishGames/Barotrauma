using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Ladder : ItemComponent
    {
        public static List<Ladder> List { get; } = new List<Ladder>();

        public Ladder(Item item, XElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
            List.Add(this);
        }

        partial void InitProjSpecific(XElement element);

        public override bool Select(Character character)
        {
            if (character == null || character.LockHands || character.Removed || !(character.AnimController is HumanoidAnimController)) return false;

            character.AnimController.Anim = AnimController.Animation.Climbing;
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            RemoveProjSpecific();
            List.Remove(this);
        }

        partial void RemoveProjSpecific();
    }
}

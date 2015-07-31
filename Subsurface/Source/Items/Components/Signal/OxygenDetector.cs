using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        private Hull hull;

        public OxygenDetector(Item item, XElement element)
            : base (item, element)
        {
            hull = Hull.FindHull(item.Position);

            isActive = true;
        }

        public override void OnMapLoaded()
        {
            hull = Hull.FindHull(item.Position);
        }

        public override void Move(Microsoft.Xna.Framework.Vector2 amount)
        {
            hull = Hull.FindHull(item.Position);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (hull == null) return;
            
            item.SendSignal(((int)hull.OxygenPercentage).ToString(), "signal_out");
            
        }

    }
}

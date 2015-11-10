using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        private Hull hull;

        public WaterDetector(Item item, XElement element)
            : base (item, element)
        {
            hull = Hull.FindHull(item.Position);

            IsActive = true;
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

            float waterDepth = hull.Volume / hull.Size.X;

            bool underWater = (hull.Rect.Y-hull.Rect.Height + waterDepth)>item.Position.Y;
            
            item.SendSignal(underWater ? "1" : "0", "signal_out");            
        }
    }
}

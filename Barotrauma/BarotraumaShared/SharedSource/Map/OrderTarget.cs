using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class OrderTarget : ISpatialEntity
    {
        public Vector2 Position { get; private set; }
        public Hull Hull { get; private set; }

        public Vector2 WorldPosition => Submarine == null ? Position : Position + Submarine.Position;
        public Vector2 SimPosition => ConvertUnits.ToSimUnits(Position);
        public Submarine Submarine => Hull?.Submarine;

        public OrderTarget(Vector2 position, Hull hull, bool creatingFromExistingData = false)
        {
            if (!creatingFromExistingData && hull?.Submarine != null) { position -= hull.Submarine.Position; }
            Position = position;
            Hull = hull;
        }
    }
}

using Microsoft.Xna.Framework;

namespace Barotrauma
{
    interface ISpatialEntity
    {
        Vector2 Position { get; }
        Vector2 WorldPosition { get; }
        Vector2 SimPosition { get; }
        Submarine Submarine { get; }
        bool IgnoreByAI => false;
    }
}

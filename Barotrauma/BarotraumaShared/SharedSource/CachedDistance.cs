using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public class CachedDistance(Vector2 startWorldPos, Vector2 endWorldPos, float dist, string label, double recalculationTime)
    {
        public readonly Vector2 StartWorldPos = startWorldPos;
        public readonly Vector2 EndWorldPos = endWorldPos;
        public readonly float Distance = dist;
        public double RecalculationTime = recalculationTime;

        public string Label = label;

        public bool ShouldUpdateDistance(Vector2 currentStartWorldPos, Vector2 currentEndWorldPos, float minDistanceToUpdate = 500.0f)
        {
            if (Timing.TotalTime < RecalculationTime) { return false; }
            float minDistSquared = minDistanceToUpdate * minDistanceToUpdate;
            return Vector2.DistanceSquared(StartWorldPos, currentStartWorldPos) > minDistSquared ||
                Vector2.DistanceSquared(EndWorldPos, currentEndWorldPos) > minDistSquared;
        }
    }

    public readonly record struct CachedLocation(Vector2 Location, double RecalculationTime);
}

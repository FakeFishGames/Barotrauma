using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public class CachedDistance
    {
        public readonly Vector2 StartWorldPos;
        public readonly Vector2 EndWorldPos;
        public readonly float Distance;
        public double RecalculationTime;

        public CachedDistance(Vector2 startWorldPos, Vector2 endWorldPos, float dist, double recalculationTime)
        {
            StartWorldPos = startWorldPos;
            EndWorldPos = endWorldPos;
            Distance = dist;
            RecalculationTime = recalculationTime;
        }

        public bool ShouldUpdateDistance(Vector2 currentStartWorldPos, Vector2 currentEndWorldPos, float minDistanceToUpdate = 500.0f)
        {
            if (Timing.TotalTime < RecalculationTime) { return false; }
            float minDistSquared = minDistanceToUpdate * minDistanceToUpdate;
            return Vector2.DistanceSquared(StartWorldPos, currentStartWorldPos) > minDistSquared ||
                Vector2.DistanceSquared(EndWorldPos, currentEndWorldPos) > minDistSquared;
        }
    }
}

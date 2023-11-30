using Microsoft.Xna.Framework;

namespace Barotrauma;

readonly record struct Triangle2D(Vector2 A, Vector2 B, Vector2 C)
{
    public bool Contains(Vector2 point)
    {
        // Get the half-plane that the point lands in, for each side of the triangle
        int halfPlaneAb = MathUtils.VectorOrientation(A, B, point);
        int halfPlaneBc = MathUtils.VectorOrientation(B, C, point);
        int halfPlaneCa = MathUtils.VectorOrientation(C, A, point);

        // The intersection of three half-planes derived from the three sides of the triangle
        // is the triangle itself, so check for the point being in those three half-planes
        bool allNonNegative = halfPlaneAb >= 0 && halfPlaneBc >= 0 && halfPlaneCa >= 0;
        bool allNonPositive = halfPlaneAb <= 0 && halfPlaneBc <= 0 && halfPlaneCa <= 0;

        return allNonNegative || allNonPositive;
    }
}

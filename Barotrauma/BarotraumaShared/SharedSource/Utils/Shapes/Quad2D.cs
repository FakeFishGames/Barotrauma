using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma;

readonly record struct Quad2D(Vector2 A, Vector2 B, Vector2 C, Vector2 D)
{
    public Vector2 Centroid => (A + B + C + D) / 4;

    public static Quad2D FromRectangle(RectangleF rectangle)
    {
        return new Quad2D(
            A: (rectangle.Left, rectangle.Top),
            B: (rectangle.Right, rectangle.Top),
            C: (rectangle.Right, rectangle.Bottom),
            D: (rectangle.Left, rectangle.Bottom));
    }

    public static Quad2D FromSubmarineRectangle(RectangleF rectangle)
    {
        return new Quad2D(
            A: (rectangle.X, rectangle.Y),
            B: (rectangle.X + rectangle.Width, rectangle.Y),
            C: (rectangle.X + rectangle.Width, rectangle.Y - rectangle.Height),
            D: (rectangle.X, rectangle.Y - rectangle.Height));
    }

    public Quad2D Rotated(float radians)
    {
        return new Quad2D(
            A: MathUtils.RotatePointAroundTarget(point: A, target: Centroid, radians: radians),
            B: MathUtils.RotatePointAroundTarget(point: B, target: Centroid, radians: radians),
            C: MathUtils.RotatePointAroundTarget(point: C, target: Centroid, radians: radians),
            D: MathUtils.RotatePointAroundTarget(point: D, target: Centroid, radians: radians));
    }

    public RectangleF BoundingAxisAlignedRectangle
    {
        get
        {
            Vector2 min = (
                X: Math.Min(A.X, Math.Min(B.X, Math.Min(C.X, D.X))),
                Y: Math.Min(A.Y, Math.Min(B.Y, Math.Min(C.Y, D.Y))));
            Vector2 max = (
                X: Math.Max(A.X, Math.Max(B.X, Math.Max(C.X, D.X))),
                Y: Math.Max(A.Y, Math.Max(B.Y, Math.Max(C.Y, D.Y))));
            return new RectangleF(location: min, size: max - min);
        }
    }

    public bool TryGetEdges(Span<(Vector2 A, Vector2 B)> outputSpan)
    {
        if (outputSpan.Length < 4) { return false; }

        outputSpan[0] = (A, B);
        outputSpan[1] = (B, C);
        outputSpan[2] = (C, D);
        outputSpan[3] = (D, A);
        return true;
    }

    public bool Contains(Vector2 point)
    {
        // Break up the quad into two triangles and then see if the point is in either triangle.
        // Since quads can be concave, care needs to be taken when splitting in two.

        (Triangle2D triangle1, Triangle2D triangle2)
            = (new Triangle2D(A, B, C), new Triangle2D(A, D, C));

        // If D is inside of the triangle ABC, or B is inside of the triangle ADC,
        // then the quad is concave and we split at the wrong diagonal.
        // Splitting at the other diagonal should be fine.
        if (triangle1.Contains(D) || triangle2.Contains(B))
        {
            (triangle1, triangle2) = (new Triangle2D(B, C, D), new Triangle2D(B, A, D));
        }

        return triangle1.Contains(point) || triangle2.Contains(point);
    }

    public bool Intersects(Quad2D other)
    {
        if (!BoundingAxisAlignedRectangle.Intersects(other.BoundingAxisAlignedRectangle))
        {
            return false;
        }

        if (Contains(other.A)) { return true; }
        if (Contains(other.B)) { return true; }
        if (Contains(other.C)) { return true; }
        if (Contains(other.D)) { return true; }

        if (other.Contains(A)) { return true; }
        if (other.Contains(B)) { return true; }
        if (other.Contains(C)) { return true; }
        if (other.Contains(D)) { return true; }

        Span<(Vector2 A, Vector2 B)> myEdges = stackalloc (Vector2 A, Vector2 B)[4];
        TryGetEdges(myEdges);
        Span<(Vector2 A, Vector2 B)> otherEdges = stackalloc (Vector2 A, Vector2 B)[4];
        other.TryGetEdges(otherEdges);
        foreach (var edge in myEdges)
        {
            foreach (var otherEdge in otherEdges)
            {
                if (MathUtils.LineSegmentsIntersect(edge.A, edge.B, otherEdge.A, otherEdge.B)) { return true; }
            }
        }
        return false;
    }
}

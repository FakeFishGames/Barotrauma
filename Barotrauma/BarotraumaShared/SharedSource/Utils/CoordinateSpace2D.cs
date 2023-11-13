using Microsoft.Xna.Framework;
namespace Barotrauma.Utils;

public struct CoordinateSpace2D
{
    public static readonly CoordinateSpace2D CanonicalSpace = new CoordinateSpace2D
    {
        Origin = Vector2.Zero,
        I = Vector2.UnitX,
        J = Vector2.UnitY
    };

    public Vector2 Origin;
    public Vector2 I;
    public Vector2 J;

    public Matrix LocalToCanonical
        => new Matrix(
                m11: I.X, m12: I.Y, m13: 0f, m14: 0f, 
                m21: J.X, m22: J.Y, m23: 0f, m24: 0f,
                m31:  0f, m32:  0f, m33: 1f, m34: 0f,
                m41:  0f, m42:  0f, m43: 0f, m44: 1f)
            * Matrix.CreateTranslation(Origin.X, Origin.Y, 0f);

    public Matrix CanonicalToLocal => Matrix.Invert(LocalToCanonical);
}

using Barotrauma.Utils;
using FluentAssertions;
using FsCheck;
using Microsoft.Xna.Framework;
using System;
using Xunit;
namespace TestProject;

public class CoordinateSpace2DTests
{
    class CustomGenerators
    {
        public static Arbitrary<Vector2> Vector2Generator()
        {
            const int intRange = 1 << 22;
            const float intToFloat = 1 << 19;
            
            return Arb.From(
                from int x in Gen.Choose(-intRange, intRange)
                from int y in Gen.Choose(-intRange, intRange)
                select new Vector2(x / intToFloat, y / intToFloat));
        }
    }

    public CoordinateSpace2DTests()
    {
        Arb.Register<CustomGenerators>();
    }

    [Fact]
    public void TestLocalToCanonical()
    { 
        void testCase(Tuple<Vector2, Vector2, Vector2, Vector2> args)
        {
            var (vector, origin, i, j) = args;

            if (Vector2.DistanceSquared(i, j) <= 0.01f) { return; }

            var space = new CoordinateSpace2D
            {
                Origin = origin,
                I = i,
                J = j
            };

            Assert.True(Vector2.DistanceSquared(
                Vector2.Transform(vector, space.LocalToCanonical),
                origin + vector.X * i + vector.Y * j) < 0.01f);
        }
        
        Prop.ForAll(
            Arb.Generate<Tuple<Vector2, Vector2, Vector2, Vector2>>().ToArbitrary(),
            testCase).QuickCheckThrowOnFailure();
    }
}

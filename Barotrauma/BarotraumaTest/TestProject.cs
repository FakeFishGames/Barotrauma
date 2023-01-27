using Barotrauma;
using FsCheck;
using Microsoft.Xna.Framework;

namespace TestProject
{
    public static class TestProject
    {
        public class CustomGenerators
        {
            public static Arbitrary<Vector2> Vector2Generator()
            {
                return Arb.From(from int x in Arb.Generate<int>()
                                from int y in Arb.Generate<int>()
                                select new Vector2(x, y));
            }

            public static Arbitrary<Identifier> IdentifierGenerator()
            {
                return Arb.From(from string value in Arb.Generate<string>().Where(static s => s != null)
                                select new Identifier(value));
            }

            public static Arbitrary<Color> ColorGenerator()
            {
                return Arb.From(from int r in Gen.Choose(0, 255)
                                from int g in Gen.Choose(0, 255)
                                from int b in Gen.Choose(0, 255)
                                select new Color(r, g, b));
            }

            public static Arbitrary<Option<T>> OptionalGenerator<T>()
            {
                return Arb.From(from T x in Arb.Generate<T>()
                                from bool isNone in Arb.Generate<bool>()
                                select x is null || isNone ? Option<T>.None() : Option<T>.Some(x));
            }
        }
    }
}
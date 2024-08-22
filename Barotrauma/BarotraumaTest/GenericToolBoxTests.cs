#nullable enable

using System;
using Xunit;
using Barotrauma;
using FluentAssertions;
using FsCheck;
using Microsoft.Xna.Framework;

namespace TestProject;

public sealed class GenericToolBoxTests
{
    public class CustomGenerators
    {
        public static Arbitrary<DifferentIdentifierPair> IdentifierPairGenerator()
        {
            return Arb.From(from Identifier first in Arb.Generate<Identifier>().Where(first => !first.Value.Contains('~'))
                            from Identifier second in Arb.Generate<Identifier>().Where(second => second != first && !second.Value.Contains('~'))
                            select new DifferentIdentifierPair(first, second));
        }
    }

    public readonly struct DifferentIdentifierPair
    {
        public readonly Identifier First,
                                   Second;

        public DifferentIdentifierPair(Identifier first, Identifier second)
        {
            if (first == second) { throw new InvalidOperationException("Identifiers must be different"); }
            //tildes have a special meaning in stat identifiers, don't use them
            if (first.Value.Contains('~')) { throw new InvalidOperationException($"{first} is not a valid identifier."); }
            if (second.Value.Contains('~')) { throw new InvalidOperationException($"{second} is not a valid identifier."); }

            First = first;
            Second = second;
        }
    }

    public GenericToolBoxTests()
    {
        Arb.Register<TestProject.CustomGenerators>();
        Arb.Register<CustomGenerators>();
    }

    [Fact]
    public void MatchesStatIdentifier()
    {
        Prop.ForAll<DifferentIdentifierPair>(static pair =>
        {
            ToolBox.StatIdentifierMatches(pair.First, $"{pair.First}~{pair.Second}".ToIdentifier()).Should().BeTrue();
            ToolBox.StatIdentifierMatches($"{pair.First}~{pair.Second}".ToIdentifier(), pair.First).Should().BeTrue();
            ToolBox.StatIdentifierMatches(pair.First, pair.First).Should().BeTrue();

            ToolBox.StatIdentifierMatches(pair.First, $"{pair.Second}~{pair.First}".ToIdentifier()).Should().BeFalse();
            ToolBox.StatIdentifierMatches(pair.First, pair.Second).Should().BeFalse();
        }).VerboseCheckThrowOnFailure();
    }

    [Fact]
    public void PointOnRectClosestToPoint()
    {
        RectangleF rect1 = new(0, 0, 5, 5);
        Vector2 point1 = new(10, 10);

        // Should be the bottom right corner of the rectangle
        Test(rect1, point1, expectedClosest: new Vector2(5, 5));
        
        RectangleF rect2 = new(0, 0, 5, 5);
        Vector2 point2 = new(2, 10);

        // Should be the bottom edge of the rectangle at x = 2 since the point is between the bottom left and bottom right corners
        Test(rect2, point2, expectedClosest: new Vector2(2, 5));
        
        RectangleF rect3 = new(0, 0, 5, 5);
        Vector2 point3 = new(-10, -3);

        // Should be the top left corner of the rectangle
        Test(rect3, point3, expectedClosest: new Vector2(0, 0));

        RectangleF rect4 = new(0, 0, 100, 100);
        Vector2 point4 = new(55, 52);

        // Should be the point itself since it's inside the rectangle
        Test(rect4, point4, expectedClosest: point4);
        
        RectangleF rect5 = new(0, 0, 100, 100);
        Vector2 point5 = new(55, 102);

        // Should be the top edge of the rectangle at y = 100 since the point is between the top left and top right corners
        Test(rect5, point5, expectedClosest: new Vector2(55, 100));

        void Test(RectangleF rect, Vector2 point, Vector2 expectedClosest)
        {
            var closest = ToolBox.GetClosestPointOnRectangle(rect, point);
            closest.Should().BeEquivalentTo(expectedClosest);
        }
    }
}

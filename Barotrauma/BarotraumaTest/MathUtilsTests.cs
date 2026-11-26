using Barotrauma;
using FluentAssertions;
using Microsoft.Xna.Framework;
using System;
using Xunit;

namespace TestProject;

public class MathUtilsTests
{
    [Fact]
    public void TestNearlyEquals()
    {
        MathUtils.NearlyEqual(0.0f, 0.0f).Should().BeTrue();
        MathUtils.NearlyEqual(-float.Epsilon, float.Epsilon).Should().BeTrue();
        MathUtils.NearlyEqual(0.1f + 0.2f, 0.3f).Should().BeTrue();
        MathUtils.NearlyEqual(-1.0f, 1.0f).Should().BeFalse();
    }

    [Fact]
    public void TestWrapAngle()
    {
        MathUtils.NearlyEqual(MathUtils.WrapAnglePi(0.0f), 0.0f).Should().BeTrue();

        CheckWrapAnglePiNearlyEqual(0, 0).Should().BeTrue();
        CheckWrapAnglePiNearlyEqual(-90, -90).Should().BeTrue();
        CheckWrapAnglePiNearlyEqual(-90, 90).Should().BeFalse();
        CheckWrapAnglePiNearlyEqual(-180, 180).Should().BeTrue();
        CheckWrapAnglePiNearlyEqual(-190.0f, 170.0f).Should().BeTrue();
        CheckWrapAnglePiNearlyEqual(-360, 0).Should().BeTrue();
        CheckWrapAnglePiNearlyEqual(360, 0).Should().BeTrue();

        bool CheckWrapAnglePiNearlyEqual(float wrappedDeg, float deg)
        {
            float wrappedRad = MathUtils.WrapAnglePi(MathHelper.ToRadians(wrappedDeg));
            float rad = MathHelper.ToRadians(deg);
            return MathUtils.NearlyEqual(wrappedRad, rad) || MathUtils.NearlyEqual(Math.Abs(wrappedRad - rad), MathHelper.TwoPi);
        }

        CheckWrapAngleTwoPiNearlyEqual(0, 0).Should().BeTrue();
        CheckWrapAngleTwoPiNearlyEqual(90, 90).Should().BeTrue();
        CheckWrapAngleTwoPiNearlyEqual(-90, 270).Should().BeTrue();
        CheckWrapAngleTwoPiNearlyEqual(180, 180).Should().BeTrue();
        CheckWrapAngleTwoPiNearlyEqual(360 * 5, 0).Should().BeTrue();
        CheckWrapAngleTwoPiNearlyEqual(-360, 0).Should().BeTrue();

        bool CheckWrapAngleTwoPiNearlyEqual(float wrappedDeg, float deg)
        {
            float wrappedRad = MathUtils.WrapAngleTwoPi(MathHelper.ToRadians(wrappedDeg));
            float rad = MathHelper.ToRadians(deg);
            return MathUtils.NearlyEqual(wrappedRad, rad) || MathUtils.NearlyEqual(Math.Abs(wrappedRad - rad), MathHelper.TwoPi);
        }

        CheckShortestAngleNearlyEqual(0.0f, 0.0f, 0.0f).Should().BeTrue();
        CheckShortestAngleNearlyEqual(0.0f, 90.0f, 90.0f).Should().BeTrue();
        CheckShortestAngleNearlyEqual(0.0f, 360.0f, 0.0f).Should().BeTrue();
        CheckShortestAngleNearlyEqual(0.0f, -365.0f, -5.0f).Should().BeTrue();
        CheckShortestAngleNearlyEqual(180.0f, -180.0f, 0.0f).Should().BeTrue();
        CheckShortestAngleNearlyEqual(-355.0f, 5.0f, 10.0f);

        bool CheckShortestAngleNearlyEqual(float deg1, float deg2, float angle)
        {
            return MathUtils.NearlyEqual(MathUtils.GetShortestAngle(MathHelper.ToRadians(deg1), MathHelper.ToRadians(deg2)), MathHelper.ToRadians(angle));
        }
    }

    [Fact]
    public void TestUpscaleVector2Array()
    {
        Vector2[,] inputArray = new Vector2[,]
        {
            { new Vector2(0, 0), new Vector2(10, 10) },
            { new Vector2(20, 20), new Vector2(30, 30) }
        };

        int newWidth = 4;
        int newHeight = 4;

        Vector2[,] result = MathUtils.ResizeVector2Array(inputArray, newWidth, newHeight);

        MathUtils.NearlyEqual(new Vector2(0, 0), result[0, 0]).Should().BeTrue();
        MathUtils.NearlyEqual(new Vector2(30, 30), result[3, 3]).Should().BeTrue();
        MathUtils.NearlyEqual(new Vector2(20, 20), result[2, 2]).Should().BeTrue();
        MathUtils.NearlyEqual(new Vector2(26.666666f, 26.666666f), result[3, 2]).Should().BeTrue();
    }

    [Fact]
    public void TestDownScaleVector2Array()
    {
        Vector2[,] inputArray = new Vector2[,]
        {
            { new Vector2(0, 0), new Vector2(10, 10), new Vector2(20, 20) },
            { new Vector2(30, 30), new Vector2(40, 40), new Vector2(50, 50) },
            { new Vector2(60, 60), new Vector2(70, 70), new Vector2(80, 80) }
        };

        int newWidth = 2;
        int newHeight = 2;

        Vector2[,] result = MathUtils.ResizeVector2Array(inputArray, newWidth, newHeight);

        MathUtils.NearlyEqual(new Vector2(0, 0), result[0, 0]).Should().BeTrue();
        MathUtils.NearlyEqual(new Vector2(80, 80), result[1, 1]).Should().BeTrue();
    }

    [Fact]
    public void TestNoChangesToVector2Array()
    {
        Vector2[,] inputArray = new Vector2[,]
        {
            { new Vector2(0, 0), new Vector2(10, 10) },
            { new Vector2(20, 20), new Vector2(30, 30) }
        };

        int newWidth = 2;
        int newHeight = 2;

        Vector2[,] result = MathUtils.ResizeVector2Array(inputArray, newWidth, newHeight);

        MathUtils.NearlyEqual(new Vector2(0, 0), result[0, 0]).Should().BeTrue();
        MathUtils.NearlyEqual(new Vector2(30, 30), result[1, 1]).Should().BeTrue();
    }

    [Fact]
    public void TestLineSegmentsIntersect()
    {
        //non-parallel, intersecting
        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(2f, 2f),
            new Vector2(0f, 2f), new Vector2(2f, 0f))
            .Should().BeTrue();

        MathUtils.LineSegmentsIntersect(
            new Vector2(-1f, -1f), new Vector2(1f, 1f),
            new Vector2(-1f, 1f), new Vector2(1f, -1f))
            .Should().BeTrue();

        //non-parallel, non-intersecting
        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(2f, 2f), new Vector2(3f, 3f))
            .Should().BeFalse();

        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(2f, 1f),
            new Vector2(3f, 0f), new Vector2(4f, 1f))
            .Should().BeFalse();

        //parallel, non-collinear
        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(2f, 0f),
            new Vector2(0f, 1f), new Vector2(2f, 1f))
            .Should().BeFalse();

        MathUtils.LineSegmentsIntersect(
            new Vector2(-1f, 2f), new Vector2(1f, 2f),
            new Vector2(-1f, 3f), new Vector2(1f, 3f))
            .Should().BeFalse();

        //collinear, overlapping
        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(2f, 0f),
            new Vector2(1f, 0f), new Vector2(3f, 0f))
            .Should().BeTrue();

        MathUtils.LineSegmentsIntersect(
            new Vector2(-2f, 0f), new Vector2(2f, 0f),
            new Vector2(-1f, 0f), new Vector2(1f, 0f))
            .Should().BeTrue();

        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(2f, 0f),
            new Vector2(2f, 0f), new Vector2(4f, 0f))
            .Should().BeTrue();

        //collinear, non-overlapping
        MathUtils.LineSegmentsIntersect(
            new Vector2(721f, 403f), new Vector2(958f, 403f),
            new Vector2(1348f, 403f), new Vector2(1496f, 403f))
            .Should().BeFalse();

        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(2f, 0f), new Vector2(3f, 0f))
            .Should().BeFalse();

        //edge cases

        //touching at a single point
        MathUtils.LineSegmentsIntersect(
            new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(2f, 0f))
            .Should().BeTrue();

        //one segment is a point (degenerate)
        MathUtils.LineSegmentsIntersect(
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(2f, 2f)).
            Should().BeTrue(); // lies on the line

        MathUtils.LineSegmentsIntersect(
            new Vector2(3f, 3f), new Vector2(3f, 3f),
            new Vector2(0f, 0f), new Vector2(2f, 2f))
            .Should().BeFalse(); // outside
    }
}
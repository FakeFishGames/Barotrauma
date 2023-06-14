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
}
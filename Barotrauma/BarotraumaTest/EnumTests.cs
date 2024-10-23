extern alias Client;

using Barotrauma;
using FluentAssertions;
using Barotrauma.Extensions;
using Xunit;
using Client::Barotrauma;

namespace TestProject;

public class EnumTests
{
    [Fact]
    public void TestFlags()
    {
        TestLevelPositionType();
        TestAlignmentType();
    }
    
    private static void TestLevelPositionType()
    {
        Level.PositionType abyss = Level.PositionType.Abyss;
        abyss.HasFlag(Level.PositionType.Ruin).Should().BeFalse();
        abyss.HasAnyFlag(Level.PositionType.Ruin).Should().BeFalse();
        
        abyss.HasFlag(Level.PositionType.Abyss).Should().BeTrue();
        abyss.HasAnyFlag(Level.PositionType.Abyss).Should().BeTrue();

        abyss = Level.PositionType.Abyss | Level.PositionType.Ruin;
        abyss.HasFlag(Level.PositionType.Cave).Should().BeFalse();
        abyss.HasAnyFlag(Level.PositionType.Cave).Should().BeFalse();

        abyss.HasFlag(Level.PositionType.Abyss).Should().BeTrue();
        abyss.HasAnyFlag(Level.PositionType.Abyss).Should().BeTrue();

        abyss.HasFlag(Level.PositionType.Ruin).Should().BeTrue();
        abyss.HasAnyFlag(Level.PositionType.Ruin).Should().BeTrue();
        
        const Level.PositionType abyssOrOutpost = Level.PositionType.Abyss | Level.PositionType.Outpost;
        abyss.HasFlag(abyssOrOutpost).Should().BeFalse();
        abyss.HasAnyFlag(abyssOrOutpost).Should().BeTrue();
        
        (Level.PositionType.Abyss.AddFlag(Level.PositionType.Outpost) == abyssOrOutpost).Should().BeTrue();
        (abyssOrOutpost.RemoveFlag(Level.PositionType.Outpost) == Level.PositionType.Abyss).Should().BeTrue();
    }
    
    private static void TestAlignmentType()
    {
        const Alignment left = Alignment.Left;
        left.HasFlag(Alignment.Left).Should().BeTrue();
        left.HasAnyFlag(Alignment.Left).Should().BeTrue();
        
        left.HasFlag(Alignment.Center).Should().BeFalse();
        left.HasAnyFlag(Alignment.Center).Should().BeFalse();
        
        left.HasFlag(Alignment.TopLeft).Should().BeFalse();
        left.HasAnyFlag(Alignment.TopLeft).Should().BeTrue();
        
        const Alignment leftOrCenter = Alignment.Left | Alignment.Center;
        left.HasFlag(leftOrCenter).Should().BeFalse();
        left.HasAnyFlag(leftOrCenter).Should().BeTrue();
        
        const Alignment topLeft = Alignment.TopLeft;
        topLeft.HasFlag(left).Should().BeTrue();
        topLeft.HasAnyFlag(left).Should().BeTrue();
        
        topLeft.HasFlag(leftOrCenter).Should().BeFalse();
        topLeft.HasAnyFlag(leftOrCenter).Should().BeTrue();
    }
}
using Barotrauma;
using FluentAssertions;
using Barotrauma.Extensions;
using FluentAssertions;
using Xunit;

namespace TestProject;

public class EnumTests
{
    [Fact]
    public void TestFlags()
    {
        TestMissionType();
        TestLevelPositionType();
        TestAlignmentType();
    }
    
    private static void TestMissionType()
    {
        const MissionType beacon = MissionType.Beacon;
        beacon.HasFlag(MissionType.Cargo).Should().BeFalse();
        beacon.HasAnyFlag(MissionType.Cargo).Should().BeFalse();

        beacon.HasFlag(MissionType.Beacon).Should().BeTrue();
        beacon.HasAnyFlag(MissionType.Beacon).Should().BeTrue();

        const MissionType beaconOrCargo = MissionType.Beacon | MissionType.Cargo;
        beaconOrCargo.HasFlag(MissionType.Monster).Should().BeFalse();
        beaconOrCargo.HasAnyFlag(MissionType.Monster).Should().BeFalse();
        MissionType testEnum = MissionType.Beacon;
        testEnum.HasFlag(MissionType.Cargo).Should().BeFalse();
        testEnum.HasAnyFlag(MissionType.Cargo).Should().BeFalse();

        beaconOrCargo.HasFlag(MissionType.Beacon).Should().BeTrue();
        beaconOrCargo.HasAnyFlag(MissionType.Beacon).Should().BeTrue();
        testEnum.HasFlag(MissionType.Beacon).Should().BeTrue();
        testEnum.HasAnyFlag(MissionType.Beacon).Should().BeTrue();

        beaconOrCargo.HasFlag(MissionType.Cargo).Should().BeTrue();
        beaconOrCargo.HasAnyFlag(MissionType.Cargo).Should().BeTrue();
        
        const MissionType all = MissionType.All;
        all.HasFlag(MissionType.All).Should().BeTrue();
        all.HasAnyFlag(MissionType.All).Should().BeTrue();
        
        all.HasFlag(MissionType.Beacon).Should().BeTrue();
        all.HasAnyFlag(MissionType.Beacon).Should().BeTrue();
        
        all.HasFlag(MissionType.Beacon | MissionType.Salvage).Should().BeTrue();
        all.HasAnyFlag(MissionType.Beacon | MissionType.Salvage).Should().BeTrue();
        
        const MissionType manyTypes = MissionType.Beacon | MissionType.Monster;
        beaconOrCargo.HasFlag(manyTypes).Should().BeFalse();
        beaconOrCargo.HasAnyFlag(manyTypes).Should().BeTrue();
        
        beaconOrCargo.HasFlag(MissionType.All).Should().BeFalse();
        beaconOrCargo.HasAnyFlag(MissionType.All).Should().BeTrue();
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
using System;
using System.Linq;

namespace Barotrauma;

class CheckMissionAction : BinaryOptionAction
{
    public enum MissionType
    {
        Current,
        Selected,
        Available
    }

    [Serialize(MissionType.Current, IsPropertySaveable.Yes)]
    public MissionType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier MissionIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier MissionTag { get; set; }

    [Serialize(1, IsPropertySaveable.Yes)]
    public int MissionCount { get; set; }

    public CheckMissionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        MissionCount = Math.Max(MissionCount, 0);
    }

    protected override bool? DetermineSuccess()
    {
        var missions = Type switch
        {
            MissionType.Current => GameMain.GameSession?.Missions,
            MissionType.Selected => GameMain.GameSession?.Campaign?.Missions,
            MissionType.Available => GameMain.GameSession?.Map?.CurrentLocation?.AvailableMissions,
            _ => null
        };
        if (missions is not null)
        {
            if (!MissionIdentifier.IsEmpty)
            {
                return missions.Any(m => m.Prefab.Identifier == MissionIdentifier);
            }
            else if (!MissionTag.IsEmpty)
            {
                return missions.Count(m => m.Prefab.Tags.Contains(MissionTag.Value)) >= MissionCount;
            }
            else
            {
                return missions.Count() >= MissionCount;
            }
        }
        return MissionIdentifier.IsEmpty && MissionTag.IsEmpty && MissionCount == 0;
    }
}
#nullable enable
using System;
using System.Collections.Immutable;

namespace Barotrauma;

public enum AchievementStat
{
    GameLaunchCount,
    MonstersKilled,
    HumansKilled,
    KMsTraveled,
    HoursInEditor,
    MetersTraveled,
    MinutesInEditor
}

public static class AchievementStatExtension
{
    public static readonly ImmutableArray<AchievementStat> SteamStats = new []
    {
        AchievementStat.KMsTraveled,
        AchievementStat.HoursInEditor,
        AchievementStat.HumansKilled,
        AchievementStat.MonstersKilled
    }.ToImmutableArray();

    public static readonly ImmutableArray<AchievementStat> EosStats = new []
    {
        AchievementStat.MetersTraveled,
        AchievementStat.MinutesInEditor,
        AchievementStat.HumansKilled,
        AchievementStat.MonstersKilled
    }.ToImmutableArray();

    public static bool IsFloatStat(this AchievementStat stat) =>
        stat switch
        {
            AchievementStat.KMsTraveled => true,
            AchievementStat.HoursInEditor => true,
            _ => false
        };

    public static AchievementStat FromIdentifier(Identifier identifier) =>
        Enum.TryParse(value: identifier.ToString().ToLowerInvariant(), ignoreCase: true, result: out AchievementStat stat)
            ? stat
            : throw new ArgumentException($"Invalid achievement stat identifier \"{identifier}\"");

    public static (AchievementStat Stat, int Value) ToEos(this AchievementStat stat, float value) =>
        stat switch
        {
            AchievementStat.KMsTraveled => (AchievementStat.MetersTraveled, (int)MathF.Floor(value * 1000f)),
            AchievementStat.HoursInEditor => (AchievementStat.MinutesInEditor, (int)MathF.Floor(value * 60f)),
            _ => (stat, (int)value)
        };

    public static (AchievementStat Stat, float Value) ToSteam(this AchievementStat stat, float value) =>
        (stat, value);
}
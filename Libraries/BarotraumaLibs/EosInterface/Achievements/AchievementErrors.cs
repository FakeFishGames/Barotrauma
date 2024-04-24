namespace Barotrauma;

public static partial class EosInterface
{
    public enum AchievementUnlockError
    {
        Unknown,
        InvalidUser,
        EosNotInitialized,
        TimedOut,
        InvalidParameters,
        NotFound
    }

    public enum IngestStatError
    {
        Unknown,
        InvalidUser,
        EosNotInitialized,
        TimedOut,
        InvalidParameters,
        NotFound
    }

    public enum QueryStatsError
    {
        Unknown,
        InvalidUser,
        EosNotInitialized,
        TimedOut,
        InvalidParameters,
        NotFound
    }

    public enum QueryAchievementsError
    {
        Unknown,
        InvalidUser,
        InvalidProductUserID,
        EosNotInitialized,
        TimedOut,
        InvalidParameters,
        NotFound
    }
}
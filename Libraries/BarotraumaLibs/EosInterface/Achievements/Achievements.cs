using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Achievements
    {
        private static Implementation? LoadedImplementation => Core.LoadedImplementation;

        public static async Task<Result<uint, AchievementUnlockError>> UnlockAchievements(
            params Identifier[] achievementIds)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.UnlockAchievements(achievementIds)
                : Result.Failure(AchievementUnlockError.EosNotInitialized);

        public static async Task<Result<Unit, IngestStatError>> IngestStats(
            params (AchievementStat Stat, int IngestAmount)[] stats)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.IngestStats(stats)
                : Result.Failure(IngestStatError.EosNotInitialized);

        public static Task<Result<ImmutableDictionary<AchievementStat, int>, QueryStatsError>> QueryStats(
            params AchievementStat[] stats)
            => QueryStats(stats.ToImmutableArray());

        public static async Task<Result<ImmutableDictionary<AchievementStat, int>, QueryStatsError>> QueryStats(
            ImmutableArray<AchievementStat> stats)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.QueryStats(stats)
                : Result.Failure(QueryStatsError.EosNotInitialized);

        public static async Task<Result<ImmutableDictionary<Identifier, double>, QueryAchievementsError>>
            QueryPlayerAchievements()
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.QueryPlayerAchievements()
                : Result.Failure(QueryAchievementsError.EosNotInitialized);
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Result<uint, AchievementUnlockError>> UnlockAchievements(
            params Identifier[] achievementIds);

        public abstract Task<Result<Unit, IngestStatError>> IngestStats(
            params (AchievementStat Stat, int IngestAmount)[] stats);

        public abstract Task<Result<ImmutableDictionary<AchievementStat, int>, QueryStatsError>> QueryStats(
            ImmutableArray<AchievementStat> stats);

        public abstract Task<Result<ImmutableDictionary<Identifier, double>, QueryAchievementsError>>
            QueryPlayerAchievements();
    }
}
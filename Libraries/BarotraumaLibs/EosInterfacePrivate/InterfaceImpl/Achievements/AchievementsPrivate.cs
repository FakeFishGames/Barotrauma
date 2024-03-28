#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma;

namespace EosInterfacePrivate;

public static class AchievementsPrivate
{
    public static async Task<Result<uint, EosInterface.AchievementUnlockError>> UnlockAchievements(params Identifier[] achievements)
    {
        if (CorePrivate.AchievementsInterface is not { } achievementsInterface) { return Result.Failure(EosInterface.AchievementUnlockError.EosNotInitialized); }

        var loggedInUsers = IdQueriesPrivate.GetLoggedInPuids();

        if (loggedInUsers is not { Length: > 0 })
        {
            return Result.Failure(EosInterface.AchievementUnlockError.InvalidUser);
        }
        var loggedInUser = loggedInUsers[0];

        var achievementUnlockWaiter = new CallbackWaiter<Epic.OnlineServices.Achievements.OnUnlockAchievementsCompleteCallbackInfo>();
        var options = new Epic.OnlineServices.Achievements.UnlockAchievementsOptions
        {
            AchievementIds = achievements.Select(static i => new Epic.OnlineServices.Utf8String(i.Value.ToLowerInvariant())).ToArray(),
            UserId = Epic.OnlineServices.ProductUserId.FromString(loggedInUser.Value)
        };

        achievementsInterface.UnlockAchievements(options: ref options, clientData: null, completionDelegate: achievementUnlockWaiter.OnCompletion);
        var resultOption = await achievementUnlockWaiter.Task;

        if (!resultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.AchievementUnlockError.TimedOut);
        }

        return callbackResult.ResultCode switch
        {
            Epic.OnlineServices.Result.Success => Result.Success(callbackResult.AchievementsCount),
            Epic.OnlineServices.Result.InvalidParameters => Result.Failure(EosInterface.AchievementUnlockError.InvalidParameters),
            Epic.OnlineServices.Result.InvalidUser => Result.Failure(EosInterface.AchievementUnlockError.InvalidUser),
            Epic.OnlineServices.Result.NotFound => Result.Failure(EosInterface.AchievementUnlockError.NotFound),
            var unhandled => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.AchievementUnlockError.Unknown))
        };
    }

    public static async Task<Result<ImmutableDictionary<AchievementStat, int>, EosInterface.QueryStatsError>> QueryStats(ImmutableArray<AchievementStat> stats)
    {
        if (CorePrivate.StatsInterface is not { } statsInterface) { return Result.Failure(EosInterface.QueryStatsError.EosNotInitialized); }

        var loggedInUsers = IdQueriesPrivate.GetLoggedInPuids();

        if (loggedInUsers is not { Length: > 0 })
        {
            return Result.Failure(EosInterface.QueryStatsError.InvalidUser);
        }
        var loggedInUser = loggedInUsers[0];

        var convertedUserId = Epic.OnlineServices.ProductUserId.FromString(loggedInUser.Value);

        var options = new Epic.OnlineServices.Stats.QueryStatsOptions
        {
            LocalUserId = convertedUserId,
            TargetUserId = convertedUserId,
            StatNames = stats.Any()
                ? stats.Select(static s => new Epic.OnlineServices.Utf8String(s.ToIdentifier().Value.ToLowerInvariant())).ToArray()
                : default
        };

        var queryWaiter = new CallbackWaiter<Epic.OnlineServices.Stats.OnQueryStatsCompleteCallbackInfo>();
        statsInterface.QueryStats(options: ref options, clientData: null, completionDelegate: queryWaiter.OnCompletion);

        var resultOption = await queryWaiter.Task;

        if (!resultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.QueryStatsError.TimedOut);
        }

        if (callbackResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return callbackResult.ResultCode switch
            {
                Epic.OnlineServices.Result.InvalidParameters => Result.Failure(EosInterface.QueryStatsError.InvalidParameters),
                Epic.OnlineServices.Result.InvalidUser => Result.Failure(EosInterface.QueryStatsError.InvalidUser),
                Epic.OnlineServices.Result.NotFound => Result.Failure(EosInterface.QueryStatsError.NotFound),
                var unhandled => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.QueryStatsError.Unknown))
            };
        }

        var builder = ImmutableDictionary.CreateBuilder<AchievementStat, int>();

        if (stats.Length is 0)
        {
            var countOptions = new Epic.OnlineServices.Stats.GetStatCountOptions
            {
                TargetUserId = convertedUserId
            };
            uint count = statsInterface.GetStatsCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyIndexOptions = new Epic.OnlineServices.Stats.CopyStatByIndexOptions
                {
                    TargetUserId = convertedUserId,
                    StatIndex = i
                };
                var copyResult = statsInterface.CopyStatByIndex(ref copyIndexOptions, out var statOut);

                if (copyResult is Epic.OnlineServices.Result.Success && statOut is { Name: var name, Value: var value })
                {
                    builder.Add(AchievementStatExtension.FromIdentifier(new Identifier(name)), value);
                }
            }
        }
        else
        {
            foreach (AchievementStat stat in stats)
            {
                var copyOptions = new Epic.OnlineServices.Stats.CopyStatByNameOptions
                {
                    TargetUserId = convertedUserId,
                    Name = new Epic.OnlineServices.Utf8String(stat.ToString().ToLowerInvariant())
                };
                var copyResult = statsInterface.CopyStatByName(ref copyOptions, out var statOut);

                if (copyResult is Epic.OnlineServices.Result.Success && statOut is { Name: var name, Value: var value })
                {
                    builder.Add(AchievementStatExtension.FromIdentifier(new Identifier(name)), value);
                }
            }
        }

        return Result.Success(builder.ToImmutable());
    }

    public static async Task<Result<ImmutableDictionary<Identifier, double>, EosInterface.QueryAchievementsError>> QueryPlayerAchievements()
    {
        if (CorePrivate.AchievementsInterface is not { } achievementsInterface) { return Result.Failure(EosInterface.QueryAchievementsError.EosNotInitialized); }

        var loggedInUsers = IdQueriesPrivate.GetLoggedInPuids();

        if (loggedInUsers is not { Length: > 0 })
        {
            return Result.Failure(EosInterface.QueryAchievementsError.InvalidUser);
        }
        var loggedInUser = loggedInUsers[0];

        var convertedUserId = Epic.OnlineServices.ProductUserId.FromString(loggedInUser.Value);

        var options = new Epic.OnlineServices.Achievements.QueryPlayerAchievementsOptions
        {
            LocalUserId = convertedUserId,
            TargetUserId = convertedUserId
        };

        var queryWaiter = new CallbackWaiter<Epic.OnlineServices.Achievements.OnQueryPlayerAchievementsCompleteCallbackInfo>();
        achievementsInterface.QueryPlayerAchievements(options: ref options, clientData: null, completionDelegate: queryWaiter.OnCompletion);

        var resultOption = await queryWaiter.Task;

        if (!resultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.QueryAchievementsError.TimedOut);
        }

        if (callbackResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return callbackResult.ResultCode switch
            {
                Epic.OnlineServices.Result.InvalidParameters => Result.Failure(EosInterface.QueryAchievementsError.InvalidParameters),
                Epic.OnlineServices.Result.InvalidUser => Result.Failure(EosInterface.QueryAchievementsError.InvalidUser),
                Epic.OnlineServices.Result.InvalidProductUserID => Result.Failure(EosInterface.QueryAchievementsError.InvalidProductUserID),
                Epic.OnlineServices.Result.NotFound => Result.Failure(EosInterface.QueryAchievementsError.NotFound),
                var unhandled => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.QueryAchievementsError.Unknown))
            };
        }

        var countOptions = new Epic.OnlineServices.Achievements.GetPlayerAchievementCountOptions
        {
            UserId = convertedUserId
        };
        uint count = achievementsInterface.GetPlayerAchievementCount(ref countOptions);

        var builder = ImmutableDictionary.CreateBuilder<Identifier, double>();
        for (uint i = 0; i < count; i++)
        {
            var copyIndexOptions = new Epic.OnlineServices.Achievements.CopyPlayerAchievementByIndexOptions
            {
                TargetUserId = convertedUserId,
                LocalUserId = convertedUserId,
                AchievementIndex = i
            };
            var copyResult = achievementsInterface.CopyPlayerAchievementByIndex(ref copyIndexOptions, out var achievementOut);

            if (copyResult is Epic.OnlineServices.Result.Success && achievementOut is { AchievementId: var name, Progress: var value })
            {
                builder.Add(new Identifier(name), value);
            }
        }

        return Result.Success(builder.ToImmutable());
    }

    public static async Task<Result<Unit, EosInterface.IngestStatError>> IngestStats(params (AchievementStat Stat, int IngestAmount)[] stats)
    {
        if (CorePrivate.StatsInterface is not { } statsInterface) { return Result.Failure(EosInterface.IngestStatError.EosNotInitialized); }

        var loggedInUsers = IdQueriesPrivate.GetLoggedInPuids();

        if (loggedInUsers is not { Length: > 0 })
        {
            return Result.Failure(EosInterface.IngestStatError.InvalidUser);
        }
        var loggedInUser = loggedInUsers[0];

        var convertedUserId = Epic.OnlineServices.ProductUserId.FromString(loggedInUser.Value);

        var options = new Epic.OnlineServices.Stats.IngestStatOptions
        {
            LocalUserId = convertedUserId,
            TargetUserId = convertedUserId,
            Stats = stats.Select(static s => new Epic.OnlineServices.Stats.IngestData
            {
                StatName = s.Stat.ToString().ToLowerInvariant(),
                IngestAmount = s.IngestAmount
            }).ToArray()
        };

        var ingestStatWaiter = new CallbackWaiter<Epic.OnlineServices.Stats.IngestStatCompleteCallbackInfo>();
        statsInterface.IngestStat(options: ref options, clientData: null, completionDelegate: ingestStatWaiter.OnCompletion);

        var resultOption = await ingestStatWaiter.Task;

        if (!resultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.IngestStatError.TimedOut);
        }

        return callbackResult.ResultCode switch
        {
            Epic.OnlineServices.Result.Success => Result.Success(Unit.Value),
            Epic.OnlineServices.Result.InvalidParameters => Result.Failure(EosInterface.IngestStatError.InvalidParameters),
            Epic.OnlineServices.Result.InvalidUser => Result.Failure(EosInterface.IngestStatError.InvalidUser),
            Epic.OnlineServices.Result.NotFound => Result.Failure(EosInterface.IngestStatError.NotFound),
            var unhandled => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.IngestStatError.Unknown))
        };
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<uint, EosInterface.AchievementUnlockError>> UnlockAchievements(params Identifier[] achievementIds)
        => TaskScheduler.Schedule(() => AchievementsPrivate.UnlockAchievements(achievementIds));

    public override Task<Result<Unit, EosInterface.IngestStatError>> IngestStats(params (AchievementStat Stat, int IngestAmount)[] stats)
        => TaskScheduler.Schedule(() => AchievementsPrivate.IngestStats(stats));

    public override Task<Result<ImmutableDictionary<AchievementStat, int>, EosInterface.QueryStatsError>> QueryStats(ImmutableArray<AchievementStat> stats)
        => TaskScheduler.Schedule(() => AchievementsPrivate.QueryStats(stats));

    public override Task<Result<ImmutableDictionary<Identifier, double>, EosInterface.QueryAchievementsError>> QueryPlayerAchievements()
        => TaskScheduler.Schedule(AchievementsPrivate.QueryPlayerAchievements);
}

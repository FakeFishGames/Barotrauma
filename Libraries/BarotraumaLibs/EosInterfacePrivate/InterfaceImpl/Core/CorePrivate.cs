#nullable enable
using System;
using Barotrauma.Debugging;
using Microsoft.Xna.Framework;
using Barotrauma;

namespace EosInterfacePrivate;

static class CorePrivate
{
    public static EosInterface.Core.Status CurrentStatus
        => platformInterface is null
            ? EosInterface.Core.Status.NotInitialized
            : platformInterface.GetNetworkStatus() == Epic.OnlineServices.Platform.NetworkStatus.Online
                ? EosInterface.Core.Status.Online
                : EosInterface.Core.Status.InitializedButOffline;

    private static Epic.OnlineServices.Platform.Options platformInterfaceOptions;
    public static Epic.OnlineServices.Platform.Options PlatformInterfaceOptions => platformInterfaceOptions;

    private static Epic.OnlineServices.Platform.PlatformInterface? platformInterface;
    public static Epic.OnlineServices.Platform.PlatformInterface? PlatformInterface => platformInterface;

    public static Epic.OnlineServices.Connect.ConnectInterface? ConnectInterface
        => PlatformInterface?.GetConnectInterface();

    public static Epic.OnlineServices.Auth.AuthInterface? EgsAuthInterface
        => PlatformInterface?.GetAuthInterface();

    public static Epic.OnlineServices.Friends.FriendsInterface? EgsFriendsInterface
        => PlatformInterface?.GetFriendsInterface();

    public static Epic.OnlineServices.UserInfo.UserInfoInterface? EgsUserInfoInterface
        => PlatformInterface?.GetUserInfoInterface();

    public static Epic.OnlineServices.Presence.PresenceInterface? EgsPresenceInterface
        => PlatformInterface?.GetPresenceInterface();

    public static Epic.OnlineServices.CustomInvites.CustomInvitesInterface? EgsCustomInvitesInterface
        => PlatformInterface?.GetCustomInvitesInterface();

    public static Epic.OnlineServices.UI.UIInterface? EgsUiInterface
        => PlatformInterface?.GetUIInterface();

    public static Epic.OnlineServices.Sessions.SessionsInterface? SessionsInterface
        => PlatformInterface?.GetSessionsInterface();

    public static Epic.OnlineServices.P2P.P2PInterface? P2PInterface
        => PlatformInterface?.GetP2PInterface();

    public static Epic.OnlineServices.Achievements.AchievementsInterface? AchievementsInterface
        => PlatformInterface?.GetAchievementsInterface();

    public static Epic.OnlineServices.Stats.StatsInterface? StatsInterface
        => PlatformInterface?.GetStatsInterface();

    public static Epic.OnlineServices.Ecom.EcomInterface? EcomInterface
        => PlatformInterface?.GetEcomInterface();

    public static Result<Unit, EosInterface.Core.InitError> Init(ImplementationPrivate implementation, EosInterface.ApplicationCredentials applicationCredentials, bool enableOverlay)
    {
        var initializeOptions = new Epic.OnlineServices.Platform.InitializeOptions
        {
            ProductName = "Barotrauma",
            ProductVersion = GameVersion.CurrentVersion.ToString(),

            SystemInitializeOptions = IntPtr.Zero,
            OverrideThreadAffinity = null,

            AllocateMemoryFunction = IntPtr.Zero,
            ReallocateMemoryFunction = IntPtr.Zero,
            ReleaseMemoryFunction = IntPtr.Zero
        };

        var result = Epic.OnlineServices.Platform.PlatformInterface.Initialize(ref initializeOptions);
        Console.WriteLine(
            $"{nameof(Epic.OnlineServices.Platform.PlatformInterface)}.{nameof(Epic.OnlineServices.Platform.PlatformInterface.Initialize)} result: {result}");

        platformInterfaceOptions = PlatformInterfaceOptionsPrivate.PlatformOptions[applicationCredentials];
        if (enableOverlay)
        {
            // Some caveats:
            // - Currently the overlay is not implemented on non-Windows platforms
            // - If you try to initialize EOS after the window has already been created,
            //   enabling the overlay will result in a crash
            // - The overlay doesn't do anything if you do not log into an Epic account
            platformInterfaceOptions.Flags = Epic.OnlineServices.Platform.PlatformFlags.None;
        }

        platformInterface = Epic.OnlineServices.Platform.PlatformInterface.Create(ref platformInterfaceOptions);

        if (ConnectInterface != null)
        {
            LoginPrivate.Init();
        }

        if (platformInterface is null) { return Result.Failure(EosInterface.Core.InitError.PlatformInterfaceNotCreated); }

        PresencePrivate.Init(implementation);

        var setLogCallbackResult = Epic.OnlineServices.Logging.LoggingInterface.SetCallback(LogCallback);
        if (setLogCallbackResult == Epic.OnlineServices.Result.Success)
        {
            Epic.OnlineServices.Logging.LoggingInterface.SetLogLevel(
                Epic.OnlineServices.Logging.LogCategory.AllCategories,
                Epic.OnlineServices.Logging.LogLevel.VeryVerbose);
        }

        return Result.Success(default(Unit));
    }

    private static void LogCallback(ref Epic.OnlineServices.Logging.LogMessage msg)
    {
        DebugConsoleCore.Log($"[EOS {msg.Category} {msg.Level}] {msg.Message}");
    }

    public static Result<EosInterface.Core.WillRestartThroughLauncher, EosInterface.Core.CheckForLauncherAndRestartError> CheckForLauncherAndRestart()
    {
        if (platformInterface is null) { return Result.Failure(EosInterface.Core.CheckForLauncherAndRestartError.EosNotInitialized); }
        var result = platformInterface.CheckForLauncherAndRestart();
        if (result == Epic.OnlineServices.Result.Success) { return Result.Success(EosInterface.Core.WillRestartThroughLauncher.Yes); }
        if (result == Epic.OnlineServices.Result.NoChange) { return Result.Success(EosInterface.Core.WillRestartThroughLauncher.No); }
        return Result.Failure(result switch
        {
            Epic.OnlineServices.Result.UnexpectedError
                => EosInterface.Core.CheckForLauncherAndRestartError.UnexpectedError,
            _ 
                => result.FailAndLogUnhandledError(EosInterface.Core.CheckForLauncherAndRestartError.UnhandledErrorCondition)
        });
    }

    private static EosInterface.Core.Status prevTickStatus = EosInterface.Core.Status.NotInitialized;
    public static void Update()
    {
        platformInterface?.Tick();
        var currentStatus = CurrentStatus;
        if (currentStatus == EosInterface.Core.Status.Online && prevTickStatus != currentStatus)
        {
            // We were offline, but now we are back online so let's update all sessions
            OwnedSessionsPrivate.ForceUpdateAllOwnedSessions();
        }
        prevTickStatus = currentStatus;
    }

    public static void Quit()
    {
        PresencePrivate.Quit();

        platformInterface?.Release();
        platformInterface = null;
        Epic.OnlineServices.Platform.PlatformInterface.Shutdown();
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    
    public override EosInterface.Core.Status CurrentStatus => CorePrivate.CurrentStatus;

    public override string NativeLibraryName => Epic.OnlineServices.Config.LibraryName;

    public override Result<Unit, EosInterface.Core.InitError> Init(EosInterface.ApplicationCredentials applicationCredentials, bool enableOverlay)
        => CorePrivate.Init(this, applicationCredentials, enableOverlay);

    public override Result<EosInterface.Core.WillRestartThroughLauncher, EosInterface.Core.CheckForLauncherAndRestartError> CheckForLauncherAndRestart()
        => CorePrivate.CheckForLauncherAndRestart();

    public override void Quit()
        => CorePrivate.Quit();

    public override void Update()
    {
        CorePrivate.Update();
        TaskScheduler.RunOnCurrentThread();
    }
}

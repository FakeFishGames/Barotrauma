using System.Diagnostics.CodeAnalysis;

namespace Barotrauma;

public static class EosStatusExtensions
{
    public static bool IsInitialized(this EosInterface.Core.Status status)
        => status is EosInterface.Core.Status.InitializedButOffline or EosInterface.Core.Status.Online;

    internal static bool IsInitialized(
        [NotNullWhen(returnValue: true)] this EosInterface.Implementation? implementation)
        => implementation is { CurrentStatus: var status } && status.IsInitialized();
}
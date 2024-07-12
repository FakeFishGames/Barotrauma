using System.Runtime.CompilerServices;
using Barotrauma.Debugging;
using Microsoft.Xna.Framework;

namespace EosInterfacePrivate;

public static class ResultExtension
{
    public static T FailAndLogUnhandledError<T>(this Epic.OnlineServices.Result result, T unknown, [CallerMemberName] string caller = null)
    {
        DebugConsoleCore.NewMessage($"Result \"{result}\" was not handled by \"{caller}\".", Color.Red);
        return unknown;
    }
}
using System;
using Microsoft.Xna.Framework;

namespace Barotrauma.Debugging;

public static class DebugConsoleCore
{
    private static Action<string, Color>? newMessage;
    private static Action<string>? log;
    private static Action<string, Exception?>? throwError;

    public static void Init(Action<string, Color> newMessage, Action<string> log, Action<string, Exception?> throwError)
    {
        DebugConsoleCore.newMessage ??= newMessage;
        DebugConsoleCore.log ??= log;
        DebugConsoleCore.throwError ??= throwError;
    }

    public static void NewMessage(string msg, Color? color = null)
    {
        newMessage?.Invoke(msg, color ?? Color.White);
    }

    public static void Log(string msg)
    {
        log?.Invoke(msg);
    }

    public static void ThrowError(string msg, Exception? exception = null)
    {
        throwError?.Invoke(msg, exception);
    }
}
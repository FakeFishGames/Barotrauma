using System;
using Microsoft.Xna.Framework;

namespace Barotrauma.Debugging;

public static class DebugConsoleCore
{
    private static Action<string, Color>? newMessage;
    private static Action<string>? log;

    public static void Init(Action<string, Color> newMessage, Action<string> log)
    {
        DebugConsoleCore.newMessage ??= newMessage;
        DebugConsoleCore.log ??= log;
    }

    public static void NewMessage(string msg, Color? color = null)
    {
        newMessage?.Invoke(msg, color ?? Color.White);
    }

    public static void Log(string msg)
    {
        log?.Invoke(msg);
    }
}
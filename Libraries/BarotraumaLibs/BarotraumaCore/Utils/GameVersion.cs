using System;
using System.Reflection;
namespace Barotrauma;

public static class GameVersion
{
    public static readonly Version CurrentVersion
        = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0,0,0,0);
}

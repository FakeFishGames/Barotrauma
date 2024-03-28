using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma;

/// <summary>
/// This type is intended to be used in using statements to automatically
/// clean up resources that are allocated incrementally
/// </summary>
public readonly struct Janitor : IDisposable
{
    private readonly List<Action> cleanupActions;
    private Janitor(List<Action> cleanupActions)
    {
        this.cleanupActions = cleanupActions;
    }

    public static Janitor Start()
        => new Janitor(new List<Action>());

    /// <summary>
    /// Give the janitor a new action to perform when disposed
    /// </summary>
    public void AddAction([NotNull]Action action)
    {
        // Null check to punish misuse early instead of having the Janitor blow up upon disposal.
        // Make sure you use nullable contexts so the compiler will stop you instead!
        if (action is null)
        {
            throw new ArgumentException($"Cannot add null as an action for {nameof(Janitor)}");
        }
        cleanupActions.Add(action);
    }

    /// <summary>
    /// Relieve the janitor of all current duties,
    /// i.e. all of the currently enqueued cleanup
    /// actions are cleared and will not execute
    /// </summary>
    public void Dismiss()
        => cleanupActions.Clear();
    
    public void Dispose()
    {
        cleanupActions.ForEach(a => a());
        Dismiss();
    }
}
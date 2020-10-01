using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// Implementation of the Command pattern.
    /// <see href="https://en.wikipedia.org/wiki/Command_pattern"/>
    /// </summary>
    /// <remarks>
    /// Created by Markus Isberg on 11th of March 2020 for the submarine editor.
    /// "Implementing a global undo and redo with Memento pattern proved too difficult of a task for me so I implemented it with this pattern instead."
    /// </remarks>
    internal abstract partial class Command
    {
        /// <summary>
        /// A method that should apply a new state on an object or perform an action
        /// </summary>
        public abstract void Execute();

        /// <summary>
        /// A method that should revert Execute() method's actions
        /// </summary>
        public abstract void UnExecute();

        /// <summary>
        /// State no longer exists, clean up the lingering garbage
        /// </summary>
        public abstract void Cleanup();
    }
}
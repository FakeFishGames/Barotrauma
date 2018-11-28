using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class Memento<T>
    {
        public T Current { get; private set; }
        public T Previous { get; private set; }

        private Stack<T> undoStack = new Stack<T>();
        private Stack<T> redoStack = new Stack<T>();

        public void Store(T newState)
        {
            redoStack.Clear();
            if (Current != null)
            {
                Previous = Current;
            }
            Current = newState;
            undoStack.Push(newState);
        }

        public T Undo()
        {
            if (undoStack.Any())
            {
                Previous = Current;
                redoStack.Push(Previous);
                Current = undoStack.Pop();
            }
            return Current;
        }

        public T Redo()
        {
            if (redoStack.Any())
            {
                Previous = Current;
                undoStack.Push(Previous);
                Current = redoStack.Pop();
            }
            return Current;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
            Current = default(T);
            Previous = default(T);
        }
    }
}

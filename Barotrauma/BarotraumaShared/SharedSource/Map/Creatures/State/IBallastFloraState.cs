#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma.MapCreatures.Behavior
{
    public enum ExitState 
    {
        Running,    // State is running
        Terminate,  // State has exited
        ReturnLast  // Return to the last running state if any
    }

    public interface IBallastFloraState
    {
        public void Enter();
        public void Exit();
        public void Update(float deltaTime);
        public ExitState GetState();
    }
}
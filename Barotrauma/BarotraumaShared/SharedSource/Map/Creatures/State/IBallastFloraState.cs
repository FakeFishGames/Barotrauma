#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma.MapCreatures.Behavior
{
    enum ExitState 
    {
        Running,    // State is running
        Terminate,  // State has exited
        ReturnLast  // Return to the last running state if any
    }

    interface IBallastFloraState
    {
        public void Enter();
        public void Exit();
        public void Update(float deltaTime);
        public ExitState GetState();
    }
}
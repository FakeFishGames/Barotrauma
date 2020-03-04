using Microsoft.Xna.Framework;

namespace Barotrauma
{
    interface ISteerable
    {
        Vector2 Steering
        {
            get;
            set;
        }

        Vector2 Velocity
        {
            get;
        }
        
        Vector2 SimPosition 
        {
            get;
        }

        Vector2 WorldPosition
        { 
            get;
        }
    }
}

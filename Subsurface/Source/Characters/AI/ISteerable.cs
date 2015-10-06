using Microsoft.Xna.Framework;

namespace Subsurface
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
        
    }
}

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class Location
    {
        string name;

        Vector2 mapPosition;

        public string Name
        {
            get { return name; }
        }

        public Vector2 MapPosition
        {
            get { return mapPosition; }
        }

        public Location(string name, Vector2 mapPosition)
        {
            this.name = name;

            this.mapPosition = mapPosition;
        }

        public static Location CreateRandom(Vector2 position)
        {
            return new Location("Location " + Rand.Int(10000, false), position);        
        }
    }
}

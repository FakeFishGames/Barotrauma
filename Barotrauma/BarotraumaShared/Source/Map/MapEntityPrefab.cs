using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    [Flags]
    enum MapEntityCategory
    {
        Structure = 1, Machine = 2, Equipment = 4, Electrical = 8, Material = 16, Misc = 32, Alien = 64
    }

    partial class MapEntityPrefab
    {
        public readonly static List<MapEntityPrefab> List = new List<MapEntityPrefab>();

        protected string name;
        
        public Sprite sprite;

        //the position where the structure is being placed (needed when stretching the structure)
        protected static Vector2 placePosition;

        protected ConstructorInfo constructor;

        //is it possible to stretch the entity horizontally/vertically
        [Serialize(false, false)]
        public bool ResizeHorizontal { get; protected set; }
        [Serialize(false, false)]
        public bool ResizeVertical { get; protected set; }

        //which prefab has been selected for placing
        protected static MapEntityPrefab selected;

        private int price;

        public string Name
        {
            get { return name; }
        }

        public List<string> Tags
        {
            get;
            protected set;
        }

        public static MapEntityPrefab Selected
        {
            get { return selected; }
            set { selected = value; }
        }

        [Serialize("", false)]
        public string Description
        {
            get;
            protected set;
        }
        
        [Serialize(false, false)]
        public bool Linkable
        {
            get;
            private set;
        }
                
        public MapEntityCategory Category
        {
            get;
            protected set;
        }

        [Serialize("1.0,1.0,1.0,1.0", false)]
        public Color SpriteColor
        {
            get;
            protected set;
        }

        [Serialize(0, false)]
        public int Price
        {
            get { return price; }
            protected set { price = Math.Max(value, 0); }
        }

        //If a matching prefab is not found when loading a sub, the game will attempt to find a prefab with a matching alias.
        //(allows changing names while keeping backwards compatibility with older sub files)
        public string[] Aliases
        {
            get;
            protected set;
        }

        public static void Init()
        {
            MapEntityPrefab ep = new MapEntityPrefab();
            ep.name = "Hull";
            ep.Description = "Hulls determine which parts are considered to be \"inside the sub\". Generally every room should be enclosed by a hull.";
            ep.constructor = typeof(Hull).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            ep.ResizeHorizontal = true;
            ep.ResizeVertical = true;
            List.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Gap";
            ep.Description = "Gaps allow water and air to flow between two hulls. ";
            ep.constructor = typeof(Gap).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            ep.ResizeHorizontal = true;
            ep.ResizeVertical = true;
            List.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Waypoint";
            ep.constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            List.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Spawnpoint";
            ep.constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            List.Add(ep);
        }

        public MapEntityPrefab()
        {
            Category = MapEntityCategory.Structure;
        }

        public virtual void UpdatePlacing(Camera cam)
        {
            Vector2 placeSize = Submarine.GridSize;

            if (placePosition == Vector2.Zero)
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
                
                if (PlayerInput.LeftButtonHeld()) placePosition = position;
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;
                
                Rectangle newRect = Submarine.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Submarine.GridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Submarine.GridSize.Y);

                if (Submarine.MainSub != null)
                {
                    newRect.Location -= MathUtils.ToPoint(Submarine.MainSub.Position);
                }

                if (PlayerInput.LeftButtonReleased())
                {
                    CreateInstance(newRect);
                    placePosition = Vector2.Zero;
                    selected = null;
                }

                newRect.Y = -newRect.Y;
            }

            if (PlayerInput.RightButtonHeld())
            {
                placePosition = Vector2.Zero;
                selected = null;
            }
        }

        protected virtual void CreateInstance(Rectangle rect)
        {
            object[] lobject = new object[] { this, rect };
            constructor.Invoke(lobject);
        }    

        public static bool SelectPrefab(object selection)
        {
            if ((selected = selection as MapEntityPrefab) != null)
            {
                placePosition = Vector2.Zero;
                return true;
            }
            else
            {
                return false;
            }
        }

        public static MapEntityPrefab Find(string name, bool caseSensitive = false)
        {
            if (caseSensitive)
            {
                foreach (MapEntityPrefab prefab in List)
                {
                    if (prefab.name == name || (prefab.Aliases != null && prefab.Aliases.Contains(name))) return prefab;
                }
            }
            else
            {
                name = name.ToLowerInvariant();
                foreach (MapEntityPrefab prefab in List)
                {
                    if (prefab.name.ToLowerInvariant() == name || (prefab.Aliases != null && prefab.Aliases.Any(a => a.ToLowerInvariant() == name))) return prefab;
                }
            }

            return null;
        }

        /// <summary>
        /// Check if the name or any of the aliases of this prefab match the given name.
        /// </summary>
        public bool NameMatches(string name, bool caseSensitive = false)
        {
            if (caseSensitive)
            {
                return this.name == name || (Aliases != null && Aliases.Any(a => a == name));
            }
            else
            {
                name = name.ToLowerInvariant();
                return this.name.ToLowerInvariant() == name || (Aliases != null && Aliases.Any(a => a.ToLowerInvariant() == name));
            }
        }

        //a method that allows the GUIListBoxes to check through a delegate if the entityprefab is still selected
        public static object GetSelected()
        {
            return (object)selected;            
        }
        
    }
}

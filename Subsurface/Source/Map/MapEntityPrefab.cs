using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    [Flags]
    enum MapEntityCategory
    {
        Structure = 1, Machine = 2, Equipment = 4, Electrical = 8, Material = 16, Misc = 32
    }

    class MapEntityPrefab
    {
        public static List<MapEntityPrefab> list = new List<MapEntityPrefab>();

        protected string name;
        
        protected bool isLinkable;

        public Sprite sprite;

        //the position where the structure is being placed (needed when stretching the structure)
        protected static Vector2 placePosition;

        protected ConstructorInfo constructor;

        //is it possible to stretch the entity horizontally/vertically
        protected bool resizeHorizontal;
        protected bool resizeVertical;
                
        //which prefab has been selected for placing
        protected static MapEntityPrefab selected;

        protected int price;

        public string Name
        {
            get { return name; }
        }

        public static MapEntityPrefab Selected
        {
            get { return selected; }
            set { selected = value; }
        }


        public string Description
        {
            get;
            protected set;
        }

        public virtual bool IsLinkable
        {
            get { return isLinkable; }
        }

        public bool ResizeHorizontal
        {
            get { return resizeHorizontal; }
        }

        public bool ResizeVertical
        {
            get { return resizeVertical; }
        }

        public MapEntityCategory Category
        {
            get;
            protected set;
        }

        public Color SpriteColor
        {
            get;
            protected set;
        }

        public int Price
        {
            get { return price; }
        }

        public static void Init()
        {
            MapEntityPrefab ep = new MapEntityPrefab();
            ep.name = "Hull";
            ep.Description = "Hulls determine which parts are considered to be ''inside the sub''. Generally every room should be enclosed by a hull.";
            ep.constructor = typeof(Hull).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            ep.resizeHorizontal = true;
            ep.resizeVertical = true;
            list.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Gap";
            ep.Description = "Gaps allow water and air to flow between two hulls. ";
            ep.constructor = typeof(Gap).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            ep.resizeHorizontal = true;
            ep.resizeVertical = true;
            list.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Waypoint";
            ep.constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            list.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "Spawnpoint";
            ep.constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) });
            list.Add(ep);

        }

        public MapEntityPrefab()
        {
            Category = MapEntityCategory.Structure;
        }

        public virtual void UpdatePlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 placeSize = Submarine.GridSize;

            if (placePosition == Vector2.Zero)
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam);

                GUI.DrawLine(spriteBatch, new Vector2(position.X-GameMain.GraphicsWidth, -position.Y), new Vector2(position.X+GameMain.GraphicsWidth, -position.Y), Color.White);

                GUI.DrawLine(spriteBatch, new Vector2(position.X, -(position.Y - GameMain.GraphicsHeight)), new Vector2(position.X, -(position.Y + GameMain.GraphicsHeight)), Color.White);

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed) placePosition = position;
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam);

                if (resizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (resizeVertical) placeSize.Y = placePosition.Y - position.Y;
                
                Rectangle newRect = Submarine.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Submarine.GridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Submarine.GridSize.Y);

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                {
                    object[] lobject = new object[] { this, newRect };
                    constructor.Invoke(lobject);
                    placePosition = Vector2.Zero;
                    selected = null;
                }

                newRect.Y = -newRect.Y;
                GUI.DrawRectangle(spriteBatch, newRect, Color.DarkBlue);
            }

            if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed)
            {
                placePosition = Vector2.Zero;
                selected = null;
            }
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

        //a method that allows the GUIListBoxes to check through a delegate if the entityprefab is still selected
        public static object GetSelected()
        {
            return (object)selected;            
        }
        
        public void DrawListLine(SpriteBatch spriteBatch, Vector2 pos, Color color)
        {            
            spriteBatch.DrawString(GUI.Font, name, pos, color);
        }

    }
}

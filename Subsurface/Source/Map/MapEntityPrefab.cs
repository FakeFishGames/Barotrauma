using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    enum MapEntityCategory
    {
        Structure, Machine, Item, Electrical, Equipment, Material
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
            ep.name = "hull";
            ep.constructor = typeof(Hull).GetConstructor(new Type[] { typeof(Rectangle) });
            ep.resizeHorizontal = true;
            ep.resizeVertical = true;
            list.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "gap";
            ep.constructor = typeof(Gap).GetConstructor(new Type[] { typeof(Rectangle) });
            ep.resizeHorizontal = true;
            ep.resizeVertical = true;
            list.Add(ep);

            ep = new MapEntityPrefab();
            ep.name = "waypoint";
            ep.constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(Rectangle) });
            list.Add(ep);
        }


        public virtual void UpdatePlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 placeSize = Submarine.GridSize;

            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                    placePosition = Submarine.MouseToWorldGrid(cam);
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
                    object[] lobject = new object[] { newRect };
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

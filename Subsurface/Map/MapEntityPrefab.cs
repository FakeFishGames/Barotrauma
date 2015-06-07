using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{
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

        public string Name
        {
            get { return name; }
        }

        public static MapEntityPrefab Selected
        {
            get { return selected; }
        }

        public virtual bool IsLinkable
        {
            get { return isLinkable; }
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
            Vector2 placeSize = Map.gridSize;

            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                    placePosition = Map.MouseToWorldGrid(cam);
            }
            else
            {
                Vector2 position = Map.MouseToWorldGrid(cam);

                if (resizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (resizeVertical) placeSize.Y = placePosition.Y - position.Y;
                
                Rectangle newRect = Map.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Map.gridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Map.gridSize.Y);

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
            spriteBatch.DrawString(GUI.font, name, pos, color);
        }

    }
}

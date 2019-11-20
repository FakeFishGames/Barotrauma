using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    [Flags]
    enum MapEntityCategory
    {
        Structure = 1, Machine = 2, Equipment = 4, Electrical = 8, Material = 16, Misc = 32, Alien = 64, ItemAssembly = 128, Legacy = 256
    }

    partial class MapEntityPrefab
    {
        public readonly static List<MapEntityPrefab> List = new List<MapEntityPrefab>();

        protected string name;
        protected string identifier;
        
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
        
        public string Name
        {
            get { return name; }
        }

        public string GetItemNameTextId()
        {
            var textId = $"entityname.{Identifier}";
            return TextManager.ContainsTag(textId) ? textId : null;
        }

        public string GetHullNameTextId()
        {
            var textId = $"roomname.{Identifier}";
            return TextManager.ContainsTag(textId) ? textId : null;
        }

        //Used to differentiate between items when saving/loading
        //Allows changing the name of an item without breaking existing subs or having multiple items with the same name
        public string Identifier
        {
            get { return identifier; }
        }

        public HashSet<string> Tags
        {
            get;
            protected set;
        } = new HashSet<string>();

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
        public bool HideInMenus { get; set; }

        [Serialize(false, false)]
        public bool Linkable
        {
            get;
            private set;
        }

        /// <summary>
        /// Links defined to identifiers.
        /// </summary>
        public List<string> AllowedLinks { get; protected set; } = new List<string>();

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

        [Serialize(1f, true), Editable(0.1f, 10f, DecimalCount = 3)]
        public float Scale { get; protected set; }

        //If a matching prefab is not found when loading a sub, the game will attempt to find a prefab with a matching alias.
        //(allows changing names while keeping backwards compatibility with older sub files)
        public HashSet<string> Aliases
        {
            get;
            protected set;
        }

        public static void Init()
        {
            MapEntityPrefab ep = new MapEntityPrefab
            {
                identifier = "hull",
                name = TextManager.Get("EntityName.hull"),
                Description = TextManager.Get("EntityDescription.hull"),
                constructor = typeof(Hull).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }),
                ResizeHorizontal = true,
                ResizeVertical = true,
                Linkable = true
            };
            ep.AllowedLinks.Add("hull");
            ep.Aliases = new HashSet<string> { "hull" };
            List.Add(ep);

            ep = new MapEntityPrefab
            {
                identifier = "gap",
                name = TextManager.Get("EntityName.gap"),
                Description = TextManager.Get("EntityDescription.gap"),
                constructor = typeof(Gap).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }),
                ResizeHorizontal = true,
                ResizeVertical = true
            };
            List.Add(ep);
            ep.Aliases = new HashSet<string> { "gap" };

            ep = new MapEntityPrefab
            {
                identifier = "waypoint",
                name = TextManager.Get("EntityName.waypoint"),
                Description = TextManager.Get("EntityDescription.waypoint"),
                constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) })
            };
            List.Add(ep);
            ep.Aliases = new HashSet<string> { "waypoint" };

            ep = new MapEntityPrefab
            {
                identifier = "spawnpoint",
                name = TextManager.Get("EntityName.spawnpoint"),
                Description = TextManager.Get("EntityDescription.spawnpoint"),
                constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) })
            };
            List.Add(ep);
            ep.Aliases = new HashSet<string> { "spawnpoint" };
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
            if (constructor == null) return;
            object[] lobject = new object[] { this, rect };
            constructor.Invoke(lobject);
        }

#if DEBUG
        public void DebugCreateInstance()
        {
            Rectangle rect = new Rectangle(new Point((int)Screen.Selected.Cam.WorldViewCenter.X, (int)Screen.Selected.Cam.WorldViewCenter.Y), new Point((int)Submarine.GridSize.X, (int)Submarine.GridSize.Y));
            CreateInstance(rect);
        }
#endif

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

        /// <summary>
        /// Find a matching map entity prefab
        /// </summary>
        /// <param name="name">The name of the item (can be omitted when searching based on identifier)</param>
        /// <param name="identifier">The identifier of the item (if null, the identifier is ignored and the search is done only based on the name)</param>
        public static MapEntityPrefab Find(string name, string identifier = null, bool showErrorMessages = true)
        {
            if (name != null) name = name.ToLowerInvariant();
            foreach (MapEntityPrefab prefab in List)
            {
                if (identifier != null)
                {
                    if (prefab.identifier != identifier)
                    {
                        continue;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(name)) return prefab;
                    }
                }
                if (!string.IsNullOrEmpty(name))
                {
                    if (prefab.name.ToLowerInvariant() == name || (prefab.Aliases != null && prefab.Aliases.Any(a => a.ToLowerInvariant() == name))) return prefab;
                }
            }

            if (showErrorMessages)
            {
                DebugConsole.ThrowError("Failed to find a matching MapEntityPrefab (name: \"" + name + "\", identifier: \"" + identifier + "\").\n" + Environment.StackTrace);
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

        public bool NameMatches(IEnumerable<string> allowedNames, bool caseSensitive = false)
        {
            foreach (string name in allowedNames)
            {
                if (NameMatches(name, caseSensitive)) return true;
            }
            return false;
        }

        public bool IsLinkAllowed(MapEntityPrefab target)
        {
            if (target == null) { return false; }
            return AllowedLinks.Contains(target.Identifier) || target.AllowedLinks.Contains(identifier)
                || target.Tags.Any(t => AllowedLinks.Contains(t)) || Tags.Any(t => target.AllowedLinks.Contains(t));
        }

        //a method that allows the GUIListBoxes to check through a delegate if the entityprefab is still selected
        public static object GetSelected()
        {
            return (object)selected;            
        }
        
        protected bool HandleExisting(string identifier, bool allowOverriding, string file = null)
        {
            if (!string.IsNullOrEmpty(identifier))
            {
                MapEntityPrefab existingPrefab = List.Find(e => e.Identifier == identifier);
                if (existingPrefab != null)
                {
                    if (allowOverriding)
                    {
                        string msg = $"Overriding an existing map entity with the identifier '{identifier}'";
                        if (!string.IsNullOrWhiteSpace(file))
                        {
                            msg += $" using the file '{file}'";
                        }
                        msg += ".";
                        DebugConsole.NewMessage(msg, Color.Yellow);
                        List.Remove(existingPrefab);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                        {
                            DebugConsole.ThrowError($"Error in '{file}': Map entity prefabs \"" + name + "\" and \"" + existingPrefab.Name + "\" have the same identifier! " +
                                "Use the <override> XML element as the parent of the map element's definition to override the existing map element.");
                        }
                        else
                        {
                            DebugConsole.ThrowError("Map entity prefabs \"" + name + "\" and \"" + existingPrefab.Name + "\" have the same identifier! " +
                                "Use the <override> XML element as the parent of the map element's definition to override the existing map element.");
                        }
                        return false;
                    }
                }
            }
            return true;
        }
    }
}

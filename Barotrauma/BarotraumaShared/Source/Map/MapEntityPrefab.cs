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
        Structure = 1, Decorative = 2, Machine = 4, Equipment = 8, Electrical = 16, Material = 32, Misc = 64, Alien = 128, ItemAssembly = 256, Legacy = 512
    }

    abstract partial class MapEntityPrefab : IPrefab, IDisposable
    {
        public static IEnumerable<MapEntityPrefab> List
        {
            get
            {
                foreach (var ep in CoreEntityPrefab.Prefabs)
                {
                    yield return ep;
                }

                foreach (var ep in StructurePrefab.Prefabs)
                {
                    yield return ep;
                }

                foreach (var ep in ItemPrefab.Prefabs)
                {
                    yield return ep;
                }

                foreach (var ep in ItemAssemblyPrefab.Prefabs)
                {
                    yield return ep;
                }
            }
        }

        protected string originalName;
        protected string identifier;
        protected ContentPackage contentPackage;

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
        
        public string OriginalName
        {
            get { return originalName; }
        }

        public virtual string Name
        {
            get { return originalName; }
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

        public string FilePath { get; protected set; }

        public ContentPackage ContentPackage { get; protected set; }

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
            protected set;
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
            CoreEntityPrefab ep = new CoreEntityPrefab
            {
                identifier = "hull",
                originalName = TextManager.Get("EntityName.hull"),
                Description = TextManager.Get("EntityDescription.hull"),
                constructor = typeof(Hull).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }),
                ResizeHorizontal = true,
                ResizeVertical = true,
                Linkable = true
            };
            ep.AllowedLinks.Add("hull");
            ep.Aliases = new HashSet<string> { "hull" };
            CoreEntityPrefab.Prefabs.Add(ep, false);

            ep = new CoreEntityPrefab
            {
                identifier = "gap",
                originalName = TextManager.Get("EntityName.gap"),
                Description = TextManager.Get("EntityDescription.gap"),
                constructor = typeof(Gap).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }),
                ResizeHorizontal = true,
                ResizeVertical = true
            };
            CoreEntityPrefab.Prefabs.Add(ep, false);
            ep.Aliases = new HashSet<string> { "gap" };

            ep = new CoreEntityPrefab
            {
                identifier = "waypoint",
                originalName = TextManager.Get("EntityName.waypoint"),
                Description = TextManager.Get("EntityDescription.waypoint"),
                constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) })
            };
            CoreEntityPrefab.Prefabs.Add(ep, false);
            ep.Aliases = new HashSet<string> { "waypoint" };

            ep = new CoreEntityPrefab
            {
                identifier = "spawnpoint",
                originalName = TextManager.Get("EntityName.spawnpoint"),
                Description = TextManager.Get("EntityDescription.spawnpoint"),
                constructor = typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) })
            };
            CoreEntityPrefab.Prefabs.Add(ep, false);
            ep.Aliases = new HashSet<string> { "spawnpoint" };
        }

        public abstract void Dispose();

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
                
                if (PlayerInput.PrimaryMouseButtonHeld()) placePosition = position;
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

                if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    CreateInstance(newRect);
                    placePosition = Vector2.Zero;
                    selected = null;
                }

                newRect.Y = -newRect.Y;
            }

            if (PlayerInput.SecondaryMouseButtonHeld())
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
                    if (prefab.Name.ToLowerInvariant() == name || prefab.originalName.ToLowerInvariant() == name || (prefab.Aliases != null && prefab.Aliases.Any(a => a.ToLowerInvariant() == name))) return prefab;
                }
            }

            if (showErrorMessages)
            {
                DebugConsole.ThrowError("Failed to find a matching MapEntityPrefab (name: \"" + name + "\", identifier: \"" + identifier + "\").\n" + Environment.StackTrace);
            }
            return null;
        }

        /// <summary>
        /// Find a matching map entity prefab
        /// </summary>
        /// <param name="predicate">A predicate that returns true on the desired prefab.</param>
        public static MapEntityPrefab Find(Predicate<MapEntityPrefab> predicate)
        {
            return List.FirstOrDefault(p => predicate(p));
        }

        /// <summary>
        /// Check if the name or any of the aliases of this prefab match the given name.
        /// </summary>
        public bool NameMatches(string name, bool caseSensitive = false)
        {
            if (caseSensitive)
            {
                return this.originalName == name || (Aliases != null && Aliases.Any(a => a == name));
            }
            else
            {
                name = name.ToLowerInvariant();
                return this.originalName.ToLowerInvariant() == name || (Aliases != null && Aliases.Any(a => a.ToLowerInvariant() == name));
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
    }
}

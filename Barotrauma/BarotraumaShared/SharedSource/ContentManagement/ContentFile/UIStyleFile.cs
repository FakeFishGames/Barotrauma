using System;
using Barotrauma.Extensions;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
#if CLIENT
    public sealed class UIStyleFile : HashlessFile
    {
        public UIStyleFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public void LoadFromXElement(ContentXElement parentElement, bool overriding)
        {
            Identifier elemName = parentElement.NameAsIdentifier();
            Identifier elemNameWithFontSuffix = elemName.AppendIfMissing("Font");
            if (parentElement.IsOverride())
            {
                foreach (var element in parentElement.Elements())
                {
                    LoadFromXElement(element, true);
                }
            }
            else if (GUIStyle.Fonts.ContainsKey(elemNameWithFontSuffix))
            {
                GUIFontPrefab prefab = new GUIFontPrefab(parentElement, this);
                GUIStyle.Fonts[elemNameWithFontSuffix].Prefabs.Add(prefab, overriding);
            }
            else if (GUIStyle.Sprites.ContainsKey(elemName))
            {
                GUISpritePrefab prefab = new GUISpritePrefab(parentElement, this);
                GUIStyle.Sprites[elemName].Prefabs.Add(prefab, overriding);
            }
            else if (GUIStyle.SpriteSheets.ContainsKey(elemName))
            {
                GUISpriteSheetPrefab prefab = new GUISpriteSheetPrefab(parentElement, this);
                GUIStyle.SpriteSheets[elemName].Prefabs.Add(prefab, overriding);
            }
            else if (GUIStyle.Colors.ContainsKey(elemName))
            {
                GUIColorPrefab prefab = new GUIColorPrefab(parentElement, this);
                GUIStyle.Colors[elemName].Prefabs.Add(prefab, overriding);
            }
            else if (elemName == "cursor")
            {
                GUICursorPrefab prefab = new GUICursorPrefab(parentElement, this);
                GUIStyle.CursorSprite.Prefabs.Add(prefab, overriding);
            }
            else if (elemName == "style")
            {
                foreach (var element in parentElement.Elements())
                {
                    LoadFromXElement(element, overriding);
                }
            }
            else
            {
                GUIComponentStyle prefab = new GUIComponentStyle(parentElement, this);
                GUIStyle.ComponentStyles.Add(prefab, overriding);
            }
        }

        public override sealed void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }

            var rootElement = doc.Root.FromContent(Path);
            LoadFromXElement(rootElement, false);
        }

        public override sealed void UnloadFile()
        {
            GUIStyle.ComponentStyles.RemoveByFile(this);
            GUIStyle.CursorSprite.Prefabs.RemoveByFile(this);
            GUIStyle.Fonts.Values.ForEach(p => p.Prefabs.RemoveByFile(this));
            GUIStyle.Sprites.Values.ForEach(p => p.Prefabs.RemoveByFile(this));
            GUIStyle.SpriteSheets.Values.ForEach(p => p.Prefabs.RemoveByFile(this));
            GUIStyle.Colors.Values.ForEach(p => p.Prefabs.RemoveByFile(this));
        }

        public override sealed void Sort()
        {
            GUIStyle.ComponentStyles.SortAll();
            GUIStyle.CursorSprite.Prefabs.Sort();
            GUIStyle.Fonts.Values.ForEach(p => p.Prefabs.Sort());
            GUIStyle.Sprites.Values.ForEach(p => p.Prefabs.Sort());
            GUIStyle.SpriteSheets.Values.ForEach(p => p.Prefabs.Sort());
            GUIStyle.Colors.Values.ForEach(p => p.Prefabs.Sort());
        }
    }
#else
    public sealed class UIStyleFile : OtherFile
    {
        public UIStyleFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }
    }
#endif
}
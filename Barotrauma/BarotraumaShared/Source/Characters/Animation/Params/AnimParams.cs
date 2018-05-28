using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    public enum AnimationType
    {
        Walk,
        Run,
        SwimSlow,
        SwimFast
    }

    abstract class GroundedMovementParams : AnimationParams
    {
        [Serialize("1.0,1.0", true), Editable]
        public Vector2 StepSize
        {
            get;
            set;
        }
    }

    abstract class AnimationParams : ISerializableEntity
    {
        protected bool Init(string file, AnimationType type)
        {
            FilePath = file;
            AnimationType = type;
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return false;
            Name = doc.Root.Name.ToString();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
            return SerializableProperties != null;
        }

        protected string FilePath { get; private set; }
        protected const string CHARACTERS_FOLDER = @"Content/Characters";
        public virtual AnimationType AnimationType { get; private set; }

        protected static Dictionary<string, Dictionary<AnimationType, AnimationParams>> animations = new Dictionary<string, Dictionary<AnimationType, AnimationParams>>();

        public static T GetAnimParams<T>(string speciesName, AnimationType type) where T : AnimationParams, new()
        {
            if (!animations.TryGetValue(speciesName, out Dictionary<AnimationType, AnimationParams> anims))
            {
                anims = new Dictionary<AnimationType, AnimationParams>();
                animations.Add(speciesName, anims);
            }
            if (!anims.TryGetValue(type, out AnimationParams anim))
            {
                T a = new T();
                string firstLetter = speciesName.First().ToString().ToUpperInvariant();
                speciesName = firstLetter + speciesName.ToLowerInvariant().Substring(1);
                if (a.Init($"{CHARACTERS_FOLDER}/{speciesName}/{speciesName}{type.ToString()}.xml", type))
                {
                    anims.Add(type, a);
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to initialize an animation {a} of type {a.AnimationType} at {a.FilePath}");
                }
                anim = a;
            }
            return anim as T;
        }

        public string Name
        {
            get;
            private set;
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        [Serialize(1.0f, true), Editable]
        public float Speed
        {
            get;
            set;
        }

#if CLIENT
        private static GUIListBox editor;
        public static GUIListBox Editor
        {
            get
            {
                if (editor == null)
                {
                    CreateEditor();
                }
                return editor;
            }
        }

        public static void CreateEditor()
        {
            editor = new GUIListBox(new RectTransform(new Vector2(0.25f, 1), GUI.Canvas) { MinSize = new Point(200, GameMain.GraphicsHeight) });
        }

        public void AddToEditor()
        {
            Editor.AddChild(new SerializableEntityEditor(Editor.RectTransform, this, false, true, elementHeight: 0.04f));
        }

        public bool Save()
        {
            XDocument doc = XMLExtensions.TryLoadXml(FilePath);
            if (doc == null || doc.Root == null) return false;       
            SerializableProperty.SerializeProperties(this, doc.Root, true);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };
            using (var writer = XmlWriter.Create(FilePath, settings))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }
            return true;
        }

        public bool Reset()
        {
            XDocument doc = XMLExtensions.TryLoadXml(FilePath);
            if (doc == null || doc.Root == null) return false;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
            return true;
        }
#endif
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class SwimAnimation : Animation
    {
        protected SwimAnimation(string file) : base(file) { }
    }

    abstract class WalkAnimation : Animation
    {
        protected WalkAnimation(string file) : base(file) { }

        [Serialize("1.0,1.0", true), Editable]
        public Vector2 StepSize
        {
            get;
            set;
        }
    }

    abstract class Animation : ISerializableEntity
    {
        protected const string CHARACTERS_FOLDER = @"Content/Characters";
        protected abstract string CharacterName { get; }
        protected abstract string ClipName { get; }
        protected string Path => $"{CHARACTERS_FOLDER}/{CharacterName}/{ClipName}";

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

        protected string filePath;

        protected Animation(string file)
        {
            filePath = file;
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;
            Name = doc.Root.Name.ToString();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
        }

        [Serialize(1.0f, true), Editable]
        public float Speed
        {
            get;
            set;
        }

        //public static IEnumerable<Animation> GetAnimParamsFromSpeciesName(string speciesName)
        //{
        //    switch (speciesName)
        //    {
        //        case "mantis":
        //            return MantisAnimParams.Instances;
        //        default:
        //            throw new NotImplementedException();
        //    }
        //}

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
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return false;       
            SerializableProperty.SerializeProperties(this, doc.Root, true);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };
            using (var writer = XmlWriter.Create(filePath, settings))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }
            return true;
        }

        public bool Reset()
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return false;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
            return true;
        }
#endif
    }
}

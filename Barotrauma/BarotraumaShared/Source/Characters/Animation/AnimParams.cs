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
    abstract class AnimParams : ISerializableEntity
    {
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

        protected AnimParams(string file)
        {
            filePath = file;
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;
            Name = doc.Root.Name.ToString();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
        }

        [Serialize("1.0,1.0", true), Editable]
        public Vector2 StepSize
        {
            get;
            set;
        }

        [Serialize(1.0f, true), Editable]
        public float WalkSpeed
        {
            get;
            set;
        }

        [Serialize(1.0f, true), Editable]
        public float RunSpeed
        {
            get;
            set;
        }

        [Serialize(1.0f, true), Editable]
        public float SwimSpeed
        {
            get;
            set;
        }

        [Serialize(2.0f, true), Editable]
        public float RunSpeedMultiplier
        {
            get;
            set;
        }

        [Serialize(1.5f, true), Editable]
        public float SwimSpeedMultiplier
        {
            get;
            set;
        }

        [Serialize(0.0f, true), Editable]
        public float LegTorque
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

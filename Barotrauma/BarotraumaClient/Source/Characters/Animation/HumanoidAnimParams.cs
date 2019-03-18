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
    partial class HumanoidAnimParams : ISerializableEntity
    {
        private static GUIListBox editor;
        public static GUIListBox Editor
        {
            get
            {
                if (editor == null)
                {
                    editor = new GUIListBox(new RectTransform(new Vector2(0.3f, 1), GUI.Canvas));
                    //editor.AddChild(new SerializableEntityEditor(editor.RectTransform, WalkInstance, false, true, elementHeight: 20));
                    //editor.AddChild(new SerializableEntityEditor(editor.RectTransform, RunInstance, false, true, elementHeight: 20));
                }
                return editor;
            }
        }

        public void Save()
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;

            SerializableProperty.SerializeProperties(this, doc.Root, true);

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.OmitXmlDeclaration = true;
            settings.NewLineOnAttributes = true;

            using (var writer = XmlWriter.Create(filePath, settings))
            {
                doc.WriteTo(writer);
                writer.Flush();
            }
        }
    }
}

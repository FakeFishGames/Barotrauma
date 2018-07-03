using Microsoft.Xna.Framework;
using System.IO;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class EditableParams : ISerializableEntity
    {
        public bool IsLoaded { get; private set; }
        public string Name { get; private set; }
        protected string FilePath { get; private set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; protected set; }
        public SerializableEntityEditor SerializableEntityEditor { get; protected set; }

        protected XDocument doc;
        protected XDocument Doc
        {
            get
            {
                if (doc == null)
                {
                    doc = XMLExtensions.TryLoadXml(FilePath);
                }
                return doc;
            }
        }

        protected virtual bool Deserialize(XDocument doc)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
            return SerializableProperties != null;
        }

        protected virtual bool Serialize(XDocument doc)
        {
            SerializableProperty.SerializeProperties(this, doc.Root, true);
            return true;
        }

        protected bool Load(string file)
        {
            FilePath = file;
            Name = Path.GetFileNameWithoutExtension(file);     
            IsLoaded = Deserialize(Doc);
            return IsLoaded;
        }

        public bool Save()
        {
            if (!IsLoaded)
            {
                DebugConsole.ThrowError("[Params] Not loaded!");
                return false;
            }
            Serialize(Doc);
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
            if (!IsLoaded)
            {
                DebugConsole.ThrowError("[Params] Not loaded!");
                return false;
            }
            return Deserialize(Doc);
        }

        public virtual void AddToEditor(ParamsEditor editor)
        {
            if (!IsLoaded)
            {
                DebugConsole.ThrowError("[Params] Not loaded!");
                return;
            }
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, false, true);
        }
    }
}

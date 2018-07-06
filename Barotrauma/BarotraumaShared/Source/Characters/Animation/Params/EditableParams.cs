using Microsoft.Xna.Framework;
using System.IO;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    abstract class EditableParams : ISerializableEntity
    {
        public bool IsLoaded { get; private set; }
        public string Name { get; private set; }
        public string FilePath { get; private set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; protected set; }

        protected XDocument doc;
        public XDocument Doc
        {
            get
            {
                if (!IsLoaded)
                {
                    DebugConsole.ThrowError("[Params] Not loaded!");
                    return new XDocument();
                }
                return doc;
            }
        }

        protected virtual bool Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            return SerializableProperties != null;
        }

        protected virtual bool Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element, true);
            return true;
        }

        protected virtual bool Load(string file)
        {
            FilePath = file;
            Name = Path.GetFileNameWithoutExtension(FilePath);
            doc = XMLExtensions.TryLoadXml(FilePath);
            IsLoaded = Deserialize(doc.Root);
            return IsLoaded;
        }

        public virtual bool Save()
        {
            Serialize(Doc.Root);
            // TODO: would Doc.Save() be enough?
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };
            using (var writer = XmlWriter.Create(FilePath, settings))
            {
                Doc.WriteTo(writer);
                writer.Flush();
            }
            return true;
        }

        public virtual bool Reset()
        {
            return Deserialize(Doc.Root);
        }

#if CLIENT
        public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
        public virtual void AddToEditor(ParamsEditor editor)
        {
            if (!IsLoaded)
            {
                DebugConsole.ThrowError("[Params] Not loaded!");
                return;
            }
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, false, true);
        }
#endif
    }
}

using System.IO;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class EditableParams : ISerializableEntity
    {
        public bool IsLoaded { get; protected set; }
        public string Name { get; private set; }
        public string FileName { get; private set; }
        public string Folder { get; private set; }
        public string FullPath { get; private set; }
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
            protected set
            {
                doc = value;
            }
        }

        public XElement MainElement => doc.Root;
        public XElement OriginalElement { get; protected set; }

        protected virtual string GetName() => Path.GetFileNameWithoutExtension(FullPath).FormatCamelCaseWithSpaces();

        protected virtual bool Deserialize(XElement element = null)
        {
            element = element ?? MainElement;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            return SerializableProperties != null;
        }

        protected virtual bool Serialize(XElement element = null)
        {
            element = element ?? MainElement;
            if (element == null)
            {
                DebugConsole.ThrowError("[EditableParams] The XML element is null!");
                return false;
            }
            SerializableProperty.SerializeProperties(this, element, true);
            return true;
        }

        protected virtual bool Load(string file)
        {
            UpdatePath(file);
            doc = XMLExtensions.TryLoadXml(FullPath);
            if (doc == null) { return false; }
            IsLoaded = Deserialize(MainElement);
            OriginalElement = new XElement(MainElement);
            return IsLoaded;
        }

        protected virtual void UpdatePath(string fullPath)
        {
            FullPath = fullPath;
            Name = GetName();
            FileName = Path.GetFileName(FullPath);
            Folder = Path.GetDirectoryName(FullPath);
        }

        public virtual bool Save(string fileNameWithoutExtension = null, XmlWriterSettings settings = null)
        {
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
            }
            OriginalElement = MainElement;
            Serialize();
            if (settings == null)
            {
                settings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                    NewLineOnAttributes = true
                };
            }
            if (fileNameWithoutExtension != null)
            {
                UpdatePath(Path.Combine(Folder, $"{fileNameWithoutExtension}.xml"));
            }
            using (var writer = XmlWriter.Create(FullPath, settings))
            {
                Doc.WriteTo(writer);
                writer.Flush();
            }
            return true;
        }

        public virtual bool Reset(bool forceReload = false)
        {
            if (forceReload)
            {
                return Load(FullPath);
            }
            return Deserialize(OriginalElement);
        }

#if CLIENT
        public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
        public virtual void AddToEditor(ParamsEditor editor, int space = 0)
        {
            if (!IsLoaded)
            {
                DebugConsole.ThrowError("[Params] Not loaded!");
                return;
            }
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, false, true, titleFont: GUI.LargeFont);
            if (space > 0)
            {
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
            }
        }
#endif
    }
}

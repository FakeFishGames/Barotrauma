using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using File = Barotrauma.IO.File;
#if DEBUG
using System.IO;
using System.Xml;
#else
using Barotrauma.IO;
#endif

namespace Barotrauma
{
    abstract class EditableParams : ISerializableEntity
    {
        public bool IsLoaded { get; protected set; }
        public string Name { get; private set; }
        public string FileName { get; private set; }
        public string Folder { get; private set; }
        public ContentPath Path { get; protected set; } = ContentPath.Empty;
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; protected set; }

        protected ContentXElement rootElement;
        protected XDocument doc;

        private XDocument Doc
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
            set
            {
                doc = value;
            }
        }

        public virtual ContentXElement MainElement
        {
            get
            {
                if (rootElement?.Element != doc.Root)
                {
                    rootElement = doc.Root.FromPackage(Path.ContentPackage);
                }
                return rootElement;
            }
        }
        
        public ContentXElement OriginalElement { get; protected set; }

        protected ContentXElement CreateElement(string name, params object[] attrs)
            => new XElement(name, attrs).FromPackage(Path.ContentPackage);

        protected virtual string GetName() => System.IO.Path.GetFileNameWithoutExtension(Path.Value).FormatCamelCaseWithSpaces();

        protected virtual bool Deserialize(XElement element = null)
        {
            element ??= MainElement;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            return SerializableProperties != null;
        }

        protected virtual bool Serialize(XElement element = null)
        {
            element ??= MainElement;
            if (element == null)
            {
                DebugConsole.ThrowError("[EditableParams] The XML element is null!");
                return false;
            }
            SerializableProperty.SerializeProperties(this, element, true);
            return true;
        }

        protected virtual bool Load(ContentPath file)
        {
            UpdatePath(file);
            doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return false; }
            IsLoaded = Deserialize(MainElement);
            OriginalElement = new XElement(MainElement).FromPackage(MainElement.ContentPackage);
            return IsLoaded;
        }

        protected virtual void UpdatePath(ContentPath fullPath)
        {
            Path = fullPath;
            Name = GetName();
            FileName = System.IO.Path.GetFileName(Path.Value);
            Folder = System.IO.Path.GetDirectoryName(Path.Value);
        }

        public virtual bool Save(string fileNameWithoutExtension = null, System.Xml.XmlWriterSettings settings = null)
        {
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
            }
            OriginalElement = MainElement;
            Serialize();
            if (settings == null)
            {
                settings = new System.Xml.XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                    NewLineOnAttributes = true
                };
            }
            if (fileNameWithoutExtension != null)
            {
                UpdatePath(ContentPath.FromRaw(Path.ContentPackage, System.IO.Path.Combine(Folder, $"{fileNameWithoutExtension}.xml")));
            }
            using (var writer = XmlWriter.Create(Path.Value, settings))
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
                return Load(Path);
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
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, false, true, titleFont: GUIStyle.LargeFont);
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

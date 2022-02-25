#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public sealed class ContentXElement
    {
        public ContentPackage? ContentPackage { get; private set; }
        public readonly XElement Element;
        
        public ContentXElement(ContentPackage? contentPackage, XElement element)
        {
            ContentPackage = contentPackage;
            Element = element;
        }

        public static implicit operator XElement?(ContentXElement? cxe) => cxe?.Element;
        //public static implicit operator ContentXElement?(XElement? xe) => xe is null ? null : new ContentXElement(null, xe);

        public XName Name => Element.Name;
        public Identifier NameAsIdentifier() => Element.NameAsIdentifier();

        public string BaseUri => Element.BaseUri;

        public XDocument Document => Element.Document ?? throw new NullReferenceException("XML element is invalid: document is null.");
        
        public ContentXElement? FirstElement() => Elements().FirstOrDefault();
        
        public ContentXElement? Parent => Element.Parent is null ? null : new ContentXElement(ContentPackage, Element.Parent);
        public bool HasElements => Element.HasElements;

        public bool IsOverride() => Element.IsOverride();
        
        public bool ComesAfter(ContentXElement other) => Element.ComesAfter(other.Element);
        
        public ContentXElement? GetChildElement(string name)
            => Element.GetChildElement(name) is { } elem ? new ContentXElement(ContentPackage, elem) : null;
        
        public IEnumerable<ContentXElement> Elements()
            => Element.Elements().Select(e => new ContentXElement(ContentPackage, e));
        
        public IEnumerable<ContentXElement> ElementsBeforeSelf()
            => Element.ElementsBeforeSelf().Select(e => new ContentXElement(ContentPackage, e));
        
        public IEnumerable<ContentXElement> Descendants()
            => Element.Descendants().Select(e => new ContentXElement(ContentPackage, e));

        public IEnumerable<ContentXElement> GetChildElements(string name)
            => Elements().Where(e => string.Equals(name, e.Name.LocalName, StringComparison.CurrentCultureIgnoreCase));

        public XAttribute? Attribute(string name) => Element.Attribute(name);
        
        public XAttribute? GetAttribute(string name) => Element.GetAttribute(name);
        
        public IEnumerable<XAttribute> Attributes() => Element.Attributes();
        public IEnumerable<XAttribute> Attributes(string name) => Element.Attributes(name);

        public string ElementInnerText() => Element.ElementInnerText();

        public Identifier GetAttributeIdentifier(string key, string def) => Element.GetAttributeIdentifier(key, def);
        public Identifier GetAttributeIdentifier(string key, Identifier def) => Element.GetAttributeIdentifier(key, def);
        public Identifier[]? GetAttributeIdentifierArray(string key, Identifier[] def, bool trim = true) => Element.GetAttributeIdentifierArray(key, def, trim);
        public string? GetAttributeString(string key, string? def) => Element.GetAttributeString(key, def);
        public string GetAttributeStringUnrestricted(string key, string def) => Element.GetAttributeStringUnrestricted(key, def);
        public string[]? GetAttributeStringArray(string key, string[]? def, bool convertToLowerInvariant = false) => Element.GetAttributeStringArray(key, def, convertToLowerInvariant);
        public ContentPath? GetAttributeContentPath(string key) => Element.GetAttributeContentPath(key, ContentPackage);
        public int GetAttributeInt(string key, int def) => Element.GetAttributeInt(key, def);
        public int[]? GetAttributeIntArray(string key, int[]? def) => Element.GetAttributeIntArray(key, def);
        public ushort[]? GetAttributeUshortArray(string key, ushort[]? def) => Element.GetAttributeUshortArray(key, def);
        public float GetAttributeFloat(string key, float def) => Element.GetAttributeFloat(key, def);
        public float[]? GetAttributeFloatArray(string key, float[]? def) => Element.GetAttributeFloatArray(key, def);
        public float GetAttributeFloat(float def, params string[] keys) => Element.GetAttributeFloat(def, keys);
        public bool GetAttributeBool(string key, bool def) => Element.GetAttributeBool(key, def);
        public Point GetAttributePoint(string key, in Point def) => Element.GetAttributePoint(key, def);
        public Vector2 GetAttributeVector2(string key, in Vector2 def) => Element.GetAttributeVector2(key, def);
        public Vector4 GetAttributeVector4(string key, in Vector4 def) => Element.GetAttributeVector4(key, def);
        public Color GetAttributeColor(string key, in Color def) => Element.GetAttributeColor(key, def);
        public Color? GetAttributeColor(string key) => Element.GetAttributeColor(key);
        public Color[]? GetAttributeColorArray(string key, Color[]? def) => Element.GetAttributeColorArray(key, def);
        public Rectangle GetAttributeRect(string key, in Rectangle def) => Element.GetAttributeRect(key, def);
        public T GetAttributeEnum<T>(string key, in T def) where T : struct, Enum => Element.GetAttributeEnum(key, def);
        public (T1, T2) GetAttributeTuple<T1, T2>(string key, in (T1, T2) def) => Element.GetAttributeTuple(key, def);
        public (T1, T2)[] GetAttributeTupleArray<T1, T2>(string key, in (T1, T2)[] def) => Element.GetAttributeTupleArray(key, def);

        public Identifier VariantOf() => Element.VariantOf();
        
        public bool DoesAttributeReferenceFileNameAlone(string key) => Element.DoesAttributeReferenceFileNameAlone(key);

        public string ParseContentPathFromUri() => Element.ParseContentPathFromUri();

        public void SetAttributeValue(string key, string val) => Element.SetAttributeValue(key, val);

        public void Add(ContentXElement elem)
        {
            Element.Add(elem.Element);
            elem.ContentPackage = ContentPackage;
            #warning TODO: update %ModDir% instances in case the content package changes
        }
        
        public void AddFirst(ContentXElement elem)
        {
            Element.AddFirst(elem.Element);
            elem.ContentPackage = ContentPackage;
            #warning TODO: update %ModDir% instances in case the content package changes
        }
        
        public void AddAfterSelf(ContentXElement elem)
        {
            Element.AddAfterSelf(elem.Element);
            elem.ContentPackage = ContentPackage;
            #warning TODO: update %ModDir% instances in case the content package changes
        }

        public void Remove() => Element.Remove();
    }

    public static class ContentXElementExtensions
    {
        public static ContentXElement FromPackage(this XElement element, ContentPackage? contentPackage)
            => new ContentXElement(contentPackage, element);

        public static IEnumerable<ContentXElement> Elements(this IEnumerable<ContentXElement> elements)
            => elements.SelectMany(e => e.Elements());
    }
}
#nullable enable
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    #warning TODO: implement properly
    public class ServerListFilters
    {
        private readonly Dictionary<Identifier, string> attributes = new Dictionary<Identifier, string>();

        private ServerListFilters() { }

        private ServerListFilters(XElement elem)
        {
            if (elem == null) { return; }
            foreach (var attr in elem.Attributes())
            {
                attributes.Add(attr.NameAsIdentifier(), attr.Value);
            }
        }
        
        public static void Init(XElement? elem)
        {
            if (elem is null) { return; }
            
            Instance = new ServerListFilters(elem);
        }

        public void SaveTo(XElement elem)
        {
            foreach (var kvp in attributes)
            {
                elem.Add(new XAttribute(kvp.Key.Value, kvp.Value));
            }
        }

        public bool GetAttributeBool(Identifier key, bool def)
        {
            if (attributes.TryGetValue(key, out string? val))
            {
                if (bool.TryParse(val, out bool result)) { return result; }
            }

            return def;
        }
        
        public T GetAttributeEnum<T>(Identifier key, T def) where T : struct, Enum
        {
            if (attributes.TryGetValue(key, out string? val))
            {
                if (Enum.TryParse(val, out T result)) { return result; }
            }

            return def;
        }

        public void SetAttribute(Identifier key, string val)
        {
            attributes[key] = val;
        }

        public static ServerListFilters Instance { get; private set; } = new ServerListFilters();
    }
}
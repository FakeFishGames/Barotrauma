#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.ClientSource.Settings
{
    public class DebugConsoleMapping
    {
        private readonly Dictionary<KeyOrMouse, string> bindings = new Dictionary<KeyOrMouse, string>();
        public IReadOnlyDictionary<KeyOrMouse, string> Bindings => bindings;

        private DebugConsoleMapping() { }
        
        private DebugConsoleMapping(XElement element)
        {
            var bindings = new Dictionary<KeyOrMouse, string>();
            foreach (var subElement in element.Elements())
            {
                KeyOrMouse keyOrMouse = subElement.GetAttributeKeyOrMouse("key", MouseButton.None);
                if (keyOrMouse == MouseButton.None) { continue; }
                string command = subElement.GetAttributeString("command", "");
                if (command.IsNullOrWhiteSpace()) { continue; }
                bindings[keyOrMouse] = command;
            }

            this.bindings = bindings;
        }

        public static void Init(XElement? element)
        {
            if (element is null) { return; }

            Instance = new DebugConsoleMapping(element);
        }

        public void SaveTo(XElement element)
        {
            Bindings
                .ForEach(kvp => element.Add(
                    new XElement("Keybind",
                        new XAttribute("key", kvp.Key),
                        new XAttribute("command", kvp.Value))));
        }

        public void Set(KeyOrMouse key, string command)
            => bindings[key] = command;

        public void Remove(KeyOrMouse key)
            => bindings.Remove(key);
        
        public static DebugConsoleMapping Instance { get; private set; } = new DebugConsoleMapping();
    }

}

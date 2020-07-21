#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class NPCSet : IDisposable
    {
        private static List<NPCSet>? Sets { get; set; }

        private string Identifier { get; }

        private readonly List<HumanPrefab> Humans = new List<HumanPrefab>();

        private bool Disposed { get; set; }

        private NPCSet(XElement element, string filePath)
        {
            Identifier = element.GetAttributeString("identifier", string.Empty);

            foreach (XElement npcElement in element.Elements())
            {
                Humans.Add(new HumanPrefab(npcElement, filePath));
            }
        }

        public static HumanPrefab? Get(string identifier, string npcidentifier)
        {
            HumanPrefab prefab = Sets.Where(set => set.Identifier == identifier).SelectMany(npcSet => npcSet.Humans.Where(npcSetHuman => npcSetHuman.Identifier == npcidentifier)).FirstOrDefault();

            if (prefab == null)
            {
                DebugConsole.ThrowError($"Could not find human prefab \"{npcidentifier}\" from \"{identifier}\".");
                return null;
            }
            return new HumanPrefab(prefab.Element, prefab.FilePath);
        }

        public static void LoadSets()
        {
            Sets?.ForEach(set => set.Dispose());
            Sets = new List<NPCSet>();
            IEnumerable<ContentFile> files = GameMain.Instance.GetFilesOfType(ContentType.NPCSets);
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                XElement? rootElement = doc?.Root;

                if (doc == null || rootElement == null) { continue; }

                if (doc.Root.IsOverride())
                {
                    Sets.Clear();
                    DebugConsole.NewMessage($"Overriding all NPC sets with '{file.Path}'", Color.Yellow);
                }

                foreach (XElement element in rootElement.Elements())
                {
                    bool isOverride = element.IsOverride();
                    XElement sourceElement = isOverride ? element.FirstElement() : element;
                    string elementName = sourceElement.Name.ToString().ToLowerInvariant();
                    string identifier = sourceElement.GetAttributeString("identifier", null);

                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        DebugConsole.ThrowError($"No identifier defined for the NPC set config '{elementName}' in file '{file.Path}'");
                        continue;
                    }

                    var existingParams = Sets.Find(set => set.Identifier == identifier);
                    if (existingParams != null)
                    {
                        if (isOverride)
                        {
                            DebugConsole.NewMessage($"Overriding NPC set config '{identifier}' using the file '{file.Path}'", Color.Yellow);
                            Sets.Remove(existingParams);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Duplicate NPC set config: '{identifier}' defined in {elementName} of '{file.Path}'");
                            continue;
                        }
                    }

                    Sets.Add(new NPCSet(element, file.Path));
                }
            }
        }

        private void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    Humans.Clear();
                }
            }

            Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
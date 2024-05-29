#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    static class WreckConverter
    {
        private static readonly string[] itemsToRemove =
        {
            "circuitboxcomponent",
            "wire",
        };

        public static XElement ConvertToWreck(XElement submarineElement)
        {
            ImmutableHashSet<Identifier> availableWreckContainerTags = ItemPrefab.Prefabs
                .SelectMany(ip => ip.PreferredContainers.SelectMany(pc => pc.Primary.Union(pc.Secondary)))
                .Where(t => !ItemPrefab.Prefabs.ContainsKey(t) && t.StartsWith("wreck"))
                .ToImmutableHashSet();

            bool monsterSpawnPointCreated = false;

            List<string> warnings = new List<string>();

            var wreckElement = new XElement(submarineElement);
            foreach (var element in wreckElement.Elements().ToList())
            {
                var identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (identifier.IsEmpty)
                {
                    if (element.NameAsIdentifier() == "waypoint")
                    {
                        if (element.GetAttributeEnum("spawn", SpawnType.Path) != SpawnType.Human) { continue; }
                        if (element.GetAttributeIdentifier("job", Identifier.Empty) == Identifier.Empty)
                        {
                            element.SetAttributeValue("spawn", SpawnType.Enemy);
                            DebugConsole.NewMessage("Converted a non-job-specific spawnpoint to an enemy spawnpoint.");
                            monsterSpawnPointCreated = true;
                        }
                        else
                        {
                            element.SetAttributeValue("spawn", SpawnType.Corpse);
                        }
                    }
                    continue; 
                }

                //remove if set to be removed
                var tags = element.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>());
                if (itemsToRemove.Any(it => tags.Contains(it.ToIdentifier())))
                {
                    element.Remove();
                    continue;
                }

                bool tagsModified = false;
                for (int i = 0; i < tags.Length; i++)
                {
                    Identifier wreckTag = ("wreck" + tags[i]).ToIdentifier();
                    if (availableWreckContainerTags.Contains(wreckTag))
                    {
                        DebugConsole.NewMessage($"Replaced tag {tags[i]} with {wreckTag} in item \"{identifier}\".");
                        tags[i] = wreckTag;
                        tagsModified = true;
                    }
                }
                if (tagsModified)
                {
                    element.SetAttributeValue("tags", string.Join(",", tags.Select(t => t.ToString())));
                }

                Identifier[] wreckedIdentifiers =
                {
                    (identifier + "wrecked").ToIdentifier(),
                    (identifier + "_wrecked").ToIdentifier(),
                };

                //turn to wrecked version if one is available
                foreach (var wreckedIdentifier in wreckedIdentifiers)
                {
                    var wreckedPrefab = MapEntityPrefab.FindByIdentifier(wreckedIdentifier);
                    if (wreckedPrefab == null) { continue; }
                    
                    var oldPrefab = MapEntityPrefab.FindByIdentifier(identifier);
                    element.SetAttributeValue("identifier", wreckedIdentifier);
                    float currentScale = element.GetAttributeFloat("scale", oldPrefab.Scale);
                    element.SetAttributeValue("scale", currentScale * (wreckedPrefab.Scale / oldPrefab.Scale));

                    if (wreckedPrefab is ItemPrefab wreckedItemPrefab)
                    {
                        //remove connections that don't exist in the wreck version
                        var originalConnectionPanelElement = element.GetChildElement(nameof(ConnectionPanel));
                        var wreckedConnectionPanelElement = wreckedItemPrefab.ConfigElement.GetChildElement(nameof(ConnectionPanel));
                        if (originalConnectionPanelElement != null && wreckedConnectionPanelElement != null)
                        {
                            foreach (var connectionElement in originalConnectionPanelElement.Elements().ToList())
                            {
                                var elementName = connectionElement.NameAsIdentifier();
                                if (elementName != "input" && elementName != "output") { continue; }
                                string connectionName = connectionElement.GetAttributeString("name", string.Empty);
                                if (wreckedConnectionPanelElement
                                        .GetChildElements(connectionElement.Name.LocalName)
                                        .None(c => c.GetAttributeString("name", string.Empty) == connectionName))
                                {
                                    connectionElement.Remove();
                                }
                            }
                        }
                    }
                    else if (wreckedPrefab is StructurePrefab wreckedStructurePrefab)
                    {
                        //if the dimensions of the structures are different, rescale
                        //ignore small differences, they tend to be just irrelevant differences in how the sourcerect is scaled
                        const int MaximumSizeDifference = 5;
                        Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
                        if (!wreckedStructurePrefab.ResizeHorizontal)
                        {
                            if (Math.Abs(wreckedStructurePrefab.ScaledSize.X - rect.Width) > MaximumSizeDifference)
                            {
                                DebugConsole.NewMessage($"The prefab {wreckedStructurePrefab.Name} has different dimensions than the original one. Changing the width from {rect.Width} to {(int)wreckedStructurePrefab.ScaledSize.X}.", Color.Yellow);
                            }
                            rect.Width = (int)wreckedStructurePrefab.ScaledSize.X;
                        }
                        if (!wreckedStructurePrefab.ResizeVertical)
                        {
                            if (Math.Abs(wreckedStructurePrefab.ScaledSize.Y - rect.Height) > MaximumSizeDifference)
                            {
                                DebugConsole.NewMessage($"The prefab {wreckedStructurePrefab.Name} has different dimensions than the original one. Changing the height from {rect.Height} to {(int)wreckedStructurePrefab.ScaledSize.Y}.", Color.Yellow);
                            }
                            rect.Height = (int)wreckedStructurePrefab.ScaledSize.Y;
                        }
                        element.SetAttributeValue("rect", XMLExtensions.RectToString(rect));
                    }
                    break;
                }

                var itemContainerElement = element.GetChildElement(nameof(ItemContainer));
                if (itemContainerElement != null)
                {
                    string containedString = itemContainerElement.GetAttributeString("contained", "");
                    string[] itemIdStrings = containedString.Split(',');
                    var itemIds = new HashSet<ushort>();
                    foreach (string idListStr in itemIdStrings)
                    {
                        foreach (string idStr in idListStr.Split(';'))
                        {
                            if (int.TryParse(idStr, out int id)) { itemIds.Add((UInt16)id); }
                        }
                    }
                    if (itemIds.Any())
                    {
                        List<string> containedItemNames = new List<string>();
                        foreach (var itemElement in wreckElement.Elements())
                        {
                            var id = itemElement.GetAttributeUInt16("id", Entity.NullEntityID);
                            if (itemIds.Contains(id))
                            {
                                containedItemNames.Add(itemElement.GetAttributeString("identifier", string.Empty));
                            }
                        }
                        warnings.Add($"Potential issue in container \"{identifier}\". The following items are pre-placed, and may interfere with the loot generated in the wreck: " + string.Join(", ", containedItemNames));
                    }
                }

                //set to 0 condition if repairable, exclude doors and hatches
                if (element.GetChildElement(nameof(Repairable)) != null && element.GetChildElement(nameof(Door)) == null)
                {
                    element.SetAttributeValue("conditionpercentage", 0.0f);
                }
            }

            foreach (var warning in warnings)
            {
                DebugConsole.AddWarning(warning);
            }

            if (!monsterSpawnPointCreated)
            {
                DebugConsole.ThrowError("There are no monster spawnpoints in the wreck. Remember to add some for monsters to spawn properly!");
            }

            return wreckElement;
        }
    }
}

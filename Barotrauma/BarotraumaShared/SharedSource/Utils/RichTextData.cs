using System;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public class RichTextData
    {
        public int StartIndex, EndIndex;
        public Color? Color;
        public string Metadata;

        public float Alpha = 1.0f;

        private const char definitionIndicator = '‖';
        private const char attributeSeparator = ';';
        private const char keyValueSeparator = ':';
        //private const char lineChangeIndicator = '\n';

        private const string colorDefinition = "color";
        private const string iconDefinition = "icon";
        private const string metadataDefinition = "metadata";
        private const string endDefinition = "end";

        public static ImmutableArray<RichTextData>? GetRichTextData(string text, out string sanitizedText)
        {
            sanitizedText = text;
            if (!string.IsNullOrEmpty(text) && text.Contains(definitionIndicator, StringComparison.Ordinal))
            {
                text = text.Replace("\r", "");
                string[] segments = text.Split(definitionIndicator);

                sanitizedText = string.Empty;

                List<RichTextData> textColors = [];
                RichTextData tempData = null;

                int prevIndex = 0;
                int currIndex = 0;
                bool wasIconBefore = false;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (i % 2 == 0)
                    {
                        string segment = segments[i];
                        // Having a string like "‖color:gui.green‖‖icon:test‖‖end‖" will leave some 0 length strings that break the indexing.
                        // Because this system was never meant to handle nesting.
                        // So to fix it, don't set prevIndex on an empty segment.
                        // Also, if there was an icon before the segment we skip,
                        // since the segments before and after the icon should be stitched together.
                        if (!string.IsNullOrEmpty(segment))
                        {
                            sanitizedText += segment;
                            if (!wasIconBefore)
                            {
                                prevIndex = currIndex;
                            }
                            currIndex += segment.Replace("\n", "").Replace("\r", "").Length;
                        }

                        wasIconBefore = false;
                    }
                    else
                    {
                        string[] attributes = segments[i].Split(attributeSeparator);
                        foreach (string attribute in attributes)
                        {
                            if (attribute.Equals(endDefinition, StringComparison.OrdinalIgnoreCase) ||
                                attribute.Equals($"{colorDefinition}:{endDefinition}", StringComparison.OrdinalIgnoreCase) ||
                                attribute.Equals($"{metadataDefinition}:{endDefinition}", StringComparison.OrdinalIgnoreCase) ||
                                attribute.Equals($"{iconDefinition}:{endDefinition}", StringComparison.OrdinalIgnoreCase))
                            {
                                if (tempData != null)
                                {
                                    tempData.StartIndex = prevIndex;
                                    tempData.EndIndex = currIndex - 1;
                                    textColors.Add(tempData);
                                }
                                tempData = null;
                            }
                            else if (attribute.StartsWith(colorDefinition, StringComparison.OrdinalIgnoreCase))
                            {
                                tempData ??= new RichTextData();
                                string valueStr = attribute[(attribute.IndexOf(keyValueSeparator) + 1)..];
                                if (valueStr.Equals("null", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    tempData.Color = null;
                                }
                                else
                                {
                                    tempData.Color = XMLExtensions.ParseColor(valueStr);
                                }
                                prevIndex = currIndex;
                            }
                            else if (attribute.StartsWith(metadataDefinition, StringComparison.OrdinalIgnoreCase))
                            {
                                tempData ??= new RichTextData();
                                tempData.Metadata = attribute[(attribute.IndexOf(keyValueSeparator) + 1)..];
                            }
                            else if (attribute.StartsWith(iconDefinition, StringComparison.OrdinalIgnoreCase))
                            {
                                wasIconBefore = true;
                                // this should be fine, we use rich text in very few places on the server side,
                                // and none of the users matter for parsing icons
#if CLIENT
                                string valueStr = attribute[(attribute.IndexOf(keyValueSeparator) + 1)..];
                                if (GUIStyle.IconFont.Prefabs.ActivePrefab is { } iconFont &&
                                    iconFont.IconFontCharacterMap.TryGetValue(valueStr.ToIdentifier(), out uint value))
                                {
                                    sanitizedText += (char)value;
                                }
                                else
                                {
                                    sanitizedText += '?';
                                }
#elif SERVER
                                sanitizedText += "?";
#endif
                                currIndex++;
                            }
                        }
                    }
                }
                return [..textColors];
            }
            return null;
        }
    }
}

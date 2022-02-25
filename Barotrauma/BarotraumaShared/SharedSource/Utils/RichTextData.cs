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
        private const string metadataDefinition = "metadata";
        private const string endDefinition = "end";

        public static ImmutableArray<RichTextData>? GetRichTextData(string text, out string sanitizedText)
        {
            sanitizedText = text;
            if (!string.IsNullOrEmpty(text) && text.Contains(definitionIndicator, System.StringComparison.Ordinal))
            {
                text = text.Replace("\r", "");
                string[] segments = text.Split(definitionIndicator);

                sanitizedText = string.Empty;

                List<RichTextData> textColors = new List<RichTextData>();
                RichTextData tempData = null;

                int prevIndex = 0;
                int currIndex = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (i % 2 == 0)
                    {
                        sanitizedText += segments[i];
                        prevIndex = currIndex;
                        currIndex += segments[i].Replace("\n", "").Replace("\r", "").Length;
                    }
                    else
                    {
                        string[] attributes = segments[i].Split(attributeSeparator);
                        for (int j = 0; j < attributes.Length; j++)
                        {
                            if (attributes[j].Contains(endDefinition))
                            {
                                if (tempData != null)
                                {
                                    tempData.StartIndex = prevIndex;
                                    tempData.EndIndex = currIndex - 1;
                                    textColors.Add(tempData);
                                }
                                tempData = null;
                            }
                            else if (attributes[j].StartsWith(colorDefinition))
                            {
                                if (tempData == null) { tempData = new RichTextData(); }
                                string valueStr = attributes[j].Substring(attributes[j].IndexOf(keyValueSeparator) + 1);
                                if (valueStr.Equals("null", System.StringComparison.InvariantCultureIgnoreCase))
                                {
                                    tempData.Color = null;
                                }
                                else
                                {
                                    tempData.Color = XMLExtensions.ParseColor(valueStr);
                                }
                            }
                            else if (attributes[j].StartsWith(metadataDefinition))
                            {
                                if (tempData == null) { tempData = new RichTextData(); }
                                tempData.Metadata = attributes[j].Substring(attributes[j].IndexOf(keyValueSeparator) + 1);
                            }
                        }
                    }
                }
                return textColors.ToImmutableArray();
            }
            return null;
        }
    }
}

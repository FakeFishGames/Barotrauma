using Microsoft.Xna.Framework;
using System;
using System.Text;

namespace Barotrauma
{
    public static partial class ToolBox
    {
        public static string LimitString(string str, ScalableFont font, int maxWidth)
        {
            if (maxWidth <= 0 || string.IsNullOrWhiteSpace(str)) return "";

            float currWidth = font.MeasureString("...").X;
            for (int i = 0; i < str.Length; i++)
            {
                currWidth += font.MeasureString(str[i].ToString()).X;

                if (currWidth > maxWidth)
                {
                    return str.Substring(0, Math.Max(i - 2, 1)) + "...";
                }
            }

            return str;
        }

        public static string WrapText(string text, float lineLength, ScalableFont font, float textScale = 1.0f) //TODO: could integrate this into the ScalableFont class directly
        {
            if (font.MeasureString(text).X < lineLength) return text;

            text = text.Replace("\n", " \n ");

            string[] words = text.Split(' ');

            StringBuilder wrappedText = new StringBuilder();
            float linePos = 0f;
            float spaceWidth = font.MeasureString(" ").X * textScale;
            for (int i = 0; i < words.Length; ++i)
            {
                if (string.IsNullOrWhiteSpace(words[i]) && words[i] != "\n") continue;

                Vector2 size = font.MeasureString(words[i]) * textScale;
                if (size.X > lineLength)
                {
                    if (linePos == 0.0f)
                    {
                        wrappedText.AppendLine(words[i]);
                    }
                    else
                    {
                        do
                        {
                            if (words[i].Length == 0) break;

                            wrappedText.Append(words[i][0]);
                            words[i] = words[i].Remove(0, 1);

                            linePos += size.X;
                        } while (words[i].Length > 0 && (size = font.MeasureString((words[i][0]).ToString()) * textScale).X + linePos < lineLength);

                        wrappedText.Append("\n");
                        linePos = 0.0f;
                        i--;
                    }

                    continue;
                }

                if (linePos + size.X < lineLength)
                {
                    wrappedText.Append(words[i]);
                    if (words[i] == "\n")
                    {
                        linePos = 0.0f;
                    }
                    else
                    {

                        linePos += size.X + spaceWidth;
                    }
                }
                else
                {
                    wrappedText.Append("\n");
                    wrappedText.Append(words[i]);

                    linePos = size.X + spaceWidth;
                }

                if (i < words.Length - 1) wrappedText.Append(" ");
            }

            return wrappedText.ToString();
        }
    }
}

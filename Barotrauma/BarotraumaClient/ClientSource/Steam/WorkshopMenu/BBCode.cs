#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Steam
{
    abstract partial class WorkshopMenu
    {
        protected readonly struct BBWord
        {
            [Flags]
            public enum TagType
            {
                None = 0x0,
                Bold = 0x1,
                Italic = 0x2,
                Header = 0x4,
                List = 0x8,
                NewLine = 0x10
            }

            public readonly string Text;
            public readonly Vector2 Size;
            public readonly TagType TagTypes;

            public readonly GUIFont Font;

            public BBWord(string text, TagType tagTypes)
            {
                Text = text;
                TagTypes = tagTypes;
                Font = tagTypes.HasFlag(TagType.Header)
                    ? GUIStyle.LargeFont
                    : tagTypes.HasFlag(TagType.Bold)
                        ? GUIStyle.SubHeadingFont
                        : GUIStyle.Font;
                Size = Font.MeasureString(Text);
            }
        }

        protected static readonly Regex bbTagRegex = new Regex(@"\[(.+?)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        protected static void CreateBBCodeElement(Steamworks.Ugc.Item workshopItem, GUIListBox container)
        {
            Point cachedContainerSize = Point.Zero;
            List<BBWord> bbWords = new List<BBWord>();
            Stack<BBWord.TagType> tagStack = new Stack<BBWord.TagType>();

            string bbCode = "";

            void forceReset()
            {
                bbWords.Clear();
                cachedContainerSize = Point.Zero;
            }
            
            void recalculate(GUICustomComponent component)
            {
                if (cachedContainerSize == component.RectTransform.NonScaledSize) { return; }

                bbWords.Clear();
                cachedContainerSize = component.RectTransform.NonScaledSize;

                var matches = new Stack<Match>(bbTagRegex.Matches(bbCode).Reverse());
                Match? nextTag = null;
                matches.TryPop(out nextTag);
                int wordStart = 0;
                BBWord.TagType currTagType;
                for (int i = 0; i < bbCode.Length; i++)
                {
                    char currChar = bbCode[i];
                    currTagType = tagStack.TryPeek(out var t) ? t : BBWord.TagType.None;

                    bool charIsCJK = TextManager.IsCJK($"{currChar}");
                    bool wordEnd = char.IsWhiteSpace(currChar) || charIsCJK;
                    int reachedTagLength = 0;
                    if (nextTag is { Index: int tagIndex, Length: int tagLength }
                        && i == tagIndex)
                    {
                        reachedTagLength = tagLength;
                        string tagStr = nextTag.Value.Replace("[", "").Replace("]", "").Trim();
                        bool isClosing = tagStr.StartsWith("/");
                        tagStr = tagStr.Replace("/", "").Trim().ToLowerInvariant();
                        BBWord.TagType tagType = tagStr switch
                        {
                            "b" => BBWord.TagType.Bold,
                            "i" => BBWord.TagType.Italic,
                            "h1" => BBWord.TagType.Header,
                            _ => BBWord.TagType.None
                        };

                        if (tagType != BBWord.TagType.None)
                        {
                            if (isClosing)
                            {
                                if (currTagType == tagType)
                                {
                                    tagStack.Pop();
                                }
                            }
                            else
                            {
                                tagStack.Push(tagType);
                            }
                        }
                    }

                    if (wordEnd || reachedTagLength > 0)
                    {
                        string word = bbCode[wordStart..i];
                        if (charIsCJK) { word = bbCode[wordStart..(i + 1)]; }
                        else if (char.IsWhiteSpace(currChar) && currChar != '\n') { word += " "; }

                        if (!word.IsNullOrEmpty())
                        {
                            bbWords.Add(new BBWord(word, currTagType));
                        }
                        else if (currChar == '\n')
                        {
                            bbWords.Add(new BBWord("", BBWord.TagType.NewLine));
                        }

                        if (reachedTagLength > 0)
                        {
                            i += reachedTagLength - 1;
                            nextTag = matches.TryPop(out var tag) ? tag : null;
                        }

                        wordStart = i + 1;
                    }
                }

                currTagType = tagStack.TryPeek(out var ft) ? ft : BBWord.TagType.None;
                string finalWord = bbCode[wordStart..];
                if (!finalWord.IsNullOrEmpty())
                {
                    bbWords.Add(new BBWord(finalWord, currTagType));
                }

                container.RecalculateChildren();
                container.UpdateScrollBarSize();
            }

            void draw(SpriteBatch spriteBatch, GUICustomComponent component)
            {
                recalculate(component);
                Vector2 currPos = Vector2.Zero;
                Vector2 rectPos = component.Rect.Location.ToVector2();
                for (int i = 0; i < bbWords.Count; i++)
                {
                    var bbWord = bbWords[i];
                    if (currPos.X > 0.0f
                        && currPos.X + bbWord.Size.X >= component.Rect.Width)
                    {
                        //wrap because we went over width limit
                        currPos = (0.0f, currPos.Y + bbWord.Size.Y);
                    }

                    bbWord.Font.DrawString(
                        spriteBatch,
                        bbWord.Text,
                        (currPos + rectPos).ToPoint().ToVector2(),
                        GUIStyle.TextColorNormal,
                        forceUpperCase: ForceUpperCase.No,
                        italics: bbWord.TagTypes.HasFlag(BBWord.TagType.Italic));
                    bool breakLine
                        = bbWord.TagTypes.HasFlag(BBWord.TagType.NewLine)
                          || (i < bbWords.Count - 1 &&
                              bbWords[i + 1].TagTypes.HasFlag(BBWord.TagType.Header) !=
                              bbWord.TagTypes.HasFlag(BBWord.TagType.Header));
                    if (breakLine)
                    {
                        //break line because of a header change or newline was found
                        currPos = (0.0f, currPos.Y + bbWord.Size.Y);
                    }
                    else
                    {
                        currPos.X += bbWord.Size.X;
                    }
                }

                component.RectTransform.NonScaledSize
                    = (component.RectTransform.NonScaledSize.X,
                        (int)(currPos.Y + bbWords.LastOrDefault().Size.Y));
                component.RectTransform.RelativeSize
                    = component.RectTransform.NonScaledSize.ToVector2() / component.Parent.Rect.Size.ToVector2();
            }

            TaskPool.Add(
                $"GetWorkshopItemLongDescriptionFor{workshopItem.Id.Value}",
                SteamManager.Workshop.GetItemAsap(workshopItem.Id.Value, withLongDescription: true),
                t =>
                {
                    if (!t.TryGetResult(out Steamworks.Ugc.Item? workshopItemWithDescription)) { return; }

                    bbCode = workshopItemWithDescription?.Description ?? "";
                    forceReset();
                });

            new GUICustomComponent(
                new RectTransform(Vector2.One, container.Content.RectTransform),
                onDraw: draw);
        }
    }
}

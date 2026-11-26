using Barotrauma.Extensions;
using Barotrauma.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    /// <param name="Color">The color of the strike. Defaults to the text color.</param>
    /// <param name="Thickness">The thickness of the strike.</param>
    /// <param name="Expand">The length (in pixels) that the strike extends beyond the text boundaries.</param>
    public readonly record struct Strikethrough(Color? Color = null, float Thickness = 2, float Expand = 0)
    {
        public readonly void Draw(SpriteBatch sb, in float lineHeight, Color textColor, Vector2 advanceUnit, Vector2 lineStart, Vector2 lineEnd)
        {
            if (lineStart == lineEnd) { return; }
            Vector2 expand = Expand * advanceUnit;
            Vector2 offset = lineHeight / 2 * new Vector2(-advanceUnit.Y, advanceUnit.X);
            sb.DrawLine(lineStart - expand + offset, lineEnd + expand + offset, Color ?? textColor, Thickness);
        }
        public readonly void Draw(SpriteBatch sb, in float lineHeight, Color textColor, Vector2 lineStart, Vector2 lineEnd)
        {
            if (lineStart == lineEnd) { return; }
            Vector2 expand = new(Expand);
            Vector2 offset = new(0, lineHeight / 2);
            sb.DrawLine(lineStart - expand + offset, lineEnd + expand + offset, Color ?? textColor, Thickness);
        }
    }

    public class ScalableFont : IDisposable
    {
        private static readonly List<ScalableFont> FontList = new List<ScalableFont>();
        private static Library Lib = null;
        private static readonly object globalMutex = new object();

        private readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();

        private readonly string filename;
        private readonly Face face;
        private uint size;
        private int baseHeight;

        private readonly Dictionary<uint, GlyphData> texCoords;
        private readonly List<Texture2D> textures;
        private readonly GraphicsDevice graphicsDevice;

        [MaybeNull, AllowNull]
        private readonly ScalableFont iconFont;

        private Vector2 currentDynamicAtlasCoords;
        private int currentDynamicAtlasNextY;
        uint[] currentDynamicPixelBuffer;

        public bool DynamicLoading
        {
            get;
            private set;
        }

        public TextManager.SpeciallyHandledCharCategory SpeciallyHandledCharCategory
        {
            get;
            private set;
        }

        public uint Size
        {
            get
            {
                return size;
            }
            set
            {
                size = value;
                if (graphicsDevice != null) { RenderAtlas(graphicsDevice); }
            }
        }

        public bool ForceUpperCase = false;

        public float LineHeight => baseHeight * 1.8f;

        /// <summary>
        /// Character ranges between each even element with their corresponding odd element. Default is 0x20 to 0xFFFF.
        /// </summary>
        private uint[] charRanges;

        /// <summary>
        /// Texture dimensions. Default is 1024x1024.
        /// </summary>
        private int texDims = 1024;

        /// <summary>
        /// Base character used to shift all other characters downwards when rendering. Defaults to T.
        /// </summary>
        private uint baseChar;

        public readonly record struct GlyphData(
            int TexIndex = default,
            Vector2 DrawOffset = default,
            float Advance = default,
            Rectangle TexCoords = default);

        public static Dictionary<Identifier, uint> GetGlyphNamesForFont(string filePath, Range<uint> charRange)
        {
            lock (globalMutex)
            {
                Lib ??= new Library();
            }

            using Face face = new Face(Lib, filePath);
            Dictionary<Identifier, uint> charMap = [];

            if (!face.HasGlyphNames)
            {
                DebugConsole.ThrowError($"Error in {nameof(GetGlyphNamesForFont)}: {filePath}: Font does not have glyph names");
                return charMap;
            }

            byte[] buffer = new byte[32];
            for (uint charIndex = charRange.Start; charIndex <= charRange.End; charIndex++)
            {
                uint glyphIndex = face.GetCharIndex(charIndex);
                face.GetGlyphName(glyphIndex, buffer);
                string name = string.Empty;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] is 0)
                    {
                        name = System.Text.Encoding.UTF8.GetString(buffer.AsSpan()[..i]);
                        break;
                    }

                }

                charMap[name.ToIdentifier()] = charIndex;
            }

            return charMap;
        }

        public static TextManager.SpeciallyHandledCharCategory ExtractShccFromXElement(XElement element)
            => TextManager.SpeciallyHandledCharCategories
                .Where(category => element.GetAttributeBool($"is{category}", category switch {
                    // CJK and Japanese aren't supported by default
                    TextManager.SpeciallyHandledCharCategory.CJK => false,
                    TextManager.SpeciallyHandledCharCategory.Japanese => false,

                    // For backwards compatibility, we assume that Cyrillic is supported by default
                    TextManager.SpeciallyHandledCharCategory.Cyrillic => true,

                    _ => throw new NotImplementedException($"nameof{category} not implemented.")
                }))
                .Aggregate(TextManager.SpeciallyHandledCharCategory.None, (current, category) => current | category);

        public ScalableFont(ContentXElement element, uint defaultSize = 14, GraphicsDevice gd = null, uint[] charRanges = null, uint baseChar = 0x54)
            : this(
                element.GetAttributeContentPath("file")?.Value,
                (uint)element.GetAttributeInt("size", (int)defaultSize),
                gd,
                element.GetAttributeBool("dynamicloading", false),
                iconFont: null,
                charRanges: charRanges,
                baseChar: baseChar,
                ExtractShccFromXElement(element))
        {
        }

        public ScalableFont(
            string filename,
            uint size,
            GraphicsDevice gd = null,
            bool dynamicLoading = false,
            ScalableFont iconFont = null,
            uint[] charRanges = null,
            uint baseChar = 0x54,
            TextManager.SpeciallyHandledCharCategory speciallyHandledCharCategory = TextManager.SpeciallyHandledCharCategory.None)
        {
            lock (globalMutex)
            {
                Lib ??= new Library();
            }

            this.filename = filename;
            this.face = null;
            this.iconFont = iconFont;
            using (new ReadLock(rwl))
            {
                foreach (ScalableFont font in FontList)
                {
                    if (font.filename == filename)
                    {
                        this.face = font.face;
                        break;
                    }
                }
            }

            this.face ??= new Face(Lib, filename);
            this.size = size;
            this.textures = new List<Texture2D>();
            this.texCoords = new Dictionary<uint, GlyphData>();
            this.DynamicLoading = dynamicLoading;
            this.SpeciallyHandledCharCategory = speciallyHandledCharCategory;
            this.graphicsDevice = gd;
            this.charRanges = charRanges ?? [0x20, 0xFFFF];
            this.baseChar = baseChar;

            if (gd != null && !dynamicLoading)
            {
                RenderAtlas(gd);
            }

            lock (globalMutex)
            {
                FontList.Add(this);
            }
        }

        /// <summary>
        /// Renders the font into at least one texture atlas, which is simply a collection of all glyphs in the ranges defined by charRanges.
        /// Don't call this too often or with very large sizes.
        /// </summary>
        /// <param name="gd">Graphics device, required to create textures.</param>
        private void RenderAtlas(GraphicsDevice gd)
        {
            if (DynamicLoading) { return; }

            textures.ForEach(t => t.Dispose());
            textures.Clear();
            texCoords.Clear();

            uint[] pixelBuffer = new uint[texDims * texDims];
            for (int i = 0; i < texDims * texDims; i++)
            {
                pixelBuffer[i] = 0;
            }

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
            });
            int texIndex = 0;

            Vector2 currentCoords = Vector2.Zero;
            int nextY = 0;

            using (new WriteLock(rwl))
            {
                face.SetPixelSizes(0, size);
                face.LoadGlyph(face.GetCharIndex(baseChar), LoadFlags.Default, LoadTarget.Normal);
                baseHeight = face.Glyph.Metrics.Height.ToInt32();

                for (int i = 0; i < charRanges.Length; i += 2)
                {
                    uint start = charRanges[i];
                    uint end = charRanges[i + 1];
                    for (uint j = start; j <= end; j++)
                    {
                        uint glyphIndex = face.GetCharIndex(j);
                        if (glyphIndex == 0)
                        {
                            texCoords.Add(j, new GlyphData(
                                Advance: 0,
                                TexIndex: -1));
                            continue;
                        }
                        face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                        if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                        {
                            //glyph is empty, but char might still apply advance
                            GlyphData blankData = new GlyphData(
                                Advance: Math.Max((float)face.Glyph.Metrics.HorizontalAdvance, 0f),
                                TexIndex: -1); //indicates no texture because the glyph is empty

                            texCoords.Add(j, blankData);
                            continue;
                        }
                        //stacktrace doesn't really work that well when RenderGlyph throws an exception
                        face.Glyph.RenderGlyph(RenderMode.Normal);
                        byte[] bitmap = face.Glyph.Bitmap.BufferData;
                        int glyphWidth = face.Glyph.Bitmap.Width;
                        int glyphHeight = bitmap.Length / glyphWidth;

                        //if (glyphHeight>lineHeight) lineHeight=glyphHeight;

                        if (glyphWidth > texDims - 1 || glyphHeight > texDims - 1)
                        {
                            throw new Exception(filename + ", " + size.ToString() + ", " + (char)j + "; Glyph dimensions exceed texture atlas dimensions");
                        }

                        nextY = Math.Max(nextY, glyphHeight + 2);

                        if (currentCoords.X + glyphWidth + 2 > texDims - 1)
                        {
                            currentCoords.X = 0;
                            currentCoords.Y += nextY;
                            nextY = 0;
                        }
                        if (currentCoords.Y + glyphHeight + 2 > texDims - 1)
                        {
                            currentCoords.X = 0;
                            currentCoords.Y = 0;
                            CrossThread.RequestExecutionOnMainThread(() =>
                            {
                                textures[texIndex].SetData<uint>(pixelBuffer);
                                textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                            });
                            texIndex++;
                            for (int k = 0; k < texDims * texDims; k++)
                            {
                                pixelBuffer[k] = 0;
                            }
                        }

                        GlyphData newData = new GlyphData(
                            Advance: (float)face.Glyph.Metrics.HorizontalAdvance,
                            TexIndex: texIndex,
                            TexCoords: new Rectangle((int)currentCoords.X, (int)currentCoords.Y, glyphWidth, glyphHeight),
                            DrawOffset: new Vector2(face.Glyph.BitmapLeft, baseHeight * 14 / 10 - face.Glyph.BitmapTop)
                        );
                        texCoords.Add(j, newData);

                        for (int y = 0; y < glyphHeight; y++)
                        {
                            for (int x = 0; x < glyphWidth; x++)
                            {
                                byte byteColor = bitmap[x + y * glyphWidth];
                                pixelBuffer[((int)currentCoords.X + x) + ((int)currentCoords.Y + y) * texDims] = (uint)(byteColor << 24 | 0x00ffffff);
                            }
                        }

                        currentCoords.X += glyphWidth + 2;
                    }
                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        textures[texIndex].SetData<uint>(pixelBuffer);
                    });
                }
            }
        }

        private void DynamicRenderAtlas(GraphicsDevice gd, uint character)
        {
            bool missingCharacterFound = false;
            using (new ReadLock(rwl))
            {
                missingCharacterFound = !texCoords.ContainsKey(character);
            }
            if (!missingCharacterFound) { return; }
            DynamicRenderAtlas(gd, character.ToEnumerable());
        }

        private void DynamicRenderAtlas(GraphicsDevice gd, string str)
        {
            bool missingCharacterFound = false;
            using (new ReadLock(rwl))
            {
                foreach (var character in str)
                {
                    if (texCoords.ContainsKey(character)) { continue; }

                    missingCharacterFound = true; 
                    break;
                }
            }
            if (!missingCharacterFound) { return; }
            DynamicRenderAtlas(gd, str.Select(c => (uint)c));
        }

        private void DynamicRenderAtlas(GraphicsDevice gd, IEnumerable<uint> characters)
        {
            if (System.Threading.Thread.CurrentThread != GameMain.MainThread)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    DynamicRenderAtlas(gd, characters);
                });                
                return;
            }

            byte[] bitmap;
            int glyphWidth; int glyphHeight;
            Fixed26Dot6 horizontalAdvance;
            Vector2 drawOffset;

            using (new WriteLock(rwl))
            {
                if (textures.Count == 0)
                {
                    face.SetPixelSizes(0, size);
                    face.LoadGlyph(face.GetCharIndex(baseChar), LoadFlags.Default, LoadTarget.Normal);
                    baseHeight = face.Glyph.Metrics.Height.ToInt32();
                    textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                }

                bool anyChanges = false;
                bool firstChar = true;
                foreach (var character in characters)
                {
                    if (texCoords.ContainsKey(character)) { continue; }

                    uint glyphIndex = face.GetCharIndex(character);
                    if (glyphIndex == 0)
                    {
                        texCoords.Add(character, new GlyphData(
                            Advance: 0,
                            TexIndex: -1));
                        continue;
                    }

                    face.SetPixelSizes(0, size);
                    face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                    if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                    {
                        //glyph is empty, but char might still apply advance
                        GlyphData blankData = new GlyphData(
                            Advance: Math.Max((float)face.Glyph.Metrics.HorizontalAdvance, 0f),
                            TexIndex: -1); //indicates no texture because the glyph is empty
                        texCoords.Add(character, blankData);
                        continue;
                    }

                    //stacktrace doesn't really work that well when RenderGlyph throws an exception
                    face.Glyph.RenderGlyph(RenderMode.Normal);
                    bitmap = (byte[])face.Glyph.Bitmap.BufferData.Clone();
                    glyphWidth = face.Glyph.Bitmap.Width;
                    glyphHeight = bitmap.Length / glyphWidth;
                    horizontalAdvance = face.Glyph.Metrics.HorizontalAdvance;
                    drawOffset = new Vector2(face.Glyph.BitmapLeft, baseHeight * 14 / 10 - face.Glyph.BitmapTop);

                    if (glyphWidth > texDims - 1 || glyphHeight > texDims - 1)
                    {
                        throw new Exception(filename + ", " + size.ToString() + ", " + (char)character + "; Glyph dimensions exceed texture atlas dimensions");
                    }

                    currentDynamicAtlasNextY = Math.Max(currentDynamicAtlasNextY, glyphHeight + 2);
                    if (currentDynamicAtlasCoords.X + glyphWidth + 2 > texDims - 1)
                    {
                        currentDynamicAtlasCoords.X = 0;
                        currentDynamicAtlasCoords.Y += currentDynamicAtlasNextY;
                        currentDynamicAtlasNextY = 0;
                    }
                    //no more room in current texture atlas, create a new one
                    if (currentDynamicAtlasCoords.Y + glyphHeight + 2 > texDims - 1)
                    {
                        if (!firstChar) { textures[^1].SetData<uint>(currentDynamicPixelBuffer); }
                        currentDynamicAtlasCoords.X = 0;
                        currentDynamicAtlasCoords.Y = 0;
                        currentDynamicAtlasNextY = 0;
                        textures.Add(new Texture2D(gd, texDims, texDims, false, SurfaceFormat.Color));
                        currentDynamicPixelBuffer = null;
                    }

                    GlyphData newData = new GlyphData(
                        Advance: (float)horizontalAdvance,
                        TexIndex: textures.Count - 1,
                        TexCoords: new Rectangle((int)currentDynamicAtlasCoords.X, (int)currentDynamicAtlasCoords.Y, glyphWidth, glyphHeight),
                        DrawOffset: drawOffset
                    );
                    texCoords.Add(character, newData);

                    if (currentDynamicPixelBuffer == null)
                    {
                        currentDynamicPixelBuffer = new uint[texDims * texDims];
                        textures[newData.TexIndex].GetData<uint>(currentDynamicPixelBuffer, 0, texDims * texDims);
                    }

                    for (int y = 0; y < glyphHeight; y++)
                    {
                        for (int x = 0; x < glyphWidth; x++)
                        {
                            byte byteColor = bitmap[x + y * glyphWidth];
                            currentDynamicPixelBuffer[((int)currentDynamicAtlasCoords.X + x) + ((int)currentDynamicAtlasCoords.Y + y) * texDims] = (uint)(byteColor << 24 | 0x00ffffff);
                        }
                    }

                    currentDynamicAtlasCoords.X += glyphWidth + 2;
                    firstChar = false;
                    anyChanges = true;
                }

                if (anyChanges) { textures[^1].SetData<uint>(currentDynamicPixelBuffer); }
            }
        }
        
        private void HandleNewLineAndAlignment(
            string text,
            in Vector2 advanceUnit,
            in Vector2 position,
            in Vector2 scale,
            Alignment alignment,
            int i,
            ref float lineWidth,
            ref Vector2 currentLineOffset,
            ref int lineNum,
            ref Vector2 currentPos,
            out uint charIndex,
            out bool isNewLine)
        {
            isNewLine = text[i] == '\n';
            if (lineWidth < 0.0f || isNewLine)
            {
                // Use bitwise operations instead of HasFlag or HasAnyFlag to avoid boxing, as this is performance-sensitive code.
                bool isHorizontallyCentered = (alignment & Alignment.CenterX) == Alignment.CenterX;
                bool isAlignedToRight = (alignment & Alignment.Right) == Alignment.Right;
                if (isHorizontallyCentered || isAlignedToRight)
                {
                    int startIndex = lineWidth < 0.0f ? i : (i + 1);
                    lineWidth = 0.0f;
                    for (int j = startIndex; j < text.Length; j++)
                    {
                        if (text[j] == '\n') { break; }
                        uint chrIndex = text[j];

                        if (TextManager.IconRange.Contains(chrIndex) && iconFont != null)
                        {
                            GlyphData gd = iconFont.GetGlyphData(chrIndex);
                            lineWidth += gd.Advance * (LineHeight / iconFont.LineHeight);
                        }
                        else
                        {
                            var gd2 = GetGlyphData(chrIndex);
                            lineWidth += gd2.Advance;
                        }
                    }
                    currentLineOffset = -lineWidth * advanceUnit * scale.X;
                    if (isHorizontallyCentered) { currentLineOffset *= 0.5f; }

                    currentLineOffset.X = MathF.Round(currentLineOffset.X);
                    currentLineOffset.Y = MathF.Round(currentLineOffset.Y);
                }
            }
            if (isNewLine)
            {
                lineNum++;
                currentPos = position;
                currentPos.X -= LineHeight * lineNum * advanceUnit.Y * scale.Y;
                // Smaller resolutions might cause a terrible subpixel position
                // causing the text to be blurry, round it up towards the nearest integer to
                // ensure a good position. I think this will be fine as UI elements use whole
                // pixels for positions anyway
                currentPos.Y += MathF.Ceiling(LineHeight * lineNum * advanceUnit.X * scale.Y);
                charIndex = 0;
                return;
            }

            charIndex = text[i];
        }

        private GlyphData GetGlyphData(uint charIndex)
        {
            const uint DEFAULT_INDEX = 0x25A1; //U+25A1 = white square
            
            if (texCoords.TryGetValue(charIndex, out GlyphData gd) ||
                texCoords.TryGetValue(DEFAULT_INDEX, out gd))
            {
                return gd;
            }

            return new GlyphData(TexIndex: -1);
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, Strikethrough? strikethrough = null)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            text = ApplyUpperCase(text, forceUpperCase);
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            float lineWidth = -1.0f;
            Vector2 currentLineOffset = Vector2.Zero;
            Vector2 strikeStart = Vector2.Zero;
            Vector2 strikeEnd = Vector2.Zero;

            int lineNum = 0;
            Vector2 currentPos = position;
            Vector2 advanceUnit = rotation == 0.0f ? Vector2.UnitX : new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
            for (int i = 0; i < text.Length; i++)
            {
                HandleNewLineAndAlignment(text, advanceUnit, position, scale, alignment, i,
                    ref lineWidth, ref currentLineOffset, ref lineNum, ref currentPos,
                    out uint charIndex, out bool isNewLine);
                if (strikethrough.HasValue && i == 0) { strikeStart = currentPos + currentLineOffset - origin * scale; }
                if (!isNewLine)
                {
                    GlyphData gd = GetGlyphData(charIndex);
                    float advance = gd.Advance;
                    if (gd.TexIndex >= 0)
                    {
                        if (gd.TexIndex >= textures.Count)
                        {
                            throw new ArgumentOutOfRangeException($"Error while rendering text. Texture index was out of range. Text: {text}, char: {charIndex} index: {gd.TexIndex}, texture count: {textures.Count}");
                        }
                        DrawGlyph(gd, textures[gd.TexIndex], glyphScale: 1f);
                    }
                    else if (TextManager.IconRange.Contains(charIndex) && iconFont != null)
                    {
                        var glyphAndTex = iconFont.GetGlyphDataAndTextureForChar((char)charIndex);
                        float glyphScale = LineHeight / iconFont.LineHeight;
                        DrawGlyph(glyphAndTex.GlyphData, glyphAndTex.Texture, glyphScale);
                        advance = glyphAndTex.GlyphData.Advance * glyphScale;
                    }
                    currentPos += advance * advanceUnit * scale.X;

                    void DrawGlyph(GlyphData glyphData, Texture2D tex, float glyphScale)
                    {
                        if (tex is null) { return; }
                        Vector2 drawOffset;
                        drawOffset.X = glyphData.DrawOffset.X * advanceUnit.X * (scale.X * glyphScale) - glyphData.DrawOffset.Y * advanceUnit.Y * (scale.Y * glyphScale);
                        drawOffset.Y = glyphData.DrawOffset.X * advanceUnit.Y * (scale.Y * glyphScale) + glyphData.DrawOffset.Y * advanceUnit.X * (scale.X * glyphScale);

                        sb.Draw(tex, currentPos + currentLineOffset + drawOffset, glyphData.TexCoords, color, rotation, origin / glyphScale, scale * glyphScale, se, layerDepth);
                    }
                }
                if (!strikethrough.HasValue) { continue; }
                if (!isNewLine) { strikeEnd = currentPos + currentLineOffset - origin * scale; }
                if (isNewLine || i == text.Length - 1) { strikethrough.Value.Draw(sb, LineHeight * scale.Y, color, advanceUnit, strikeStart, strikeEnd); }
                if (isNewLine) { strikeStart = strikeEnd = currentPos + currentLineOffset - origin * scale; }
            }
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, Strikethrough? strikethrough = null)
            => DrawString(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth, alignment, forceUpperCase, strikethrough);

        private string ApplyUpperCase(string text, ForceUpperCase forceUpperCase)
            => forceUpperCase switch
            {
                Barotrauma.ForceUpperCase.Inherit => ForceUpperCase ? text.ToUpperInvariant() : text,
                Barotrauma.ForceUpperCase.Yes => text.ToUpperInvariant(),
                Barotrauma.ForceUpperCase.No => text
            };
        
        private readonly static VertexPositionColorTexture[] quadVertices = new VertexPositionColorTexture[4];
        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, bool italics = false, Strikethrough? strikethrough = null)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            text = ApplyUpperCase(text, forceUpperCase);
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            quadVertices[0].Color = color;
            quadVertices[1].Color = color;
            quadVertices[2].Color = color;
            quadVertices[3].Color = color;

            Vector2 strikeStart = position;
            Vector2 strikeEnd = position;

            Vector2 currentPos = position;
            for (int i = 0; i < text.Length; i++)
            {
                bool isNewLine = text[i] == '\n';
                if (isNewLine)
                {
                    currentPos.X = position.X;
                    currentPos.Y += LineHeight;
                }
                else
                {
                    uint charIndex = text[i];

                    GlyphData gd = GetGlyphData(charIndex);
                    float advance = gd.Advance;
                    if (gd.TexIndex >= 0)
                    {
                        DrawChar(sb, italics, gd, currentPos);
                    }
                    else if (TextManager.IconRange.Contains(charIndex) && iconFont != null)
                    {
                        GlyphData iconGlyph = iconFont.GetGlyphData(charIndex);
                        float scale =  LineHeight / iconFont.LineHeight;
                        iconFont.DrawChar(sb, italics, iconGlyph, currentPos, scale);
                        advance = iconGlyph.Advance * scale;
                    }

                    currentPos.X += advance;
                }
                if (!strikethrough.HasValue) { continue; }
                if (!isNewLine) { strikeEnd = currentPos; }
                if (isNewLine || i == text.Length - 1) { strikethrough.Value.Draw(sb, LineHeight, color, strikeStart, strikeEnd); }
                if (isNewLine) { strikeStart = strikeEnd = currentPos; }
            }
        }

        public void DrawChar(SpriteBatch sb, bool italics, GlyphData gd, Vector2 currentPos, float scale = 1f)
        {
            if (gd.TexIndex < 0) { return; }

            float halfCharHeight = gd.TexCoords.Height * 0.5f;
            const float slantStrength = 0.35f;
            float topItalicOffset = 0.0f;
            float bottomItalicOffset = 0.0f;
            if (italics)
            {
                topItalicOffset = ((halfCharHeight - gd.DrawOffset.Y) * slantStrength) + baseHeight * 0.18f;
                bottomItalicOffset = ((-halfCharHeight - gd.DrawOffset.Y) * slantStrength) + baseHeight * 0.18f;
            }

            Texture2D tex = textures[gd.TexIndex];

            float left = (float)gd.TexCoords.Left / tex.Width;
            float bottom = (float)gd.TexCoords.Bottom / tex.Height;
            float top = (float)gd.TexCoords.Top / tex.Height;
            float right = (float)gd.TexCoords.Right / tex.Width;

            quadVertices[0].Position = new Vector3(currentPos + gd.DrawOffset * scale + (bottomItalicOffset, gd.TexCoords.Height * scale ), 0.0f);
            quadVertices[0].TextureCoordinate = new Vector2(left, bottom);

            quadVertices[1].Position = new Vector3(currentPos + gd.DrawOffset * scale + (topItalicOffset, 0.0f), 0.0f);
            quadVertices[1].TextureCoordinate = new Vector2(left, top);

            quadVertices[2].Position = new Vector3(currentPos + gd.DrawOffset * scale + ((gd.TexCoords.Width + bottomItalicOffset) * scale, gd.TexCoords.Height * scale), 0.0f);
            quadVertices[2].TextureCoordinate = new Vector2(right, bottom);

            quadVertices[3].Position = new Vector3(currentPos + gd.DrawOffset * scale + ((gd.TexCoords.Width + topItalicOffset) * scale, 0.0f), 0.0f);
            quadVertices[3].TextureCoordinate = new Vector2(right, top);

            sb.Draw(tex, quadVertices, 0.0f);
        }

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth, in ImmutableArray<RichTextData>? richTextData, int rtdOffset = 0, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, Strikethrough? strikethrough = null)
            => DrawStringWithColors(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth, richTextData, rtdOffset, alignment, forceUpperCase, strikethrough);

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth, in ImmutableArray<RichTextData>? richTextData, int rtdOffset = 0, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, Strikethrough? strikethrough = null)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            if (!richTextData.HasValue || richTextData.Value.Length <= 0) { DrawString(sb, text, position, color, rotation, origin, scale, se, layerDepth, forceUpperCase: forceUpperCase, strikethrough: strikethrough, alignment: alignment); return; }

            text = ApplyUpperCase(text, forceUpperCase);
            
            float lineWidth = -1.0f;
            Vector2 currentLineOffset = Vector2.Zero;
            Vector2 strikeStart = Vector2.Zero;
            Vector2 strikeEnd = Vector2.Zero;
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            int lineNum = 0;
            Vector2 currentPos = position;
            Vector2 advanceUnit = rotation == 0.0f ? Vector2.UnitX : new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

            int richTextDataIndex = 0;
            RichTextData currentRichTextData = richTextData.Value[richTextDataIndex];

            for (int i = 0; i < text.Length; i++)
            {
                HandleNewLineAndAlignment(text, advanceUnit, position, scale, alignment, i,
                    ref lineWidth, ref currentLineOffset, ref lineNum, ref currentPos,
                    out uint charIndex, out bool isNewLine);
                if (strikethrough.HasValue && i == 0) { strikeStart = currentPos + currentLineOffset - origin * scale; }
                if (!isNewLine)
                {
                    Color currentTextColor;

                    while (currentRichTextData != null && i + rtdOffset > currentRichTextData.EndIndex + lineNum)
                    {
                        richTextDataIndex++;
                        currentRichTextData = richTextDataIndex < richTextData.Value.Length ? richTextData.Value[richTextDataIndex] : null;
                    }

                    if (currentRichTextData != null && currentRichTextData.StartIndex + lineNum <= i + rtdOffset && i + rtdOffset <= currentRichTextData.EndIndex + lineNum)
                    {
                        currentTextColor = currentRichTextData.Color * currentRichTextData.Alpha ?? color;
                        if (!string.IsNullOrEmpty(currentRichTextData.Metadata))
                        {
                            currentTextColor = Color.Lerp(currentTextColor, Color.White, 0.5f);
                        }
                    }
                    else
                    {
                        currentTextColor = color;
                    }

                    GlyphData gd = GetGlyphData(charIndex);
                    float advance = gd.Advance;
                    if (gd.TexIndex >= 0)
                    {
                        DrawGlyph(gd, textures[gd.TexIndex], glyphScale: 1f);
                    }
                    else if (TextManager.IconRange.Contains(charIndex) && iconFont != null)
                    {
                        var glyphAndTex = iconFont.GetGlyphDataAndTextureForChar((char)charIndex);
                        float glyphScale = LineHeight / iconFont.LineHeight;
                        DrawGlyph(glyphAndTex.GlyphData, glyphAndTex.Texture, glyphScale);
                        advance = glyphAndTex.GlyphData.Advance * glyphScale;
                    }
                    currentPos += advance * advanceUnit * scale.X;

                    void DrawGlyph(GlyphData glyphData, Texture2D tex, float glyphScale)
                    {
                        if (tex is null) { return; }
                        Vector2 drawOffset;
                        drawOffset.X = glyphData.DrawOffset.X * advanceUnit.X * (scale.X * glyphScale) - glyphData.DrawOffset.Y * advanceUnit.Y * (scale.Y * glyphScale);
                        drawOffset.Y = glyphData.DrawOffset.X * advanceUnit.Y * (scale.Y * glyphScale) + glyphData.DrawOffset.Y * advanceUnit.X * (scale.X * glyphScale);

                        sb.Draw(tex, currentPos + currentLineOffset + drawOffset, glyphData.TexCoords, currentTextColor, rotation, origin / glyphScale, scale * glyphScale, se, layerDepth);
                    }
                }
                if (!strikethrough.HasValue) { continue; }
                if (!isNewLine) { strikeEnd = currentPos + currentLineOffset - origin * scale; }
                if (isNewLine || i == text.Length - 1) { strikethrough.Value.Draw(sb, LineHeight * scale.Y, color, advanceUnit, strikeStart, strikeEnd); }
                if (isNewLine) { strikeStart = strikeEnd = currentPos + currentLineOffset - origin * scale; }
            }
        }

        public string WrapText(string text, float width)
            => WrapText(text, width, requestCharPos: 0, out _, returnAllCharPositions: false, out _);

        public string WrapText(string text, float width, int requestCharPos, out Vector2 requestedCharPos)
            => WrapText(text, width, requestCharPos, out requestedCharPos, returnAllCharPositions: false, out _);
        
        public string WrapText(string text, float width, out Vector2[] allCharPositions)
            => WrapText(text, width, requestCharPos: 0, out _, returnAllCharPositions: true, out allCharPositions);
        
        /// <summary>
        /// Wraps a string of text to fit within a given width.
        /// Optionally returns the caret position of a certain character,
        /// or all of them.
        /// </summary>
        private string WrapText(string text,
            float width,
            int requestCharPos,
            out Vector2 requestedCharPos,
            bool returnAllCharPositions,
            out Vector2[] allCharPositions)
        {
            int currLineStart = 0;
            Vector2 currentPos = Vector2.Zero;
            Vector2 foundCharPos = Vector2.Zero;
            int? lastBreakerIndex = null;
            string result = "";
            var allCharPos = returnAllCharPositions ? new Vector2[text.Length+1] : null;
            for (int i = 0; i < text.Length; i++)
            {
                //Records the caret position of the current character
                void recordCurrentPos()
                {
                    if (i == requestCharPos) { foundCharPos = currentPos; }

                    if (allCharPos != null) { allCharPos[i] = currentPos; }
                }
                recordCurrentPos();
    
                //Appends a newline to the result and resets the caret position's X value
                void nextLine()
                {
                    result += text[currLineStart..i].Remove("\n") + "\n";
                    lastBreakerIndex = null;
                    currentPos.X = 0.0f;
                    currentPos.Y += LineHeight;
                    currLineStart = i;
                }

                //If a newline is found in the source, split immediately
                if (text[i] == '\n')
                {
                    nextLine();
                    continue;
                }

                //Otherwise, advance based on the width of the current character
                float gdAdvance = GetGlyphAdvance(text[i]);
                float advance = gdAdvance;
                if (currentPos.X + advance >= width)
                {
                    //Advancing based on the last character
                    //would put us past the max width!
                    if (i > 0 && char.IsWhiteSpace(text[i]) && !char.IsWhiteSpace(text[i - 1]))
                    {
                        //Whitespace immediately after a visible
                        //character can be shrunk down to fit
                        advance = width - currentPos.X;
                    }
                    else
                    {
                        if (lastBreakerIndex.HasValue)
                        {
                            //A breaker (whitespace or CJK) was found earlier
                            //in this line, so let's break the line there
                            i = lastBreakerIndex.Value + 1;
                            advance = GetGlyphAdvance(text[i]);
                        }

                        nextLine();
                        recordCurrentPos(); //must re-record current caret position since we are on a new line now
                    }
                }
                currentPos.X += advance;

                if (char.IsWhiteSpace(text[i]) || TextManager.IsCJK($"{text[i]}"))
                {
                    lastBreakerIndex = i;
                }
            }
            if (requestCharPos >= text.Length) { foundCharPos = currentPos; }
            if (allCharPos != null) { allCharPos[text.Length] = currentPos; }
            allCharPositions = allCharPos;
            result += text[currLineStart..].Remove("\n");
            requestedCharPos = foundCharPos;
            return result;

            float GetGlyphAdvance(char c)
            {
                if (TextManager.IconRange.Contains(c) && iconFont != null)
                {
                    float scale = LineHeight / iconFont.LineHeight;
                    return iconFont.GetGlyphData(c).Advance * scale;
                }

                return GetGlyphData(c).Advance;
            }
        }

        public Vector2 MeasureString(LocalizedString str, bool removeExtraSpacing = false)
        {
            return MeasureString(str.Value, removeExtraSpacing);
        }

        public Vector2 MeasureString(string text, bool removeExtraSpacing = false)
        {
            if (text == null)
            {
                return Vector2.Zero;
            }

            float currentLineX = 0.0f;
            Vector2 retVal = Vector2.Zero;

            if (!removeExtraSpacing)
            {
                retVal.Y = LineHeight;
            }
            else
            {
                retVal.Y = baseHeight;
            }
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    currentLineX = 0.0f;
                    retVal.Y += LineHeight;
                    continue;
                }
                uint charIndex = text[i];

                if (TextManager.IconRange.Contains(charIndex) && iconFont != null)
                {
                    GlyphData gd = iconFont.GetGlyphData(charIndex);
                    float scale = LineHeight / iconFont.LineHeight;
                    currentLineX += gd.Advance * scale;
                }
                else
                {
                    currentLineX += GetGlyphData(charIndex).Advance;
                }

                retVal.X = Math.Max(retVal.X, currentLineX);
            }
            return retVal;
        }

        public Vector2 MeasureChar(char c)
        {
            Vector2 retVal = Vector2.Zero;
            retVal.Y = LineHeight;

            var (gd, _) = GetGlyphDataAndTextureForChar(c);
            retVal.X = gd.Advance;
            return retVal;
        }

        public (GlyphData GlyphData, Texture2D Texture) GetGlyphDataAndTextureForChar(char c)
        {
            if (DynamicLoading && !texCoords.ContainsKey(c))
            {
                DynamicRenderAtlas(graphicsDevice, c);
            }

            GlyphData gd = GetGlyphData(c);
            var tex = gd.TexIndex >= 0 ? textures[gd.TexIndex] : null;
            return (gd, tex);
        }

        public void Dispose()
        {
            FontList.Remove(this);
            foreach (Texture2D texture in textures)
            {
                texture.Dispose();
            }
            textures.Clear();
        }
    }
}

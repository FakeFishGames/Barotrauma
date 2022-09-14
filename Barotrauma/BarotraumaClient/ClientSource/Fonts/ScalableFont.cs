using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Barotrauma.Threading;

namespace Barotrauma
{
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

        private Vector2 currentDynamicAtlasCoords;
        private int currentDynamicAtlasNextY;
        uint[] currentDynamicPixelBuffer;

        public bool DynamicLoading
        {
            get;
            private set;
        }

        public bool IsCJK
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
                if (graphicsDevice != null) { RenderAtlas(graphicsDevice, charRanges, texDims, baseChar); }
            }
        }

        public bool ForceUpperCase = false;

        public float LineHeight => baseHeight * 1.8f;

        private uint[] charRanges;
        private int texDims;
        private uint baseChar;

        private readonly struct GlyphData
        {
            public readonly int TexIndex;
            public readonly Vector2 DrawOffset;
            public readonly float Advance;
            public readonly Rectangle TexCoords;

            public GlyphData(
                int texIndex = default,
                Vector2 drawOffset = default,
                float advance = default,
                Rectangle texCoords = default)
            {
                TexIndex = texIndex;
                DrawOffset = drawOffset;
                Advance = advance;
                TexCoords = texCoords;
            }
        }

        public ScalableFont(ContentXElement element, GraphicsDevice gd = null)
            : this(
                element.GetAttributeContentPath("file")?.Value,
                (uint)element.GetAttributeInt("size", 14),
                gd,
                element.GetAttributeBool("dynamicloading", false),
                element.GetAttributeBool("iscjk", false))
        {
        }

        public ScalableFont(string filename, uint size, GraphicsDevice gd = null, bool dynamicLoading = false, bool isCJK = false)
        {
            lock (globalMutex)
            {
                Lib ??= new Library();
            }

            this.filename = filename;
            this.face = null;
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
            this.IsCJK = isCJK;
            this.graphicsDevice = gd;

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
        /// <param name="charRanges">Character ranges between each even element with their corresponding odd element. Default is 0x20 to 0xFFFF.</param>
        /// <param name="texDims">Texture dimensions. Default is 512x512.</param>
        /// <param name="baseChar">Base character used to shift all other characters downwards when rendering. Defaults to T.</param>
        private void RenderAtlas(GraphicsDevice gd, uint[] charRanges = null, int texDims = 1024, uint baseChar = 0x54)
        {
            if (DynamicLoading) { return; }

            if (charRanges == null)
            {
                charRanges = new uint[] { 0x20, 0xFFFF };
            }
            this.charRanges = charRanges;
            this.texDims = texDims;
            this.baseChar = baseChar;

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
                                advance: 0,
                                texIndex: -1));
                            continue;
                        }
                        face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                        if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                        {
                            //glyph is empty, but char might still apply advance
                            GlyphData blankData = new GlyphData(
                                advance: Math.Max((float)face.Glyph.Metrics.HorizontalAdvance, 0f),
                                texIndex: -1); //indicates no texture because the glyph is empty

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
                            advance: (float)face.Glyph.Metrics.HorizontalAdvance,
                            texIndex: texIndex,
                            texCoords: new Rectangle((int)currentCoords.X, (int)currentCoords.Y, glyphWidth, glyphHeight),
                            drawOffset: new Vector2(face.Glyph.BitmapLeft, baseHeight * 14 / 10 - face.Glyph.BitmapTop)
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

        private void DynamicRenderAtlas(GraphicsDevice gd, uint character, int texDims = 1024, uint baseChar = 0x54)
        {
            bool missingCharacterFound = false;
            using (new ReadLock(rwl))
            {
                missingCharacterFound = !texCoords.ContainsKey(character);
            }
            if (!missingCharacterFound) { return; }
            DynamicRenderAtlas(gd, character.ToEnumerable(), texDims, baseChar);
        }

        private void DynamicRenderAtlas(GraphicsDevice gd, string str, int texDims = 1024, uint baseChar = 0x54)
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
            DynamicRenderAtlas(gd, str.Select(c => (uint)c), texDims, baseChar);
        }

        private void DynamicRenderAtlas(GraphicsDevice gd, IEnumerable<uint> characters, int texDims = 1024, uint baseChar = 0x54)
        {
            if (System.Threading.Thread.CurrentThread != GameMain.MainThread)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    DynamicRenderAtlas(gd, characters, texDims, baseChar);
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
                    this.texDims = texDims;
                    this.baseChar = baseChar;
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
                            advance: 0,
                            texIndex: -1));
                        continue;
                    }

                    face.SetPixelSizes(0, size);
                    face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);
                    if (face.Glyph.Metrics.Width == 0 || face.Glyph.Metrics.Height == 0)
                    {
                        //glyph is empty, but char might still apply advance
                        GlyphData blankData = new GlyphData(
                            advance: Math.Max((float)face.Glyph.Metrics.HorizontalAdvance, 0f),
                            texIndex: -1); //indicates no texture because the glyph is empty
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
                        advance: (float)horizontalAdvance,
                        texIndex: textures.Count - 1,
                        texCoords: new Rectangle((int)currentDynamicAtlasCoords.X, (int)currentDynamicAtlasCoords.Y, glyphWidth, glyphHeight),
                        drawOffset: drawOffset
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

        // TODO: refactor this further
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
            out bool shouldContinue)
        {
            if ((alignment.HasFlag(Alignment.CenterX) || alignment.HasFlag(Alignment.Right)) && (lineWidth < 0.0f || text[i] == '\n'))
            {
                int startIndex = lineWidth < 0.0f ? i : (i + 1);
                lineWidth = 0.0f;
                for (int j = startIndex; j < text.Length; j++)
                {
                    if (text[j] == '\n') { break; }
                    uint chrIndex = text[j];

                    var gd2 = GetGlyphData(chrIndex);
                    lineWidth += gd2.Advance;
                }
                currentLineOffset = -lineWidth * advanceUnit * scale.X;
                if (alignment.HasFlag(Alignment.CenterX)) { currentLineOffset *= 0.5f; }

                currentLineOffset.X = MathF.Round(currentLineOffset.X);
                currentLineOffset.Y = MathF.Round(currentLineOffset.Y);
            }
            if (text[i] == '\n')
            {
                lineNum++;
                currentPos = position;
                currentPos.X -= LineHeight * lineNum * advanceUnit.Y * scale.Y;
                currentPos.Y += LineHeight * lineNum * advanceUnit.X * scale.Y;
                shouldContinue = true; charIndex = 0; return;
            }

            shouldContinue = false;
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

            return new GlyphData(texIndex: -1);
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            text = ApplyUpperCase(text, forceUpperCase);
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            float lineWidth = -1.0f;
            Vector2 currentLineOffset = Vector2.Zero;
            
            int lineNum = 0;
            Vector2 currentPos = position;
            Vector2 advanceUnit = rotation == 0.0f ? Vector2.UnitX : new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));
            for (int i = 0; i < text.Length; i++)
            {
                HandleNewLineAndAlignment(text, advanceUnit, position, scale, alignment, i,
                    ref lineWidth, ref currentLineOffset, ref lineNum, ref currentPos,
                    out uint charIndex, out bool shouldContinue);
                if (shouldContinue) { continue; }

                GlyphData gd = GetGlyphData(charIndex);
                if (gd.TexIndex >= 0)
                {
                    Texture2D tex = textures[gd.TexIndex];
                    Vector2 drawOffset;
                    drawOffset.X = gd.DrawOffset.X * advanceUnit.X * scale.X - gd.DrawOffset.Y * advanceUnit.Y * scale.Y;
                    drawOffset.Y = gd.DrawOffset.X * advanceUnit.Y * scale.Y + gd.DrawOffset.Y * advanceUnit.X * scale.X;

                    sb.Draw(tex, currentPos + currentLineOffset + drawOffset, gd.TexCoords, color, rotation, origin, scale, se, layerDepth);
                }
                currentPos += gd.Advance * advanceUnit * scale.X;
            }
        }

        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit)
        {
            DrawString(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth, alignment, forceUpperCase);
        }

        private string ApplyUpperCase(string text, ForceUpperCase forceUpperCase)
            => forceUpperCase switch
            {
                Barotrauma.ForceUpperCase.Inherit => ForceUpperCase ? text.ToUpperInvariant() : text,
                Barotrauma.ForceUpperCase.Yes => text.ToUpperInvariant(),
                Barotrauma.ForceUpperCase.No => text
            };
        
        private readonly static VertexPositionColorTexture[] quadVertices = new VertexPositionColorTexture[4];
        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit, bool italics = false)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            text = ApplyUpperCase(text, forceUpperCase);
            if (DynamicLoading)
            {
                DynamicRenderAtlas(graphicsDevice, text);
            }

            Vector2 currentPos = position;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    currentPos.X = position.X;
                    currentPos.Y += LineHeight;
                    continue;
                }

                uint charIndex = text[i];

                GlyphData gd = GetGlyphData(charIndex);
                if (gd.TexIndex >= 0)
                {
                    float halfCharHeight = gd.TexCoords.Height * 0.5f;
                    float slantStrength = 0.35f;
                    float topItalicOffset = italics ? ((halfCharHeight - gd.DrawOffset.Y) * slantStrength) + baseHeight * 0.18f : 0.0f;
                    float bottomItalicOffset = italics ? ((-halfCharHeight - gd.DrawOffset.Y) * slantStrength) + baseHeight * 0.18f : 0.0f;
                    
                    Texture2D tex = textures[gd.TexIndex];
                    quadVertices[0].Position = new Vector3(currentPos + gd.DrawOffset + (bottomItalicOffset, gd.TexCoords.Height), 0.0f);
                    quadVertices[0].TextureCoordinate = ((float)gd.TexCoords.Left / tex.Width, (float)gd.TexCoords.Bottom / tex.Height);
                    quadVertices[0].Color = color;

                    quadVertices[1].Position = new Vector3(currentPos + gd.DrawOffset + (topItalicOffset, 0.0f), 0.0f);
                    quadVertices[1].TextureCoordinate = ((float)gd.TexCoords.Left / tex.Width, (float)gd.TexCoords.Top / tex.Height);
                    quadVertices[1].Color = color;

                    quadVertices[2].Position = new Vector3(currentPos + gd.DrawOffset + (gd.TexCoords.Width + bottomItalicOffset, gd.TexCoords.Height), 0.0f);
                    quadVertices[2].TextureCoordinate = ((float)gd.TexCoords.Right / tex.Width, (float)gd.TexCoords.Bottom / tex.Height);
                    quadVertices[2].Color = color;

                    quadVertices[3].Position = new Vector3(currentPos + gd.DrawOffset + (gd.TexCoords.Width + topItalicOffset, 0.0f), 0.0f);
                    quadVertices[3].TextureCoordinate = ((float)gd.TexCoords.Right / tex.Width, (float)gd.TexCoords.Top / tex.Height);
                    quadVertices[3].Color = color;

                    sb.Draw(tex, quadVertices, 0.0f);
                }
                currentPos.X += gd.Advance;
            }
        }

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects se, float layerDepth, in ImmutableArray<RichTextData>? richTextData, int rtdOffset = 0, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit)
        {
            DrawStringWithColors(sb, text, position, color, rotation, origin, new Vector2(scale), se, layerDepth, richTextData, rtdOffset, alignment, forceUpperCase);
        }

        public void DrawStringWithColors(SpriteBatch sb, string text, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects se, float layerDepth, in ImmutableArray<RichTextData>? richTextData, int rtdOffset = 0, Alignment alignment = Alignment.TopLeft, ForceUpperCase forceUpperCase = Barotrauma.ForceUpperCase.Inherit)
        {
            if (textures.Count == 0 && !DynamicLoading) { return; }
            if (!richTextData.HasValue || richTextData.Value.Length <= 0) { DrawString(sb, text, position, color, rotation, origin, scale, se, layerDepth, forceUpperCase: forceUpperCase); return; }

            text = ApplyUpperCase(text, forceUpperCase);
            
            float lineWidth = -1.0f;
            Vector2 currentLineOffset = Vector2.Zero;
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
                    out uint charIndex, out bool shouldContinue);
                if (shouldContinue) { continue; }

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
                if (gd.TexIndex >= 0)
                {
                    Texture2D tex = textures[gd.TexIndex];
                    Vector2 drawOffset;
                    drawOffset.X = gd.DrawOffset.X * advanceUnit.X * scale.X - gd.DrawOffset.Y * advanceUnit.Y * scale.Y;
                    drawOffset.Y = gd.DrawOffset.X * advanceUnit.Y * scale.Y + gd.DrawOffset.Y * advanceUnit.X * scale.X;

                    sb.Draw(tex, currentPos + currentLineOffset + drawOffset, gd.TexCoords, currentTextColor, rotation, origin, scale, se, layerDepth);
                }
                currentPos += gd.Advance * advanceUnit * scale.X;
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
                GlyphData gd = GetGlyphData(text[i]);
                float advance = gd.Advance;
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
                            gd = GetGlyphData(text[i]);
                            advance = gd.Advance;
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

                GlyphData gd = GetGlyphData(charIndex);
                currentLineX += gd.Advance;
                retVal.X = Math.Max(retVal.X, currentLineX);
            }
            return retVal;
        }

        public Vector2 MeasureChar(char c)
        {
            Vector2 retVal = Vector2.Zero;
            retVal.Y = LineHeight;
            if (DynamicLoading && !texCoords.ContainsKey(c))
            {
                DynamicRenderAtlas(graphicsDevice, c);
            }

            GlyphData gd = GetGlyphData(c);
            retVal.X = gd.Advance;
            return retVal;
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

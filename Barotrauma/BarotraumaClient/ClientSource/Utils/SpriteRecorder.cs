using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    sealed class SpriteRecorder : ISpriteBatch, IDisposable
    {
        public readonly record struct Command(
            Texture2D Texture,
            VertexPositionColorTexture VertexBL,
            VertexPositionColorTexture VertexBR,
            VertexPositionColorTexture VertexTL,
            VertexPositionColorTexture VertexTR,
            float Depth,
            Vector2 Min,
            Vector2 Max,
            int Index)
        {
            public static Vector2 GetMinPosition(params VertexPositionColorTexture[] vertices)
                => new Vector2(
                    MathUtils.Min(vertices.Select(v => v.Position.X).ToArray()),
                    MathUtils.Min(vertices.Select(v => v.Position.Y).ToArray()));
            
            public static Vector2 GetMaxPosition(params VertexPositionColorTexture[] vertices)
                => new Vector2(
                    MathUtils.Max(vertices.Select(v => v.Position.X).ToArray()),
                    MathUtils.Max(vertices.Select(v => v.Position.Y).ToArray()));
            
            public static Command FromTransform(
                Texture2D texture,
                Vector2 pos,
                Rectangle srcRect,
                Color color,
                float rotation,
                Vector2 origin,
                Vector2 scale,
                SpriteEffects effects,
                float depth,
                int index)
            {
                int srcRectLeft = srcRect.Left;
                int srcRectRight = srcRect.Right;
                int srcRectTop = srcRect.Top;
                int srcRectBottom = srcRect.Bottom;
                if (effects.HasFlag(SpriteEffects.FlipHorizontally))
                {
                    (srcRectRight, srcRectLeft) = (srcRectLeft, srcRectRight);
                }
                if (effects.HasFlag(SpriteEffects.FlipVertically))
                {
                    (srcRectBottom, srcRectTop) = (srcRectTop, srcRectBottom);
                }

                rotation = MathHelper.ToRadians(rotation);
                float sin = (float)Math.Sin(rotation);
                float cos = (float)Math.Cos(rotation);

                var size = srcRect.Size.ToVector2() * scale;

                Vector2 wAdd = new Vector2(size.X * cos, size.X * sin);
                Vector2 hAdd = new Vector2(-size.Y * sin, size.Y * cos);
                pos.X -= origin.X * scale.X * cos - origin.Y * scale.Y * sin;
                pos.Y -= origin.Y * scale.Y * cos + origin.X * scale.X * sin;

                var vertexTl = new VertexPositionColorTexture
                {
                    Color = color,
                    Position = new Vector3(pos.X, pos.Y, 0f),
                    TextureCoordinate = new Vector2((float)srcRectLeft / (float)texture.Width, (float)srcRectTop / (float)texture.Height)
                };

                var vertexTr = new VertexPositionColorTexture
                {
                    Color = color,
                    Position = new Vector3(pos.X + wAdd.X, pos.Y + wAdd.Y, 0f),
                    TextureCoordinate = new Vector2((float)srcRectRight / (float)texture.Width, (float)srcRectTop / (float)texture.Height)
                };

                var vertexBl = new VertexPositionColorTexture
                {
                    Color = color,
                    Position = new Vector3(pos.X + hAdd.X, pos.Y + hAdd.Y, 0f),
                    TextureCoordinate = new Vector2((float)srcRectLeft / (float)texture.Width, (float)srcRectBottom / (float)texture.Height)
                };

                var vertexBr = new VertexPositionColorTexture
                {
                    Color = color,
                    Position = new Vector3(pos.X + wAdd.X + hAdd.X, pos.Y + wAdd.Y + hAdd.Y, 0f),
                    TextureCoordinate = new Vector2((float)srcRectRight / (float)texture.Width, (float)srcRectBottom / (float)texture.Height)
                };

                var min = GetMinPosition(
                    vertexTl,
                    vertexTr,
                    vertexBl,
                    vertexBr);

                var max = GetMaxPosition(
                    vertexTl,
                    vertexTr,
                    vertexBl,
                    vertexBr);

                return new Command(
                    texture,
                    vertexBl,
                    vertexBr,
                    vertexTl,
                    vertexTr,
                    depth,
                    min,
                    max,
                    index);
            }

            public bool Overlaps(Command other)
            {
                return
                    Min.X <= other.Max.X && Max.X >= other.Min.X &&
                    Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;
            }
        }

        private struct RecordedBuffer
        {
            public readonly Texture2D Texture;
            public readonly VertexBuffer VertexBuffer;
            public readonly int PolyCount;

            public RecordedBuffer(List<Command> commandList, int startIndex, int count)
            {
                Texture = commandList[startIndex].Texture;

                VertexBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, count * 4, BufferUsage.WriteOnly);
                VertexPositionColorTexture[] vertices = new VertexPositionColorTexture[count * 4];
                for (int i = 0; i < count; i++)
                {
                    vertices[(i * 4) + 0] = commandList[startIndex + i].VertexBL;
                    vertices[(i * 4) + 1] = commandList[startIndex + i].VertexBR;
                    vertices[(i * 4) + 2] = commandList[startIndex + i].VertexTL;
                    vertices[(i * 4) + 3] = commandList[startIndex + i].VertexTR;
                }
                VertexBuffer.SetData(vertices);

                PolyCount = count * 2;
            }
        }

        public static BasicEffect BasicEffect = null;

        private readonly List<RecordedBuffer> recordedBuffers = new List<RecordedBuffer>();
        private readonly List<Command> commandList = new List<Command>();
        private SpriteSortMode currentSortMode;

        private IndexBuffer indexBuffer = null;
        private int maxSpriteCount = 0;

        public volatile bool ReadyToRender = false;
        private volatile bool isDisposed = false;

        public Vector2 Min { get; private set; }
        public Vector2 Max { get; private set; }

        public void Begin(SpriteSortMode sortMode)
        {
            ReadyToRender = false;
            currentSortMode = sortMode;
        }

        private void AppendCommand(Command command)
        {
            if (isDisposed) { return; }
            
            if (commandList.Count == 0) { Min = command.Min; Max = command.Max; }
            Min = new Vector2(Math.Min(command.Min.X, Min.X), Math.Min(command.Min.Y, Min.Y));
            Max = new Vector2(Math.Max(command.Max.X, Max.X), Math.Max(command.Max.Y, Max.Y));

            commandList.Add(command);
        }
        
        public void Draw(Texture2D texture, Vector2 pos, Rectangle? srcRect, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
        {
            if (isDisposed) { return; }

            var command = Command.FromTransform(texture, pos, srcRect ?? texture.Bounds, color, rotation, origin, scale, effects, depth, commandList.Count);
            AppendCommand(command);
        }

        public void Draw(Texture2D texture, VertexPositionColorTexture[] vertices, float layerDepth, int? count = null)
        {
            if (isDisposed) { return; }
            
            int iters = count ?? (vertices.Length / 4);
            for (int i=0;i<iters;i++)
            {
                var subset = vertices[((i * 4) + 0)..((i * 4) + 4)];
                var command = new Command(
                    texture,
                    subset[2],
                    subset[3],
                    subset[0],
                    subset[1],
                    layerDepth,
                    Command.GetMinPosition(subset),
                    Command.GetMaxPosition(subset),
                    commandList.Count);
                AppendCommand(command);
            }
        }

        public void End()
        {
            if (isDisposed) { return; }
            //sort commands according to the sorting
            //mode given in the last Begin call
            switch (currentSortMode)
            {
                case SpriteSortMode.FrontToBack:
                    commandList.Sort((c1, c2) =>
                    {
                        return c1.Depth < c2.Depth ? -1
                             : c1.Depth > c2.Depth ? 1
                             : c1.Index < c2.Index ? 1
                             : c1.Index > c2.Index ? -1
                             : 0;
                    });
                    break;
                case SpriteSortMode.BackToFront:
                    commandList.Sort((c1, c2) =>
                    {
                        return c1.Depth < c2.Depth ? 1
                             : c1.Depth > c2.Depth ? -1
                             : c1.Index < c2.Index ? 1
                             : c1.Index > c2.Index ? -1
                             : 0;
                    });
                    break;
            }

            //try to place commands of the same texture
            //contiguously for optimal buffer generation
            //while maintaining the same visual result
            for (int i = 1; i < commandList.Count; i++)
            {
                if (commandList[i].Texture != commandList[i - 1].Texture)
                {
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (commandList[j].Texture == commandList[i].Texture)
                        {
                            //no commands between i and j overlap with
                            //i, therefore we can safely sift i down to
                            //make a contiguous block
                            commandList.SiftElement(i, j + 1);
                            break;
                        }
                        else if (commandList[j].Overlaps(commandList[i]))
                        {
                            //an overlapping command was found, therefore
                            //attempting to sift this one down would change
                            //the visual result
                            break;
                        }
                    }
                }
            }

            if (isDisposed) { return; }
            //each contiguous block of commands of the same texture
            //requires a vertex buffer to be rendered
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                if (isDisposed) { return; }
                if (commandList.Count == 0) { return; }
                int startIndex = 0;
                for (int i = 1; i < commandList.Count; i++)
                {
                    if (commandList[i].Texture != commandList[startIndex].Texture)
                    {
                        maxSpriteCount = Math.Max(maxSpriteCount, i - startIndex);
                        recordedBuffers.Add(new RecordedBuffer(commandList, startIndex, i - startIndex));
                        startIndex = i;
                    }
                }
                recordedBuffers.Add(new RecordedBuffer(commandList, startIndex, commandList.Count - startIndex));
                maxSpriteCount = Math.Max(maxSpriteCount, commandList.Count - startIndex);
            });
            
            commandList.Clear();

            ReadyToRender = true;
        }

        public void Render(Camera cam)
        {
            if (!ReadyToRender) { return; }
            var gfxDevice = GameMain.Instance.GraphicsDevice;

            BasicEffect ??= new BasicEffect(gfxDevice);
            BasicEffect.Projection = Matrix.CreateOrthographicOffCenter(new Rectangle(0, 0, cam.Resolution.X, cam.Resolution.Y), -1f, 1f);
            BasicEffect.View = cam.Transform;
            BasicEffect.World = Matrix.Identity;
            BasicEffect.TextureEnabled = true;
            BasicEffect.VertexColorEnabled = true;
            BasicEffect.Alpha = 1f;

            int requiredIndexCount = maxSpriteCount * 6;
            if (requiredIndexCount > 0 && (indexBuffer == null || indexBuffer.IndexCount < requiredIndexCount))
            {
                indexBuffer?.Dispose();
                indexBuffer = new IndexBuffer(gfxDevice, IndexElementSize.SixteenBits, requiredIndexCount * 2, BufferUsage.WriteOnly);
                ushort[] indices = new ushort[requiredIndexCount * 2];
                for (int i = 0; i < indices.Length; i += 6)
                {
                    indices[i + 0] = (ushort)((i / 6) * 4 + 1);
                    indices[i + 1] = (ushort)((i / 6) * 4 + 0);
                    indices[i + 2] = (ushort)((i / 6) * 4 + 2);
                    indices[i + 3] = (ushort)((i / 6) * 4 + 1);
                    indices[i + 4] = (ushort)((i / 6) * 4 + 2);
                    indices[i + 5] = (ushort)((i / 6) * 4 + 3);
                }
                indexBuffer.SetData(indices);
            }

            gfxDevice.Indices = indexBuffer;
            for (int i = 0; i < recordedBuffers.Count; i++)
            {
                gfxDevice.SetVertexBuffer(recordedBuffers[i].VertexBuffer);
                BasicEffect.Texture = recordedBuffers[i].Texture;
                BasicEffect.CurrentTechnique.Passes[0].Apply();
                gfxDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, recordedBuffers[i].PolyCount);
            }
        }

        public void Dispose()
        {
            isDisposed = true;
            foreach (var buffer in recordedBuffers)
            {
                buffer.VertexBuffer.Dispose();
            }
            recordedBuffers.Clear();
            commandList.Clear();
            indexBuffer?.Dispose(); indexBuffer = null;
            ReadyToRender = false;
        }
    }
}

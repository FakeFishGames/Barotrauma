using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Barotrauma.Sounds;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent, IDrawableComponent
    {
        private Sprite sprite, startSprite, endSprite;
        
        private RoundSound snapSound, reelSound;
        private SoundChannel reelSoundChannel;

        [Serialize(5, IsPropertySaveable.No)]
        public int SpriteWidth
        {
            get;
            set;
        }

        [Serialize("255,255,255,255", IsPropertySaveable.No)]
        public Color SpriteColor
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool Tile
        {
            get;
            set;
        }

        [Serialize("0.5,0.5)", IsPropertySaveable.No)]
        public Vector2 Origin { get; set; } = new Vector2(0.5f, 0.5f);

        [Serialize(true, IsPropertySaveable.No, description: "")]
        public bool BreakFromMiddle
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            get 
            {
                if (target == null || source == null) { return Vector2.Zero; }

                Vector2 sourcePos = GetSourcePos();

                return new Vector2(
                    Math.Abs(target.DrawPosition.X - sourcePos.X),
                    Math.Abs(target.DrawPosition.Y - sourcePos.Y)) * 1.5f;
            }
        }
        
        [Serialize("1.0, 1.0", IsPropertySaveable.No, description: "When reeling in, the pitch slides from X to Y, depending on the length of the rope.")]
        public Vector2 ReelSoundPitchSlide
        {
            get => _reelSoundPitchSlide;
            set
            {
                _reelSoundPitchSlide = new Vector2(
                    Math.Max(value.X, SoundChannel.MinFrequencyMultiplier), 
                    Math.Min(value.Y, SoundChannel.MaxFrequencyMultiplier));
            }
        }
        private Vector2 _reelSoundPitchSlide;

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        sprite = new Sprite(subElement);
                        break;
                    case "startsprite":
                        startSprite = new Sprite(subElement);
                        break;
                    case "endsprite":
                        endSprite = new Sprite(subElement);
                        break;
                    case "snapsound":
                        snapSound = RoundSound.Load(subElement);
                        break;
                    case "reelsound":
                        reelSound = RoundSound.Load(subElement);
                        break;
                }
            }
        }
        
        partial void UpdateProjSpecific()
        {
            if (isReelingIn && !Snapped)
            {
                PlaySound(reelSound, source.WorldPosition);
            }
            else
            {
                reelSoundChannel?.FadeOutAndDispose();
                reelSoundChannel = null;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (target == null || target.Removed) { return; }
            if (target.ParentInventory != null) { return; }
            if (source is Limb limb && limb.Removed) { return; }
            if (source is Entity e && e.Removed) { return; }

            Vector2 startPos = GetSourcePos(useDrawPosition: true);
            startPos.Y = -startPos.Y;
            if ((source as Item)?.GetComponent<Turret>() is { } turret)
            {
                if (turret.BarrelSprite != null)
                {
                    startPos += new Vector2((float)Math.Cos(turret.Rotation), (float)Math.Sin(turret.Rotation)) * turret.BarrelSprite.size.Y * turret.BarrelSprite.RelativeOrigin.Y * item.Scale * 0.9f;
                }
                startPos -= turret.GetRecoilOffset();
            }
            Vector2 endPos = new Vector2(target.DrawPosition.X, target.DrawPosition.Y);
            Vector2 flippedPos = target.Sprite.size * target.Scale * (Origin - new Vector2(0.5f));
            if (target.body.Dir < 0.0f)
            {
                flippedPos.X = -flippedPos.X;
            }
            endPos += Vector2.Transform(flippedPos, Matrix.CreateRotationZ(target.body.Rotation));
            endPos.Y = -endPos.Y;

            if (Snapped)
            {
                float snapState = 1.0f - snapTimer / SnapAnimDuration;
                Vector2 diff = target.DrawPosition - new Vector2(startPos.X, -startPos.Y);
                diff.Y = -diff.Y;

                int width = (int)(SpriteWidth * snapState);
                if (width > 0.0f) 
                {
                    float positionMultiplier = snapState;
                    if (BreakFromMiddle)
                    {
                        positionMultiplier /= 2;
                        DrawRope(spriteBatch, endPos - diff * positionMultiplier, endPos, width);
                    }
                    DrawRope(spriteBatch, startPos, startPos + diff * positionMultiplier, width);
                }
            }
            else
            {
                DrawRope(spriteBatch, startPos, endPos, SpriteWidth);
            }

            if (startSprite != null || endSprite != null)
            {
                Vector2 dir = endPos - startPos;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                if (startSprite != null)
                {
                    float depth = Math.Min(item.GetDrawDepth() + (startSprite.Depth - item.Sprite.Depth), 0.999f);
                    startSprite?.Draw(spriteBatch, startPos, overrideColor ?? SpriteColor, angle, depth: depth);
                }
                if (endSprite != null && (!Snapped || BreakFromMiddle))
                {
                    float depth = Math.Min(item.GetDrawDepth() + (endSprite.Depth - item.Sprite.Depth), 0.999f);
                    endSprite?.Draw(spriteBatch, endPos, overrideColor ?? SpriteColor, angle, depth: depth);
                }
            }
        }

        private void DrawRope(SpriteBatch spriteBatch, Vector2 startPos, Vector2 endPos, int width, Color? overrideColor = null)
        {
            float depth = sprite == null ?
                item.Sprite.Depth + 0.001f :
                Math.Min(item.GetDrawDepth() + (sprite.Depth - item.Sprite.Depth), 0.999f);

            if (sprite?.Texture == null)
            {
                GUI.DrawLine(spriteBatch,
                    startPos,
                    endPos,
                    overrideColor ?? SpriteColor, depth: depth, width: width);
                return;
            }

            if (Tile)
            {
                float length = Vector2.Distance(startPos, endPos);
                Vector2 dir = (endPos - startPos) / length;
                float x;
                for (x = 0.0f; x <= length - sprite.size.X; x += sprite.size.X)
                {
                    GUI.DrawLine(spriteBatch, sprite,
                        startPos + dir * (x - 5.0f),
                        startPos + dir * (x + sprite.size.X),
                        overrideColor ?? SpriteColor, depth: depth, width: width);
                }
                float leftOver = length - x;
                if (leftOver > 0.0f)
                {
                    GUI.DrawLine(spriteBatch, sprite,
                        startPos + dir * (x - 5.0f),
                        endPos,
                        overrideColor ?? SpriteColor, depth: depth, width: width);
                }
            }
            else
            {
                GUI.DrawLine(spriteBatch, sprite,
                    startPos,
                    endPos,
                    overrideColor ?? SpriteColor, depth: depth, width: width);
            }
        }
        
        private void PlaySound(RoundSound sound, Vector2 position)
        {
            if (sound == null) { return; }
            if (sound == reelSound)
            {
                if (reelSoundChannel is not { IsPlaying: true })
                {
                    reelSoundChannel = SoundPlayer.PlaySound(sound.Sound, position, sound.Volume, sound.Range, ignoreMuffling: sound.IgnoreMuffling, freqMult: sound.GetRandomFrequencyMultiplier());
                    if (reelSoundChannel != null)
                    {
                        reelSoundChannel.Looping = true;
                    }
                }
                else
                {
                    reelSoundChannel.Position = new Vector3(position, 0);
                    reelSoundChannel.Gain = MathHelper.Lerp(0, 1.0f, MathUtils.InverseLerp(MinPullDistance, MaxLength, MathUtils.Pow(currentRopeLength, 1.5f)));
                    reelSoundChannel.FrequencyMultiplier = MathHelper.Lerp(ReelSoundPitchSlide.X, ReelSoundPitchSlide.Y, MathUtils.InverseLerp(MinPullDistance, MaxLength, currentRopeLength));
                }
            }
            else
            { 
                SoundPlayer.PlaySound(sound.Sound, position, sound.Volume, sound.Range, ignoreMuffling: sound.IgnoreMuffling, freqMult: sound.GetRandomFrequencyMultiplier());
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            snapped = msg.ReadBoolean();

            if (!snapped)
            {
                ushort targetId = msg.ReadUInt16();
                ushort sourceId = msg.ReadUInt16();
                byte limbIndex = msg.ReadByte();

                if (Entity.FindEntityByID(targetId) is not Item target) { return; }
                var source = Entity.FindEntityByID(sourceId);
                switch (source)
                {
                    case Character sourceCharacter when limbIndex >= 0 && limbIndex < sourceCharacter.AnimController.Limbs.Length:
                    {
                        Limb sourceLimb = sourceCharacter.AnimController.Limbs[limbIndex];
                        Attach(sourceLimb, target);
                        sourceCharacter.AnimController.DragWithRope();
                        break;
                    }
                    case ISpatialEntity spatialEntity:
                        Attach(spatialEntity, target);
                        break;
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            sprite?.Remove();
            sprite = null;
            startSprite?.Remove();
            startSprite = null;
            endSprite?.Remove();
            endSprite = null;
            reelSoundChannel?.FadeOutAndDispose();
            reelSoundChannel = null;
        }
    }
}

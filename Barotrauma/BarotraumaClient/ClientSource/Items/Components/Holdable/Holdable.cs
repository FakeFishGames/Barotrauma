using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Holdable : IDrawableComponent
    {
        /// <summary>
        /// Has an attach request been sent to the server during the current press of the Use key? Used for preventing repeated attach requests when holding down the key.
        /// </summary>
        private bool attachRequestSent;

        public Vector2 DrawSize
        {
            get { return item.Rect.Size.ToVector2(); }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (!IsActive || picker == null || !picker.IsKeyDown(InputType.Aim) || picker != Character.Controlled || !attachable)
            {
                Drawable = false;
                Item.ForceDrawOverWater = false;
                return;
            }

            Color indicatorColor = Color.White;
            if (!CanBeAttached(picker, out IEnumerable<Item> overlappingItems, showMessage: false))
            {
                foreach (var overlappingItem in overlappingItems)
                {
                    overlappingItem.Draw(spriteBatch, editing: false, overrideColor: Color.Red * 0.7f, overrideDepth: 0.0f);
                }
                indicatorColor = Color.Red;
            }
            
            Vector2 attachPos = GetAttachPosition(picker);

            Vector2 gridPos = picker.Position;
            if (AttachesToFloor)
            {
                gridPos.Y = attachPos.Y - item.Rect.Height / 2;
            }
            Vector2 roundedGridPos = new Vector2(
                MathUtils.RoundTowardsClosest(gridPos.X, Submarine.GridSize.X),
                MathUtils.RoundTowardsClosest(gridPos.Y, Submarine.GridSize.Y));

            if (item.Submarine == null)
            {
                Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                if (attachTarget != null)
                {
                    if (attachTarget.Submarine != null)
                    {
                        //set to submarine-relative position
                        gridPos += attachTarget.Submarine.DrawPosition;
                        roundedGridPos += attachTarget.Submarine.DrawPosition;
                        attachPos += attachTarget.Submarine.DrawPosition;
                    }
                }
            }
            else
            {
                gridPos += item.Submarine.DrawPosition;
                roundedGridPos += item.Submarine.DrawPosition;
                attachPos += item.Submarine.DrawPosition;
            }

            if (ShouldSnapToGrid(picker))
            {
                Submarine.DrawGrid(spriteBatch, 14, gridPos, roundedGridPos, alpha: 0.4f, color: indicatorColor);
            }

            float alpha = (AllowInGameSpriteDepthEditing ? 0.8f : 0.5f);

            // Use the sprite depth without DrawDepthOffset: the offset is set in DeattachFromWall (when the item is picked up) to
            // make the item render at SpriteDepthWhenDropped, and isn't refreshed when the sprite depth is edited, so including
            // it would make the preview render at a different depth than the item will be placed at
            float previewDepth = item.SpriteDepth;

            if (!AttachAsIdentifier.IsEmpty)
            {
                ItemPrefab toAttach = ItemPrefab.Find(null, AttachAsIdentifier);
                Sprite sprite = toAttach.Sprite;
                foreach (ContainedItemSprite containedSprite in toAttach.ContainedSprites)
                {
                    if (containedSprite.UseWhenAttached)
                    {
                        sprite = containedSprite.Sprite;
                        break;
                    }
                }

                sprite.Draw(spriteBatch, 
                    pos: attachPos.FlipY(),
                    color: toAttach.SpriteColor.Multiply(indicatorColor) * alpha,
                    rotate: 0f, 
                    scale: toAttach.Scale, 
                    spriteEffect: currentHorizontalFlipState ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 
                    depth: item.GetDrawDepth(previewDepth, sprite));
            }
            else
            {
                Sprite sprite = item.Sprite;
                foreach (ContainedItemSprite containedSprite in item.Prefab.ContainedSprites)
                {
                    if (containedSprite.UseWhenAttached)
                    {
                        sprite = containedSprite.Sprite;
                        break;
                    }
                }

                SpriteEffects previewSpriteEffects;
                if (CanBeFlippedInGame())
                {
                    previewSpriteEffects = currentHorizontalFlipState ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                else
                {
                    //show the orientation the item will be attached in: the item gets flipped when the holder faces left, and AttachToWall undoes that flip
                    previewSpriteEffects = item.SpriteEffects;
                    if (item.body is { Dir: < 0 } && item.Prefab.CanFlipX && item.Prefab.CanSpriteFlipX)
                    {
                        previewSpriteEffects ^= SpriteEffects.FlipHorizontally;
                    }
                }

                sprite.Draw(spriteBatch,
                    pos: attachPos.FlipY(),
                    color: item.SpriteColor.Multiply(indicatorColor) * alpha,
                    rotate: item.RotationRad,
                    scale: item.Scale,
                    spriteEffect: previewSpriteEffects,
                    depth: item.GetDrawDepth(previewDepth, sprite));
            }

            GUI.DrawRectangle(spriteBatch, new Vector2(attachPos.X - 2, -attachPos.Y - 2), Vector2.One * 5, GUIStyle.Red, thickness: 3);
        }

        /// <summary>
        /// Is the character currently aiming with the item to attach it somewhere (i.e. seeing the placement preview)?
        /// </summary>
        public bool IsBeingPlacedBy(Character character)
        {
            return IsActive && attachable && picker != null && picker == character && character.IsKeyDown(InputType.Aim);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            base.DrawHUD(spriteBatch, character);

            if (character != Character.Controlled || !IsActive || picker == null || !picker.IsKeyDown(InputType.Aim) || picker != Character.Controlled || !attachable)
            {
                return;
            }

            Vector2 attachPos = GetAttachPosition(picker, true);

            string inGameText = "";

            if (AllowInGameSpriteDepthEditing)
            {
                inGameText += 
                    TextManager.ParseInputTypes(
                        TextManager.GetWithVariable("Holdable.SpriteDepth", "[SpriteDepth]", 
                        Math.Round(item.SpriteDepth, 2).ToString()))
                    .Value;
            }

            if (CanBeFlippedInGame())
            {
                if (!inGameText.IsNullOrEmpty()) { inGameText += "\n"; }
                inGameText +=
                    TextManager.ParseInputTypes(TextManager.Get("Holdable.Flip")).Value;
            }

            if (!PlayerInput.KeyDown(InputType.ShowInteractionLabels))
            {
                if (!inGameText.IsNullOrEmpty()) { inGameText += "\n"; }
                inGameText +=
                    TextManager.ParseInputTypes(TextManager.Get("Holdable.FreePlacement")).Value;
            }

            if (!inGameText.IsNullOrEmpty())
            {
                var richString = RichString.Rich(inGameText);
                GUI.DrawStringWithColors(spriteBatch, 
                    pos: Screen.Selected.Cam.WorldToScreen(new Vector2(attachPos.X - 2, attachPos.Y - GUI.yScale * 30f)), 
                    text: richString.SanitizedValue,
                    richTextData: richString.RichTextData,
                    color: GUIStyle.TextColorNormal,
                    backgroundColor: Color.Black * 0.7f, 
                    backgroundPadding: 2, 
                    font: GUIStyle.SmallFont);
            }
        }

        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<AttachEventData>(data, out _);

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            if (!attachable || originalBody == null) { return; }
            
            var eventData = ExtractEventData<AttachEventData>(extraData);

            Vector2 attachPos = eventData.AttachPos;
            msg.WriteSingle(attachPos.X);
            msg.WriteSingle(attachPos.Y);
            msg.WriteSingle(item.SpriteDepth);
            msg.WriteBoolean(currentHorizontalFlipState);
        }

        public override void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            base.ClientEventRead(msg, sendingTime);

            bool readAttachData = msg.ReadBoolean();
            if (!readAttachData) { return; }

            bool shouldBeAttached = msg.ReadBoolean();
            Vector2 simPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            UInt16 submarineID = msg.ReadUInt16();
            UInt16 attacherID = msg.ReadUInt16();
            Submarine sub = Entity.FindEntityByID(submarineID) as Submarine;
            Character attacher = Entity.FindEntityByID(attacherID) as Character;

            if (shouldBeAttached)
            {
                if (!attached)
                {
                    Drop(false, null);
                    item.SetTransform(simPosition, 0.0f, forceSubmarine: sub);
                    AttachToWall();
                    PlaySound(ActionType.OnUse, attacher);
                    ApplyStatusEffects(ActionType.OnUse, (float)Timing.Step, character: attacher, user: attacher);
                    OnAttached?.Invoke();
                }
            }
            else
            {
                if (attached)
                {
                    DropConnectedWires(null);
                    if (originalBody != null)
                    {
                        item.body = originalBody;
                        item.body.Enabled = true;
                    }
                    IsActive = false;

                    DeattachFromWall();
                }
                else
                {
                    item.SetTransform(simPosition, 0.0f, forceSubmarine: sub);
                }
            }
        }
    }
}

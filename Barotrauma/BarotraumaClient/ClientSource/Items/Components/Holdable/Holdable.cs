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
        public Vector2 DrawSize
        {
            get { return item.Rect.Size.ToVector2(); }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (!IsActive || picker == null || !picker.IsKeyDown(InputType.Aim) || picker != Character.Controlled || !attachable)
            {
                Drawable = false;
                return;
            }

            Color indicatorColor = Color.White;
            if (!CanBeAttached(picker, out IEnumerable<Item> overlappingItems))
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

            Submarine.DrawGrid(spriteBatch, 14, gridPos, roundedGridPos, alpha: 0.4f, color: indicatorColor);

            Sprite sprite = item.Sprite;
            foreach (ContainedItemSprite containedSprite in item.Prefab.ContainedSprites)
            {
                if (containedSprite.UseWhenAttached)
                {
                    sprite = containedSprite.Sprite;
                    break;
                }
            }

            sprite.Draw(
                spriteBatch,
                new Vector2(attachPos.X, -attachPos.Y),
                item.SpriteColor.Multiply(indicatorColor) * 0.5f,
                item.RotationRad, 
                item.Scale, SpriteEffects.None, 0.0f);

            GUI.DrawRectangle(spriteBatch, new Vector2(attachPos.X - 2, -attachPos.Y - 2), Vector2.One * 5, GUIStyle.Red, thickness: 3);
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

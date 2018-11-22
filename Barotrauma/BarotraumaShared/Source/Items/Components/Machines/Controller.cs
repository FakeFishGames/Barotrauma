using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    struct LimbPos
    {
        public LimbType limbType;
        public Vector2 position;

        public LimbPos(LimbType limbType, Vector2 position)
        {
            this.limbType = limbType;
            this.position = position;
        }
    }

    partial class Controller : ItemComponent
    {
        //where the limbs of the user should be positioned when using the controller
        private List<LimbPos> limbPositions;

        private Direction dir;

        //the position where the user walks to when using the controller 
        //(relative to the position of the item)
        private Vector2 userPos;

        private Camera cam;

        private Character character;

        private Item focusTarget;
        private float targetRotation;

        public Vector2 UserPos
        {
            get { return userPos; }
            set { userPos = value; }
        }

        [Serialize(false, true)]
        public bool RequireAimToUse
        {
            get; set;
        }

        public Controller(Item item, XElement element)
            : base(item, element)
        {
            limbPositions = new List<LimbPos>();

            userPos = element.GetAttributeVector2("UserPos", Vector2.Zero);

            Enum.TryParse<Direction>(element.GetAttributeString("direction", "None"), out dir);
                
            foreach (XElement el in element.Elements())
            {
                if (el.Name != "limbposition") continue;

                LimbPos lp = new LimbPos();

                try
                {
                    lp.limbType = (LimbType)Enum.Parse(typeof(LimbType), el.Attribute("limb").Value, true);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + element + ": " + e.Message, e);
                }

                lp.position = el.GetAttributeVector2("position", Vector2.Zero);

                limbPositions.Add(lp);
            }

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;
            
            if (character == null 
                || character.Removed
                || character.SelectedConstruction != item
                || !character.CanInteractWith(item))
            {
                if (character != null)
                {
                    CancelUsing(character);
                    character = null;
                }
                IsActive = false;
                return;
            }

            character.AnimController.Anim = AnimController.Animation.UsingConstruction;

            if (userPos != Vector2.Zero)
            {
                Vector2 diff = (item.WorldPosition + userPos) - character.WorldPosition;

                if (character.AnimController.InWater)
                {
                    if (diff.Length() > 30.0f)
                    {
                        character.AnimController.TargetMovement = Vector2.Clamp(diff*0.01f, -Vector2.One, Vector2.One);
                        character.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                    }
                    else
                    {
                        character.AnimController.TargetMovement = Vector2.Zero;
                    }
                }
                else
                {
                    diff.Y = 0.0f;
                    if (diff != Vector2.Zero && diff.LengthSquared() > 10.0f * 10.0f)
                    {
                        character.AnimController.TargetMovement = Vector2.Normalize(diff);
                        character.AnimController.TargetDir = diff.X > 0.0f ? Direction.Right : Direction.Left;
                        return;
                    }
                    character.AnimController.TargetMovement = Vector2.Zero;                    
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, character);

            if (limbPositions.Count == 0) return;

            character.AnimController.Anim = AnimController.Animation.UsingConstruction;

            character.AnimController.ResetPullJoints();

            if (dir != 0) character.AnimController.TargetDir = dir;

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.AnimController.GetLimb(lb.limbType);
                if (limb == null || !limb.body.Enabled) continue;

                limb.Disabled = true;
                
                Vector2 worldPosition = lb.position + new Vector2(item.WorldRect.X, item.WorldRect.Y);
                Vector2 diff = worldPosition - limb.WorldPosition;

                limb.PullJointEnabled = true;
                limb.PullJointWorldAnchorB = limb.SimPosition + ConvertUnits.ToSimUnits(diff);
            }            
        }

        public override bool Use(float deltaTime, Character activator = null)
        {
            if (activator != character)
            {
                return false;
            }

            if (character == null || character.Removed ||
                character.SelectedConstruction != item || !character.CanInteractWith(item))
            {
                character = null;
                return false;
            }

            if (RequireAimToUse && !activator.IsKeyDown(InputType.Aim)) return false;

            item.SendSignal(0, "1", "trigger_out", character);
            
            ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);
            
            return true;
        }
        
        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (this.character != character)
            {
                return false;
            }

            if (this.character == null || character.Removed ||
                this.character.SelectedConstruction != item || !character.CanInteractWith(item))
            {
                this.character = null;
                return false;
            }
            if (character == null) return false;     

            focusTarget = GetFocusTarget();
            if (focusTarget == null)
            {
                Vector2 centerPos = new Vector2(item.WorldRect.Center.X, item.WorldRect.Center.Y);

                Vector2 offset = character.CursorWorldPosition - centerPos;
                offset.Y = -offset.Y;

                targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));
                return false;
            }
            
            character.ViewTarget = focusTarget;
#if CLIENT
            if (character == Character.Controlled && cam != null)
            {
                Lights.LightManager.ViewTarget = focusTarget;
                cam.TargetPos = focusTarget.WorldPosition;

                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, (focusTarget as Item).Prefab.OffsetOnSelected, deltaTime * 10.0f);
            }
#endif
            
            if (!character.IsRemotePlayer || character.ViewTarget == focusTarget)
            {
                Vector2 centerPos = new Vector2(item.WorldRect.Center.X, item.WorldRect.Center.Y);

                Item targetItem = focusTarget as Item;
                if (targetItem != null)
                {
                    Turret turret = targetItem.GetComponent<Turret>();
                    if (turret != null)
                    {
                        centerPos = new Vector2(targetItem.WorldRect.X + turret.BarrelPos.X, targetItem.WorldRect.Y - turret.BarrelPos.Y);
                    }
                }

                Vector2 offset = character.CursorWorldPosition - centerPos;
                offset.Y = -offset.Y;

                targetRotation = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(offset));
            }

            return true;
        }

        private Item GetFocusTarget()
        {
            item.SendSignal(0, targetRotation.ToString(), "position_out", character);

            for (int i = item.LastSentSignalRecipients.Count - 1; i >= 0; i--)
            {
                if (item.LastSentSignalRecipients[i].Condition <= 0.0f) continue;
                if (item.LastSentSignalRecipients[i].Prefab.FocusOnSelected)
                {
                    return item.LastSentSignalRecipients[i];
                }
            }
            
            return null;
        }

        public override bool Pick(Character picker)
        {
            item.SendSignal(0, "1", "signal_out", picker);

#if CLIENT
            PlaySound(ActionType.OnUse, item.WorldPosition);
#endif

            return true;
        }

        private void CancelUsing(Character character)
        {
            if (character == null || character.Removed) return;

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.AnimController.GetLimb(lb.limbType);
                if (limb == null) continue;

                limb.Disabled = false;
                limb.PullJointEnabled = false;
            }

            if (character.SelectedConstruction == this.item) character.SelectedConstruction = null;

            character.AnimController.Anim = AnimController.Animation.None;
        }

        public override bool Select(Character activator)
        {
            if (activator == null || activator.Removed) return false;

            //someone already using the item
            if (character != null && !character.Removed)
            {
                if (character == activator)
                {
                    IsActive = false;
                    CancelUsing(character);
                    character = null;
                    return false;
                }
            }
            else
            {
                character = activator;                    
                IsActive = true;
            }

            item.SendSignal(0, "1", "signal_out", character);
            return true;
        }

        public override void FlipX()
        {
            if (dir != Direction.None)
            {
                dir = dir == Direction.Left ? Direction.Right : Direction.Left;
            }

            userPos.X = -UserPos.X;            

            for (int i = 0; i < limbPositions.Count; i++)
            {
                float diff = (item.Rect.X + limbPositions[i].position.X) - item.Rect.Center.X;

                Vector2 flippedPos =
                    new Vector2(
                        item.Rect.Center.X - diff - item.Rect.X,
                        limbPositions[i].position.Y);

                limbPositions[i] = new LimbPos(limbPositions[i].limbType, flippedPos);
            }
        }

    }
}

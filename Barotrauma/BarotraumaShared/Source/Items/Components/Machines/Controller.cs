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

        public Vector2 UserPos
        {
            get { return userPos; }
            set { userPos = value; }
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
                    if (diff != Vector2.Zero && diff.Length() > 10.0f)
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
                if (limb == null) continue;

                limb.Disabled = true;

                if (limb.pullJoint == null) continue;

                Vector2 position = ConvertUnits.ToSimUnits(lb.position + new Vector2(item.Rect.X, item.Rect.Y));
                limb.pullJoint.Enabled = true;
                limb.pullJoint.WorldAnchorB = position;
            }
            
        }

        public override bool Use(float deltaTime, Character activator = null)
        {
            if (character == null || activator != character || character.SelectedConstruction != item || !character.CanInteractWith(item))
            {
                character = null;
                return false;
            }

            item.SendSignal(0, "1", "trigger_out", character);
            
            ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);
            
            return true;
        }

        public override void Aim(float deltaTime, Character character = null)
        {
            if (this.character == null || this.character != character || this.character.SelectedConstruction != item || !character.CanInteractWith(item))
            {
                character = null;
                return;
            }
            if (character == null) return;     

            Entity focusTarget = GetFocusTarget();
            if (focusTarget == null)
            {
                item.SendSignal(0, XMLExtensions.Vector2ToString(character.CursorWorldPosition), "position_out", character);
                return;
            }
            
            character.ViewTarget = focusTarget;
#if CLIENT
            if (character == Character.Controlled && cam != null)
            {
                Lights.LightManager.ViewTarget = focusTarget;
                cam.TargetPos = focusTarget.WorldPosition;

                cam.OffsetAmount = MathHelper.Lerp(cam.OffsetAmount, (focusTarget as Item).Prefab.OffsetOnSelected, deltaTime*10.0f);
            }
#endif
            
            if (!character.IsRemotePlayer || character.ViewTarget == focusTarget)
            {
                item.SendSignal(0, XMLExtensions.Vector2ToString(character.CursorWorldPosition), "position_out", character);
            }
        }

        private Item GetFocusTarget()
        {
            foreach (Connection c in item.Connections)
            {
                if (c.Name != "position_out") continue;

                foreach (Connection c2 in c.Recipients)
                {
                    if (c2 == null || c2.Item == null || !c2.Item.Prefab.FocusOnSelected) continue;
                    return c2.Item;
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
            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.AnimController.GetLimb(lb.limbType);
                if (limb == null) continue;

                limb.Disabled = false;

                limb.pullJoint.Enabled = false;
            }

            if (character.SelectedConstruction == this.item) character.SelectedConstruction = null;

            character.AnimController.Anim = AnimController.Animation.None;
        }

        public override bool Select(Character activator)
        {
            if (activator == null) return false;

            //someone already using the item
            if (character != null)
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

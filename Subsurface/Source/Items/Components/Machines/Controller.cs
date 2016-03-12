using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    struct LimbPos
    {
        public LimbType limbType;
        public Vector2 position;
    }

    class Controller : ItemComponent
    {
        //where the limbs of the user should be positioned when using the controller
        private List<LimbPos> limbPositions;

        private Direction dir;

        //the x-position where the user walks to when using the controller
        private float userPos;

        private Camera cam;

        private Character character;

        [HasDefaultValue(0.0f, false)]
        public float UserPos
        {
            get { return userPos; }
            set { userPos = value; }
        }

        public Controller(Item item, XElement element)
            : base(item, element)
        {
            limbPositions = new List<LimbPos>();

            dir = (Direction)Enum.Parse(typeof(Direction), ToolBox.GetAttributeString(element, "direction", "None"), true);

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

                lp.position = ToolBox.GetAttributeVector2(el, "position", Vector2.Zero);

                limbPositions.Add(lp);
            }

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            this.cam = cam;

            if (character == null 
                || character.IsDead
                || character.Stun > 0.0f
                || character.SelectedConstruction != item
                || Vector2.Distance(character.Position, item.Position) > item.PickDistance * 2.0f)
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

            if (userPos != 0.0f)
            {
                float torsoX = ConvertUnits.ToDisplayUnits(character.AnimController.RefLimb.SimPosition.X);

                Vector2 diff = new Vector2(item.Rect.X + UserPos - torsoX, 0.0f);

                if (diff!= Vector2.Zero && diff.Length() > 10.0f)
                {
                    //character.AnimController.Anim = AnimController.Animation.None;

                    character.AnimController.TargetMovement = new Vector2(Math.Sign(diff.X), 0.0f);
                    character.AnimController.TargetDir = (Math.Sign(diff.X) == 1) ? Direction.Right : Direction.Left;
                    return;
                }
                else
                {
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
            if (character == null || activator != character || character.SelectedConstruction != item)
            {
                character = null;
                return false;
            }

            item.SendSignal("1", "trigger_out");

            ApplyStatusEffects(ActionType.OnUse, 1.0f, activator);
            
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (this.character == null || this.character != character || this.character.SelectedConstruction != item)
            {
                character = null;
                return;
            }

            Entity focusTarget = null;

            if (character == null) return;
            

            foreach (Connection c in item.Connections)
            {
                if (c.Name != "position_out") continue;

                foreach (Connection c2 in c.Recipients)
                {
                    if (c2 == null || c2.Item == null || !c2.Item.Prefab.FocusOnSelected) continue;

                    focusTarget = c2.Item;

                    break;
                }
            }

            if (focusTarget == null)
            {
                item.SendSignal(ToolBox.Vector2ToString(character.CursorWorldPosition), "position_out");
                return;
            }

            if (character == Character.Controlled && cam != null)
            {
                Lights.LightManager.ViewTarget = focusTarget;
                cam.TargetPos = focusTarget.WorldPosition;
            }
            
            if (!character.IsNetworkPlayer || character.ViewTarget == focusTarget)
            {
                item.SendSignal(ToolBox.Vector2ToString(character.CursorWorldPosition), "position_out");
            }
        }

        public override bool Pick(Character picker)
        {
            item.SendSignal("1", "signal_out");

            PlaySound(ActionType.OnUse, item.WorldPosition);

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

            item.SendSignal("1", "signal_out");
            return true;
        }

    }
}

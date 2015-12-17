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
        List<LimbPos> limbPositions;

        Direction dir;

        //the x-position where the user walks to when using the controller
        float userPos;

        Camera cam;

        Character character;

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
                || character.SelectedConstruction != item
                || Vector2.Distance(character.SimPosition, item.SimPosition) > item.PickDistance * 1.5f)
            {
                if (character != null)
                {
                    character.SelectedConstruction = null;
                    character.AnimController.Anim = AnimController.Animation.None;
                    character = null;
                }
                IsActive = false;
                return;
            }

            if (userPos != 0.0f && character.AnimController.Anim != AnimController.Animation.UsingConstruction)
            {
                float torsoX = ConvertUnits.ToDisplayUnits(character.AnimController.RefLimb.SimPosition.X);

                Vector2 diff = new Vector2(item.Rect.X + UserPos - torsoX, 0.0f);

                if (diff!= Vector2.Zero && diff.Length() > 10.0f)
                {
                    character.AnimController.Anim = AnimController.Animation.None;

                    character.AnimController.TargetMovement = new Vector2(Math.Sign(diff.X), 0.0f);
                    character.AnimController.TargetDir = (Math.Sign(diff.X) == 1) ? Direction.Right : Direction.Left;
                    return;
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

                FixedMouseJoint fmj = limb.pullJoint;
                if (fmj == null) continue;

                Vector2 position = ConvertUnits.ToSimUnits(lb.position + new Vector2(item.Rect.X, item.Rect.Y));
                fmj.Enabled = true;
                fmj.WorldAnchorB = position;
            }
            
            item.SendSignal(ToolBox.Vector2ToString(character.CursorWorldPosition), "position_out");
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
            if (this.character == null || this.character!=character || this.character.SelectedConstruction!=item)
            {
                character = null;
                return;
            }

            foreach (Connection c in item.Connections)
            {
                if (c.Name != "position_out") continue;

                foreach (Connection c2 in c.Recipients)
                {
                    if (c2 == null || c2.Item==null || !c2.Item.Prefab.FocusOnSelected) continue;

                    Vector2 centerPos = c2.Item.WorldPosition;

                    if (character == Character.Controlled && cam != null)
                    {
                        Lights.LightManager.ViewPos = centerPos;
                        cam.TargetPos = c2.Item.WorldPosition;
                    }

                    break;
                }
            }
        }

        public override bool Pick(Character picker)
        {
            item.SendSignal("1", "signal_out");

            PlaySound(ActionType.OnUse, item.WorldPosition);

            return true;
        }

        public override bool Select(Character activator = null)
        {
            if (character!=null && character.SelectedConstruction == item)
            {
                character = null;
                IsActive = false;
                if (activator != null) activator.AnimController.Anim = AnimController.Animation.None;

                return true;
            }
            else
            {
                character = activator;
                if (activator == null) return false;
                    
                IsActive = true;
            }

            item.SendSignal("1", "signal_out");
            return true;
        }

    }
}

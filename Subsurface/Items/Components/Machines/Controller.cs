using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace Subsurface.Items.Components
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

        Character character;

        [HasDefaultValue(1.0f,false)]
        public float UserPos
        {
            set { userPos = value; }
        }

        public Controller(Item item, XElement element)
            : base(item, element)
        {
            limbPositions = new List<LimbPos>();

            dir = (Direction)Enum.Parse(typeof(Direction), ToolBox.GetAttributeString(element, "direction", "None"), true);

            //userPos = ToolBox.GetAttributeInt(element, "userpos", 1);
            
            //string allowedIdString = ToolBox.GetAttributeString(element, "allowedids", "");
            //if (allowedIdString!="")
            //{
            //    string[] splitIds = allowedIdString.Split(',');
            //    allowedIDs = new string[splitIds.Length];
            //    for (int i = 0; i<splitIds.Length; i++)
            //    {
            //        allowedIDs[i] = allowedIDs[i].Trim();
            //    }
            //}

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

            isActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            if (character == null || character.SelectedConstruction != item)
            {
                if (character != null)
                {
                    character.SelectedConstruction = null;
                    character.animController.anim = AnimController.Animation.None;
                    character = null;
                }
                isActive = false;
                return;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, character);

            if (userPos != 0.0f && character.animController.anim != AnimController.Animation.UsingConstruction)
            {
                Limb torso = character.animController.GetLimb(LimbType.Torso);
                float torsoX = ConvertUnits.ToDisplayUnits(torso.SimPosition.X);

                if (Math.Abs(torsoX - item.Rect.X + userPos) > 10.0f)
                {
                    character.animController.anim = AnimController.Animation.None;

                    character.animController.TargetMovement = 
                        new Vector2(
                            Math.Min(Math.Max(item.Rect.X + userPos - torsoX, -1.0f), 1.0f), 
                            0.0f);
                    character.animController.targetDir = (Math.Sign(torsoX - item.Rect.X + userPos) == 1) ? Direction.Right : Direction.Left;
                    return;
                }
            }

            if (limbPositions.Count == 0) return;

            character.animController.anim = AnimController.Animation.UsingConstruction;
            character.animController.ResetPullJoints();
            if (dir != 0) character.animController.targetDir = dir;

            foreach (LimbPos lb in limbPositions)
            {
                Limb limb = character.animController.GetLimb(lb.limbType);
                if (limb == null) continue;

                FixedMouseJoint fmj = limb.pullJoint;
                if (fmj == null) continue;

                Vector2 position = ConvertUnits.ToSimUnits(lb.position + new Vector2(item.Rect.X, item.Rect.Y));
                fmj.Enabled = true;
                fmj.WorldAnchorB = position;
            }

            foreach (MapEntity e in item.linkedTo)
            {
                Item linkedItem = e as Item;
                if (linkedItem == null) continue;
                linkedItem.Update(cam, deltaTime);
            }
        }

        public override bool Use(float deltaTime, Character activator = null)
        {
            character = activator;
            foreach (MapEntity e in item.linkedTo)
            {
                Item linkedItem = e as Item;
                if (linkedItem == null) continue;
                linkedItem.Use(deltaTime, activator);
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, character);
            
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (character == null) return;

            foreach (MapEntity e in item.linkedTo)
            {
                Item linkedItem = e as Item;
                if (linkedItem == null) continue;
                linkedItem.SecondaryUse(deltaTime, character);
            }
        }

        public override bool Pick(Character activator = null)
        {
            if (character!=null && character.SelectedConstruction == item)
            {
                character = null;
                isActive = false;
                if (activator != null) activator.animController.anim = AnimController.Animation.None;

                return false;
            }
            else
            {
                character = activator;
                if (activator == null) return false;
                    
                isActive = true;
            }

            item.SendSignal("1", "signal_out");
            return true;
        }

    }
}

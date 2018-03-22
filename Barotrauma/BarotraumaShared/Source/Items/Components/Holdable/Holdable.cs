using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Holdable : Pickable, IServerSerializable
    {
        //the position(s) in the item that the Character grabs
        protected Vector2[] handlePos;
        
        private InputType prevPickKey;
        private string prevMsg;
        private List<RelatedItem> prevRequiredItems;

        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

        protected Vector2 aimPos;

        //protected bool aimable;

        private bool attachable, attached, attachedByDefault;
        private PhysicsBody body;

        //the angle in which the Character holds the item
        protected float holdAngle;

        [Serialize(false, true)]
        public bool Attached
        {
            get { return attached && item.ParentInventory == null; }
            set { attached = value; }
        }

        [Serialize(false, false)]
        public bool ControlPose
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool Attachable
        {
            get { return attachable; }
            set { attachable = value; }
        }

        [Serialize(false, false)]
        public bool AttachedByDefault
        {
            get { return attachedByDefault; }
            set { attachedByDefault = value; }
        }

        [Serialize("0.0,0.0", false)]
        public Vector2 HoldPos
        {
            get { return ConvertUnits.ToDisplayUnits(holdPos); }
            set { holdPos = ConvertUnits.ToSimUnits(value); }
        }

        [Serialize("0.0,0.0", false)]
        public Vector2 AimPos
        {
            get { return ConvertUnits.ToDisplayUnits(aimPos); }
            set { aimPos = ConvertUnits.ToSimUnits(value); }
        }

        [Serialize(0.0f, false)]
        public float HoldAngle
        {
            get { return MathHelper.ToDegrees(holdAngle); }
            set { holdAngle = MathHelper.ToRadians(value); }
        }

        public Holdable(Item item, XElement element)
            : base(item, element)
        {
            body = item.body;

            handlePos = new Vector2[2];

            for (int i = 1; i < 3; i++)
            {
                handlePos[i - 1] = element.GetAttributeVector2("handle" + i, Vector2.Zero);

                handlePos[i - 1] = ConvertUnits.ToSimUnits(handlePos[i - 1]);
            }

            canBePicked = true;
            
            if (attachable)
            {
                prevMsg = Msg;
                prevPickKey = PickKey;
                prevRequiredItems = new List<RelatedItem>(requiredItems);
                                
                if (item.Submarine != null)
                {
                    if (item.Submarine.Loading)
                    {
                        AttachToWall();
                        attached = false;
                    }
                    else //the submarine is not being loaded, which means we're either in the sub editor or the item has been spawned mid-round
                    {
                        if (Screen.Selected == GameMain.SubEditorScreen)
                        {
                            //in the sub editor, attach
                            AttachToWall();
                        }
                        else
                        {
                            //spawned mid-round, deattach
                            DeattachFromWall();
                        }
                    }
                }
            }    
        }

        public override void Drop(Character dropper)
        {
            Drop(true, dropper);
        }

        private void Drop(bool dropConnectedWires, Character dropper)
        {
            if (dropConnectedWires)
            {
                DropConnectedWires(dropper);
            }

            if (attachable)
            {
                DeattachFromWall();

                if (body != null)
                {
                    item.body = body;
                }
            }

            if (item.body != null) item.body.Enabled = true;
            IsActive = false;

            if (picker == null)
            {
                if (dropper == null) return;
                picker = dropper;
            }
            if (picker.Inventory == null) return;

            item.Submarine = picker.Submarine;
            if (item.body != null)
            {
                item.body.ResetDynamics();
                item.SetTransform(picker.SimPosition, 0.0f);
            }

            picker.DeselectItem(item);
            picker.Inventory.RemoveItem(item);
            picker = null;
        }

        public override void Equip(Character character)
        {
            picker = character;

            if (character != null) item.Submarine = character.Submarine;

            if (item.body == null)
            {
                if (body != null)
                {
                    item.body = body;
                }
                else
                {
                    return;
                }
            }

            if (!item.body.Enabled)
            {
                Limb rightHand = picker.AnimController.GetLimb(LimbType.RightHand);
                item.SetTransform(rightHand.SimPosition, 0.0f);
            }

            bool alreadySelected = character.HasEquippedItem(item);
            if (picker.TrySelectItem(item) || picker.HasEquippedItem(item))
            {
                item.body.Enabled = true;
                IsActive = true;

                if (!alreadySelected) GameServer.Log(character.LogName + " equipped " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;

            picker.DeselectItem(item);

            GameServer.Log(character.LogName + " unequipped " + item.Name, ServerLog.MessageType.ItemInteraction);

            item.body.Enabled = false;
            IsActive = false;
        }

        public override bool Pick(Character picker)
        {
            if (!attachable)
            {
                return base.Pick(picker);
            }

            if (Attached)
            {
                return base.Pick(picker);
            }
            else
            {
                //not attached -> pick the item instantly, ignoring picking time
                return OnPicked(picker);
            }
        }

        public override bool OnPicked(Character picker)
        {
            if (base.OnPicked(picker))
            {
                DeattachFromWall();

                if (GameMain.Server != null && attachable)
                {
                    item.CreateServerEvent(this);
                    if (picker != null)
                    {
                        Networking.GameServer.Log(picker.LogName + " detached " + item.Name + " from a wall", ServerLog.MessageType.ItemInteraction);
                    }
                }
                return true;
            }

            return false;
        }

        private void AttachToWall()
        {
            if (!attachable) return;

            var containedItems = item.ContainedItems;
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained.body == null) continue;
                    contained.SetTransform(item.SimPosition, contained.body.Rotation);
                }
            }

            body.Enabled = false;
            item.body = null;
            
            Msg = prevMsg;
            PickKey = prevPickKey;
            requiredItems = new List<RelatedItem>(prevRequiredItems);

            attached = true;
        }

        private void DeattachFromWall()
        {
            if (!attachable) return;

            attached = false;

            //make the item pickable with the default pick key and with no specific tools/items when it's deattached
            requiredItems.Clear();
            Msg = "";
            PickKey = InputType.Select;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!attachable || item.body == null) return true;
            if (character != null)
            {
                if (!character.IsKeyDown(InputType.Aim)) return false;
                if (character.CurrentHull == null) return false;
                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                    GameServer.Log(character.LogName + " attached " + item.Name+" to a wall", ServerLog.MessageType.ItemInteraction);
                }
                item.Drop();
            }
            
            AttachToWall();

            return true;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.body == null || !item.body.Enabled) return;
            if (picker == null || !picker.HasEquippedItem(item))
            {
                IsActive = false;
                return;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip(item);

            item.Submarine = picker.Submarine;

            if (picker.HasSelectedItem(item))
            {
                picker.AnimController.HoldItem(deltaTime, item, handlePos, holdPos, aimPos, picker.IsKeyDown(InputType.Aim), holdAngle);
            }
            else
            {
                Limb equipLimb = null;
                if (picker.Inventory.IsInLimbSlot(item, InvSlotType.Headset) || picker.Inventory.IsInLimbSlot(item, InvSlotType.Head))
                {
                    equipLimb = picker.AnimController.GetLimb(LimbType.Head);
                }
                else if (picker.Inventory.IsInLimbSlot(item, InvSlotType.InnerClothes) || 
                    picker.Inventory.IsInLimbSlot(item, InvSlotType.OuterClothes))
                {
                    equipLimb = picker.AnimController.GetLimb(LimbType.Torso);
                }

                if (equipLimb != null)
                {
                    float itemAngle = (equipLimb.Rotation + holdAngle * picker.AnimController.Dir);

                    Matrix itemTransfrom = Matrix.CreateRotationZ(equipLimb.Rotation);
                    Vector2 transformedHandlePos = Vector2.Transform(handlePos[0], itemTransfrom);

                    item.body.ResetDynamics();
                    item.SetTransform(equipLimb.SimPosition - transformedHandlePos, itemAngle);
                }
            }
        }

        protected void Flip(Item item)
        {
            handlePos[0].X = -handlePos[0].X;
            handlePos[1].X = -handlePos[1].X;
            item.body.Dir = -item.body.Dir;
        }

        public override void OnItemLoaded()
        {
            if (item.Submarine != null && item.Submarine.Loading) return;
            OnMapLoaded();
        }

        public override void OnMapLoaded()
        {
            if (!attachable) return;
            
            if (Attached)
            {
                AttachToWall();
            }
            else
            {
                if (item.ParentInventory != null)
                {
                    if (body != null)
                    {
                        item.body = body;
                        body.Enabled = false;
                    }
                }
                DeattachFromWall();
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            if (!attachable || body == null)
            {
                DebugConsole.ThrowError("Sent an attachment event for an item that's not attachable.");
            }

            msg.Write(Attached);
            msg.Write(body.SimPosition.X);
            msg.Write(body.SimPosition.Y);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            bool isAttached = msg.ReadBoolean();
            Vector2 simPosition = new Vector2(msg.ReadFloat(), msg.ReadFloat());

            if (!attachable)
            {
                DebugConsole.ThrowError("Received an attachment event for an item that's not attachable.");
                return;
            }

            if (isAttached)
            {
                Drop(false, null);
                item.SetTransform(simPosition, 0.0f);
                AttachToWall();
            }
            else
            {
                DropConnectedWires(null);

                if (body != null)
                {
                    item.body = body;
                    item.body.Enabled = true;
                }
                IsActive = false;

                DeattachFromWall();
            }
        }
    }
}

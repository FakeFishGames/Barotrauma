using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Holdable : Pickable, IServerSerializable, IClientSerializable
    {
        const float MaxAttachDistance = 150.0f;

        //the position(s) in the item that the Character grabs
        protected Vector2[] handlePos;
        private readonly Vector2[] scaledHandlePos;

        private InputType prevPickKey;
        private string prevMsg;
        private Dictionary<RelatedItem.RelationType, List<RelatedItem>> prevRequiredItems;

        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

        protected Vector2 aimPos;

        private float swingState;

        private bool attachable, attached, attachedByDefault;
        private readonly PhysicsBody body;
        public PhysicsBody Pusher
        {
            get;
            private set;
        }

        //the angle in which the Character holds the item
        protected float holdAngle;

        public PhysicsBody Body
        {
            get { return item.body ?? body; }
        }

        [Serialize(false, true, description: "Is the item currently attached to a wall (only valid if Attachable is set to true).")]
        public bool Attached
        {
            get { return attached && item.ParentInventory == null; }
            set
            {
                attached = value;
                item.SetActiveSprite();
            }
        }

        [Serialize(true, true, description: "Can the item be pointed to a specific direction or do the characters always hold it in a static pose.")]
        public bool Aimable
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the character adjust its pose when aiming with the item. Most noticeable underwater, where the character will rotate its entire body to face the direction the item is aimed at.")]
        public bool ControlPose
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item be attached to walls.")]
        public bool Attachable
        {
            get { return attachable; }
            set { attachable = value; }
        }

        [Serialize(true, false, description: "Can the item be reattached to walls after it has been deattached (only valid if Attachable is set to true).")]
        public bool Reattachable
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the item be attached to a wall by default when it's placed in the submarine editor.")]
        public bool AttachedByDefault
        {
            get { return attachedByDefault; }
            set { attachedByDefault = value; }
        }

        [Editable, Serialize("0.0,0.0", false, description: "The position the character holds the item at (in pixels, as an offset from the character's shoulder)."+
            " For example, a value of 10,-100 would make the character hold the item 100 pixels below the shoulder and 10 pixels forwards.")]
        public Vector2 HoldPos
        {
            get { return ConvertUnits.ToDisplayUnits(holdPos); }
            set { holdPos = ConvertUnits.ToSimUnits(value); }
        }

        [Serialize("0.0,0.0", false, description: "The position the character holds the item at when aiming (in pixels, as an offset from the character's shoulder)."+
            " Works similarly as HoldPos, except that the position is rotated according to the direction the player is aiming at. For example, a value of 10,-100 would make the character hold the item 100 pixels below the shoulder and 10 pixels forwards when aiming directly to the right.")]
        public Vector2 AimPos
        {
            get { return ConvertUnits.ToDisplayUnits(aimPos); }
            set { aimPos = ConvertUnits.ToSimUnits(value); }
        }

        [Editable, Serialize(0.0f, false, description: "The rotation at which the character holds the item (in degrees, relative to the rotation of the character's hand).")]
        public float HoldAngle
        {
            get { return MathHelper.ToDegrees(holdAngle); }
            set { holdAngle = MathHelper.ToRadians(value); }
        }

        private Vector2 swingAmount;
        [Editable, Serialize("0.0,0.0", false, description: "How much the item swings around when aiming/holding it (in pixels, as an offset from AimPos/HoldPos).")]
        public Vector2 SwingAmount
        {
            get { return ConvertUnits.ToDisplayUnits(swingAmount); }
            set { swingAmount = ConvertUnits.ToSimUnits(value); }
        }
        
        [Editable, Serialize(0.0f, false, description: "How fast the item swings around when aiming/holding it (only valid if SwingAmount is set).")]
        public float SwingSpeed { get; set; }

        [Editable, Serialize(false, false, description: "Should the item swing around when it's being held.")]
        public bool SwingWhenHolding { get; set; }
        [Editable, Serialize(false, false, description: "Should the item swing around when it's being aimed.")]
        public bool SwingWhenAiming { get; set; }
        [Editable, Serialize(false, false, description: "Should the item swing around when it's being used (for example, when firing a weapon or a welding tool).")]
        public bool SwingWhenUsing { get; set; }
        
        public Holdable(Item item, XElement element)
            : base(item, element)
        {
            body = item.body;

            Pusher = null;
            if (element.GetAttributeBool("blocksplayers", false))
            {
                Pusher = new PhysicsBody(item.body.width, item.body.height, item.body.radius, item.body.Density)
                {
                    BodyType = BodyType.Dynamic,
                    CollidesWith = Physics.CollisionCharacter,
                    CollisionCategories = Physics.CollisionItemBlocking,
                    Enabled = false
                };
                Pusher.FarseerBody.OnCollision += OnPusherCollision;
                Pusher.FarseerBody.FixedRotation = false;
                Pusher.FarseerBody.IgnoreGravity = true;
            }

            handlePos = new Vector2[2];
            scaledHandlePos = new Vector2[2];
            Vector2 previousValue = Vector2.Zero;
            for (int i = 1; i < 3; i++)
            {
                int index = i - 1;
                string attributeName = "handle" + i;
                var attribute = element.Attribute(attributeName);
                // If no value is defind for handle2, use the value of handle1.
                var value = attribute != null ? ConvertUnits.ToSimUnits(XMLExtensions.ParseVector2(attribute.Value)) : previousValue;
                handlePos[index] = value;
                previousValue = value;
            }

            canBePicked = true;
            
            if (attachable)
            {
                prevMsg = DisplayMsg;
                prevPickKey = PickKey;
                prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(requiredItems);
                                
                if (item.Submarine != null)
                {
                    if (item.Submarine.Loading)
                    {
                        AttachToWall();
                        Attached = false;
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

        private bool OnPusherCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body.UserData is Character character)
            {
                if (!IsActive) { return false; }
                return character != picker;
            }
            else
            {
                return true;
            }
        }

        public override void Load(XElement componentElement, bool usePrefabValues)
        {
            base.Load(componentElement, usePrefabValues);

            if (usePrefabValues)
            {
                //this needs to be loaded regardless
                Attached = componentElement.GetAttributeBool("attached", attached);
            }

            if (attachable)
            {
                prevMsg = DisplayMsg;
                prevPickKey = PickKey;
                prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(requiredItems);
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

            if (Pusher != null) { Pusher.Enabled = false; }
            if (item.body != null) { item.body.Enabled = true; }
            IsActive = false;

            if (picker == null)
            {
                if (dropper == null) { return; }
                picker = dropper;
            }
            if (picker.Inventory == null) { return; }

            item.Submarine = picker.Submarine;

            if (item.body != null)
            {
                if (item.body.Removed)
                {
                    DebugConsole.ThrowError(
                        "Failed to drop the Holdable component of the item \"" + item.Name + "\" (body has been removed"
                        + (item.Removed ? ", item has been removed)" : ")"));
                }
                else
                {
                    item.body.ResetDynamics();
                    Limb heldHand, arm;
                    if (picker.Inventory.IsInLimbSlot(item, InvSlotType.LeftHand))
                    {
                        heldHand = picker.AnimController.GetLimb(LimbType.LeftHand);
                        arm = picker.AnimController.GetLimb(LimbType.LeftArm);
                    }
                    else
                    {
                        heldHand = picker.AnimController.GetLimb(LimbType.RightHand);
                        arm = picker.AnimController.GetLimb(LimbType.RightArm);
                    }
                    if (heldHand != null && arm != null)
                    {
                        //hand simPosition is actually in the wrist so need to move the item out from it slightly
                        Vector2 diff = new Vector2(
                            (heldHand.SimPosition.X - arm.SimPosition.X) / 2f,
                            (heldHand.SimPosition.Y - arm.SimPosition.Y) / 2.5f);
                        item.SetTransform(heldHand.SimPosition + diff, 0.0f);
                    }
                    else
                    {
                        item.SetTransform(picker.SimPosition, 0.0f);
                    }     
                }
           
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

            bool alreadyEquipped = character.HasEquippedItem(item);
            bool canSelect = picker.TrySelectItem(item);

            if (canSelect || picker.HasEquippedItem(item))
            {
                if (!canSelect)
                {
                    character.DeselectItem(item);
                }

                item.body.Enabled = true;
                item.body.PhysEnabled = false;
                IsActive = true;

#if SERVER
                if (!alreadyEquipped) GameServer.Log(GameServer.CharacterLogName(character) + " equipped " + item.Name, ServerLog.MessageType.ItemInteraction);
#endif
            }
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;

            picker.DeselectItem(item);
#if SERVER
            GameServer.Log(GameServer.CharacterLogName(character) + " unequipped " + item.Name, ServerLog.MessageType.ItemInteraction);
#endif

            item.body.PhysEnabled = true;
            item.body.Enabled = false;
            IsActive = false;
        }

        public bool CanBeAttached()
        {
            if (!attachable || !Reattachable) return false;

            //can be attached anywhere in sub editor
            if (Screen.Selected == GameMain.SubEditorScreen) return true;

            //can be attached anywhere inside hulls
            if (item.CurrentHull != null) return true;

            return Structure.GetAttachTarget(item.WorldPosition) != null;
        }
        
        public bool CanBeDeattached()
        {
            if (!attachable || !attached) return true;

            //allow deattaching everywhere in sub editor
            if (Screen.Selected == GameMain.SubEditorScreen) return true;

            //don't allow deattaching if part of a sub and outside hulls
            return item.Submarine == null || item.CurrentHull != null;
        }

        public override bool Pick(Character picker)
        {
            if (!attachable)
            {
                return base.Pick(picker);
            }

            if (!CanBeDeattached()) return false;

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

#if SERVER
                if (GameMain.Server != null && attachable)
                {
                    item.CreateServerEvent(this);
                    if (picker != null)
                    {
                        GameServer.Log(GameServer.CharacterLogName(picker) + " detached " + item.Name + " from a wall", ServerLog.MessageType.ItemInteraction);
                    }
                }
#endif
                return true;
            }

            return false;
        }

        public void AttachToWall()
        {
            if (!attachable) return;

            //outside hulls/subs -> we need to check if the item is being attached on a structure outside the sub
            if (item.CurrentHull == null && item.Submarine == null)
            {
                Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                if (attachTarget != null)
                {
                    if (attachTarget.Submarine != null)
                    {
                        //set to submarine-relative position
                        item.SetTransform(ConvertUnits.ToSimUnits(item.WorldPosition - attachTarget.Submarine.Position), 0.0f, false);
                    }
                    item.Submarine = attachTarget.Submarine;
                }
            }

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

            DisplayMsg = prevMsg;
            PickKey = prevPickKey;
            requiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(prevRequiredItems);

            Attached = true;
        }

        public void DeattachFromWall()
        {
            if (!attachable) return;

            Attached = false;

            //make the item pickable with the default pick key and with no specific tools/items when it's deattached
            requiredItems.Clear();
            DisplayMsg = "";
            PickKey = InputType.Select;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!attachable || item.body == null) { return character == null || character.IsKeyDown(InputType.Aim); }
            if (character != null)
            {
                if (!character.IsKeyDown(InputType.Aim)) { return false; }
                if (!CanBeAttached()) { return false; }

                if (GameMain.NetworkMember != null)
                {
                    if (character != Character.Controlled)
                    {
                        return false;
                    }
                    else if (GameMain.NetworkMember.IsServer)
                    {
                        return false;
                    }
                    else
                    {
#if CLIENT
                        Vector2 attachPos = ConvertUnits.ToSimUnits(GetAttachPosition(character));
                        GameMain.Client.CreateEntityEvent(item, new object[] 
                        { 
                            NetEntityEvent.Type.ComponentState, 
                            item.GetComponentIndex(this), 
                            attachPos
                        });
#endif
                    }
                    return false;
                }
                else
                {
                    item.Drop(character);
                    item.SetTransform(ConvertUnits.ToSimUnits(GetAttachPosition(character)), 0.0f);
                }
            }

            AttachToWall();           

            return true;
        }

        private Vector2 GetAttachPosition(Character user)
        {
            if (user == null) { return item.Position; }

            Vector2 mouseDiff = user.CursorWorldPosition - user.WorldPosition;
            mouseDiff = mouseDiff.ClampLength(MaxAttachDistance);

            return new Vector2(
                MathUtils.RoundTowardsClosest(user.Position.X + mouseDiff.X, Submarine.GridSize.X),
                MathUtils.RoundTowardsClosest(user.Position.Y + mouseDiff.Y, Submarine.GridSize.Y));
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
                if (Pusher != null) { Pusher.Enabled = false; }
                IsActive = false;
                return;
            }

            Vector2 swing = Vector2.Zero;
            if (swingAmount != Vector2.Zero)
            {
                swingState += deltaTime;
                swingState %= 1.0f;
                if (SwingWhenHolding ||
                    (SwingWhenAiming && picker.IsKeyDown(InputType.Aim)) ||
                    (SwingWhenUsing && picker.IsKeyDown(InputType.Aim) && picker.IsKeyDown(InputType.Shoot)))
                {
                    swing = swingAmount * new Vector2(
                        PerlinNoise.GetPerlin(swingState * SwingSpeed * 0.1f, swingState * SwingSpeed * 0.1f) - 0.5f,
                        PerlinNoise.GetPerlin(swingState * SwingSpeed * 0.1f + 0.5f, swingState * SwingSpeed * 0.1f + 0.5f) - 0.5f);
                }
            }
            
            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) Flip();

            item.Submarine = picker.Submarine;
            
            if (picker.HasSelectedItem(item))
            {
                scaledHandlePos[0] = handlePos[0] * item.Scale;
                scaledHandlePos[1] = handlePos[1] * item.Scale;
                bool aim = picker.IsKeyDown(InputType.Aim) && aimPos != Vector2.Zero && (picker.SelectedConstruction == null || picker.SelectedConstruction.GetComponent<Ladder>() != null);
                picker.AnimController.HoldItem(deltaTime, item, scaledHandlePos, holdPos + swing, aimPos + swing, aim, holdAngle);
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
                    Vector2 transformedHandlePos = Vector2.Transform(handlePos[0] * item.Scale, itemTransfrom);

                    item.body.ResetDynamics();
                    item.SetTransform(equipLimb.SimPosition - transformedHandlePos, itemAngle);
                }
            }
        }

        public void Flip()
        {
            handlePos[0].X = -handlePos[0].X;
            handlePos[1].X = -handlePos[1].X;
            item.body.Dir = -item.body.Dir;
        }

        public override void OnItemLoaded()
        {
            if (item.Submarine != null && item.Submarine.Loading) return;
            OnMapLoaded();
            item.SetActiveSprite();
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
        
        public override XElement Save(XElement parentElement)
        {
            if (!attachable)
            {
                return base.Save(parentElement);
            }

            var tempMsg = DisplayMsg;
            var tempPickKey = PickKey;
            var tempRequiredItems = requiredItems;

            DisplayMsg = prevMsg;
            PickKey = prevPickKey;
            requiredItems = prevRequiredItems;
            
            XElement saveElement = base.Save(parentElement);

            DisplayMsg = tempMsg;
            PickKey = tempPickKey;
            requiredItems = tempRequiredItems;

            return saveElement;
        }       

    }
}

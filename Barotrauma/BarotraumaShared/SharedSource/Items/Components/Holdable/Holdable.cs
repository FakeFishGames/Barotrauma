using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    public partial class Holdable : Pickable, IServerSerializable, IClientSerializable
    {
        private readonly struct AttachEventData : IEventData
        {
            public readonly Vector2 AttachPos;
            public readonly Character Attacher;

            public AttachEventData(Vector2 attachPos, Character attacher)
            {
                AttachPos = attachPos;
                Attacher = attacher;
            }
        }

        public Action OnAttached, OnDeattached;

        private const float MaxAttachDistance = ItemPrefab.DefaultInteractDistance * 0.95f;
        /// <summary>
        /// How far down from the aimed position floor-attached items look for a floor
        /// </summary>
        private const float FloorAttachRaycastLength = MaxAttachDistance * 2;

        public const float MinDefaultSpriteDepthEdit = 0.6f;
        public const float MaxDefaultSpriteDepthEdit = 0.9f;

        //the position(s) in the item that the Character grabs
        protected Vector2[] handlePos;
        private readonly Vector2[] scaledHandlePos;

        private readonly InputType prevPickKey;
        private LocalizedString prevMsg;
        private Dictionary<RelatedItem.RelationType, List<RelatedItem>> prevRequiredItems;

        private float swingState;

        private Character prevEquipper;

        public override bool IsAttached => Attached;

        private bool attachable, attached, attachedByDefault;
        private Voronoi2.VoronoiCell attachTargetCell;

        /// <summary>
        /// Which structure is the item is currently attached to (if any)?
        /// </summary>
        public Structure AttachTargetStructure { get; private set; }

       /// <summary>
       /// Has the item been deattached at some point during the round?
       /// </summary>
        public bool DeattachedDuringRound { get; private set; }

        /// <summary>
        /// The item's original physics body (if one exists). When the item is attached to a wall, it's <see cref="Item.body"/> gets set to null,
        /// and we use this field to keep track of the original body.
        /// </summary>
        private PhysicsBody originalBody;

        public readonly ImmutableDictionary<StatTypes, float> HoldableStatValues;

        /// <summary>
        /// Temporarily disallows this holdable from being detached, doesn't save between rounds.
        /// </summary>
        public bool TemporarilyDisableDetaching { get; set; }

        public PhysicsBody Pusher
        {
            get;
            private set;
        }
        [Serialize(true, IsPropertySaveable.Yes, description: "Is the item currently able to push characters around? True by default. Only valid if blocksplayers is set to true.")]
        public bool CanPush
        {
            get;
            set;
        }

        public PhysicsBody Body
        {
            get { return item.body ?? originalBody; }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Is the item currently attached to a wall (only valid if Attachable is set to true).")]
        public bool Attached
        {
            get { return attached && item.ParentInventory == null; }
            set
            {
                attached = value;
                item.CheckCleanable();
                item.SetActiveSprite();
            }
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Can the item be pointed to a specific direction or do the characters always hold it in a static pose.")]
        public bool Aimable
        {
            get;
            set;
        }

        [Serialize(0f, IsPropertySaveable.Yes, description: "Camera offset to apply when aiming this item. Only valid if Aimable is set to true.")]
        public float CameraAimOffset { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Should the character adjust its pose when aiming with the item. Most noticeable underwater, where the character will rotate its entire body to face the direction the item is aimed at.")]
        public bool ControlPose
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Use the hand rotation instead of torso rotation for the item hold angle. Enable this if you want the item just to follow with the arm when not aiming instead of forcing the arm to a hold pose.")]
        public bool UseHandRotationForHoldAngle
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item be attached to walls.")]
        public bool Attachable
        {
            get { return attachable; }
            set
            {
                attachable = value;
                // This needs to be set to true so DrawHUD can display attach info to the user
                if (attachable) { DrawHudWhenEquipped = true; }
            }
        }

        [Serialize(true, IsPropertySaveable.No, description: "Can the item be reattached to walls after it has been deattached (only valid if Attachable is set to true).")]
        public bool Reattachable
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item only be attached in limited amount? Uses permanent stat values to check for legibility.")]
        public bool LimitedAttachable
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "When enabled, the item can only be attached to a position where it touches the floor.")]
        public bool AttachesToFloor
        {
            get;
            set;
        }

        [Serialize(-1, IsPropertySaveable.No)]
        public int MaxAttachedPerSub { get; set; }

        [Serialize(-1, IsPropertySaveable.No)]
        public int MaxAttachedPerHull { get; set;  }

        [Serialize(-1, IsPropertySaveable.No)]
        public int MaxAttachedPerOutpostModule { get; set; }

        [Serialize(-1, IsPropertySaveable.No)]
        public int MaxAttachedPerOutpost { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "Can the item be attached inside doors?")]
        public bool AllowAttachInsideDoors
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Allows the sprite depth of the item to be edited while being placed down.")]
        public bool AllowInGameSpriteDepthEditing
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Allows the item to be flipped in game when placing it.")]
        public bool AllowInGameFlipping
        {
            get;
            set;
        }

        [Serialize(MinDefaultSpriteDepthEdit, IsPropertySaveable.No, description: "Minimum sprite depth that can be edited while being placed down.")]
        public float MinSpriteDepth
        {
            get;
            set;
        }

        [Serialize(MaxDefaultSpriteDepthEdit, IsPropertySaveable.No, description: "Maximum sprite depth that can be edited while being placed down.")]
        public float MaxSpriteDepth
        {
            get;
            set;
        }

        /// <summary>
        /// Used to represent the current horizontal flip state set by the player. 
        /// Only relevant when using <see cref="AllowInGameFlipping"/>.
        /// </summary>
        private bool currentHorizontalFlipState;

        public float DefaultSpriteDepth
        {
            get
            {
                if (!AttachAsIdentifier.IsEmpty && ItemPrefab.Prefabs.TryGet(AttachAsIdentifier, out ItemPrefab prefab))
                {
                    return prefab.Sprite.Depth;
                }
                else
                {
                    return item.SpriteDepth;
                }
            }
        }

        private const float SpriteDepthEditStep = 0.01f;
        //how long the key needs to be held down before the depth starts changing continuously
        private const float SpriteDepthEditHoldDelay = 0.5f;
        private const float SpriteDepthEditHoldSpeed = 0.1f;

        private float increaseSpriteDepthKeyHeldTimer;
        private float decreaseSpriteDepthKeyHeldTimer;

        private HashSet<Identifier> disallowAttachingOverTags = new HashSet<Identifier>();

        [Editable, Serialize("", IsPropertySaveable.Yes)]
        public string DisallowAttachingOverTags
        {
            get => disallowAttachingOverTags.ConvertToString();
            set
            {
                disallowAttachingOverTags = value.ToIdentifiers().ToHashSet();
            }
        }

        [Serialize("0,0", IsPropertySaveable.Yes)]
        public Point DisallowAttachingOverSize
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Should the item be attached to a wall by default when it's placed in the submarine editor.")]
        public bool AttachedByDefault
        {
            get { return attachedByDefault; }
            set { attachedByDefault = value; }
        }

        [Serialize("", IsPropertySaveable.No, description: "This item will be placed instead when the holdable is attached.")]
        public Identifier AttachAsIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.No, description: "These tags will be added when the item is attached.")]
        public string AttachTags { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "Can the item be attached to subs/outposts belonging to some other team?")]
        public bool AllowAttachingInOtherTeamsSubs { get; set; }

        /// <summary>
        /// Sets <see cref="ItemContainer.AlwaysContainedItemsSpawned"/> to true when using <see cref="AttachAsIdentifier"/>, to prevent 
        /// always contained items from spawning.
        /// </summary>
        public bool DisableContainerAlwaysSpawnedItems { get; set; }

#if DEBUG
        [Editable]
#endif
        [Serialize("0.0,0.0", IsPropertySaveable.No, description: "The position the character holds the item at (in pixels, as an offset from the character's shoulder)."+
            " For example, a value of 10,-100 would make the character hold the item 100 pixels below the shoulder and 10 pixels forwards.")]
        public Vector2 HoldPos
        {
            get { return ConvertUnits.ToDisplayUnits(holdPos); }
            set { holdPos = ConvertUnits.ToSimUnits(value); }
        }
        //the distance from the holding characters elbow to center of the physics body of the item
        protected Vector2 holdPos;

#if DEBUG
        [Editable]
#endif
        [Serialize("0.0,0.0", IsPropertySaveable.No, description: "The position the character holds the item at when aiming (in pixels, as an offset from the character's shoulder)."+
            " Works similarly as HoldPos, except that the position is rotated according to the direction the player is aiming at. For example, a value of 10,-100 would make the character hold the item 100 pixels below the shoulder and 10 pixels forwards when aiming directly to the right.")]
        public Vector2 AimPos
        {
            get { return ConvertUnits.ToDisplayUnits(aimPos); }
            set { aimPos = ConvertUnits.ToSimUnits(value); }
        }
        protected Vector2 aimPos;

        protected float holdAngle;
#if DEBUG
        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "The rotation at which the character holds the item (in degrees, relative to the rotation of the character's hand).")]
#else
        [Serialize(0.0f, IsPropertySaveable.No)] 
#endif
        public float HoldAngle
        {
            get { return MathHelper.ToDegrees(holdAngle); }
            set { holdAngle = MathHelper.ToRadians(value); }
        }

        protected float aimAngle;
#if DEBUG
        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "The rotation at which the character holds the item while aiming (in degrees, relative to the rotation of the character's hand).")]
#else
        [Serialize(0.0f, IsPropertySaveable.No)] 
#endif
        public float AimAngle
        {
            get { return MathHelper.ToDegrees(aimAngle); }
            set { aimAngle = MathHelper.ToRadians(value); }
        }


        private float equipRotationRad;
        [Editable, Serialize(90.0f, IsPropertySaveable.No, description: "How many degrees the item rotates during the \"equip animation\" which rotates the item to the correct orientation on the character's hand.")]
        public float EquipRotationDegrees
        {
            get { return MathHelper.ToDegrees(equipRotationRad); }
            set { equipRotationRad = MathHelper.ToRadians(value); }
        }

        [Editable, Serialize(0.15f, IsPropertySaveable.No, description: "Duration of the \"equip animation\" which rotates the item to the correct orientation on the character's hand.")]
        public float EquipRotationDuration
        {
            get;
            set;
        }

        protected float equipRotationTimer;

        private Vector2 swingAmount;
#if DEBUG
        [Editable, Serialize("0.0,0.0", IsPropertySaveable.No, description: "How much the item swings around when aiming/holding it (in pixels, as an offset from AimPos/HoldPos).")]
#else
        [Serialize("0.0,0.0", IsPropertySaveable.No)] 
#endif
        public Vector2 SwingAmount
        {
            get { return ConvertUnits.ToDisplayUnits(swingAmount); }
            set { swingAmount = ConvertUnits.ToSimUnits(value); }
        }
#if DEBUG
        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How fast the item swings around when aiming/holding it (only valid if SwingAmount is set).")]
#else
        [Serialize(0.0f, IsPropertySaveable.No)]
#endif

        public float SwingSpeed { get; set; }

#if DEBUG
        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the item swing around when it's being held.")]
#else
        [Serialize(false, IsPropertySaveable.No)]
#endif
        public bool SwingWhenHolding { get; set; }

#if DEBUG
        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the item swing around when it's being aimed.")]
#else
        [Serialize(false, IsPropertySaveable.No)]
#endif
        public bool SwingWhenAiming { get; set; }

#if DEBUG
        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the item swing around when it's being used (for example, when firing a weapon or a welding tool).")]
#else
        [Serialize(false, IsPropertySaveable.No)]
#endif
        public bool SwingWhenUsing { get; set; }

#if DEBUG
        [Editable, Serialize(false, IsPropertySaveable.No)]
#else
        [Serialize(false, IsPropertySaveable.No)]
#endif
        public bool DisableHeadRotation { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "If true, this item can't be used if the character is also holding a ranged weapon.")]
        public bool DisableWhenRangedWeaponEquipped { get; set; }

        [ConditionallyEditable(ConditionallyEditable.ConditionType.Attachable, MinValueFloat = 0.0f, MaxValueFloat = 0.999f, DecimalCount = 3), Serialize(0.55f, IsPropertySaveable.No, description: "Sprite depth that's used when the item is NOT attached to a wall.")]
        public float SpriteDepthWhenDropped
        {
            get;
            set;
        }

        [Editable, Serialize("", IsPropertySaveable.Yes, translationTextTag: "ItemMsg", description: "A text displayed next to the item when it's been dropped on the floor (not attached to a wall).")]
        public string MsgWhenDropped
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.Yes, "If false, characters can not let go of the item when holding it.")]
        public bool CanBeUnheld { get; set; }

        /// <summary>
        /// For setting the handle positions using status effects
        /// </summary>
#if DEBUG
        [Editable]
#endif
        public Vector2 Handle1
        {
            get { return ConvertUnits.ToDisplayUnits(handlePos[0]); }
            set 
            { 
                handlePos[0] = ConvertUnits.ToSimUnits(value); 
                if (item.FlippedX)
                {
                    handlePos[0].X = -handlePos[0].X;
                }
                if (!secondHandlePosDefined)
                {
                    Handle2 = value;
                }
            }
        }

        /// <summary>
        /// For setting the handle positions using status effects
        /// </summary>
#if DEBUG
        [Editable]
#endif
        public Vector2 Handle2
        {
            get { return ConvertUnits.ToDisplayUnits(handlePos[1]); }
            set 
            { 
                handlePos[1] = ConvertUnits.ToSimUnits(value);
                if (item.FlippedX)
                {
                    handlePos[1].X = -handlePos[1].X;
                }
            }
        }

        private bool secondHandlePosDefined;

        public Holdable(Item item, ContentXElement element)
            : base(item, element)
        {
            originalBody = item.body;

            Pusher = null;
            if (element.GetAttributeBool("blocksplayers", false))
            {
                Pusher = new PhysicsBody(item.body.Width, item.body.Height, item.body.Radius, 
                    item.body.Density,
                    BodyType.Dynamic,
                    Physics.CollisionItemBlocking, 
                    Physics.CollisionCharacter | Physics.CollisionProjectile)
                {
                    Enabled = false,
                    UserData = this
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
                // If no value is defind for handle2, use the value of handle1.
                Vector2 value = previousValue;
                var attribute = element.GetAttribute(attributeName);
                if (attribute != null)
                {
                    secondHandlePosDefined = i > 1;
                    value = ConvertUnits.ToSimUnits(XMLExtensions.ParseVector2(attribute.Value));
                }
                handlePos[index] = value;
                previousValue = value;
            }

            canBePicked = true;
            prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(RequiredItems);
            
            if (!AttachAsIdentifier.IsEmpty)
            {
                // if this is supposed to attach as something else (= a holdable that spawns some other item when placed),
                // lets not have it attach automatically when placing on sub editor, since that would just delete the item
                // and spawn a new one, something that sub builders can just do themselves instead.
                AttachedByDefault = false;
            }

            if (attachable)
            {
                prevMsg = DisplayMsg;
                prevPickKey = PickKey;
                                
                if (item.Submarine != null)
                {
                    if (item.Submarine.Loading)
                    {
                        AttachToWall();
                        Attached = false;
                    }
                    else //the submarine is not being loaded, which means we're either in the sub editor or the item has been spawned mid-round
                    {
                        if (Screen.Selected == GameMain.SubEditorScreen && AttachedByDefault)
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
            characterUsable = element.GetAttributeBool("characterusable", true);

            Dictionary<StatTypes, float> statValues = new Dictionary<StatTypes, float>();
            foreach (var subElement in element.GetChildElements("statvalue"))
            {
                StatTypes statType = CharacterAbilityGroup.ParseStatType(subElement.GetAttributeString("stattype", ""), Name);
                float statValue = subElement.GetAttributeFloat("value", 0f);
                if (MathUtils.NearlyEqual(statValue, 0.0f))
                {
                    DebugConsole.AddWarning($"Potential error in {item.Name} ({nameof(Wearable)}): stat value {statType} has a value of 0, meaning it won't do anything.",
                        contentPackage: item.ContentPackage);
                }
                if (statValues.ContainsKey(statType))
                {
                    statValues[statType] += statValue;
                }
                else
                {
                    statValues.TryAdd(statType, statValue);
                }                
            }
            HoldableStatValues = statValues.ToImmutableDictionary();
        }

        private bool OnPusherCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body.UserData is Character character)
            {
                if (!IsActive) { return false; }
                if (!CanPush) { return false; }
                return character != picker;
            }
            else
            {
                return true;
            }
        }

        private bool loadedFromInstance;
        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);

            loadedFromInstance = true;

            if (usePrefabValues)
            {
                //this needs to be loaded regardless
                Attached = componentElement.GetAttributeBool("attached", attached);
            }

            if (attachable)
            {
                prevMsg = DisplayMsg;
                prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(RequiredItems);
            }
        }

        public override void Drop(Character dropper, bool setTransform = true)
        {
            Drop(true, dropper, setTransform);
        }

        private void Drop(bool dropConnectedWires, Character dropper, bool setTransform = true)
        {
            GetRope()?.Snap();
            if (dropConnectedWires)
            {
                DropConnectedWires(dropper);
            }

            if (attachable)
            {
                if (originalBody != null)
                {
                    item.body = originalBody;
                }
                DeattachFromWall();
            }

            if (Pusher != null) { Pusher.Enabled = false; }
            if (item.body != null) { item.body.Enabled = true; }

            IsActive = false;
            attachTargetCell = null;

            if (picker == null || picker.Removed)
            {
                if (dropper == null || dropper.Removed) { return; }
                picker = dropper;
            }
            if (picker.Inventory == null) { return; }

            item.Submarine = picker.Submarine;

            if (item.body != null && setTransform)
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
                    if (heldHand != null && !heldHand.Removed && arm != null && !arm.Removed)
                    {
                        //hand simPosition is actually in the wrist so need to move the item out from it slightly
                        Vector2 diff = new Vector2(
                            (heldHand.SimPosition.X - arm.SimPosition.X) / 2f,
                            (heldHand.SimPosition.Y - arm.SimPosition.Y) / 2.5f);

                        //we have forced the item to be in the same sub as the dropper above,
                        //and are placing it to the position of the hands in "local" coordinates
                        //which may be outside the sub if the character is e.g. standing half-way through the airlock
                        // -> let's use the forceSubmarine argument ensure the item is still considered to be in the sub's coordinate space,
                        //    or it will end up in a weird state and seemingly disappear
                        item.SetTransform(heldHand.SimPosition + diff, 0.0f, forceSubmarine: picker.Submarine);
                    }
                    else
                    {
                        item.SetTransform(picker.SimPosition, 0.0f, forceSubmarine: picker.Submarine);
                    }
                }
            }

            picker.Inventory.RemoveItem(item);
            picker = null;
        }

        public override void Equip(Character character)
        {
            //if the item has multiple Pickable components (e.g. Holdable and Wearable, check that we don't equip it in hands when the item is worn or vice versa)
            if (item.GetComponents<Pickable>().Count() > 0)
            {
                bool inSuitableSlot = false;
                for (int i = 0; i < character.Inventory.Capacity; i++)
                {
                    if (character.Inventory.GetItemsAt(i).Contains(item))
                    {
                        if (character.Inventory.SlotTypes[i] != InvSlotType.Any && 
                            allowedSlots.Any(a => a.HasFlag(character.Inventory.SlotTypes[i])))
                        {
                            inSuitableSlot = true;
                            break;
                        }
                    }
                }
                if (!inSuitableSlot) { return; }
            }

            picker = character;
            equipRotationTimer = 0.0f;

            if (item.Removed)
            {
                DebugConsole.ThrowError($"Attempted to equip a removed item ({item.Name})\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            //cannot hold and wear an item at the same time
            //(unless the slot in which it's held and worn are equal - e.g. a suit with built-in tool or weapon on one hand)
            var wearable = item.GetComponent<Wearable>();
            if (wearable != null &&               
                wearable.AllowedSlots.None(allowedSlots.Contains))
            {
                wearable.Unequip(character);
            }

            if (character != null) { item.Submarine = character.Submarine; }
            if (item.body == null)
            {
                if (originalBody != null)
                {
                    item.body = originalBody;
                }
                else
                {
                    return;
                }
            }

            if (!item.body.Enabled)
            {
                Limb hand = picker.AnimController.GetLimb(LimbType.RightHand) ?? picker.AnimController.GetLimb(LimbType.LeftHand);
                //call HoldItem to get the item to the correct position and orientation immediately
                picker.AnimController.HoldItem(1.0f, item, scaledHandlePos, itemPos: holdPos, aim: false, holdAngle, GetEquipRotationOffset());
                item.body.UpdateDrawPosition(interpolate: false);
            }

            bool alreadyEquipped = character.HasEquippedItem(item);
            if (picker != null && picker.HasEquippedItem(item))
            {
                item.body.Enabled = true;
                item.body.PhysEnabled = false;
                IsActive = true;

#if SERVER
                if (picker != prevEquipper) { GameServer.Log(GameServer.CharacterLogName(character) + " equipped " + item.Name, ServerLog.MessageType.ItemInteraction); }
#endif
                prevEquipper = picker;
            }
            else
            {
                prevEquipper = null;
            }
        }

        public override void Unequip(Character character)
        {
#if SERVER
            if (prevEquipper != null)
            {
                GameServer.Log(GameServer.CharacterLogName(character) + " unequipped " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
            prevEquipper = null;
            if (picker == null) { return; }
            item.body.PhysEnabled = true;
            item.body.Enabled = false;
            IsActive = false;
        }

        public bool CanBeAttached(Character user, bool showMessage = false)
        {
            return CanBeAttached(user, out _, showMessage);
        }

        private bool CanBeFlippedInGame()
        {
            if (!AllowInGameFlipping) { return false; }

            if (AttachAsIdentifier.IsEmpty)
            {
                if (!Item.Prefab.CanSpriteFlipX || !item.Prefab.CanFlipX)
                {
                    return false;
                }
            }
            else if (ItemPrefab.Prefabs.TryGet(AttachAsIdentifier, out ItemPrefab attachPrefab))
            {
                if (!attachPrefab.CanSpriteFlipX || !attachPrefab.CanFlipX)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<Item> tempOverlappingItems = new List<Item>();

        private bool CanBeAttached(Character user, out IEnumerable<Item> overlappingItems, bool showMessage = false)
        {
            tempOverlappingItems.Clear();
            overlappingItems = tempOverlappingItems;
            if (!attachable || !Reattachable) { return false; }

            //can be attached anywhere in sub editor
            if (Screen.Selected == GameMain.SubEditorScreen) { return true; }

            if (AttachesToFloor && item.CurrentHull == null) { return false; }

            Vector2 attachPos = user == null ? item.WorldPosition : GetAttachPosition(user, useWorldCoordinates: true);

            if (disallowAttachingOverTags.Any() || !AllowAttachInsideDoors)
            {
                //do not ignore closed gaps here: we may need to check for overlaps with doors in the room that are considered to be in an adjacent hull,
                //or with items that are in an adjacent hull and poking into this one through some closed gap/door
                var connectedHulls = item.CurrentHull?.GetConnectedHulls(includingThis: true, searchDepth: 5, ignoreClosedGaps: false);

                Vector2 size = DisallowAttachingOverSize == Point.Zero ? 
                    item.Rect.Size.ToVector2() : 
                    DisallowAttachingOverSize.ToVector2() * item.Scale;
                if (!AttachAsIdentifier.IsEmpty)
                {
                    ItemPrefab toAttach = ItemPrefab.Find(null, AttachAsIdentifier);
                    size = toAttach.Size * toAttach.Scale;
                }
                size /= 2f;

                foreach (Item otherItem in Item.ItemList)
                {
                    if (otherItem == item || otherItem.body is { BodyType: BodyType.Dynamic, Enabled: true }) { continue; }
                    if (connectedHulls != null && !connectedHulls.Contains(otherItem.CurrentHull)) { continue; }
                    if (disallowAttachingOverTags.None(tag => otherItem.HasTag(tag) || otherItem.Prefab.Identifier == tag) &&
                        (otherItem.GetComponent<Door>() == null || AllowAttachInsideDoors)) 
                    { 
                        continue; 
                    }
                    Rectangle worldRect = otherItem.WorldRect;

                    if (otherItem.GetComponent<Holdable>() is Holdable otherHoldable)
                    {
                        if (!otherHoldable.attached) { continue; }
                        if (otherHoldable.DisallowAttachingOverSize != Point.Zero)
                        {
                            Vector2 scaledSize = otherHoldable.DisallowAttachingOverSize.ToVector2() * item.Scale;
                            worldRect = new Rectangle(
                                otherItem.WorldPosition.ToPoint() - new Point((int)(scaledSize.X / 2), (int)(-scaledSize.Y / 2)),
                                scaledSize.ToPoint());
                        }
                    }                        
                    if (attachPos.X + size.X < worldRect.X || attachPos.X - size.X > worldRect.Right) { continue; }
                    if (attachPos.Y - size.Y > worldRect.Y || attachPos.Y + size.Y < worldRect.Y - worldRect.Height) { continue; }
                    tempOverlappingItems.Add(otherItem);
                }
                if (tempOverlappingItems.Any()) { return false; }
            }

            if (user != null)
            {
                Identifier prefabIdentifier = AttachAsIdentifier.IfEmpty(item.Prefab.Identifier);
                Structure attachTargetStructure = Structure.GetAttachTarget(attachPos);
                Submarine attachSubmarine = attachTargetStructure?.Submarine ?? item.Submarine;
                Hull attachHull = Hull.FindHull(attachPos) ?? item.CurrentHull;

                if (!AllowAttachingInOtherTeamsSubs)
                {
                    if (attachSubmarine != null && attachSubmarine.TeamID != user.TeamID)
                    {
#if CLIENT
                        if (showMessage && user == Character.Controlled)
                        {
                            GUI.AddMessage(TextManager.Get("itemmsgcannotattachotherteam"), Color.Red);
                        }
#endif
                        return false;
                    }
                }

                if (attachSubmarine != null)
                {
                    int perSubLimit = -1;
                    if (attachSubmarine.Info is { IsPlayer: true })
                    {
                        perSubLimit = MaxAttachedPerSub;
                    }
                    else if (attachSubmarine.Info is { IsOutpost: true })
                    {
                        perSubLimit = MaxAttachedPerOutpost;
                    }
                    if (perSubLimit >= 0)
                    {
                        int currentlyAttachedCount = Item.ItemList.Count(i => i.Prefab.Identifier == prefabIdentifier
                            && i.Submarine == attachSubmarine && i.GetComponent<Holdable>() is not { Attached: false });
                        if (!WithinLimit(currentlyAttachedCount, perSubLimit, user,
                            attachSubmarine.Info is { IsPlayer: true } ? "itemmsgtotalnumberlimited" : "itemmsgtotalnumberlimitedoutpost",
                            showMessage))
                        {
                            return false;
                        }
                    }
                }

                if (MaxAttachedPerHull >= 0)
                {
                    if (attachHull == null) { return false; }

                    List<Hull> linkedHulls = [attachHull];
                    attachHull.GetLinkedHulls(linkedHulls, includeHiddenHulls: true);

                    int currentlyAttachedCount = Item.ItemList.Count(i => i.Prefab.Identifier == prefabIdentifier
                        && linkedHulls.Contains(i.CurrentHull) && i.GetComponent<Holdable>() is not { Attached: false });

                    if (!WithinLimit(currentlyAttachedCount, MaxAttachedPerHull, user, "toomanyattachedinhull", showMessage)) { return false; }
                }

                if (attachHull != null && MaxAttachedPerOutpostModule >= 0 && attachHull.Submarine.OutpostModules != null)
                {
                    bool isAttachedToHallway =
                        attachHull.Submarine.OutpostModules.Any(m => m.HallwayEntities.Contains(attachHull) || m.HallwayEntities.Contains(attachTargetStructure));

                    List<MapEntity> GetEntities(PlacedModule module)
                    {
                        return isAttachedToHallway ? module.HallwayEntities : module.Entities;
                    }

                    //handle modules in hallways separately, as if the hallway was a separate module
                    PlacedModule targetModule = attachHull.Submarine.OutpostModules.FirstOrDefault(m => GetEntities(m).Contains(attachTargetStructure) || GetEntities(m).Contains(attachHull));
                    if (targetModule != null)
                    {
                        var moduleItems = Item.ItemList.Where(it => GetEntities(targetModule).Contains(it.CurrentHull));
                        int currentlyAttachedCount = moduleItems.Count(i => 
                            i.Prefab.Identifier == prefabIdentifier
                            && i.GetComponent<Holdable>() is not { Attached: false });
                        if (!WithinLimit(currentlyAttachedCount, MaxAttachedPerOutpostModule, user, "toomanyattachedinmodule", showMessage))
                        {
                            return false;
                        }
                    }
                }

                if (LimitedAttachable)
                {
                    if (user?.Info == null)
                    {
                        DebugConsole.AddWarning("Character without CharacterInfo attempting to attach a limited attachable item!");
                        return false;
                    }
                    int maxAttachableCount = (int)user.Info.GetSavedStatValueWithBotsInMp(StatTypes.MaxAttachableCount, prefabIdentifier);

                    int currentlyAttachedCount = Item.ItemList.Count(i => i.Prefab.Identifier == prefabIdentifier
                        && i.Submarine == attachSubmarine && i.GetComponent<Holdable>() is not { Attached: false });

                    if (maxAttachableCount == 0)
                    {
#if CLIENT
                        if (user == Character.Controlled)
                        {
                            GUI.AddMessage(TextManager.Get("itemmsgrequiretraining"), Color.Red);
                        }
#endif
                        return false;
                    }
                    else if (!WithinLimit(currentlyAttachedCount, maxAttachableCount, user, "itemmsgtotalnumberlimited", showMessage))
                    {
                        return false;
                    }
                }
            }

            //can be attached anywhere inside hulls
            if (item.CurrentHull != null && Submarine.RectContains(item.CurrentHull.WorldRect, attachPos)) { return true; }

            return Structure.GetAttachTarget(attachPos) != null || GetAttachTargetCell(100.0f) != null;

            static bool WithinLimit(int current, int max, Character character, string msgKey, bool showMessage)
            {
                if (current < max) { return true; }
#if CLIENT
                if (showMessage && character == Character.Controlled)
                {
                    GUI.AddMessage($"{TextManager.Get(msgKey)} ({current}/{max})", Color.Red);
                }
#endif
                return false;
            }
        }

        public bool CanBeDeattached()
        {
            if (TemporarilyDisableDetaching) { return false; }

            if (!attachable || !attached) { return true; }

            //allow deattaching everywhere in sub editor
            if (Screen.Selected == GameMain.SubEditorScreen) { return true; }

            if (item.GetComponent<LevelResource>() != null) { return true; }

            if (item.GetComponent<Planter>() is { } planter && planter.GrowableSeeds.Any(seed => seed != null)) { return false; } 

            //if the item has a connection panel and rewiring is disabled, don't allow deattaching
            var connectionPanel = item.GetComponent<ConnectionPanel>();
            if (connectionPanel != null && 
                !connectionPanel.AlwaysAllowRewiring &&
                (connectionPanel.Locked || connectionPanel.TemporarilyLocked || GameMain.NetworkMember?.ServerSettings is { AllowRewiring: false }))
            {
                return false;
            }

            if (item.CurrentHull == null)
            {
                return attachTargetCell != null || Structure.GetAttachTarget(item.WorldPosition) != null;
            }
            else
            {
                return true;
            }
        }

        public override bool Pick(Character picker)
        {
            if (item.Removed)
            {
                DebugConsole.ThrowError($"Attempted to pick up a removed item ({item.Name})\n" + Environment.StackTrace.CleanupStackTrace());
                return false;
            }

            if (!attachable)
            {
                return base.Pick(picker);
            }

            if (TemporarilyDisableDetaching)
            {
                return false;
            }

            if (!CanBeDeattached()) { return false; }

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
#if CLIENT
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                if (!picker.Inventory.CanBeAutoMovedToCorrectSlots(item))
                {
                    picker.Inventory.FlashAllowedSlots(item, Color.Red);
                }
                else
                {
                    //normally this would be done in the base.OnPicked method, but clients don't call it, 
                    //but instead rely on the server telling them to put the item in the inventory
                    SoundPlayer.PlayUISound(GUISoundType.PickItem);
                }
                return false;
            }
#endif
            bool wasAttached = IsAttached;
            if (base.OnPicked(picker))
            {
                DeattachFromWall();

#if SERVER
                if (GameMain.Server != null && attachable)
                {
                    item.CreateServerEvent(this);
                    if (picker != null && wasAttached)
                    {
                        GameServer.Log(GameServer.CharacterLogName(picker) + " detached " + item.Name + " from a wall", ServerLog.MessageType.ItemInteraction);
                    }
                }
#endif
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attaches the item to a wall. Returns the item that actually ended up attached, which is usually this one, except
        /// when using <see cref="AttachAsIdentifier"/> (eg. catalog blueprints).
        /// </summary>
        public Item AttachToWall()
        {
            if (!attachable) { return item; }
#if CLIENT
            item.ForceDrawOverWater = false;
            // DeattachFromWall (which is called when the item is dropped/placed) offsets the draw depth to make the item render at SpriteDepthWhenDropped,
            // we need to undo that, otherwise the attached item doesn't render at the sprite depth it was placed at
            item.DrawDepthOffset = 0.0f;
#endif
            if (!AttachAsIdentifier.IsEmpty)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return item; }

                ItemPrefab toAttach = ItemPrefab.Find(null, AttachAsIdentifier);

                Item spawnedItem = new Item(toAttach, Item.Position, Item.Submarine, callOnItemLoaded: false);

                if (DisableContainerAlwaysSpawnedItems)
                {
                    // This needs to be set before the item is loaded, otherwise it will be too late
                    spawnedItem.GetComponents<ItemContainer>().ForEach(static container =>
                    {
                        if (container.SpawnWithIdOnce)
                        {
                            container.AlwaysContainedItemsSpawned = true;
                        }
                    });
                }

                foreach (ItemComponent ic in spawnedItem.Components)
                {
                    ic.OnItemLoaded();
                }

#if SERVER
                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(spawnedItem));
#endif

                // Attach to wall if the attach item we are spawning also has a holdable
                Holdable holdable = spawnedItem.GetComponent<Holdable>();
                if (holdable != null)
                {
                    holdable.DeattachedDuringRound = true;
                    if (AllowInGameSpriteDepthEditing)
                    {
                        holdable.item.SpriteDepth = item.SpriteDepth;
                    }
                    if (AllowInGameFlipping)
                    {
                        holdable.currentHorizontalFlipState = currentHorizontalFlipState;
                    }
                    holdable.AttachToWall();
                }

                ApplyAttachInGameProperties(spawnedItem);

                spawnedItem.Tags = AttachTags;
                CheckAttachTargets(spawnedItem);

                if (AttachTargetStructure?.Submarine != null &&
                    AttachTargetStructure.Submarine.OutpostModules != null &&
                    AttachTargetStructure.Submarine.OutpostModules.FirstOrDefault(m => m.Entities.Contains(AttachTargetStructure)) is PlacedModule module)
                {
                    DebugConsole.NewMessage($"Attached item {spawnedItem.Prefab.Identifier} in module {module} (structure {AttachTargetStructure.Prefab.Identifier}).");
                    module.Entities.Add(spawnedItem);
                    foreach (var otherModule in spawnedItem.Submarine.OutpostModules)
                    {
                        if (otherModule != module)
                        {
                            otherModule.Entities.Remove(spawnedItem);
                        }
                    }
                }

                spawnedItem.FinishInitialization();

                Entity.Spawner.AddItemToRemoveQueue(Item);

                Attached = true;
                OnAttached?.Invoke();

                return spawnedItem;
            }
            else
            {
                if (originalBody == null)
                {
                    throw new InvalidOperationException($"Tried to attach an item with no physics body to a wall ({item.Prefab.Identifier}).");
                }

                originalBody.Enabled = false;
                originalBody.SetTransformIgnoreContacts(originalBody.SimPosition, rotation: 0f);
                if (item.body != null)
                {
                    //undo the flip applied when the holder turned left (see Update), so the item is attached in its "resting" orientation
                    //instead of keeping a flip that depends on which way the holder happened to be facing (and matches the placement preview)
                    if (item.body.Dir < 0)
                    {
                        item.FlipX(relativeToSub: false);
                    }
                    item.body.Dir = 1f;
                    item.body = null;
                }
                //lights on the item will turn from moving to static -> refresh the draw position and rotation immediately and ensure the light components are up-to-date
                originalBody.UpdateDrawPosition(interpolate: false);
                item.GetComponents<LightComponent>().ForEach(static light => light.SetLightSourceTransform());

                CheckAttachTargets(Item);

                IEnumerable<Item> containedItems = item.OwnInventory?.AllItems;
                if (containedItems != null)
                {
                    foreach (Item contained in containedItems)
                    {
                        if (contained?.body == null) { continue; }
                        contained.SetTransform(item.SimPosition, contained.body.Rotation);
                    }
                }

                DisplayMsg = prevMsg;
                PickKey = prevPickKey;
                RequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(prevRequiredItems);

                ApplyAttachInGameProperties(Item);

                Attached = true;

                //clients handle the OnAttached event in ClientEventRead, so we don't need to call it here
                if (GameMain.NetworkMember is not { IsClient: true })
                {
                    OnAttached?.Invoke();
                }
                
                return item;
            }
        }

        private void ApplyAttachInGameProperties(Item attachItem)
        {
            if (!item.FullyInitialized)
            {
                return;
            }

            if (AllowInGameSpriteDepthEditing)
            {
                // When attaching as another item (e.g. a catalog blueprint), the item that actually gets attached needs to inherit the depth the player chose
                if (attachItem != item)
                {
                    attachItem.SpriteDepth = item.SpriteDepth;
                }
#if SERVER
                GameMain.Server.CreateEntityEvent(attachItem, new Item.ChangePropertyEventData(attachItem.SerializableProperties[new Identifier("SpriteDepth")], attachItem));
#endif
            }

            if (CanBeFlippedInGame())
            {
                attachItem.FlippedX = currentHorizontalFlipState;
#if SERVER
                GameMain.Server.CreateEntityEvent(attachItem, new Item.FlipItemEventData(attachItem.FlippedX, attachItem.FlippedY));
#endif
            }
        }

        private void CheckAttachTargets(Item itemToAttach)
        {
            AttachTargetStructure = Structure.GetAttachTarget(itemToAttach.WorldPosition);
            // outside hulls/subs -> we need to check if the item is being attached on a structure outside the sub
            if (itemToAttach.CurrentHull == null && itemToAttach.Submarine == null) 
            {
                if (AttachTargetStructure != null)
                {
                    if (AttachTargetStructure.Submarine != null)
                    {
                        // set to submarine-relative position
                        itemToAttach.SetTransform(ConvertUnits.ToSimUnits(itemToAttach.WorldPosition - AttachTargetStructure.Submarine.Position), 0f, false);
                        originalBody.SetTransformIgnoreContacts(itemToAttach.SimPosition, 0f);
                    }
                    itemToAttach.Submarine = AttachTargetStructure.Submarine;
                }
                else
                {
                    attachTargetCell = GetAttachTargetCell(150f);
                    if (attachTargetCell != null && attachTargetCell.IsDestructible)
                    {
                        attachTargetCell.OnDestroyed += () =>
                        {
                            if (attachTargetCell != null && attachTargetCell.CellType != Voronoi2.CellType.Solid)
                            {
                                Drop(dropConnectedWires: true, dropper: null);
                            }
                        };
                    }
                }
            }
        }

        public void DeattachFromWall()
        {
            if (!attachable) { return; }

            bool wasAttached = attached;

            Attached = false;
            attachTargetCell = null;
            AttachTargetStructure = null;
#if CLIENT
            item.DrawDepthOffset = 0.0f;
#endif
            //make the item pickable with the default pick key and with no specific tools/items when it's deattached
            RequiredItems.Clear();
            if (MsgWhenDropped.IsNullOrEmpty())
            {
                DisplayMsg = "";
            }
            else
            {
                DisplayMsg = TextManager.Get(MsgWhenDropped);
                DisplayMsg =
                    DisplayMsg.Loaded ?
                    TextManager.ParseInputTypes(DisplayMsg) :
                    MsgWhenDropped;
            }

            PickKey = InputType.Select;
#if CLIENT
            item.DrawDepthOffset = SpriteDepthWhenDropped - item.SpriteDepth;
#endif
            foreach (LightComponent light in item.GetComponents<LightComponent>())
            {
                light.CheckIfNeedsUpdate();
            }

            if (wasAttached)
            {
                DeattachedDuringRound = true;
                OnDeattached?.Invoke();
            }
        }

        public override void ParseMsg()
        {
            base.ParseMsg();
            if (Attachable)
            {
                prevMsg = DisplayMsg;
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (UsageDisabledByRangedWeapon(character)) { return false; }
            if (!attachable || item.body == null) { return character == null || (character.IsKeyDown(InputType.Aim) && characterUsable); }
            if (character != null)
            {
                if (!characterUsable && !attachable) { return false; }
                if (!character.IsKeyDown(InputType.Aim)) { return false; }
                if (!CanBeAttached(character, showMessage: true)) { return false; }

                if (GameMain.NetworkMember != null)
                {
#if CLIENT
                    // Only send one request per Use press
                    if (character == Character.Controlled && !attachRequestSent)
                    {
                        Vector2 attachPosLocalCoordinates = ConvertUnits.ToSimUnits(GetAttachPosition(character, useWorldCoordinates: false));
                        item.CreateClientEvent(this, new AttachEventData(attachPosLocalCoordinates, character));
                        attachRequestSent = true;
                    }
#endif
                    //don't attach at this point in MP: instead rely on the network events created above
                    return false;
                }
                else
                {
                    item.Drop(character);
                    item.SetTransform(ConvertUnits.ToSimUnits(GetAttachPosition(character)), 0.0f, findNewHull: false);
                    //don't find the new hull in SetTransform, because that'd also potentially change the submarine (teleport the item outside if it's attached outside)
                    //instead just find the hull, so the item is considered to be in the right hull
                    item.CurrentHull = Hull.FindHull(item.WorldPosition, item.CurrentHull);
                    //the light source won't get properly updated if lighting is disabled (even though the light sprite is still drawn when lighting is disabled)
                    //so let's ensure the light source is up-to-date
                    RefreshLightSources(item);
                }
                AttachToWall();
            }
            return true;

            static void RefreshLightSources(Item item)
            {
                item.body?.UpdateDrawPosition();
                foreach (var light in item.GetComponents<LightComponent>())
                {
                    light.SetLightSourceTransform();
                }
                item.GetComponent<ItemContainer>()?.SetContainedItemPositions();
                foreach (var containedItem in item.ContainedItems)
                {
                    RefreshLightSources(containedItem);
                }
            }
        }


        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (character != null)
            {
                if (AllowInGameSpriteDepthEditing)
                {
                    if (character.IsKeyDown(InputType.ScrollUp))
                    {
                        if (character.IsKeyHit(InputType.ScrollUp))
                        {
                            item.SpriteDepth = Math.Min(MaxSpriteDepth, item.SpriteDepth + SpriteDepthEditStep);
                        }
                        else if (increaseSpriteDepthKeyHeldTimer > SpriteDepthEditHoldDelay)
                        {
                            item.SpriteDepth = Math.Min(MaxSpriteDepth, item.SpriteDepth + SpriteDepthEditHoldSpeed * deltaTime);
                        }
                        increaseSpriteDepthKeyHeldTimer += deltaTime;
                    }
                    else if (character.IsKeyHit(InputType.ScrollDown))
                    {
                        increaseSpriteDepthKeyHeldTimer = 0.0f;
                    }

                    if (character.IsKeyDown(InputType.ScrollDown))
                    {
                        if (character.IsKeyHit(InputType.ScrollDown))
                        {
                            item.SpriteDepth = Math.Max(MinSpriteDepth, item.SpriteDepth - SpriteDepthEditStep);
                        }
                        else if (decreaseSpriteDepthKeyHeldTimer > SpriteDepthEditHoldDelay)
                        {
                            item.SpriteDepth = Math.Max(MinSpriteDepth, item.SpriteDepth - SpriteDepthEditHoldSpeed * deltaTime);
                        }
                        decreaseSpriteDepthKeyHeldTimer += deltaTime;
                    }
                    else if (character.IsKeyHit(InputType.ScrollUp))
                    {
                        decreaseSpriteDepthKeyHeldTimer = 0.0f;
                    }

                    // Ensure the sprite depth is always within the range while editing
                    item.SpriteDepth = Math.Clamp(item.SpriteDepth, MinSpriteDepth, MaxSpriteDepth);

#if CLIENT
                    item.ForceDrawOverWater = item.SpriteDepth <= 0.5f;
#endif
                }

                if (CanBeFlippedInGame())
                {
                    if (character.IsKeyHit(InputType.Use))
                    {
                        currentHorizontalFlipState = !currentHorizontalFlipState;
                    }
                }
            }

            return true;
        }

        private Vector2 GetAttachPosition(Character user, bool useWorldCoordinates = false)
        {
            if (user == null) { return useWorldCoordinates ? item.WorldPosition : item.Position; }

            bool snapToGrid = ShouldSnapToGrid(user);

            Vector2 mouseDiff = user.CursorWorldPosition - user.WorldPosition;
            mouseDiff = mouseDiff.ClampLength(MaxAttachDistance);

            Vector2 submarinePos = useWorldCoordinates && user.Submarine != null ? user.Submarine.Position : Vector2.Zero;
            Vector2 userPos = useWorldCoordinates ? user.WorldPosition : user.Position;
            Vector2 attachPos = userPos + mouseDiff;

            Vector2 halfSize = new Vector2(item.Rect.Width, item.Rect.Height) / 2;

            //offset the position by half the size of the grid to get the item to adhere to the grid in the same way as in the sub editor
            //in the sub editor, we align the top-left corner of the item with the grid
            //but here the origin of the item is placed at the attach position, so we need to offset it
            Vector2 offset = new Vector2(
                -halfSize.X % Submarine.GridSize.X,
                halfSize.Y % Submarine.GridSize.Y);

            if (user.Submarine != null)
            {
                //we must add some "padding" to the raycast to ensure it reaches all the way to a wall
                //otherwise the cursor might be outside a wall, but the grid cell it's in might be partially inside
                Vector2 padding = halfSize * new Vector2(Math.Sign(mouseDiff.X), Math.Sign(mouseDiff.Y));

                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(user.Position), 
                    ConvertUnits.ToSimUnits(user.Position + mouseDiff + padding), collisionCategory: Physics.CollisionWall, 
                    /*don't ignore sensors so the raycast can hit open doors or broken walls*/
                    ignoreSensors: AllowAttachInsideDoors, customPredicate: (Fixture fixture) =>
                    {
                        if (fixture.UserData is Door) { return false; }
                        return true;
                    }) != null)
                {
                    attachPos = userPos + mouseDiff * Submarine.LastPickedFraction;
                    if (snapToGrid)
                    {
                        Vector2 pickedPos = attachPos + offset - submarinePos;
                        //round down if we're placing on the right side and vice versa: ensures we don't round the position inside a wall
                        attachPos =
                            new Vector2(
                                RoundToGrid(pickedPos.X, Submarine.GridSize.X, roundingDir: -Math.Sign(mouseDiff.X)),
                                RoundToGrid(pickedPos.Y, Submarine.GridSize.Y, roundingDir: -Math.Sign(mouseDiff.Y)))
                             - offset + submarinePos;
                    }
                }

                Vector2[] obstructionTestDirections = { -Vector2.UnitX, Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY };

                Vector2 correction = Vector2.Zero;

                // Check all four sides of the item bounds for remaining wall intersections, since the above
                // raycast is not always sufficient depending on the attachment angle, so apply a corrective
                // offset when an obstruction is detected
                foreach (Vector2 dir in obstructionTestDirections)
                {
                    Vector2 rayStart = attachPos - submarinePos;
                    Vector2 rayEnd = rayStart + dir * halfSize;
                    if (Submarine.PickBody(
                        ConvertUnits.ToSimUnits(rayStart),
                        ConvertUnits.ToSimUnits(rayEnd), collisionCategory: Physics.CollisionWall | Physics.CollisionPlatform) != null)
                    {
                        correction -= halfSize * dir * (1f - Submarine.LastPickedFraction);
                    }
                }

                attachPos += correction;

                if (AttachesToFloor)
                {
                    //if attaching to floor, do a raycast down and move the attach pos where it hits
                    float size = item.Rect.Height / 2.0f;
                    Vector2 rayStart = attachPos - submarinePos;
                    Vector2 rayEnd = rayStart - Vector2.UnitY * FloorAttachRaycastLength;
                    if (Submarine.PickBody(
                        ConvertUnits.ToSimUnits(rayStart),
                        ConvertUnits.ToSimUnits(rayEnd), collisionCategory: Physics.CollisionWall | Physics.CollisionPlatform) != null)
                    {
                        attachPos = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + Vector2.UnitY * size + submarinePos;
                    }
                    else
                    {
                        return Vector2.Zero;
                    }
                }
            }
            else if (Level.Loaded != null)
            {
                bool edgeFound = false;
                foreach (var cell in Level.Loaded.GetCells(attachPos))
                {
                    if (cell.CellType != Voronoi2.CellType.Solid) { continue; }
                    foreach (var edge in cell.Edges)
                    {
                        if (!edge.IsSolid) { continue; }
                        if (MathUtils.GetLineSegmentIntersection(edge.Point1, edge.Point2, user.WorldPosition, attachPos, out Vector2 intersection))
                        {
                            attachPos = intersection;
                            edgeFound = true;
                            break;
                        }
                    }
                    if (edgeFound) { break; }
                }
            }

            if (!snapToGrid) { return attachPos; }

            //subtract the submarine position so we're doing the rounding in the sub's
            //internal/local coordinate space regardless if we're using world coordinates
            //(otherwise the rounding would behave differently depending on the value of useWorldCoordinates)
            Vector2 offsetAttachPos = attachPos + offset - submarinePos;
            return
                new Vector2(
                    RoundToGrid(offsetAttachPos.X, Submarine.GridSize.X),
                    //don't round the vertical position if we're attaching to floor - we want the item to align with the floor, not the grid
                    AttachesToFloor ? offsetAttachPos.Y : RoundToGrid(offsetAttachPos.Y, Submarine.GridSize.Y))
                - offset + submarinePos;

            ///<param name="roundingDir">If < 0, the method rounds down. If > 0, rounds up. If 0, rounds to the closest integer.</param>
            static float RoundToGrid(float position, float gridSize, int roundingDir = 0)
            {
                if (roundingDir < 0)
                {
                    return MathF.Floor(position / gridSize) * gridSize;
                }
                else if (roundingDir > 0)
                {
                    return MathF.Ceiling(position / gridSize) * gridSize;
                }
                return MathUtils.RoundTowardsClosest(position, gridSize);
            }
        }

        /// <summary>
        /// Should the attach position be snapped to the grid? Holding InputType.ShowInteractionLabels (by default, alt) allows placing the item freely.
        /// </summary>
        private static bool ShouldSnapToGrid(Character user)
        {
#if CLIENT
            if (user == Character.Controlled && PlayerInput.KeyDown(InputType.ShowInteractionLabels)) { return false; }
#endif
            return true;
        }

        /// <summary>
        /// Could <see cref="GetAttachPosition"/> have produced an attach position this far from the user?
        /// </summary>
        /// <param name="offset">Attach position relative to the character, in display units</param>
        private bool IsWithinAttachReach(Vector2 offset)
        {
            // Leave a harmless margin, the point is just to block placement obviously too far
            const float maxDistance = MaxAttachDistance * 1.5f;
            if (AttachesToFloor && offset.Y < 0)
            {
                // Floor items can additionally drop straight down from the aimed position by up to the raycast length
                offset.Y = Math.Min(offset.Y + FloorAttachRaycastLength, 0);
            }
            return offset.LengthSquared() <= maxDistance * maxDistance;
        }

        private Voronoi2.VoronoiCell GetAttachTargetCell(float maxDist)
        {
            if (Level.Loaded == null) { return null; }
            foreach (var cell in Level.Loaded.GetCells(item.WorldPosition, searchDepth: 1))
            {
                if (cell.CellType != Voronoi2.CellType.Solid) { continue; }
                Vector2 diff = cell.Center - item.WorldPosition;
                if (diff.LengthSquared() > 0.0001f) { diff = Vector2.Normalize(diff); }
                if (cell.IsPointInside(item.WorldPosition + diff * maxDist))
                {
                    return cell;
                }
            }
            return null;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public Rope GetRope()
        {
            var rangedWeapon = Item.GetComponent<RangedWeapon>();
            if (rangedWeapon != null)
            {
                var lastProjectile = rangedWeapon.LastProjectile;
                if (lastProjectile != null)
                {
                    return lastProjectile.Item.GetComponent<Rope>();
                }
            }
            return null;
        }

        /// <summary>
        /// Get the rotational offset for the purposes of the "equip animation" which rotates the item to it's normal orientation when equipping.
        /// </summary>
        protected float GetEquipRotationOffset()
        {
            if (MathUtils.NearlyEqual(equipRotationRad, 0.0f))
            {
                return 0.0f;
            }
            return MathHelper.Lerp(equipRotationRad, 0.0f, MathHelper.Clamp(equipRotationTimer / EquipRotationDuration, 0.0f, 1.0f));
        }
                   

        public override void Update(float deltaTime, Camera cam)
        {
#if CLIENT
            // Allow a new attach request once the Use key is released (or Shoot for Shootables)
            if (attachRequestSent && (picker == null || (!picker.IsKeyDown(InputType.Use) && !picker.IsKeyDown(InputType.Shoot))))
            {
                attachRequestSent = false;
            }
#endif
            if (item.body == null || !item.body.Enabled) { return; }

            Character owner = picker ?? item.GetRootInventoryOwner() as Character;

            if (owner != null)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, owner);
            }

            if (picker == null || !picker.HasEquippedItem(item))
            {
                if (Pusher != null) { Pusher.Enabled = false; }
                if (attachTargetCell == null && owner == null) { IsActive = false; }
                return;
            }

            if (picker == Character.Controlled && picker.IsKeyDown(InputType.Aim) && attachable && Reattachable)
            {
                Drawable = true;
            }

            UpdateSwingPos(deltaTime, out Vector2 swingPos);
            if (item.body.Dir != picker.AnimController.Dir) 
            {
                item.FlipX(relativeToSub: false);
            }

            item.Submarine = picker.Submarine;
            
            if (picker.HeldItems.Contains(item))
            {
                equipRotationTimer += deltaTime;
                float rotationOffset = GetEquipRotationOffset();

                scaledHandlePos[0] = handlePos[0] * item.Scale;
                scaledHandlePos[1] = handlePos[1] * item.Scale;
                bool aim = picker.IsKeyDown(InputType.Aim) && aimPos != Vector2.Zero && picker.CanAim && !UsageDisabledByRangedWeapon(picker);
                if (aim)
                {
                    if (picker.AnimController.IsHoldingToRope && GetRope() is { Snapped: false } rope)
                    {
                        Vector2 targetPos = Submarine.GetRelativeSimPosition(picker, rope.Item);
                        picker.AnimController.HoldItem(deltaTime, item, scaledHandlePos, itemPos: aimPos, aim: true, holdAngle,
                            aimAngle + rotationOffset, targetPos: targetPos);
                    }
                    else
                    {
                        picker.AnimController.HoldItem(deltaTime, item, scaledHandlePos, itemPos: aimPos + swingPos, aim: true, holdAngle, 
                            aimAngle + rotationOffset);   
                    }
                }
                else
                {
                    picker.AnimController.HoldItem(deltaTime, item, scaledHandlePos, itemPos: holdPos + swingPos, aim: false, holdAngle,
                        rotationOffset);
                    if (GetRope() is { SnapWhenNotAimed: true } rope)
                    {
                        if (rope.Item.ParentInventory == null)
                        {
                            rope.Snap();   
                        }
                    }
                }
            }
            else
            {
                GetRope()?.Snap();
                Limb equipLimb = null;
                if (picker.Inventory.IsInLimbSlot(item, InvSlotType.Headset) || picker.Inventory.IsInLimbSlot(item, InvSlotType.Head) || picker.Inventory.IsInLimbSlot(item, InvSlotType.HealthInterface))
                {
                    equipLimb = picker.AnimController.GetLimb(LimbType.Head);
                }
                else if (picker.Inventory.IsInLimbSlot(item, InvSlotType.InnerClothes) || 
                    picker.Inventory.IsInLimbSlot(item, InvSlotType.OuterClothes))
                {
                    equipLimb = picker.AnimController.GetLimb(LimbType.Torso);
                }

                if (equipLimb != null && !equipLimb.Removed)
                {
                    float itemAngle = (equipLimb.Rotation + holdAngle * picker.AnimController.Dir);

                    Matrix itemTransfrom = Matrix.CreateRotationZ(equipLimb.Rotation);
                    Vector2 transformedHandlePos = Vector2.Transform(handlePos[0] * item.Scale, itemTransfrom);

                    item.body.ResetDynamics();
                    item.SetTransform(equipLimb.SimPosition - transformedHandlePos, itemAngle);
                }
            }
        }

        public void UpdateSwingPos(float deltaTime, out Vector2 swingPos)
        {
            swingPos = Vector2.Zero;
            if (swingAmount != Vector2.Zero && !picker.IsUnconscious && picker.Stun <= 0.0f)
            {
                swingState += deltaTime;
                swingState %= 1.0f;
                if (SwingWhenHolding ||
                    (SwingWhenAiming && picker.IsKeyDown(InputType.Aim)) ||
                    (SwingWhenUsing && picker.IsKeyDown(InputType.Aim) && picker.IsKeyDown(InputType.Shoot)))
                {
                    swingPos = swingAmount * new Vector2(
                        PerlinNoise.GetPerlin(swingState * SwingSpeed * 0.1f, swingState * SwingSpeed * 0.1f) - 0.5f,
                        PerlinNoise.GetPerlin(swingState * SwingSpeed * 0.1f + 0.5f, swingState * SwingSpeed * 0.1f + 0.5f) - 0.5f);
                }
            }
        }

        protected bool UsageDisabledByRangedWeapon(Character character)
        {
            if (DisableWhenRangedWeaponEquipped && character != null)
            {
                if (character.HeldItems.Any(it => it.GetComponent<RangedWeapon>() != null)) { return true; }
            }
            return false;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            //do nothing
        }

        public override void FlipX(bool relativeToSub)
        {
            handlePos[0].X = -handlePos[0].X;
            handlePos[1].X = -handlePos[1].X;
            if (item.body != null)
            {
                item.body.Dir = -item.body.Dir;
            }
        }

        public override void OnItemLoaded()
        {
            if (item.Submarine != null && item.Submarine.Loading) { return; }
            OnMapLoaded();
            item.SetActiveSprite();
        }

        public override void OnMapLoaded()
        {
            if (!attachable) { return; }
            
            //the Holdable component didn't get loaded from an instance of the item, just the prefab xml = a mod or update must've made the item movable/detachable
            if (!loadedFromInstance)
            {
                if (attachedByDefault)
                {
                    AttachToWall();
                    return;
                }
            }

            if (Attached)
            {
                AttachToWall();
            }
            else
            {
                if (originalBody != null)
                {
                    originalBody.SetTransformIgnoreContacts(item.SimPosition, item.Rotation);
                    item.body = originalBody;
                    originalBody.Enabled = item.ParentInventory == null;
                }                
                DeattachFromWall();
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            attachTargetCell = null;
            if (Pusher != null)
            {
                Pusher.Remove();
                Pusher = null;
            }
            originalBody = null; 
        }

        public override XElement Save(XElement parentElement)
        {
            if (!attachable)
            {
                return base.Save(parentElement);
            }

            var tempMsg = DisplayMsg;
            var tempRequiredItems = RequiredItems;

            DisplayMsg = prevMsg;
            RequiredItems = prevRequiredItems;
            
            XElement saveElement = base.Save(parentElement);

            DisplayMsg = tempMsg;
            RequiredItems = tempRequiredItems;

            return saveElement;
        }       

    }
}

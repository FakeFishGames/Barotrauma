#nullable enable

using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    /// <summary>
    /// Item component used by <see cref="Controller.SpawnItemOnSelected"/> for keeping a reference to the character that is currently 
    /// selecting the controller. Also provides functionality for changing the inventory sprite of the item based on the linked character.
    /// </summary>
    partial class LinkedControllerCharacterComponent : ItemComponent, IServerSerializable
    {
#if CLIENT
        private class SpriteOverride
        {
            public readonly Sprite? Sprite;
            public readonly Identifier SpeciesName;
            public readonly Identifier SpeciesGroup;
            public SpriteOverride(ContentXElement element)
            {
                if (element.GetChildElement("Sprite") is ContentXElement spriteElement)
                {
                    Sprite = new Sprite(spriteElement);
                }
                SpeciesName = element.GetAttributeIdentifier("speciesname", Identifier.Empty);
                SpeciesGroup = element.GetAttributeIdentifier("speciesgroup", Identifier.Empty);
            }
        }

        private readonly ImmutableArray<SpriteOverride> spriteOverrides;
#endif

        [Serialize(0.5f, IsPropertySaveable.No, description: $"Maximum value which {nameof(DeconstructTimeMultiplier)} can be.")]
        public float MaxDeconstructTimeMultiplier
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: $"Should this item be removed if the linked character is null?")]
        public bool RemoveItemIfCharacterNull { get; set; }

        public Character? Character { get; private set; }

        public bool DoesBleed => Character?.DoesBleed == true;

        public float DeconstructTimeMultiplier { get; private set; } = 1f;

        public LinkedControllerCharacterComponent(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;

#if CLIENT
            spriteOverrides = element.Elements()
                .Where(static e => e.Name.LocalName.ToLowerInvariant() == "spriteoverride")
                .Select(static e => new SpriteOverride(e))
                .ToImmutableArray();
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (RemoveItemIfCharacterNull && GameMain.NetworkMember is not { IsClient: true } && (Character == null || Character.Removed))
            {
                Entity.Spawner?.AddEntityToRemoveQueue(Item);
            }
        }

        public void UpdateLinkedCharacter(Character? character)
        {
            Character = character;

            if (character != null)
            {
                var animController = character.AnimController;
                float totalLimbs = animController.Limbs.Length;
                float nonSeveredLimbs = animController.Limbs.Count(static l => !l.IsSevered);

                // Decrease deconstruction time if the character is missing some limbs
                DeconstructTimeMultiplier *= MathF.Max(MaxDeconstructTimeMultiplier, nonSeveredLimbs / totalLimbs);
            }

#if CLIENT
            if (character != null)
            {
                SpriteOverride? spriteOverride =
                    spriteOverrides.Where(s => s.SpeciesName == character.SpeciesName).FirstOrDefault()
                    ?? spriteOverrides.Where(s => s.SpeciesGroup == character.Group).FirstOrDefault();
                
                if (spriteOverride != null)
                {
                    item.OverrideInventorySprite = spriteOverride.Sprite;
                }
            }
            else
            {
                item.OverrideInventorySprite = null;
            }
#elif SERVER
            Item.CreateServerEvent(this);
#endif
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            UInt16 characterId = msg.ReadUInt16();
            if (characterId == Entity.NullEntityID)
            {
                UpdateLinkedCharacter(null);
            }
            else if (Entity.FindEntityByID(characterId) is Character character)
            {
                UpdateLinkedCharacter(character);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
        {
            if (Character != null)
            {
                msg.WriteUInt16(Character.ID);
            }
            else
            {
                msg.WriteUInt16(Entity.NullEntityID);
            }
        }
    }
}
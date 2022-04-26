using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace Barotrauma.Items.Components
{
    partial class IdCard : Pickable
    {
        [Serialize(CharacterTeamType.None, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public CharacterTeamType TeamID
        {
            get;
            set;
        }

        [Serialize(0, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public int SubmarineSpecificID
        {
            get;
            set;
        }

        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public string OwnerTags
        {
            get => string.Join(',', OwnerTagSet);
            set => OwnerTagSet = value.Split(',').ToIdentifiers().ToImmutableHashSet();
        }

        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public string Description
        {
            get;
            set;
        }

        public ImmutableHashSet<Identifier> OwnerTagSet { get; set; }

        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public string OwnerName { get; set; }
        
        [Serialize("", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public Identifier OwnerJobId { get; set; }

        public JobPrefab OwnerJob => JobPrefab.Prefabs.TryGet(OwnerJobId, out var prefab) ? prefab : null;
        
        [Serialize(-1, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public int OwnerHairIndex { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public int OwnerBeardIndex { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public int OwnerMoustacheIndex { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public int OwnerFaceAttachmentIndex { get; set; }
        
        [Serialize("#ffffff", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public Color OwnerHairColor { get; set; }
        
        [Serialize("#ffffff", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public Color OwnerFacialHairColor { get; set; }
        
        [Serialize("#ffffff", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public Color OwnerSkinColor { get; set; }

        [Serialize("0,0", IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public Vector2 OwnerSheetIndex { get; set; }

        public IdCard(Item item, ContentXElement element) : base(item, element) { }

        public void Initialize(WayPoint spawnPoint, Character character)
        {
            item.AddTag("name:" + character.Name);

            CharacterInfo info = character.Info;
            if (info == null) { return; }

            if (spawnPoint != null)
            {
                foreach (string s in spawnPoint.IdCardTags)
                {
                    item.AddTag(s);
                }
                if (!string.IsNullOrWhiteSpace(spawnPoint.IdCardDesc)) 
                { 
                    item.Description = Description = spawnPoint.IdCardDesc; 
                }
            }

            TeamID = info.TeamID;

            var head = info.Head;
            if (head == null) { return; }

            OwnerName = info.Name;
            OwnerJobId = info.Job?.Prefab.Identifier ?? Identifier.Empty;
            item.AddTag($"jobid:{OwnerJobId}");
            OwnerTagSet = info.Head.Preset.TagSet;
            OwnerHairIndex = head.HairIndex;
            OwnerBeardIndex = head.BeardIndex;
            OwnerMoustacheIndex = head.MoustacheIndex;
            OwnerFaceAttachmentIndex = head.FaceAttachmentIndex;
            OwnerHairColor = head.HairColor;
            OwnerFacialHairColor = head.FacialHairColor;
            OwnerSkinColor = head.SkinColor;
            OwnerSheetIndex = head.SheetIndex;
        }

        public override void Equip(Character character)
        {
            base.Equip(character);
            character.Info?.CheckDisguiseStatus(true, this);
        }

        public override void Unequip(Character character)
        {
            base.Unequip(character);
            character.Info?.CheckDisguiseStatus(true, this);
        }
        public override void OnItemLoaded()
        {
            if (!string.IsNullOrEmpty(Description))
            {
                item.Description = Description;
            }
        }
    }
}
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class IdCard : Pickable
    {
        [Serialize(CharacterTeamType.None, true, alwaysUseInstanceValues: true)]
        public CharacterTeamType TeamID
        {
            get;
            set;
        }

        [Serialize(0, true, alwaysUseInstanceValues: true)]
        public int SubmarineSpecificID
        {
            get;
            set;
        }

        private JobPrefab cachedJobPrefab;
        private string cachedName;

        public IdCard(Item item, XElement element) : base(item, element)
        {

        }

        public void Initialize(CharacterInfo info)
        {
            if (info == null) { return; }

            if (info.Job?.Prefab != null)
            {
                item.AddTag("jobid:" + info.Job.Prefab.Identifier);
            }

            TeamID = info.TeamID;

            var head = info.Head;

            if (head == null) { return; }
            
            if (info.HasGenders) { item.AddTag($"gender:{head.gender.ToString().ToLowerInvariant()}"); }
            if (info.HasRaces) { item.AddTag($"race:{head.race}"); }
            item.AddTag($"headspriteid:{info.HeadSpriteId}");
            item.AddTag($"hairindex:{head.HairIndex}");
            item.AddTag($"beardindex:{head.BeardIndex}");
            item.AddTag($"moustacheindex:{head.MoustacheIndex}");
            item.AddTag($"faceattachmentindex:{head.FaceAttachmentIndex}");
            item.AddTag($"haircolor:{head.HairColor.ToStringHex()}");
            item.AddTag($"facialhaircolor:{head.FacialHairColor.ToStringHex()}");
            item.AddTag($"skincolor:{head.SkinColor.ToStringHex()}");

            if (head.SheetIndex != null)
            {
                item.AddTag($"sheetindex:{head.SheetIndex.Value.X};{head.SheetIndex.Value.Y}");
            }
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

        public JobPrefab GetJob()
        {
            if (cachedJobPrefab != null)
            {
                return cachedJobPrefab;
            }

            foreach (string tag in item.GetTags())
            {
                if (tag.StartsWith("jobid:"))
                {
                    string jobIdentifier = tag.Split(':').Last();
                    if (JobPrefab.Get(jobIdentifier) is { } jobPrefab)
                    {
                        cachedJobPrefab = jobPrefab;
                        return jobPrefab;
                    }
                }
            }

            return null;
        }
        
        public string GetName()
        {
            if (cachedName != null)
            {
                return cachedName;
            }

            foreach (string tag in item.GetTags())
            {
                if (tag.StartsWith("name:"))
                {
                    string ownerName = tag.Split(':').Last();
                    cachedName = ownerName;
                    return ownerName;
                }
            }

            return null;
        }
    }
}
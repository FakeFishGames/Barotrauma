using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class IdCard : Pickable
    {
        public IdCard(Item item, XElement element) : base(item, element)
        {

        }

        public void Initialize(CharacterInfo info)
        {
            if (info == null) return;

            if (info.Job?.Prefab != null)
            {
                item.AddTag("jobid:" + info.Job.Prefab.Identifier);
            }

            var head = info.Head;

            if (info != null && head != null)
            {
                item.AddTag("gender:" + head.gender.ToString().ToLowerInvariant());
                item.AddTag("race:" + head.race.ToString());
                item.AddTag("headspriteid:" + info.HeadSpriteId.ToString());
                item.AddTag("hairindex:" + head.HairIndex);
                item.AddTag("beardindex:" + head.BeardIndex);
                item.AddTag("moustacheindex:" + head.MoustacheIndex);
                item.AddTag("faceattachmentindex:" + head.FaceAttachmentIndex);

                if (head.SheetIndex != null)
                {
                    item.AddTag("sheetindex:" + head.SheetIndex.Value.X + ";" + head.SheetIndex.Value.Y);
                }
            }
        }

        public override void Equip(Character character)
        {
            base.Equip(character);
            character.Info.CheckDisguiseStatus(true, this);
        }

        public override void Unequip(Character character)
        {
            base.Unequip(character);
            character.Info.CheckDisguiseStatus(true, this);
        }
    }
}
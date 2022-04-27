using System;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.IO;

namespace Barotrauma.Items.Components
{
    partial class IdCard
    {
        public struct OwnerAppearance
        {
            public Sprite Portrait;
            public Vector2 SheetIndex;
            public JobPrefab JobPrefab;
            public List<WearableSprite> Attachments;
            public Color HairColor;
            public Color FacialHairColor;
            public Color SkinColor;

            public void ExtractJobPrefab(IReadOnlyDictionary<Identifier, string> tags)
            {
                if (!tags.TryGetValue("jobid".ToIdentifier(), out string jobId)) { return; }
                
                if (!jobId.IsNullOrEmpty())
                {
                    JobPrefab = JobPrefab.Get(jobId);
                }
            }

            public void ExtractAppearance(CharacterInfo characterInfo, IdCard idCard)
            {
                int disguisedHairIndex = idCard.OwnerHairIndex;
                int disguisedBeardIndex = idCard.OwnerBeardIndex;
                int disguisedMoustacheIndex = idCard.OwnerMoustacheIndex;
                int disguisedFaceAttachmentIndex = idCard.OwnerFaceAttachmentIndex;
                Color hairColor = idCard.OwnerHairColor;
                Color facialHairColor = idCard.OwnerFacialHairColor;
                Color skinColor = idCard.OwnerSkinColor;
                var tags = idCard.OwnerTagSet;

                if ((characterInfo.HasSpecifierTags && !tags.Any()))
                {
                    Portrait = null;
                    Attachments = null;
                    return;
                }

                foreach (ContentXElement limbElement in characterInfo.Ragdoll.MainElement.Elements())
                {
                    if (!limbElement.GetAttributeString("type", "").Equals("head", StringComparison.OrdinalIgnoreCase)) { continue; }

                    ContentXElement spriteElement = limbElement.GetChildElement("sprite");
                    if (spriteElement == null) { continue; }

                    ContentPath contentPath = spriteElement.GetAttributeContentPath("texture");

                    string spritePath = characterInfo.ReplaceVars(contentPath.Value);
                    string fileName = Path.GetFileNameWithoutExtension(spritePath);

                    //go through the files in the directory to find a matching sprite
                    foreach (string file in Directory.GetFiles(Path.GetDirectoryName(spritePath)))
                    {
                        if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                        fileWithoutTags = fileWithoutTags.Split('[', ']').First();
                        if (fileWithoutTags != fileName) { continue; }
                        Portrait = new Sprite(spriteElement, "", file) { RelativeOrigin = Vector2.Zero };
                        break;
                    }

                    break;
                }

                if (characterInfo.Wearables != null)
                {
                    float baldnessChance = 0.1f;

                    List<ContentXElement> createElementList(WearableType wearableType, float emptyCommonness = 1.0f)
                        => CharacterInfo.AddEmpty(
                            characterInfo.FilterElements(characterInfo.Wearables, tags, wearableType),
                            wearableType, emptyCommonness);

                    var disguisedHairs = createElementList(WearableType.Hair, baldnessChance);
                    var disguisedBeards = createElementList(WearableType.Beard);
                    var disguisedMoustaches = createElementList(WearableType.Moustache);
                    var disguisedFaceAttachments = createElementList(WearableType.FaceAttachment);

                    ContentXElement getElementFromList(List<ContentXElement> list, int index)
                        => CharacterInfo.IsValidIndex(index, list)
                            ? list[index]
                            : null;
                    
                    var disguisedHairElement = getElementFromList(disguisedHairs, disguisedHairIndex);
                    var disguisedBeardElement = getElementFromList(disguisedBeards, disguisedBeardIndex);
                    var disguisedMoustacheElement = getElementFromList(disguisedMoustaches, disguisedMoustacheIndex);
                    var disguisedFaceAttachmentElement = getElementFromList(disguisedFaceAttachments, disguisedFaceAttachmentIndex);
                    
                    Attachments = new List<WearableSprite>();

                    void loadAttachments(List<WearableSprite> attachments, ContentXElement element, WearableType wearableType)
                    {
                        foreach (var s in element?.GetChildElements("sprite") ?? Enumerable.Empty<ContentXElement>())
                        {
                            attachments.Add(new WearableSprite(s, wearableType));
                        }
                    }
                    
                    loadAttachments(Attachments, disguisedFaceAttachmentElement, WearableType.FaceAttachment);
                    loadAttachments(Attachments, disguisedBeardElement, WearableType.Beard);
                    loadAttachments(Attachments, disguisedMoustacheElement, WearableType.Moustache);
                    loadAttachments(Attachments, disguisedHairElement, WearableType.Hair);

                    loadAttachments(Attachments,
                        characterInfo.OmitJobInPortraitClothing
                            ? JobPrefab.NoJobElement?.GetChildElement("PortraitClothing")
                            : JobPrefab?.ClothingElement,
                        WearableType.JobIndicator);
                }

                HairColor = hairColor;
                FacialHairColor = facialHairColor;
                SkinColor = skinColor;
            }
        }

        public OwnerAppearance StoredOwnerAppearance = default;
    }
}

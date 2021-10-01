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

            public void ExtractJobPrefab(string[] tags)
            {
                string jobIdTag = tags.FirstOrDefault(s => s.StartsWith("jobid:"));

                if (jobIdTag != null && jobIdTag.Length > 6)
                {
                    string jobId = jobIdTag.Substring(6);
                    if (jobId != string.Empty)
                    {
                        JobPrefab = JobPrefab.Get(jobId);
                    }
                }
            }

            public void ExtractAppearance(CharacterInfo characterInfo, string[] tags)
            {
                Gender disguisedGender = Gender.None;
                Race disguisedRace = Race.None;
                int disguisedHeadSpriteId = -1;
                int disguisedHairIndex = -1;
                int disguisedBeardIndex = -1;
                int disguisedMoustacheIndex = -1;
                int disguisedFaceAttachmentIndex = -1;
                Color hairColor = Color.Black;
                Color facialHairColor = Color.Black;
                Color skinColor = Color.Black;

                foreach (string tag in tags)
                {
                    string[] s = tag.Split(':');

                    switch (s[0].ToLowerInvariant())
                    {
                        case "haircolor":
                            hairColor = XMLExtensions.ParseColor(s[1]);
                            break;
                        
                        case "facialhaircolor":
                            facialHairColor = XMLExtensions.ParseColor(s[1]);
                            break;
                        
                        case "skincolor":
                            skinColor = XMLExtensions.ParseColor(s[1]);
                            break;
                            
                        case "gender":
                            Enum.TryParse(s[1], ignoreCase: true, out disguisedGender);
                            break;

                        case "race":
                            Enum.TryParse(s[1], ignoreCase: true, out disguisedRace);
                            break;

                        case "headspriteid":
                            int.TryParse(s[1], NumberStyles.Any, CultureInfo.InvariantCulture, out disguisedHeadSpriteId);
                            break;

                        case "hairindex":
                            disguisedHairIndex = int.Parse(s[1]);
                            break;

                        case "beardindex":
                            disguisedBeardIndex = int.Parse(s[1]);
                            break;

                        case "moustacheindex":
                            disguisedMoustacheIndex = int.Parse(s[1]);
                            break;

                        case "faceattachmentindex":
                            disguisedFaceAttachmentIndex = int.Parse(s[1]);
                            break;

                        case "sheetindex":
                            string[] vectorValues = s[1].Split(";");
                            SheetIndex = new Vector2(float.Parse(vectorValues[0]), float.Parse(vectorValues[1]));
                            break;
                    }
                }

                if ((characterInfo.HasGenders && disguisedGender == Gender.None)
                    || (characterInfo.HasRaces && disguisedRace == Race.None)
                    || disguisedHeadSpriteId <= 0)
                {
                    Portrait = null;
                    Attachments = null;
                    return;
                }

                foreach (XElement limbElement in characterInfo.Ragdoll.MainElement.Elements())
                {
                    if (!limbElement.GetAttributeString("type", "").Equals("head", StringComparison.OrdinalIgnoreCase)) { continue; }

                    XElement spriteElement = limbElement.Element("sprite");
                    if (spriteElement == null) { continue; }

                    string spritePath = spriteElement.Attribute("texture").Value;

                    spritePath = spritePath.Replace("[GENDER]", disguisedGender.ToString().ToLowerInvariant());
                    spritePath = spritePath.Replace("[RACE]", disguisedRace.ToString().ToLowerInvariant());
                    spritePath = spritePath.Replace("[HEADID]", disguisedHeadSpriteId.ToString());

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
                    float baldnessChance = disguisedGender == Gender.Female ? 0.05f : 0.2f;

                    List<XElement> createElementList(WearableType wearableType, float emptyCommonness = 1.0f)
                        => CharacterInfo.AddEmpty(
                            characterInfo.FilterByTypeAndHeadID(
                                characterInfo.FilterElementsByGenderAndRace(characterInfo.Wearables, disguisedGender, disguisedRace),
                                wearableType, disguisedHeadSpriteId),
                            wearableType, emptyCommonness);

                    var disguisedHairs = createElementList(WearableType.Hair, baldnessChance);
                    var disguisedBeards = createElementList(WearableType.Beard);
                    var disguisedMoustaches = createElementList(WearableType.Moustache);
                    var disguisedFaceAttachments = createElementList(WearableType.FaceAttachment);

                    XElement getElementFromList(List<XElement> list, int index)
                        => CharacterInfo.IsValidIndex(index, list)
                            ? list[index]
                            : characterInfo.GetRandomElement(list);
                    
                    var disguisedHairElement = getElementFromList(disguisedHairs, disguisedHairIndex);
                    var disguisedBeardElement = getElementFromList(disguisedBeards, disguisedBeardIndex);
                    var disguisedMoustacheElement = getElementFromList(disguisedMoustaches, disguisedMoustacheIndex);
                    var disguisedFaceAttachmentElement = getElementFromList(disguisedFaceAttachments, disguisedFaceAttachmentIndex);
                    
                    Attachments = new List<WearableSprite>();

                    void loadAttachments(List<WearableSprite> attachments, XElement element, WearableType wearableType)
                    {
                        foreach (var s in element?.Elements("sprite") ?? Enumerable.Empty<XElement>())
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
                            ? JobPrefab.NoJobElement?.Element("PortraitClothing")
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

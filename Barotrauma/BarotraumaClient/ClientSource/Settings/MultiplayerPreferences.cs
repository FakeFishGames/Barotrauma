#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MultiplayerPreferences
    {
        public readonly struct JobPreference
        {
            public JobPreference(Identifier jobIdentifier, int variant)
            {
                JobIdentifier = jobIdentifier;
                Variant = variant;
            }
            
            public JobPreference(XElement element) : this(
                element.GetAttributeIdentifier("identifier", Identifier.Empty),
                element.GetAttributeInt("variant", -1)) { }
            
            public readonly Identifier JobIdentifier;
            public readonly int Variant;

            public static bool operator ==(JobPreference a, JobPreference b)
                => a.JobIdentifier == b.JobIdentifier && a.Variant == b.Variant;

            public static bool operator !=(JobPreference a, JobPreference b) => !(a == b);

            public override bool Equals(object? obj)
                => obj is JobPreference jp && jp == this;

            public bool Equals(JobPreference other) => other == this;

            public override int GetHashCode() => HashCode.Combine(JobIdentifier, Variant);
        }

        public readonly List<JobPreference> JobPreferences = new List<JobPreference>();
        public CharacterTeamType TeamPreference;
        public string PlayerName = string.Empty;

        public readonly HashSet<Identifier> TagSet = new HashSet<Identifier>();
        public int HairIndex = -1;
        public int BeardIndex = -1;
        public int MoustacheIndex = -1;
        public int FaceAttachmentIndex = -1;
        public Color HairColor = Color.Black;
        public Color FacialHairColor = Color.Black;
        public Color SkinColor = Color.Black;

        public static MultiplayerPreferences Instance { get; private set; } = new MultiplayerPreferences();

        private MultiplayerPreferences() { }

        private MultiplayerPreferences(IEnumerable<XElement> elements)
        {
            foreach (var element in elements)
            {
                PlayerName = element.GetAttributeString("name", PlayerName);
                
                TagSet.UnionWith(element.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()));
                HairIndex = element.GetAttributeInt(nameof(HairIndex), HairIndex);
                BeardIndex = element.GetAttributeInt(nameof(BeardIndex), BeardIndex);
                MoustacheIndex = element.GetAttributeInt(nameof(MoustacheIndex), MoustacheIndex);
                FaceAttachmentIndex = element.GetAttributeInt(nameof(FaceAttachmentIndex), FaceAttachmentIndex);

                HairColor = element.GetAttributeColor(nameof(HairColor), HairColor);
                FacialHairColor = element.GetAttributeColor(nameof(FacialHairColor), FacialHairColor);
                SkinColor = element.GetAttributeColor(nameof(SkinColor), SkinColor);

                foreach (var subElement in element.GetChildElements("job"))
                {
                    JobPreferences.Add(new JobPreference(subElement));
                }
            }
        }
        
        public static void Init(params XElement?[] elements)
        {
            Instance = new MultiplayerPreferences(elements.Where(e => e != null)!);
        }

        public void SaveTo(XElement element)
        {
            element.SetAttributeValue("name", PlayerName);
            
            element.SetAttributeValue("tags", string.Join(",", TagSet));
            element.SetAttributeValue(nameof(HairIndex), HairIndex);
            element.SetAttributeValue(nameof(BeardIndex), BeardIndex);
            element.SetAttributeValue(nameof(MoustacheIndex), MoustacheIndex);
            element.SetAttributeValue(nameof(FaceAttachmentIndex), FaceAttachmentIndex);
            
            element.SetAttributeValue(nameof(HairColor), HairColor.ToStringHex());
            element.SetAttributeValue(nameof(FacialHairColor), FacialHairColor.ToStringHex());
            element.SetAttributeValue(nameof(SkinColor), SkinColor.ToStringHex());

            foreach (var jobPreference in JobPreferences)
            {
                element.Add(new XElement("job",
                    new XAttribute("identifier", jobPreference.JobIdentifier.Value),
                    new XAttribute("variant", jobPreference.Variant.ToString(CultureInfo.InvariantCulture))));
            }
        }
        
        public bool AreJobPreferencesEqual(IReadOnlyList<JobPreference> other)
            => JobPreferences.SequenceEqual(other);
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class StringTags
    {
        public HashSet<StringIdentifier> TagIdentifiers
        {
            get;
            private set;
        }

        public IEnumerable<string> ToStringEnumerable() => TagIdentifiers.Select(Tag => Tag.IdentifierString);
        public string[] ToStringArray() => ToStringEnumerable().ToArray();
        public bool HasTag(StringIdentifier Tag) => TagIdentifiers.Contains(Tag);
        public bool HasTag(string TagString)
        {
            DebugConsole.Log("Tagstring: " + TagString);
            return TagIdentifiers.Select(Tag => Tag.IdentifierString).Contains(TagString);
        }
        public bool HasAnyTag(IEnumerable<StringIdentifier> Tags) => Tags.Any(Tag => HasTag(Tag));
        public bool HasAnyTag(IEnumerable<string> TagsStrings) => TagsStrings.Any(Tag => HasTag(Tag));

        public void AddTag(StringIdentifier Tag) => TagIdentifiers.Add(Tag);
        public void AddTag(string TagString) => AddTag(new StringIdentifier(TagString));
        public void RemoveTag(StringIdentifier Tag)
        {
            if (!HasTag(Tag)) return;

            TagIdentifiers.Remove(Tag);
        }

        public void RemoveTag(string TagString) => RemoveTag(new StringIdentifier(TagString));

        public void ReplaceTag(string OldTag, string NewTag) => ReplaceTag(new StringIdentifier(OldTag), new StringIdentifier(NewTag));
        public void ReplaceTag(StringIdentifier OldTag, string NewTag) => ReplaceTag(OldTag, new StringIdentifier(NewTag));
        public void ReplaceTag(string OldTag, StringIdentifier NewTag) => ReplaceTag(new StringIdentifier(OldTag), NewTag);
        public void ReplaceTag(StringIdentifier OldTag, StringIdentifier NewTag)
        {
            if (!HasTag(OldTag)) return;

            RemoveTag(OldTag);
            AddTag(NewTag);
        }

        public StringTags PrefabTags;
        public string AllTagsString
        {
            get => string.Join(",", TagIdentifiers.Select(TagId => TagId.IdentifierString));
            set
            {
                Reset();

                if (PrefabTags != null)
                {
                    TagIdentifiers = PrefabTags.TagIdentifiers.CreateCopy();
                }

                if (string.IsNullOrWhiteSpace(value)) return;

                string[] SplitTags = value.Split(',');

                foreach (string TagString in SplitTags)
                {
                    string[] splitTag = TagString.Trim().Split(':');
                    splitTag[0] = splitTag[0].ToLowerInvariant();
                    TagIdentifiers.Add(new StringIdentifier(string.Join(":", splitTag)));
                }
            }
        }
        public void Reset() => TagIdentifiers.Clear();

        public StringTags()
        {
            TagIdentifiers = new HashSet<StringIdentifier>();
        }
    }
}
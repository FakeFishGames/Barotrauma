#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class TagLString : LocalizedString
    {
        private readonly ImmutableArray<Identifier> tags;

        public TagLString(params Identifier[] tags)
        {
            this.tags = tags.ToImmutableArray();
        }

        private LoadedSuccessfully loadedSuccessfully = LoadedSuccessfully.Unknown;

        public override bool Loaded
        {
            get
            {
                if (loadedSuccessfully == LoadedSuccessfully.Unknown) { RetrieveValue(); }
                return loadedSuccessfully == LoadedSuccessfully.Yes;
            }
        }

        public override void RetrieveValue()
        {
            UpdateLanguage();

            (string value, bool loaded) tryLoad(LanguageIdentifier lang)
            {
                IReadOnlyList<string> candidates = Array.Empty<string>();
                int tagIndex = 0;
            
                if (TextManager.TextPacks.TryGetValue(lang, out var packs))
                {
                    while (candidates.Count == 0 && tagIndex < tags.Length)
                    {
                        foreach (var pack in packs)
                        {
                            if (pack.Texts.TryGetValue(tags[tagIndex], out var texts))
                            {
                                candidates = candidates.ListConcat(texts);
                            }
                        }
                        tagIndex++;
                    }
                }

                bool loaded = candidates.Count > 0;
                return (loaded ? candidates.GetRandomUnsynced() : "", loaded);
            }

            var (value, loaded) = tryLoad(Language);
            loadedSuccessfully = loaded ? LoadedSuccessfully.Yes : LoadedSuccessfully.No;
            cachedValue = value;
            if (!loaded && Language != TextManager.DefaultLanguage)
            {
                (value, _) = tryLoad(TextManager.DefaultLanguage);
                cachedValue = value;
                //Notice how we don't set loadedSuccessfully again here.
                //This is by design; falling back to English means that
                //this text did NOT load successfully, so Loaded must
                //return false.
            }
        }
    }
}
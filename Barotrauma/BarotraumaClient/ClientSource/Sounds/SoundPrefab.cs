using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    public class TagNames : Attribute
    {
        public readonly ImmutableHashSet<Identifier> Names;

        public TagNames(params string[] names)
        {
            Names = names.Select(n => n.ToIdentifier()).ToImmutableHashSet();
        }
    }

    class SoundPrefab : Prefab
    {
        private class PrefabCollectionHandler
        {
            public readonly object Collection;
            public readonly MethodInfo AddMethod;
            public readonly MethodInfo RemoveMethod;
            public readonly MethodInfo SortAllMethod;
            public readonly MethodInfo AddOverrideFileMethod;
            public readonly MethodInfo RemoveOverrideFileMethod;

            public void Add(SoundPrefab p, bool isOverride)
            {
                AddMethod.Invoke(Collection, new object[] { p, isOverride });
            }

            public void Remove(SoundPrefab p)
            {
                RemoveMethod.Invoke(Collection, new object[] { p });
            }

            public void AddOverrideFile(ContentFile file)
            {
                AddOverrideFileMethod.Invoke(Collection, new object[] { file });
            }

            public void RemoveOverrideFile(ContentFile file)
            {
                RemoveOverrideFileMethod.Invoke(Collection, new object[] { file });
            }

            public void SortAll()
            {
                SortAllMethod.Invoke(Collection, null);
            }

            public PrefabCollectionHandler(Type type)
            {
                var collectionField = type.GetField($"{type.Name}Prefabs", BindingFlags.Public | BindingFlags.Static);
                if (collectionField is null) { throw new InvalidOperationException($"Couldn't determine PrefabCollection for {type.Name}"); }
                Collection = collectionField.GetValue(null) ?? throw new InvalidOperationException($"PrefabCollection for {type.Name} was null");
                AddMethod = Collection.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                RemoveMethod = Collection.GetType().GetMethod("Remove", BindingFlags.Public | BindingFlags.Instance);
                AddOverrideFileMethod = Collection.GetType().GetMethod("AddOverrideFile", BindingFlags.Public | BindingFlags.Instance);
                RemoveOverrideFileMethod = Collection.GetType().GetMethod("RemoveOverrideFile", BindingFlags.Public | BindingFlags.Instance);
                SortAllMethod = Collection.GetType().GetMethod("SortAll", BindingFlags.Public | BindingFlags.Instance);
            }
        }

        public readonly static PrefabSelector<SoundPrefab> WaterAmbienceIn = new PrefabSelector<SoundPrefab>();
        public readonly static PrefabSelector<SoundPrefab> WaterAmbienceOut = new PrefabSelector<SoundPrefab>();
        public readonly static PrefabSelector<SoundPrefab> WaterAmbienceMoving = new PrefabSelector<SoundPrefab>();
        public readonly static PrefabSelector<SoundPrefab> StartupSound = new PrefabSelector<SoundPrefab>();

        private readonly static List<SoundPrefab> flowSounds = new List<SoundPrefab>();
        public static IReadOnlyList<SoundPrefab> FlowSounds => flowSounds;
        private readonly static List<SoundPrefab> splashSounds = new List<SoundPrefab>();
        public static IReadOnlyList<SoundPrefab> SplashSounds => splashSounds;

        public readonly static ImmutableDictionary<Identifier, Type> TagToDerivedPrefab;
        private readonly static ImmutableDictionary<Type, PrefabCollectionHandler> derivedPrefabCollections;
        private readonly static ImmutableDictionary<Identifier, PrefabSelector<SoundPrefab>> prefabSelectors;
        private readonly static ImmutableDictionary<Identifier, List<SoundPrefab>> prefabsWithTag;
        public readonly static PrefabCollection<SoundPrefab> Prefabs;

        static SoundPrefab()
        {
            var types = ReflectionUtils.GetDerivedNonAbstract<SoundPrefab>();
            //types.ForEach(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static));
            TagToDerivedPrefab = types.SelectMany(t =>
                t.GetCustomAttribute<TagNames>().Names.Select(n => (n, t))).ToImmutableDictionary();
            derivedPrefabCollections = types.Select(t => (t, new PrefabCollectionHandler(t))).ToImmutableDictionary();

            var prefabSelectorFields = typeof(SoundPrefab).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(PrefabSelector<SoundPrefab>));
            prefabSelectors = prefabSelectorFields.Select(f => (f.Name.ToIdentifier(), (PrefabSelector<SoundPrefab>)f.GetValue(null))).ToImmutableDictionary();

            var prefabsOfTagName = typeof(SoundPrefab).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(List<SoundPrefab>));
            prefabsWithTag = prefabsOfTagName.Select(f => (f.Name.Substring(0, f.Name.Length-6).ToIdentifier(), (List<SoundPrefab>)f.GetValue(null))).ToImmutableDictionary();

            Prefabs = new PrefabCollection<SoundPrefab>(
                onAdd: (SoundPrefab p, bool isOverride) =>
                {
                    if (derivedPrefabCollections.ContainsKey(p.GetType()))
                    {
                        derivedPrefabCollections[p.GetType()].Add(p, isOverride);
                    }
                    if (prefabSelectors.ContainsKey(p.ElementName)) { prefabSelectors[p.ElementName].Add(p, isOverride); }
                    UpdateSoundsWithTag();
                },
                onRemove: (SoundPrefab p) =>
                {
                    if (derivedPrefabCollections.ContainsKey(p.GetType()))
                    {
                        derivedPrefabCollections[p.GetType()].Remove(p);
                    }
                    if (prefabSelectors.ContainsKey(p.ElementName)) { prefabSelectors[p.ElementName].RemoveIfContains(p); }
                    UpdateSoundsWithTag();
                    SoundPlayer.DisposeDisabledMusic();
                },
                onSort: () =>
                {
                    derivedPrefabCollections.Values.ForEach(h => h.SortAll());
                    prefabSelectors.Values.ForEach(h => h.Sort());
                },
                onAddOverrideFile: (file) => {derivedPrefabCollections.Values.ForEach(h => h.AddOverrideFile(file)); },
                onRemoveOverrideFile: (file) => { derivedPrefabCollections.Values.ForEach(h => h.RemoveOverrideFile(file)); }
            );
        }

        private static void UpdateSoundsWithTag()
        {
            foreach (var tag in prefabsWithTag.Keys)
            {
                var list = prefabsWithTag[tag];
                list.Clear();
                list.AddRange(Prefabs.Where(p => p.ElementName == tag));
                list.Sort((p1, p2) =>
                {
                    if (p1.ContentFile.ContentPackage.Index < p2.ContentFile.ContentPackage.Index) { return -1; }
                    if (p1.ContentFile.ContentPackage.Index > p2.ContentFile.ContentPackage.Index) { return 1; }
                    if (p2.Element.ComesAfter(p1.Element)) { return -1; }
                    if (p1.Element.ComesAfter(p2.Element)) { return 1; }
                    return 0;
                });
            }
        }

        protected override Identifier DetermineIdentifier(XElement element)
        {
            Identifier id = base.DetermineIdentifier(element);
            if (id.IsEmpty)
            {
                if (id.IsEmpty) { id = Path.GetFileNameWithoutExtension(element.GetAttributeStringUnrestricted("path", "")).ToIdentifier(); }
                if (id.IsEmpty) { id = Path.GetFileNameWithoutExtension(element.GetAttributeStringUnrestricted("file", "")).ToIdentifier(); }

                if (!id.IsEmpty)
                {
                    id = $"{element.Name}_{id}".ToIdentifier();
                    
                    string damageSoundType = element.GetAttributeString("damagesoundtype", "");
                    if (!damageSoundType.IsNullOrEmpty())
                    {
                        id = $"{id}_{damageSoundType}".ToIdentifier();
                    }
                    
                    string musicType = element.GetAttributeString("type", "");
                    if (!musicType.IsNullOrEmpty())
                    {
                        id = $"{id}_{musicType}".ToIdentifier();
                    }
                }
            }
            
            return id;
        }

        public readonly ContentPath SoundPath;
        public readonly ContentXElement Element;
        public readonly Identifier ElementName;
        public Sound Sound { get; private set; }

        public SoundPrefab(ContentXElement element, SoundsFile file, bool stream = false) : base(file, element)
        {
            SoundPath = element.GetAttributeContentPath("file") ?? ContentPath.Empty;
            Element = element;
            ElementName = element.NameAsIdentifier();
            Sound = GameMain.SoundManager.LoadSound(element, stream: stream);
        }

        public bool IsPlaying()
        {
            return Sound.IsPlaying();
        }

        public override void Dispose()
        {
            Sound?.Dispose(); Sound = null;
        }
    }

    [TagNames("damagesound")]
    class DamageSound : SoundPrefab
    {
        public readonly static PrefabCollection<DamageSound> DamageSoundPrefabs = new PrefabCollection<DamageSound>();

        //the range of inflicted damage where the sound can be played
        //(10.0f, 30.0f) would be played when the inflicted damage is between 10 and 30
        public readonly Vector2 DamageRange;

        public readonly Identifier DamageType;

        public readonly Identifier RequiredTag;

        public bool IgnoreMuffling;

        public DamageSound(ContentXElement element, SoundsFile file) : base(element, file)
        {
            DamageRange = element.GetAttributeVector2("damagerange", Vector2.Zero);
            DamageType = element.GetAttributeIdentifier("damagesoundtype", "None");
            IgnoreMuffling = element.GetAttributeBool("ignoremuffling", false);
            RequiredTag = element.GetAttributeIdentifier("requiredtag", "");
        }
    }

    [TagNames("music")]
    class BackgroundMusic : SoundPrefab
    {
        public readonly static PrefabCollection<BackgroundMusic> BackgroundMusicPrefabs = new PrefabCollection<BackgroundMusic>();

        public readonly Identifier Type;
        public readonly bool DuckVolume;
        public readonly float Volume;

        public readonly Vector2 IntensityRange;

        public readonly bool ContinueFromPreviousTime;
        public readonly bool MuteIntensityMusic;
        public readonly float MinimumRequiredTimeToPlay;
        public int PreviousTime;

        public BackgroundMusic(ContentXElement element, SoundsFile file) : base(element, file, stream: true)
        {
            Type = element.GetAttributeIdentifier("type", "");
            IntensityRange = element.GetAttributeVector2("intensityrange", new Vector2(0.0f, 100.0f));
            DuckVolume = element.GetAttributeBool("duckvolume", false);
            this.Volume = element.GetAttributeFloat("volume", 1.0f);
            ContinueFromPreviousTime = element.GetAttributeBool("continuefromprevioustime", false);
            MuteIntensityMusic = element.GetAttributeBool("muteintensitymusic", false);
            MinimumRequiredTimeToPlay = element.GetAttributeFloat("minimumrequiredtimetoplay", 30.0f);
        }
    }

    [TagNames("guisound")]
    class GUISound : SoundPrefab
    {
        //public readonly static Dictionary<GUISoundType, List<GUISound>> GUISoundsByType = new Dictionary<GUISoundType, List<GUISound>>();
        public readonly static PrefabCollection<GUISound> GUISoundPrefabs = new PrefabCollection<GUISound>();

        public readonly GUISoundType Type;

        public GUISound(ContentXElement element, SoundsFile file) : base(element, file)
        {
            Type = element.GetAttributeEnum("guisoundtype", GUISoundType.UIMessage);
        }
    }
}

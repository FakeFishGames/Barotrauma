﻿#nullable enable
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public enum FactionAffiliation
    {
        Affiliated,
        Neutral
    }

    class Faction
    {
        public Reputation Reputation { get; }
        public FactionPrefab Prefab { get; }

        public Faction(CampaignMetadata metadata, FactionPrefab prefab)
        {
            Prefab = prefab;
            Reputation = new Reputation(metadata, this, prefab.MinReputation, prefab.MaxReputation, prefab.InitialReputation);
        }

        /// <summary>
        /// Get what kind of affiliation this faction has towards the player depending on who they chose to side with via talents
        /// </summary>
        /// <returns></returns>
        public FactionAffiliation GetPlayerAffiliationStatus()
        {
            float affiliation = 1f;
            foreach (Character character in GameSession.GetSessionCrewCharacters(CharacterType.Both))
            {
                if (character.Info is not { } info) { continue; }

                affiliation *= 1f + info.GetSavedStatValue(StatTypes.Affiliation, Prefab.Identifier);
            }

            return affiliation switch
            {
                >= 1f => FactionAffiliation.Affiliated,
                _ => FactionAffiliation.Neutral
            };
        }
    }

    internal class FactionPrefab : Prefab
    {
        public readonly static PrefabCollection<FactionPrefab> Prefabs = new PrefabCollection<FactionPrefab>();

        public LocalizedString Name { get; }

        public LocalizedString Description { get; }
        public LocalizedString ShortDescription { get; }

        public int MenuOrder { get; }

        /// <summary>
        /// How low the reputation can drop on this faction
        /// </summary>
        public int MinReputation { get; }

        /// <summary>
        /// Maximum reputation level you can gain on this faction
        /// </summary>
        public int MaxReputation { get; }

        /// <summary>
        /// What reputation does this faction start with
        /// </summary>
        public int InitialReputation { get; }

#if CLIENT
        public Sprite? Icon { get; private set; }

        public Sprite? BackgroundPortrait { get; private set; }

        public Color IconColor { get; }
#endif

        public FactionPrefab(ContentXElement element, FactionsFile file) : base(file, element.GetAttributeIdentifier("identifier", string.Empty))
        {
            MenuOrder = element.GetAttributeInt("menuorder", 0);
            MinReputation = element.GetAttributeInt("minreputation", -100);
            MaxReputation = element.GetAttributeInt("maxreputation", 100);
            InitialReputation = element.GetAttributeInt("initialreputation", 0);
            Name = element.GetAttributeString("name", null) ?? TextManager.Get($"faction.{Identifier}").Fallback("Unnamed");
            Description = element.GetAttributeString("description", null) ?? TextManager.Get($"faction.{Identifier}.description").Fallback("");
            ShortDescription = element.GetAttributeString("shortdescription", null) ?? TextManager.Get($"faction.{Identifier}.shortdescription").Fallback("");
#if CLIENT
            foreach (var subElement in element.Elements())
            {

                if (subElement.Name.ToString().Equals("icon", StringComparison.OrdinalIgnoreCase))
                {
                    IconColor = subElement.GetAttributeColor("color", Color.White);
                    Icon = new Sprite(subElement);
                }
                else if (subElement.Name.ToString().Equals("portrait", StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundPortrait = new Sprite(subElement);
                }
            }
#endif
        }

        public override void Dispose()
        {
#if CLIENT
            Icon?.Remove();
            Icon = null;
#endif
        }
    }
}
﻿using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Ladder : ItemComponent
    {
        public static List<Ladder> List { get; } = new List<Ladder>();

        public Ladder(Item item, ContentXElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
            List.Add(this);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override bool Select(Character character)
        {
            if (character == null || character.LockHands || character.Removed ) { return false; }
            if (!character.CanClimb) { return false; }
            character.AnimController.StartClimbing();
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            RemoveProjSpecific();
            List.Remove(this);
        }

        partial void RemoveProjSpecific();
    }
}

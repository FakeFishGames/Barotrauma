using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class OutpostModuleInfo : ISerializableEntity
    {
        [Flags]
        public enum GapPosition
        {
            None = 0,
            Right = 1,
            Left = 2,
            Top = 4,
            Bottom = 8
        }

        private readonly HashSet<Identifier> moduleFlags = new HashSet<Identifier>();
        public IEnumerable<Identifier> ModuleFlags
        {
            get { return moduleFlags; }
        }

        private readonly HashSet<Identifier> allowAttachToModules = new HashSet<Identifier>();
        public IEnumerable<Identifier> AllowAttachToModules
        {
            get { return allowAttachToModules; }
        }

        private readonly HashSet<Identifier> allowedLocationTypes = new HashSet<Identifier>();
        public IEnumerable<Identifier> AllowedLocationTypes
        {
            get { return allowedLocationTypes; }
        }

        [Serialize(100, IsPropertySaveable.Yes, description: "How many instances of this module can be used in one outpost."), Editable]
        public int MaxCount { get; set; }

        [Serialize(10.0f, IsPropertySaveable.Yes, description: "How likely it is for the module to get picked when selecting from a set of modules during the outpost generation."), Editable]
        public float Commonness { get; set; }

        [Serialize(GapPosition.None, IsPropertySaveable.Yes, description: "Which sides of the module have gaps on them (i.e. from which sides the module can be attached to other modules). Center = no gaps available.")]
        public GapPosition GapPositions { get; set; }

        [Serialize(GapPosition.Right | GapPosition.Left | GapPosition.Bottom | GapPosition.Top, IsPropertySaveable.Yes, description: "Which sides of this module are allowed to attach to the previously placed module. E.g. if you want a module to always attach to the left side of the docking module, you could set this to Right.")]
        public GapPosition CanAttachToPrevious { get; set; }

        public string Name { get; private set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        public OutpostModuleInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"OutpostModuleInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            SetFlags(
                element.GetAttributeIdentifierArray("flags", null) ?? 
                element.GetAttributeIdentifierArray("moduletypes", Array.Empty<Identifier>()));
            SetAllowAttachTo(element.GetAttributeIdentifierArray("allowattachto", Array.Empty<Identifier>()));
            allowedLocationTypes = new HashSet<Identifier>(element.GetAttributeIdentifierArray("allowedlocationtypes", Array.Empty<Identifier>()));
        }

        public OutpostModuleInfo(SubmarineInfo submarineInfo)
        {
            Name = $"OutpostModuleInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
        }
        public OutpostModuleInfo(OutpostModuleInfo original)
        {
            Name = original.Name;
            moduleFlags = new HashSet<Identifier>(original.moduleFlags);
            allowAttachToModules = new HashSet<Identifier>(original.allowAttachToModules);
            allowedLocationTypes = new HashSet<Identifier>(original.allowedLocationTypes);
            SerializableProperties = new Dictionary<Identifier, SerializableProperty>();
            GapPositions = original.GapPositions;
            foreach (KeyValuePair<Identifier, SerializableProperty> kvp in original.SerializableProperties)
            {
                SerializableProperties.Add(kvp.Key, kvp.Value);
                if (SerializableProperty.GetSupportedTypeName(kvp.Value.PropertyType) != null)
                {
                    kvp.Value.TrySetValue(this, kvp.Value.GetValue(original));
                }
            }
        }

        public void SetFlags(IEnumerable<Identifier> newFlags)
        {
            moduleFlags.Clear();
            if (newFlags.Contains("hallwayhorizontal".ToIdentifier()))
            {
                moduleFlags.Add("hallwayhorizontal".ToIdentifier());
                if (newFlags.Contains("ruin".ToIdentifier())) { moduleFlags.Add("ruin".ToIdentifier()); }
            }
            if (newFlags.Contains("hallwayvertical".ToIdentifier()))
            {
                moduleFlags.Add("hallwayvertical".ToIdentifier());
                if (newFlags.Contains("ruin".ToIdentifier())) { moduleFlags.Add("ruin".ToIdentifier()); }
            }
            if (!newFlags.Any())
            {
                moduleFlags.Add("none".ToIdentifier());
            }
            foreach (Identifier flag in newFlags)
            {
                if (flag == "none" && newFlags.Count() > 1) { continue; }
                moduleFlags.Add(flag);
            }
        }
        public void SetAllowAttachTo(IEnumerable<Identifier> allowAttachTo)
        {
            allowAttachToModules.Clear();
            if (!allowAttachTo.Any())
            {
                allowAttachToModules.Add("any".ToIdentifier());
            }
            foreach (Identifier flag in allowAttachTo)
            {
                if (flag == "any" && allowAttachTo.Count() > 1) { continue; }
                allowAttachToModules.Add(flag);
            }
        }

        public void SetAllowedLocationTypes(IEnumerable<Identifier> allowedLocationTypes)
        {
            this.allowedLocationTypes.Clear();
            foreach (Identifier locationType in allowedLocationTypes)
            {
                if (locationType == "any") { continue; }
                this.allowedLocationTypes.Add(locationType);
            }
        }

        public void DetermineGapPositions(Submarine sub)
        {
            GapPositions = GapPosition.None;
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Submarine != sub || gap.linkedTo.Count != 1) { continue; }
                if (gap.ConnectedDoor != null && !gap.ConnectedDoor.UseBetweenOutpostModules) { continue; }

                //ignore gaps that are at a docking port
                bool portFound = false;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (Submarine.RectContains(gap.WorldRect, port.Item.WorldPosition))
                    {
                        portFound = true;
                        break;
                    }
                }
                if (portFound) { continue; }

                GapPositions |= gap.IsHorizontal ?
                    gap.linkedTo[0].WorldPosition.X < gap.WorldPosition.X ? GapPosition.Right : GapPosition.Left : 
                    gap.linkedTo[0].WorldPosition.Y < gap.WorldPosition.Y ? GapPosition.Top : GapPosition.Bottom;
            }
        }

        public void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
            element.SetAttributeValue("flags", string.Join(",", ModuleFlags));
            element.SetAttributeValue("allowattachto", string.Join(",", AllowAttachToModules));
            element.SetAttributeValue("allowedlocationtypes", string.Join(",", AllowedLocationTypes));
        }
    }
}

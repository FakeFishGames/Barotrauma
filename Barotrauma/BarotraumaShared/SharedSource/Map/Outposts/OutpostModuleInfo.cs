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

        private readonly HashSet<string> moduleFlags = new HashSet<string>();
        public IEnumerable<string> ModuleFlags
        {
            get { return moduleFlags; }
        }

        private readonly HashSet<string> allowAttachToModules = new HashSet<string>();
        public IEnumerable<string> AllowAttachToModules
        {
            get { return allowAttachToModules; }
        }

        private readonly HashSet<string> allowedLocationTypes = new HashSet<string>();
        public IEnumerable<string> AllowedLocationTypes
        {
            get { return allowedLocationTypes; }
        }

        [Serialize(100, isSaveable: true, description: "How many instances of this module can be used in one outpost."), Editable]
        public int MaxCount { get; set; }

        [Serialize(10.0f, isSaveable: true, description: "How likely it is for the module to get picked when selecting from a set of modules during the outpost generation."), Editable]
        public float Commonness { get; set; }

        [Serialize(GapPosition.None, isSaveable: true, description: "Which sides of the module have gaps on them (i.e. from which sides the module can be attached to other modules). Center = no gaps available.")]
        public GapPosition GapPositions { get; set; }

        public string Name { get; private set; }

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        public OutpostModuleInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"OutpostModuleInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            SetFlags(
                element.GetAttributeStringArray("flags", null, convertToLowerInvariant: true) ?? 
                element.GetAttributeStringArray("moduletypes", new string[0], convertToLowerInvariant: true));
            SetAllowAttachTo(element.GetAttributeStringArray("allowattachto", new string[0], convertToLowerInvariant: true));
            allowedLocationTypes = new HashSet<string>(element.GetAttributeStringArray("allowedlocationtypes", new string[0], convertToLowerInvariant: true));
        }

        public OutpostModuleInfo(SubmarineInfo submarineInfo)
        {
            Name = $"OutpostModuleInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
        }
        public OutpostModuleInfo(OutpostModuleInfo original)
        {
            Name = original.Name;
            moduleFlags = new HashSet<string>(original.moduleFlags);
            allowAttachToModules = new HashSet<string>(original.allowAttachToModules);
            allowedLocationTypes = new HashSet<string>(original.allowedLocationTypes);
            SerializableProperties = new Dictionary<string, SerializableProperty>();
            GapPositions = original.GapPositions;
            foreach (KeyValuePair<string, SerializableProperty> kvp in original.SerializableProperties)
            {
                SerializableProperties.Add(kvp.Key, kvp.Value);
                if (SerializableProperty.GetSupportedTypeName(kvp.Value.PropertyType) != null)
                {
                    kvp.Value.TrySetValue(this, kvp.Value.GetValue(original));
                }
            }
        }

        public void SetFlags(IEnumerable<string> newFlags)
        {
            moduleFlags.Clear();
            if (!newFlags.Any())
            {
                moduleFlags.Add("none");
            }
            foreach (string flag in newFlags)
            {
                if (flag == "none" && newFlags.Count() > 1) { continue; }
                moduleFlags.Add(flag.ToLowerInvariant());
            }
        }
        public void SetAllowAttachTo(IEnumerable<string> allowAttachTo)
        {
            allowAttachToModules.Clear();
            if (!allowAttachTo.Any())
            {
                allowAttachToModules.Add("any");
            }
            foreach (string flag in allowAttachTo)
            {
                if (flag == "any" && allowAttachTo.Count() > 1) { continue; }
                allowAttachToModules.Add(flag);
            }
        }

        public void SetAllowedLocationTypes(IEnumerable<string> allowedLocationTypes)
        {
            this.allowedLocationTypes.Clear();
            foreach (string locationType in allowedLocationTypes)
            {
                if (locationType.Equals("any", StringComparison.OrdinalIgnoreCase)) { continue; }
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

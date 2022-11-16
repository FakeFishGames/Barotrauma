using System;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent, IClientSerializable, IServerSerializable
    {
        private readonly struct EventData : IEventData
        {
            public readonly CustomInterfaceElement BtnElement;
            
            public EventData(CustomInterfaceElement btnElement)
            {
                BtnElement = btnElement;
            }
        }
        
        class CustomInterfaceElement : ISerializableEntity
        {
            public bool ContinuousSignal;
            public bool State;
            public string ConnectionName;
            public Connection Connection;

            [Serialize("", IsPropertySaveable.No, translationTextTag: "Label.", description: "The text displayed on this button/tickbox."), Editable]
            public string Label { get; set; }

            [Serialize("1", IsPropertySaveable.No, description: "The signal sent out when this button is pressed or this tickbox checked."), Editable]
            public string Signal { get; set; }

            public Identifier PropertyName { get; }
            public bool TargetOnlyParentProperty { get; }

            public string NumberInputMin { get; }
            public string NumberInputMax { get; }
            public string NumberInputStep { get; }
            public int NumberInputDecimalPlaces { get; }

            public int MaxTextLength { get; }

            public const string DefaultNumberInputMin = "0", DefaultNumberInputMax = "99", DefaultNumberInputStep = "1";
            public const int DefaultNumberInputDecimalPlaces = 0;
            public bool IsNumberInput { get; }
            public NumberType? NumberType { get; }
            public bool HasPropertyName { get; }
            public bool ShouldSetProperty { get; set; }

            public string Name => "CustomInterfaceElement";

            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            public List<StatusEffect> StatusEffects = new List<StatusEffect>();

            /// <summary>
            /// Pass the parent component to the constructor to access the serializable properties
            /// for elements which change property values.
            /// </summary>
            public CustomInterfaceElement(Item item, ContentXElement element, CustomInterface parent)
            {
                Label = element.GetAttributeString("text", "");
                ConnectionName = element.GetAttributeString("connection", "");
                PropertyName = element.GetAttributeIdentifier("propertyname", "");
                TargetOnlyParentProperty = element.GetAttributeBool("targetonlyparentproperty", false);
                NumberInputMin = element.GetAttributeString("min", DefaultNumberInputMin);
                NumberInputMax = element.GetAttributeString("max", DefaultNumberInputMax);
                NumberInputStep = element.GetAttributeString("step", DefaultNumberInputStep);
                NumberInputDecimalPlaces = element.GetAttributeInt("decimalplaces", DefaultNumberInputDecimalPlaces);
                MaxTextLength = element.GetAttributeInt("maxtextlength", int.MaxValue);

                HasPropertyName = !PropertyName.IsEmpty;
                if (HasPropertyName)
                {
                    string elementName = element.Name.ToString().ToLowerInvariant();
                    IsNumberInput = elementName == "numberinput" || elementName == "integerinput"; // backwards compatibility
                    if (IsNumberInput)
                    {
                        string numberType = element.GetAttributeString("numbertype", string.Empty);
                        switch (numberType)
                        {
                            case "f":
                            case "float":
                                NumberType = Barotrauma.NumberType.Float;
                                break;
                            case "int":
                            case "integer":
                            default:  // backwards compatibility
                                NumberType = Barotrauma.NumberType.Int;
                                break;
                        }
                    }
                }

                if (element.GetAttribute("signal") is XAttribute attribute)
                {
                    Signal = attribute.Value;
                    ShouldSetProperty = HasPropertyName;
                }
                else if (HasPropertyName && parent != null)
                {
                    if (TargetOnlyParentProperty)
                    {
                        if (parent.SerializableProperties.ContainsKey(PropertyName))
                        {
                            Signal = parent.SerializableProperties[PropertyName].GetValue(parent) as string;
                        }
                    }
                    else
                    {
                        foreach (ISerializableEntity e in parent.item.AllPropertyObjects)
                        {
                            if (!e.SerializableProperties.ContainsKey(PropertyName)) { continue; }
                            Signal = e.SerializableProperties[PropertyName].GetValue(e) as string;
                            break;
                        }
                    }
                }
                else
                {
                    Signal = "1";
                }

                foreach (var subElement in element.Elements())
                {
                    if (subElement.Name.ToString().Equals("statuseffect", System.StringComparison.OrdinalIgnoreCase))
                    {
                        StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName: "custom interface element (label " + Label + ")"));
                    }
                }
            }
        }

        private string[] labels;
        [Serialize("", IsPropertySaveable.Yes, description: "The texts displayed on the buttons/tickboxes, separated by commas.", alwaysUseInstanceValues: true)]
        public string Labels
        {
            get { return string.Join(",", labels); }
            set
            {
                if (value == null) { return; }
                if (customInterfaceElementList.Count > 0)
                {
                    string[] splitValues = value == "" ? Array.Empty<string>() : value.Split(',');
                    UpdateLabels(splitValues);
                }
            }
        }

        private string[] signals;
        [Serialize("", IsPropertySaveable.Yes, description: "The signals sent when the buttons are pressed or the tickboxes checked, separated by commas.", alwaysUseInstanceValues: true)]
        public string Signals
        {
            //use semicolon as a separator because comma may be needed in the signals (for color or vector values for example)
            //kind of hacky, we should probably add support for (string) arrays to SerializableEntityEditor so this wouldn't be needed
            get { return signals == null ? string.Empty : string.Join(";", signals); }
            set
            {
                if (value == null) { return; }
                if (customInterfaceElementList.Count > 0)
                {
                    string[] splitValues = value == "" ? Array.Empty<string>() : value.Split(';');
                    UpdateSignals(splitValues);
                }
            }
        }

        private bool[] elementStates;
        [Serialize("", IsPropertySaveable.Yes, description: "", alwaysUseInstanceValues: true)]
        public string ElementStates
        {
            get { return elementStates == null ? string.Empty : string.Join(",", elementStates); }
            set
            {
                if (value == null) { return; }
                if (customInterfaceElementList.Count > 0)
                {
                    string[] splitValues = value == "" ? Array.Empty<string>() : value.Split(',');
                    for (int i = 0; i < customInterfaceElementList.Count && i < splitValues.Length; i++)
                    {
                        if (!bool.TryParse(splitValues[i], out bool val)) { continue; }
                        customInterfaceElementList[i].State = val;
#if CLIENT
                        if (uiElements != null && i < uiElements.Count && uiElements[i] is GUITickBox tickBox)
                        {
                            tickBox.Selected = val;
                        }
#endif
                    }
                }
            }
        }

        private readonly List<CustomInterfaceElement> customInterfaceElementList = new List<CustomInterfaceElement>();
        
        public CustomInterface(Item item, ContentXElement element)
            : base(item, element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                    case "textbox":
                    case "integerinput": // backwards compatibility
                    case "numberinput":
                        var button = new CustomInterfaceElement(item, subElement, this)
                        {
                            ContinuousSignal = false
                        };
                        if (string.IsNullOrEmpty(button.Label))
                        {
                            button.Label = "Signal out " + customInterfaceElementList.Count(e => !e.ContinuousSignal);
                        }
                        customInterfaceElementList.Add(button);
                        break;
                    case "tickbox":
                        var tickBox = new CustomInterfaceElement(item, subElement, this)
                        {
                            ContinuousSignal = true
                        };
                        if (string.IsNullOrEmpty(tickBox.Label))
                        {
                            tickBox.Label = "Signal out " + customInterfaceElementList.Count(e => e.ContinuousSignal);
                        }
                        customInterfaceElementList.Add(tickBox);
                        break;
                }
            }
            IsActive = true;
            InitProjSpecific();
            //load these here to ensure the UI elements (created in InitProjSpecific) are up-to-date
            Labels = element.GetAttributeString("labels", "");
            Signals = element.GetAttributeString("signals", "");
            ElementStates = element.GetAttributeString("elementstates", "");
        }

        private void UpdateLabels(string[] newLabels)
        {
            labels = new string[customInterfaceElementList.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = i < newLabels.Length ? newLabels[i] : customInterfaceElementList[i].Label;
                if (Screen.Selected != GameMain.SubEditorScreen)
                {
                    customInterfaceElementList[i].Label = TextManager.Get(labels[i]).Fallback(labels[i]).Value;
                }
                else
                {
                    customInterfaceElementList[i].Label = labels[i];
                }
            }
            UpdateLabelsProjSpecific();
        }

        private void UpdateSignals(string[] newSignals)
        {
            signals = new string[customInterfaceElementList.Count];
            for (int i = 0; i < customInterfaceElementList.Count; i++)
            {
                var element = customInterfaceElementList[i];
                if (i < newSignals.Length)
                {
                    var newSignal = newSignals[i];
                    signals[i] = newSignal;
                    element.ShouldSetProperty = element.Signal != newSignal;
                    element.Signal = newSignal;
                }
                else
                {
                    signals[i] = element.Signal;
                }

                if (element.HasPropertyName && element.ShouldSetProperty)
                {
                    if (element.TargetOnlyParentProperty)
                    {
                        if (SerializableProperties.ContainsKey(element.PropertyName))
                        {
                            SerializableProperties[element.PropertyName].TrySetValue(this, element.Signal);
                        }
                    }
                    else
                    {
                        foreach (var po in item.AllPropertyObjects)
                        {
                            if (!po.SerializableProperties.ContainsKey(element.PropertyName)) { continue; }
                            po.SerializableProperties[element.PropertyName].TrySetValue(po, element.Signal);
                        }
                    }
                    customInterfaceElementList[i].ShouldSetProperty = false;
                }
            }
            UpdateSignalsProjSpecific();
        }

        public override void OnItemLoaded()
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                ciElement.Connection = item.Connections?.FirstOrDefault(c => c.Name == ciElement.ConnectionName);
            }
#if SERVER
            //make sure the clients know about the states of the checkboxes and text fields
            if (item.Submarine == null || !item.Submarine.Loading)
            {
                item.CreateServerEvent(this);
            }
#endif
        }

        partial void UpdateLabelsProjSpecific();

        partial void UpdateSignalsProjSpecific();

        partial void InitProjSpecific();     
        
        private void ButtonClicked(CustomInterfaceElement btnElement)
        {
            if (btnElement == null) return;
            if (btnElement.Connection != null)
            {
                item.SendSignal(new Signal(btnElement.Signal, 0, null, item), btnElement.Connection);
            }
            foreach (StatusEffect effect in btnElement.StatusEffects)
            {
                item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f);
            }
        }

        private void TickBoxToggled(CustomInterfaceElement tickBoxElement, bool state)
        {
            if (tickBoxElement == null) { return; }
            tickBoxElement.State = state;
        }

        private void TextChanged(CustomInterfaceElement textElement, string text)
        {
            if (textElement == null) { return; }
            textElement.Signal = text;
            if (!textElement.TargetOnlyParentProperty)
            {
                foreach (ISerializableEntity e in item.AllPropertyObjects)
                {
                    if (!e.SerializableProperties.ContainsKey(textElement.PropertyName)) { continue; }
                    e.SerializableProperties[textElement.PropertyName].TrySetValue(e, text);
                }
            }
            else if (SerializableProperties.ContainsKey(textElement.PropertyName))
            {
                SerializableProperties[textElement.PropertyName].TrySetValue(this, text);
            }
        }

        private void ValueChanged(CustomInterfaceElement numberInputElement, int value)
        {
            if (numberInputElement == null) { return; }
            numberInputElement.Signal = value.ToString();
            if (!numberInputElement.TargetOnlyParentProperty)
            {
                foreach (ISerializableEntity e in item.AllPropertyObjects)
                {
                    if (!e.SerializableProperties.ContainsKey(numberInputElement.PropertyName)) { continue; }
                    e.SerializableProperties[numberInputElement.PropertyName].TrySetValue(e, value);
                }
            }
            else if (SerializableProperties.ContainsKey(numberInputElement.PropertyName))
            {
                SerializableProperties[numberInputElement.PropertyName].TrySetValue(this, value);
            }
        }

        private void ValueChanged(CustomInterfaceElement numberInputElement, float value)
        {
            if (numberInputElement == null) { return; }
            numberInputElement.Signal = value.ToString();
            if (!numberInputElement.TargetOnlyParentProperty)
            {
                foreach (ISerializableEntity e in item.AllPropertyObjects)
                {
                    if (!e.SerializableProperties.ContainsKey(numberInputElement.PropertyName)) { continue; }
                    e.SerializableProperties[numberInputElement.PropertyName].TrySetValue(e, value);
                }
            }
            else if (SerializableProperties.ContainsKey(numberInputElement.PropertyName))
            {
                SerializableProperties[numberInputElement.PropertyName].TrySetValue(this, value);
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (!ciElement.ContinuousSignal) { continue; }
                //TODO: allow changing output when a tickbox is not selected
                if (!string.IsNullOrEmpty(ciElement.Signal) && ciElement.Connection != null)
                {
                    item.SendSignal(new Signal(ciElement.State ? ciElement.Signal : "0", source: item), ciElement.Connection);
                }

                foreach (StatusEffect effect in ciElement.StatusEffects)
                {
                    item.ApplyStatusEffect(effect, ciElement.State ? ActionType.OnUse : ActionType.OnSecondaryUse, 1.0f, null, null, null, true, false);
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            labels = customInterfaceElementList.Select(ci => ci.Label).ToArray();
            signals = customInterfaceElementList.Select(ci => ci.Signal).ToArray();
            elementStates = customInterfaceElementList.Select(ci => ci.State).ToArray();
            return base.Save(parentElement);
        }

        private static bool TryParseFloatInvariantCulture(string s, out float f)
        {
            return float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out f);
        }
    }
}

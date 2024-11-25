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
            public enum InputTypeOption
            {
                Number,
                Text,
                Button,
                TickBox
            }

            public bool ContinuousSignal;
            public bool State;
            public string ConnectionName;
            public Connection Connection;

            [Serialize("", IsPropertySaveable.No, translationTextTag: "Label.", description: "The text displayed on this button/tickbox."), Editable]
            public string Label { get; set; }

            [Serialize("1", IsPropertySaveable.No, description: "The signal sent out when this button is pressed or this tickbox checked."), Editable]
            public string Signal { get; set; }

            public Identifier PropertyName { get; }
            public Identifier TargetItemComponent { get; }
            public bool TargetOnlyParentProperty { get; }

            public string NumberInputMin { get; }
            public string NumberInputMax { get; }
            public string NumberInputStep { get; }
            public int NumberInputDecimalPlaces { get; }

            public int MaxTextLength { get; }

            public const string DefaultNumberInputMin = "0", DefaultNumberInputMax = "99", DefaultNumberInputStep = "1";
            public const int DefaultNumberInputDecimalPlaces = 0;
            public InputTypeOption InputType { get; }
            public NumberType? NumberType { get; }
            public bool HasPropertyName { get; }
            public bool ShouldSetProperty { get; set; }

            /// <summary>
            /// By default, the elements in the interface only set values of the item or send signals. 
            /// This can be used to make them additionally work the other way around, periodically getting the current value of the property from the item and refreshing the UI.
            /// </summary>
            public float GetValueInterval { get; set; } = -1.0f;

#if CLIENT
            public float GetValueTimer;
#endif

            public string Name => "CustomInterfaceElement";

            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            public List<StatusEffect> StatusEffects = new List<StatusEffect>();

            /// <summary>
            /// Pass the parent component to the constructor to access the serializable properties
            /// for elements which change property values.
            /// </summary>
            public CustomInterfaceElement(Item item, ContentXElement element, CustomInterface parent, InputTypeOption inputType)
            {
                Label = element.GetAttributeString("text", "");
                ConnectionName = element.GetAttributeString("connection", "");
                PropertyName = element.GetAttributeIdentifier("propertyname", Identifier.Empty);
                TargetItemComponent = element.GetAttributeIdentifier("targetitemcomponent", Identifier.Empty);
                TargetOnlyParentProperty = element.GetAttributeBool("targetonlyparentproperty", false);
                NumberInputMin = element.GetAttributeString("min", DefaultNumberInputMin);
                NumberInputMax = element.GetAttributeString("max", DefaultNumberInputMax);
                NumberInputStep = element.GetAttributeString("step", DefaultNumberInputStep);
                NumberInputDecimalPlaces = element.GetAttributeInt("decimalplaces", DefaultNumberInputDecimalPlaces);
                MaxTextLength = element.GetAttributeInt("maxtextlength", int.MaxValue);
                GetValueInterval = element.GetAttributeFloat(nameof(GetValueInterval), -1.0f);

                InputType = inputType;

                HasPropertyName = !PropertyName.IsEmpty;
                if (HasPropertyName)
                {
                    if (inputType == InputTypeOption.Number)
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
                    parent.SetSignalToPropertyValue(this);
                }
                else
                {
                    Signal = "1";
                }

                foreach (var subElement in element.Elements())
                {
                    if (subElement.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase))
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

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool ShowInsufficientPowerWarning
        {
            get;
            set;            
        }

        private readonly List<CustomInterfaceElement> customInterfaceElementList = new List<CustomInterfaceElement>();
        
        public CustomInterface(Item item, ContentXElement element)
            : base(item, element)
        {
            foreach (var subElement in element.Elements())
            {
                bool continuousSignalByDefault = false;
                CustomInterfaceElement.InputTypeOption inputType = CustomInterfaceElement.InputTypeOption.Number;
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                        inputType = CustomInterfaceElement.InputTypeOption.Button;
                        continuousSignalByDefault = false;
                        break;
                    case "textbox":
                        inputType = CustomInterfaceElement.InputTypeOption.Text;
                        continuousSignalByDefault = false;
                        break;
                    case "integerinput": // backwards compatibility
                    case "numberinput":
                        inputType = CustomInterfaceElement.InputTypeOption.Number;
                        continuousSignalByDefault = false;
                        break;
                    case "tickbox":
                        inputType = CustomInterfaceElement.InputTypeOption.TickBox;
                        //the default behavior of tickboxes is different for mainly backwards compatibility reasons
                        //(e.g. keeps sending a true/false signal depending on the state of the tickbox, while the others send a signal when the value changes)
                        continuousSignalByDefault = true;
                        break;
                    default:
                        continue;
                }
                var ciElement = new CustomInterfaceElement(item, subElement, this, inputType)
                {
                    ContinuousSignal = subElement.GetAttributeBool(nameof(CustomInterfaceElement.ContinuousSignal), def: continuousSignalByDefault)
                };
                if (string.IsNullOrEmpty(ciElement.Label))
                {
                    ciElement.Label = "Signal out " + customInterfaceElementList.Count(e => e.ContinuousSignal == ciElement.ContinuousSignal);
                }
                customInterfaceElementList.Add(ciElement);
                IsActive |= ciElement.ContinuousSignal;
            }

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
                customInterfaceElementList[i].Label = labels[i];                
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
                    SetPropertyValueToSignal(element);
                    customInterfaceElementList[i].ShouldSetProperty = false;
                }
            }
            UpdateSignalsProjSpecific();
        }

        private void SetPropertyValueToSignal(CustomInterfaceElement element)
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
                    if (!element.TargetItemComponent.IsEmpty && po.Name != element.TargetItemComponent) { continue; }
                    po.SerializableProperties[element.PropertyName].TrySetValue(po, element.Signal);
                }
            }
        }

        private void SetSignalToPropertyValue(CustomInterfaceElement element)
        {
            if (element.TargetOnlyParentProperty)
            {
                if (SerializableProperties.ContainsKey(element.PropertyName))
                {
                    element.Signal = SerializableProperties[element.PropertyName].GetValue(this)?.ToString();
                }
            }
            else
            {
                foreach (ISerializableEntity e in item.AllPropertyObjects)
                {
                    if (!e.SerializableProperties.ContainsKey(element.PropertyName)) { continue; }
                    if (!element.TargetItemComponent.IsEmpty && e.Name != element.TargetItemComponent) { continue; }
                    element.Signal = e.SerializableProperties[element.PropertyName].GetValue(e)?.ToString();
                    break;
                }
            }
        }


        public override void OnItemLoaded()
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                ciElement.Connection = item.Connections?.FirstOrDefault(c => c.Name == ciElement.ConnectionName);
            }
#if SERVER
            //make sure the clients know about the states of the checkboxes and text fields
            if (customInterfaceElementList.Any())
            {
                if (item.FullyInitialized)
                {
                    CoroutineManager.Invoke(() =>
                    {
                        if (!item.Removed) { item.CreateServerEvent(this); }
                    }, delay: 0.1f);
                }
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
                item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f, character: item.ParentInventory?.Owner as Character);
            }
        }

        private void TickBoxToggled(CustomInterfaceElement tickBoxElement, bool state)
        {
            if (tickBoxElement == null) { return; }
            tickBoxElement.State = state;
            tickBoxElement.Signal = state.ToString();
            if (!tickBoxElement.ContinuousSignal)
            {
                SetPropertyValueToSignal(tickBoxElement);
            }
        }

        private void TextChanged(CustomInterfaceElement textElement, string text)
        {
            if (textElement == null) { return; }
            textElement.Signal = text;
            SetPropertyValueToSignal(textElement);
        }

        private void ValueChanged(CustomInterfaceElement numberInputElement, int value)
        {
            if (numberInputElement == null) { return; }
            numberInputElement.Signal = value.ToString();
            SetPropertyValueToSignal(numberInputElement);
            foreach (StatusEffect effect in numberInputElement.StatusEffects)
            {
                item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f, character: item.ParentInventory?.Owner as Character);
            }
        }

        private void ValueChanged(CustomInterfaceElement numberInputElement, float value)
        {
            if (numberInputElement == null) { return; }
            numberInputElement.Signal = value.ToString();
            SetPropertyValueToSignal(numberInputElement);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (!ciElement.ContinuousSignal && ciElement.PropertyName != "Voltage") { continue; }
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

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            //CustomInterface works even when broken (it should be possible to tick the checkboxes and change values,
            //it's up to the other components to work or not work depending on whether the item is broken)
            Update(deltaTime, cam);
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

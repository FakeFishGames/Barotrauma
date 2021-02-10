using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface : ItemComponent, IClientSerializable, IServerSerializable
    {
        class CustomInterfaceElement : ISerializableEntity
        {
            public bool ContinuousSignal;
            public bool State;
            public string ConnectionName;
            public Connection Connection;

            [Serialize("", false, translationTextTag: "Label.", description: "The text displayed on this button/tickbox."), Editable]
            public string Label { get; set; }
            [Serialize("1", false, description: "The signal sent out when this button is pressed or this tickbox checked."), Editable]
            public string Signal { get; set; }

            public string PropertyName { get; }
            public bool TargetOnlyParentProperty { get; }
            public int NumberInputMin { get; }
            public int NumberInputMax { get; }
            public const int DefaultNumberInputMin = 0, DefaultNumberInputMax = 99;
            public bool IsIntegerInput { get; }
            public bool HasPropertyName { get; }
            public bool ShouldSetProperty { get; set; }

            public string Name => "CustomInterfaceElement";

            public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

            public List<StatusEffect> StatusEffects = new List<StatusEffect>();

            /// <summary>
            /// Pass the parent component to the constructor to access the serializable properties
            /// for elements which change property values.
            /// </summary>
            public CustomInterfaceElement(XElement element, CustomInterface parent)
            {
                Label = element.GetAttributeString("text", "");
                ConnectionName = element.GetAttributeString("connection", "");
                PropertyName = element.GetAttributeString("propertyname", "").ToLowerInvariant();
                TargetOnlyParentProperty = element.GetAttributeBool("targetonlyparentproperty", false);
                NumberInputMin = element.GetAttributeInt("min", DefaultNumberInputMin);
                NumberInputMax = element.GetAttributeInt("max", DefaultNumberInputMax);

                HasPropertyName = !string.IsNullOrEmpty(PropertyName);
                IsIntegerInput = HasPropertyName && element.Name.ToString().ToLowerInvariant() == "integerinput";

                if (element.Attribute("signal") is XAttribute attribute)
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

                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().Equals("statuseffect", System.StringComparison.OrdinalIgnoreCase))
                    {
                        StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName: "custom interface element (label " + Label + ")"));
                    }
                }
            }
        }

        private string[] labels;
        [Serialize("", true, description: "The texts displayed on the buttons/tickboxes, separated by commas.")]
        public string Labels
        {
            get { return string.Join(",", labels); }
            set
            {
                if (value == null) { return; }
                if (customInterfaceElementList.Count > 0)
                {
                    string[] splitValues = value == "" ? new string[0] : value.Split(',');
                    UpdateLabels(splitValues);
                }
            }
        }

        private string[] signals;
        [Serialize("", true, description: "The signals sent when the buttons are pressed or the tickboxes checked, separated by commas.")]
        public string Signals
        {
            //use semicolon as a separator because comma may be needed in the signals (for color or vector values for example)
            //kind of hacky, we should probably add support for (string) arrays to SerializableEntityEditor so this wouldn't be needed
            get { return signals == null ? "" : string.Join(";", signals); }
            set
            {
                if (value == null) { return; }
                if (customInterfaceElementList.Count > 0)
                {
                    string[] splitValues = value == "" ? new string[0] : value.Split(';');
                    UpdateSignals(splitValues);
                }
            }
        }

        public override bool RecreateGUIOnResolutionChange => true;

        private readonly List<CustomInterfaceElement> customInterfaceElementList = new List<CustomInterfaceElement>();
        
        public CustomInterface(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                    case "textbox":
                    case "integerinput":
                        var button = new CustomInterfaceElement(subElement, this)
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
                        var tickBox = new CustomInterfaceElement(subElement, this)
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
            Labels = element.GetAttributeString("labels", "");
            Signals = element.GetAttributeString("signals", "");
        }

        private void UpdateLabels(string[] newLabels)
        {
            labels = new string[customInterfaceElementList.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = i < newLabels.Length ? newLabels[i] : customInterfaceElementList[i].Label;
                if (Screen.Selected != GameMain.SubEditorScreen)
                {
                    customInterfaceElementList[i].Label = TextManager.Get(labels[i], returnNull: true) ?? labels[i];
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
        }

        partial void UpdateLabelsProjSpecific();

        partial void UpdateSignalsProjSpecific();

        partial void InitProjSpecific();     
        
        private void ButtonClicked(CustomInterfaceElement btnElement)
        {
            if (btnElement == null) return;
            if (btnElement.Connection != null)
            {
                item.SendSignal(new Signal(btnElement.Signal, 0, btnElement.Connection, null, item));
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

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific();
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (!ciElement.ContinuousSignal) { continue; }
                //TODO: allow changing output when a tickbox is not selected
                if (!string.IsNullOrEmpty(ciElement.Signal) && ciElement.Connection != null)
                {
                    item.SendSignal(new Signal(ciElement.State ? ciElement.Signal : "0", connection: ciElement.Connection, source: item));
                }

                foreach (StatusEffect effect in ciElement.StatusEffects)
                {
                    item.ApplyStatusEffect(effect, ciElement.State ? ActionType.OnUse : ActionType.OnSecondaryUse, 1.0f, null, null, null, true, false);
                }
            }
        }

        partial void UpdateProjSpecific();

        public override XElement Save(XElement parentElement)
        {
            labels = customInterfaceElementList.Select(ci => ci.Label).ToArray();
            signals = customInterfaceElementList.Select(ci => ci.Signal).ToArray();
            return base.Save(parentElement);
        }
    }
}

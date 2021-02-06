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
            public string PropertyName;
            public Connection Connection;
            [Serialize("", false, translationTextTag: "Label.", description: "The text displayed on this button/tickbox."), Editable]
            public string Label { get; set; }
            [Serialize("1", false, description: "The signal sent out when this button is pressed or this tickbox checked."), Editable]
            public string Signal { get; set; }

            public string Name => "CustomInterfaceElement";

            public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

            public List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public CustomInterfaceElement(XElement element)
            {
                Label = element.GetAttributeString("text", "");
                ConnectionName = element.GetAttributeString("connection", "");
                PropertyName = element.GetAttributeString("propertyname", "").ToLowerInvariant();
                Signal = element.GetAttributeString("signal", "1");

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
                string[] splitValues = value == "" ? new string[0] : value.Split(',');
                if (customInterfaceElementList.Count > 0)
                {
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
                string[] splitValues = value == "" ? new string[0] : value.Split(';');
                if (customInterfaceElementList.Count > 0)
                {
                    signals = new string[customInterfaceElementList.Count];
                    for (int i = 0; i < customInterfaceElementList.Count; i++)
                    {
                        signals[i] = i < splitValues.Length ? splitValues[i] : customInterfaceElementList[i].Signal;
                        customInterfaceElementList[i].Signal = signals[i];
                    }
                }
            }
        }

        public override bool RecreateGUIOnResolutionChange => true;

        private List<CustomInterfaceElement> customInterfaceElementList = new List<CustomInterfaceElement>();
        
        public CustomInterface(Item item, XElement element)
            : base(item, element)
        {
            int i = 0;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "button":
                    case "textbox":
                        var button = new CustomInterfaceElement(subElement)
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
                        var tickBox = new CustomInterfaceElement(subElement)
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
                i++;
            }
            IsActive = true;
            InitProjSpecific(element);
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

        public override void OnItemLoaded()
        {
            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                ciElement.Connection = item.Connections?.FirstOrDefault(c => c.Name == ciElement.ConnectionName);
            }
        }

        partial void UpdateLabelsProjSpecific();

        partial void InitProjSpecific(XElement element);     
        
        private void ButtonClicked(CustomInterfaceElement btnElement)
        {
            if (btnElement == null) return;
            if (btnElement.Connection != null)
            {
                item.SendSignal(new Signal(0, btnElement.Signal, btnElement.Connection, null, item));
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
            textElement.Signal = text;
            foreach (ISerializableEntity e in item.AllPropertyObjects)
            {
                if (e.SerializableProperties.ContainsKey(textElement.PropertyName))
                {
                    e.SerializableProperties[textElement.PropertyName].TrySetValue(e, text);
                }
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
                    item.SendSignal(new Signal(0, ciElement.State ? ciElement.Signal : "0", ciElement.Connection, null, item));
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

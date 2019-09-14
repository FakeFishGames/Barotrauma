using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public class GUIRadioButtonGroup : GUIComponent
    {
        private Dictionary<Enum, GUITickBox> radioButtons; //TODO: use children list instead?

        public GUIRadioButtonGroup() : base("GUIFrame")
        {
            radioButtons = new Dictionary<Enum, GUITickBox>();
        }

        public override bool Enabled
        {
            get => base.Enabled;
            set
            {
                base.Enabled = value;
                foreach(KeyValuePair<Enum, GUITickBox> rbPair in radioButtons)
                {
                    rbPair.Value.Enabled = value;
                }
            }
        }

        public void AddRadioButton(Enum key, GUITickBox radioButton)
        {
            if (selected == key) radioButton.Selected = true;
            else if (radioButton.Selected) selected = key;

            radioButton.SetRadioButtonGroup(this);
            radioButtons.Add(key, radioButton);
        }

        public delegate void RadioButtonGroupDelegate(GUIRadioButtonGroup rbg, Enum val);
        public RadioButtonGroupDelegate OnSelect = null;

        public void SelectRadioButton(GUITickBox radioButton)
        {
            foreach (KeyValuePair<Enum, GUITickBox> rbPair in radioButtons)
            {
                if (radioButton == rbPair.Value)
                {
                    Selected = rbPair.Key;
                    return;
                }
            }
        }

        private Enum selected;
        public Enum Selected
        {
            get
            {
                return selected;
            }
            set
            {
                OnSelect?.Invoke(this, value);
                if (selected != null && selected.Equals((Enum)value)) return;
                selected = value;
                foreach (KeyValuePair<Enum, GUITickBox> radioButton in radioButtons)
                {
                    if (radioButton.Key.Equals((Enum)value))
                    {
                        radioButton.Value.Selected = true;
                    }
                    else if (radioButton.Value.Selected) radioButton.Value.Selected = false;
                }
            }
        }

        public GUITickBox SelectedRadioButton
        {
            get
            {
                return radioButtons[selected];
            }
        }
    }
}

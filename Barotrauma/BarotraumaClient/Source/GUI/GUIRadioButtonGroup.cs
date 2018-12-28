using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    //TODO: merge this and GUITickBox.radioButtonGroup in some way
    public class GUIRadioButtonGroup : GUIComponent
    {
        private Dictionary<Enum, GUITickBox> radioButtons; //TODO: use children list instead?

        public GUIRadioButtonGroup() : base("GUIFrame")
        {
            radioButtons = new Dictionary<Enum, GUITickBox>();
        }
        
        public void AddRadioButton(Enum key, GUITickBox radioButton)
        {
            if (selected == key) radioButton.Selected = true;
            radioButtons.Add(key, radioButton);
            radioButton.OnSelected = (GUITickBox rb) =>
            {
                if (rb.Selected) Selected = key;
                else if (selected == key) rb.Selected = true;
                return false;
            };
        }

        public delegate void RadioButtonGroupDelegate(GUIRadioButtonGroup rbg, Enum val);
        public RadioButtonGroupDelegate OnSelect = null;

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
    }
}

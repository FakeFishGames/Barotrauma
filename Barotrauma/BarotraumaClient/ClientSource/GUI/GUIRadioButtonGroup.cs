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
        private Dictionary<int, GUITickBox> radioButtons; //TODO: use children list instead?

        public GUIRadioButtonGroup() : base(null)
        {
            radioButtons = new Dictionary<int, GUITickBox>();
            selected = null;
        }

        public override bool Enabled
        {
            get => base.Enabled;
            set
            {
                base.Enabled = value;
                foreach(KeyValuePair<int, GUITickBox> rbPair in radioButtons)
                {
                    rbPair.Value.Enabled = value;
                }
            }
        }

        public void AddRadioButton(int key, GUITickBox radioButton)
        {
            if (selected == key) radioButton.Selected = true;
            else if (radioButton.Selected) selected = key;

            radioButton.SetRadioButtonGroup(this);
            radioButtons.Add((int)key, radioButton);
        }

        public delegate void RadioButtonGroupDelegate(GUIRadioButtonGroup rbg, int? val);
        public RadioButtonGroupDelegate OnSelect = null;

        public void SelectRadioButton(GUITickBox radioButton)
        {
            foreach (KeyValuePair<int, GUITickBox> rbPair in radioButtons)
            {
                if (radioButton == rbPair.Value)
                {
                    Selected = rbPair.Key;
                    return;
                }
            }
        }

        private int? selected;
        public int? Selected
        {
            get
            {
                return selected;
            }
            set
            {
                OnSelect?.Invoke(this, value);
                if (selected != null && selected.Equals(value)) return;
                selected = value;
                foreach (KeyValuePair<int, GUITickBox> radioButton in radioButtons)
                {
                    if (radioButton.Key.Equals(value))
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
                return selected.HasValue ? radioButtons[selected.Value] : null;
            }
        }
    }
}

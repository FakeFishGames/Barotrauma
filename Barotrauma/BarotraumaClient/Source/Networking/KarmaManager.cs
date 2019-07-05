using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        public void CreateSettingsFrame(GUIComponent parent)
        {
            CreateLabeledSlider(parent, 0.0f, 40.0f, 1.0f, "KickBanThreshold");

            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "KarmaDecay");
            CreateLabeledSlider(parent, 50.0f, 100.0f, 1.0f, "KarmaDecayThreshold");
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "KarmaIncrease");
            CreateLabeledSlider(parent, 0.0f, 50.0f, 1.0f, "KarmaIncreaseThreshold");

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.1f), parent.RectTransform), TextManager.Get("Karma.PositiveActions"))
            {
                CanBeFocused = false
            };

            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "StructureRepairKarmaIncrease");
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "DamageEnemyKarmaIncrease");
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "ItemRepairKarmaIncrease");

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.1f), parent.RectTransform), TextManager.Get("Karma.NegativeActions"))
            {
                CanBeFocused = false
            };

            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "StructureDamageKarmaDecrease");
            CreateLabeledSlider(parent, 0.0f, 1.0f, 0.05f, "DamageFriendlyKarmaDecrease");
        }

        private void CreateLabeledSlider(GUIComponent parent, float min, float max, float step, string propertyName)
        {
            var container = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f,
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            string labelText = TextManager.Get("Karma." + propertyName);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.8f), container.RectTransform),
                labelText, font: GUI.SmallFont)
            {
                ToolTip = TextManager.Get("Karma." + propertyName + "ToolTip")
            };

            var slider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 0.8f), container.RectTransform), barSize: 0.1f);
            slider.Step = step <= 0.0f ? 0.0f : step / (max - min);
            slider.Range = new Vector2(min, max);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                label.Text = TextManager.AddPunctuation(':', labelText, scrollBar.BarScrollValue.Format(decimalCount: step <= 1.0f ? 1 : 0));
                return true;
            };
            GameMain.NetworkMember.ServerSettings.AssignGUIComponent(propertyName, slider);
            slider.OnMoved(slider, slider.BarScroll);
        }
    }
}

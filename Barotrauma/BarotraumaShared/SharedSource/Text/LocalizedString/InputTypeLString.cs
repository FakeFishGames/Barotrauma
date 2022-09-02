#nullable enable
using System;

namespace Barotrauma
{
    public class InputTypeLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        private bool useColorHighlight;

        public InputTypeLString(LocalizedString nStr, bool useColorHighlight = false) 
        { 
            nestedStr = nStr;
            this.useColorHighlight = useColorHighlight;
        }

        protected override bool MustRetrieveValue()
        {
            //TODO: check for config changes!
            return base.MustRetrieveValue();
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
#if CLIENT
            //TODO: server shouldn't have this type at all
            foreach (InputType? inputType in Enum.GetValues(typeof(InputType)))
            {
                if (!inputType.HasValue) { continue; }

                string keyBindText = GameSettings.CurrentConfig.KeyMap.KeyBindText(inputType.Value).Value;
                if (useColorHighlight)
                {
                    keyBindText = $"‖color:gui.orange‖{keyBindText}‖end‖";
                }
                cachedValue = cachedValue.Replace($"[{inputType}]", keyBindText, StringComparison.OrdinalIgnoreCase);
                cachedValue = cachedValue.Replace($"[InputType.{inputType}]", keyBindText, StringComparison.OrdinalIgnoreCase);
            }
#endif
            UpdateLanguage();
        }
    }
}
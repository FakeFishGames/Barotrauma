namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        private RoundSound powerOnSound;
        private bool powerOnSoundPlayed;
        
        partial void InitProjectSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "poweronsound":
                        powerOnSound = RoundSound.Load(subElement, false);
                        break;
                }
            }
        }
    }
}

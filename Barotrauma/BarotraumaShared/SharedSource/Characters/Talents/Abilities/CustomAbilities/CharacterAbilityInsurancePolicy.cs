using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityInsurancePolicy : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;
        public override bool RequiresAlive => false;

        private readonly int moneyPerLevel;
        private bool hasOccurred = false;

        private static List<Client> clientsAlreadyUsed = new List<Client>();

        public CharacterAbilityInsurancePolicy(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            moneyPerLevel = abilityElement.GetAttributeInt("moneyperlevel", 0);
        }

        protected override void ApplyEffect()
        {
            if (Character?.Info is CharacterInfo info && !hasOccurred)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    foreach (Client client in GameMain.NetworkMember.ConnectedClients)
                    {
                        if (client.Character == Character && clientsAlreadyUsed.Contains(client)) { return; }
                    }
                }

                Character.GiveMoney(moneyPerLevel * info.GetCurrentLevel());
                hasOccurred = true;

                // this is an ugly way to do this, but this effect should not occur more than once per round for a client
                // this seemed like the simplest way to do it since characters are instantiated from scratch each time
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    foreach (Client client in GameMain.NetworkMember.ConnectedClients)
                    {
                        if (client.Character == Character)
                        {
                            clientsAlreadyUsed.Add(client);
                        }
                    }
                }
            }
        }
    }
}

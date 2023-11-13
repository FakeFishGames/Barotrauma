using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        public readonly CharacterInfo CharacterInfo;

        public readonly string Name;

        public readonly Address ClientAddress;
        public readonly Option<AccountId> AccountId;

        private XElement itemData;
        private XElement healthData;
        public XElement OrderData { get; private set; }
        public XElement WalletData;
    }
}

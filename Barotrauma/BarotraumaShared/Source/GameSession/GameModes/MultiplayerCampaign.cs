using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class MultiplayerCampaign : CampaignMode
    {
        public MultiplayerCampaign(GameModePreset preset, object param) : 
            base(preset, param)
        {
        }

        public override void Save(XElement element)
        {
            throw new NotImplementedException();
        }
    }
}

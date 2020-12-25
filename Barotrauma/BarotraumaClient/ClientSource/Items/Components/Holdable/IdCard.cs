using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class IdCard
    {
        public Sprite StoredPortrait;
        public Vector2 StoredSheetIndex;
        public JobPrefab StoredJobPrefab;
        public List<WearableSprite> StoredAttachments;
    }
}

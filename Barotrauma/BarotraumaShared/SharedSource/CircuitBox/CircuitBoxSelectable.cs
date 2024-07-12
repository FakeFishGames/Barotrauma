#nullable enable

using System;

namespace Barotrauma
{
    internal class CircuitBoxSelectable
    {
        public bool IsSelected;
        public ushort SelectedBy;

        public bool IsSelectedByMe
        {
            get
            {
                if (GameMain.NetworkMember is { IsServer: true })
                {
                    throw new Exception("CircuitBoxSelectable.IsSelectedByMe should never be used by the server.");
                }

                if (Character.Controlled is { } controlled)
                {
                    return SelectedBy == controlled.ID;
                }

                return false;
            }
        }

        public void SetSelected(Option<ushort> selectedBy)
        {
            if (selectedBy.TryUnwrap(out ushort id))
            {
                SelectedBy = id;
                IsSelected = true;
                return;
            }

            IsSelected = false;
            SelectedBy = 0;
        }
    }
}
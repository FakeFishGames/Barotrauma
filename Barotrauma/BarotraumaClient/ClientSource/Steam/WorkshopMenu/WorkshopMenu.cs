using System;

#nullable enable

namespace Barotrauma.Steam
{
    abstract partial class WorkshopMenu
    {
        public WorkshopMenu(GUIFrame parent) { }

        protected abstract void UpdateModListItemVisibility();

        protected bool ModNameMatches(ContentPackage p, string query)
            => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
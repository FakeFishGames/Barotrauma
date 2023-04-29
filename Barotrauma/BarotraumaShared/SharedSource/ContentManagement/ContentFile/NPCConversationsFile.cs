using System.Xml.Linq;

namespace Barotrauma
{
    sealed class NPCConversationsFile : ContentFile
    {
        public NPCConversationsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }
            var mainElement = doc.Root.FromContent(Path);
            bool allowOverriding = doc.Root.IsOverride();
            if (allowOverriding)
            {
                mainElement = mainElement.FirstElement();
            }

            var npcConversationCollection = new NPCConversationCollection(this, mainElement);
            if (!NPCConversationCollection.Collections.ContainsKey(npcConversationCollection.Language))
            {
                NPCConversationCollection.Collections.Add(npcConversationCollection.Language, new PrefabCollection<NPCConversationCollection>());
            }
            NPCConversationCollection.Collections[npcConversationCollection.Language].Add(npcConversationCollection, allowOverriding);
        }

        public override void UnloadFile()
        {
            foreach (var collection in NPCConversationCollection.Collections.Values)
            {
                collection.RemoveByFile(this);
            }
        }

        public override void Sort()
        {
            foreach (var collection in NPCConversationCollection.Collections.Values)
            {
                collection.SortAll();
            }
        }
    }
}
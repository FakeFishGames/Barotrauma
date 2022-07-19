using System.Xml.Linq;

namespace Barotrauma
{
    sealed class SkillSettingsFile : ContentFile
    {
        public SkillSettingsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }
            var mainElement = doc.Root.FromPackage(ContentPackage);
            bool allowOverriding = mainElement.IsOverride();
            if (allowOverriding)
            {
                mainElement = mainElement.FirstElement();
            }
            var prefab = new SkillSettings(mainElement, this);
            SkillSettings.Prefabs.Add(prefab, allowOverriding);
        }

        public override void UnloadFile()
        {
            SkillSettings.Prefabs.RemoveByFile(this);
        }

        public override void Sort()
        {
            SkillSettings.Prefabs.Sort();
        }
    }
}
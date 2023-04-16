using Barotrauma;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{

    [RequiredByCorePackage]
    sealed class JobsFile : ContentFile
    {
        public JobsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }
            LoadElements(doc.Root.FromPackage(ContentPackage), false);
        }

        private void LoadElements(ContentXElement mainElement, bool isOverride)
        {
            foreach (var element in mainElement.Elements())
            {
                if (element.NameAsIdentifier() == "ItemRepairPriorities")
                {
                    foreach (var subElement in element.Elements())
                    {
                        ItemRepairPriority prio = new ItemRepairPriority(subElement, this);
                        ItemRepairPriority.Prefabs.Add(prio, isOverride);
                    }
                }
                else if (element.IsOverride())
                {
                    LoadElements(element, true);
                }
                else
                {
                    var job = new JobPrefab(element, this);
                    JobPrefab.Prefabs.Add(job, isOverride);
                }
            }
        }

        public override void UnloadFile()
        {
            JobPrefab.Prefabs.RemoveByFile(this);
            ItemRepairPriority.Prefabs.RemoveByFile(this);
        }

        public override void Sort()
        {
            JobPrefab.Prefabs.SortAll();
        }
    }
}

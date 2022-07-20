namespace Barotrauma
{
    public sealed class DecalsFile : ContentFile
    {
        public DecalsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            DecalManager.LoadFromFile(this);
        }

        public override void UnloadFile()
        {
            DecalManager.RemoveByFile(this);
        }

        public override void Sort()
        {
            DecalManager.SortAll();
        }
    }
}
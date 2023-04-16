namespace Barotrauma
{
    [NotSyncedInMultiplayer]
    public abstract class HashlessFile : ContentFile
    {
        public HashlessFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public sealed override Md5Hash CalculateHash() => Md5Hash.Blank;
    }
}

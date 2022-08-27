using System;
using Barotrauma.IO;

namespace Barotrauma
{
    sealed class ServerExecutableFile : OtherFile
    {
        //This content type doesn't do very much on its own, it's handled manually by the Host Server menu
        public ServerExecutableFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public static ContentPath MutateContentPath(ContentPath path)
        {
            if (File.Exists(path.FullPath)) { return path; }

            string rawValueWithoutExtension()
                => Barotrauma.IO.Path.Combine(
                    Barotrauma.IO.Path.GetDirectoryName(path.RawValue ?? ""),
                    Barotrauma.IO.Path.GetFileNameWithoutExtension(path.RawValue ?? "")).CleanUpPath();
            
            path = ContentPath.FromRaw(path, rawValueWithoutExtension());
            if (File.Exists(path.FullPath)) { return path; }
            
            path = ContentPath.FromRaw(path,
                rawValueWithoutExtension() + ".exe");
            return path;
        }
    }
}

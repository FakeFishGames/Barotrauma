using System;

namespace Microsoft.Xna.Framework
{
    public class FileDropEventArgs : EventArgs
    {
        public string FilePath;
        public FileDropEventArgs(string filePath)
        {
            FilePath = filePath;
        }
    }
}

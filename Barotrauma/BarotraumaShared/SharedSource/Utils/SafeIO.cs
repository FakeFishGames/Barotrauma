using System;
using System.Collections.Generic;

namespace Barotrauma.IO
{
    static class Validation
    {
        static readonly string[] unwritableDirs = new string[] { "Content", "Data/ContentPackages" };

        /// <summary>
        /// When set to true, the game is allowed to modify the vanilla content in debug builds. Has no effect in non-debug builds.
        /// </summary>
        public static bool SkipValidationInDebugBuilds;

        public static bool CanWrite(string path)
        {
            path = System.IO.Path.GetFullPath(path).CleanUpPath();

            foreach (string unwritableDir in unwritableDirs)
            {
                string dir = System.IO.Path.GetFullPath(unwritableDir).CleanUpPath();

                if (path.StartsWith(dir, StringComparison.InvariantCultureIgnoreCase))
                {
#if DEBUG
                    return SkipValidationInDebugBuilds;
#else
                    return false;
#endif
                }
            }

            return true;
        }
    }

    public static class SafeXML
    {
        public static void SaveSafe(this System.Xml.Linq.XDocument doc, string path)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot save XML document to \"{path}\": modifying the files in the folder is not allowed.");
                return;
            }
            doc.Save(path);
        }

        public static void SaveSafe(this System.Xml.Linq.XElement element, string path)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot save XML element to \"{path}\": modifying the files in the folder is not allowed.");
                return;
            }
            element.Save(path);
        }

        public static void SaveSafe(this System.Xml.Linq.XDocument doc, XmlWriter writer)
        {
            doc.WriteTo(writer);
        }

        public static void WriteTo(this System.Xml.Linq.XDocument doc, XmlWriter writer)
        {
            writer.Write(doc);
        }
    }

    public class XmlWriter : IDisposable
    {
        public readonly System.Xml.XmlWriter Writer;

        public XmlWriter(string path, System.Xml.XmlWriterSettings settings)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot write XML document to \"{path}\": modifying the files in the folder is not allowed.");
                Writer = null;
                return;
            }
            Writer = System.Xml.XmlWriter.Create(path, settings);
        }

        public static XmlWriter Create(string path, System.Xml.XmlWriterSettings settings)
        {
            return new XmlWriter(path, settings);
        }

        public void Write(System.Xml.Linq.XDocument doc)
        {
            if (Writer == null)
            {
                DebugConsole.ThrowError("Cannot write to invalid XmlWriter");
                return;
            }
            doc.WriteTo(Writer);
        }

        public void Flush()
        {
            if (Writer == null)
            {
                DebugConsole.ThrowError("Cannot flush invalid XmlWriter");
                return;
            }
            Writer.Flush();
        }

        public void Dispose()
        {
            if (Writer == null)
            {
                DebugConsole.ThrowError("Cannot dispose invalid XmlWriter");
                return;
            }
            Writer.Dispose();
        }
    }

    public static class Path
    {
        public static readonly char DirectorySeparatorChar = System.IO.Path.DirectorySeparatorChar;
        public static readonly char AltDirectorySeparatorChar = System.IO.Path.AltDirectorySeparatorChar;

        public static string GetExtension(string path)
        {
            return System.IO.Path.GetExtension(path);
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public static string GetPathRoot(string path)
        {
            return System.IO.Path.GetPathRoot(path);
        }

        public static string GetRelativePath(string relativeTo, string path)
        {
            return System.IO.Path.GetRelativePath(relativeTo, path);
        }

        public static string GetDirectoryName(string path)
        {
            return System.IO.Path.GetDirectoryName(path);
        }

        public static string GetFileName(string path)
        {
            return System.IO.Path.GetFileName(path);
        }

        public static string GetFullPath(string path)
        {
            return System.IO.Path.GetFullPath(path);
        }

        public static string Combine(params string[] s)
        {
            return System.IO.Path.Combine(s);
        }

        public static string GetTempFileName()
        {
            return System.IO.Path.GetTempFileName();
        }

        public static bool IsPathRooted(string path)
        {
            return System.IO.Path.IsPathRooted(path);
        }
        public static IEnumerable<char> GetInvalidFileNameChars()
        {
            return System.IO.Path.GetInvalidFileNameChars();
        }

    }

    public static class Directory
    {
        public static string GetCurrentDirectory()
        {
            return System.IO.Directory.GetCurrentDirectory();
        }

        public static void SetCurrentDirectory(string path)
        {
            System.IO.Directory.SetCurrentDirectory(path);
        }

        public static IEnumerable<string> GetFiles(string path)
        {
            return System.IO.Directory.GetFiles(path);
        }

        public static IEnumerable<string> GetFiles(string path, string pattern, System.IO.SearchOption option = System.IO.SearchOption.AllDirectories)
        {
            return System.IO.Directory.GetFiles(path, pattern, option);
        }

        public static IEnumerable<string> GetDirectories(string path)
        {
            return System.IO.Directory.GetDirectories(path);
        }

        public static IEnumerable<string> GetFileSystemEntries(string path)
        {
            return System.IO.Directory.GetFileSystemEntries(path);
        }

        public static IEnumerable<string> EnumerateDirectories(string path, string pattern)
        {
            return System.IO.Directory.EnumerateDirectories(path, pattern);
        }

        public static IEnumerable<string> EnumerateFiles(string path, string pattern)
        {
            return System.IO.Directory.EnumerateFiles(path, pattern);
        }

        public static bool Exists(string path)
        {
            return System.IO.Directory.Exists(path);
        }

        public static System.IO.DirectoryInfo CreateDirectory(string path)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot create directory \"{path}\": modifying the contents of the folder is not allowed.");
                return null;
            }
            return System.IO.Directory.CreateDirectory(path);
        }

        public static void Delete(string path, bool recursive=true)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot delete directory \"{path}\": modifying the contents of the folder is not allowed.");
                return;
            }
            //TODO: validate recursion?
            System.IO.Directory.Delete(path, recursive);
        }
    }

    public static class File
    {
        public static bool Exists(string path)
        {
            return System.IO.File.Exists(path);
        }

        public static void Copy(string src, string dest, bool overwrite=false)
        {
            if (!Validation.CanWrite(dest))
            {
                DebugConsole.ThrowError($"Cannot copy \"{src}\" to \"{dest}\": modifying the contents of the folder is not allowed.");
                return;
            }
            System.IO.File.Copy(src, dest, overwrite);
        }

        public static void Move(string src, string dest)
        {
            if (!Validation.CanWrite(src))
            {
                DebugConsole.ThrowError($"Cannot move \"{src}\" to \"{dest}\": modifying the contents of the source folder is not allowed.");
                return;
            }
            if (!Validation.CanWrite(dest))
            {
                DebugConsole.ThrowError($"Cannot move \"{src}\" to \"{dest}\": modifying the contents of the destination folder is not allowed");
                return;
            }
            System.IO.File.Move(src, dest);
        }

        public static void Delete(string path)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot delete file \"{path}\": modifying the contents of the folder is not allowed.");
                return;
            }
            System.IO.File.Delete(path);
        }

        public static DateTime GetLastWriteTime(string path)
        {
            return System.IO.File.GetLastWriteTime(path);
        }

        public static FileStream Open(string path, System.IO.FileMode mode, System.IO.FileAccess access = System.IO.FileAccess.ReadWrite)
        {
            switch (mode)
            {
                case System.IO.FileMode.Create:
                case System.IO.FileMode.CreateNew:
                case System.IO.FileMode.OpenOrCreate:
                case System.IO.FileMode.Append:
                case System.IO.FileMode.Truncate:
                    if (!Validation.CanWrite(path))
                    {
                        DebugConsole.ThrowError($"Cannot open \"{path}\" in {mode} mode: modifying the contents of the folder is not allowed.");
                        return null;
                    }
                    break;
            }
            return new FileStream(path, System.IO.File.Open(path, mode,
                !Validation.CanWrite(path) ?
                System.IO.FileAccess.Read :
                access));
        }

        public static FileStream OpenRead(string path)
        {
            return Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
        }

        public static FileStream OpenWrite(string path)
        {
            return Open(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write);
        }

        public static FileStream Create(string path)
        {
            return Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot write all bytes to \"{path}\": modifying the files in the folder is not allowed.");
                return;
            }
            System.IO.File.WriteAllBytes(path, contents);
        }

        public static void WriteAllText(string path, string contents, System.Text.Encoding? encoding = null)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot write all text to \"{path}\": modifying the files in the folder is not allowed.");
                return;
            }
            System.IO.File.WriteAllText(path, contents, encoding ?? System.Text.Encoding.UTF8);
        }

        public static void WriteAllLines(string path, IEnumerable<string> contents, System.Text.Encoding? encoding = null)
        {
            if (!Validation.CanWrite(path))
            {
                DebugConsole.ThrowError($"Cannot write all lines to \"{path}\": modifying the files in the folder is not allowed.");
                return;
            }
            System.IO.File.WriteAllLines(path, contents, encoding ?? System.Text.Encoding.UTF8);
        }

        public static byte[] ReadAllBytes(string path)
        {
            return System.IO.File.ReadAllBytes(path);
        }

        public static string ReadAllText(string path, System.Text.Encoding? encoding = null)
        {
            return System.IO.File.ReadAllText(path, encoding ?? System.Text.Encoding.UTF8);
        }

        public static string[] ReadAllLines(string path, System.Text.Encoding? encoding = null)
        {
            return System.IO.File.ReadAllLines(path, encoding ?? System.Text.Encoding.UTF8);
        }
    }

    public class FileStream : System.IO.Stream
    {
        private System.IO.FileStream innerStream;
        private string fileName;

        public FileStream(string fn, System.IO.FileStream stream)
        {
            innerStream = stream;
            fileName = fn;
        }

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanTimeout => innerStream.CanTimeout;
        public override bool CanWrite
        {
            get
            {
                if (!Validation.CanWrite(fileName)) { return false; }
                return innerStream.CanWrite;
            }
        }

        public override long Length => innerStream.Length;

        public override long Position
        {
            get
            {
                return innerStream.Position;
            }
            set
            {
                innerStream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return innerStream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Validation.CanWrite(fileName))
            {
                innerStream.Write(buffer, offset, count);
            }
            else
            {
                DebugConsole.ThrowError($"Cannot write to file \"{fileName}\": modifying the files in the folder is not allowed.");
            }
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            innerStream.SetLength(value);
        }

        public override void Flush()
        {
            innerStream.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            innerStream.Dispose();
        }
    }

    public class DirectoryInfo
    {
        private System.IO.DirectoryInfo innerInfo;

        public DirectoryInfo(string path)
        {
            innerInfo = new System.IO.DirectoryInfo(path);
        }

        private DirectoryInfo(System.IO.DirectoryInfo info)
        {
            innerInfo = info;
        }

        public bool Exists => innerInfo.Exists;
        public string Name => innerInfo.Name;
        public string FullName => innerInfo.FullName;

        public System.IO.FileAttributes Attributes => innerInfo.Attributes;

        public IEnumerable<DirectoryInfo> GetDirectories()
        {
            var dirs = innerInfo.GetDirectories();
            foreach (var dir in dirs)
            {
                yield return new DirectoryInfo(dir);
            }
        }

        public IEnumerable<FileInfo> GetFiles()
        {
            var files = innerInfo.GetFiles();
            foreach (var file in files)
            {
                yield return new FileInfo(file);
            }
        }

        public void Delete()
        {
            if (!Validation.CanWrite(innerInfo.FullName))
            {
                DebugConsole.ThrowError($"Cannot delete directory \"{Name}\": modifying the contents of the folder is not allowed.");
                return;
            }
            innerInfo.Delete();
        }
    }

    public class FileInfo
    {
        private System.IO.FileInfo innerInfo;

        public FileInfo(string path)
        {
            innerInfo = new System.IO.FileInfo(path);
        }

        public FileInfo(System.IO.FileInfo info)
        {
            innerInfo = info;
        }

        public bool Exists => innerInfo.Exists;
        public string Name => innerInfo.Name;
        public string FullName => innerInfo.FullName;
        public long Length => innerInfo.Length;

        public bool IsReadOnly
        {
            get
            {
                return innerInfo.IsReadOnly;
            }
            set
            {
                if (!Validation.CanWrite(innerInfo.FullName))
                {
                    DebugConsole.ThrowError($"Cannot set read-only to {value} for \"{Name}\": modifying the files in the folder is not allowed.");
                    return;
                }
                innerInfo.IsReadOnly = value;
            }
        }

        public void CopyTo(string dest, bool overwriteExisting = false)
        {
            if (!Validation.CanWrite(dest))
            {
                DebugConsole.ThrowError($"Cannot copy \"{Name}\" to \"{dest}\": modifying the contents of the destination folder is not allowed.");
                return;
            }
            innerInfo.CopyTo(dest, overwriteExisting);
        }

        public void Delete()
        {
            if (!Validation.CanWrite(innerInfo.FullName))
            {
                DebugConsole.ThrowError($"Cannot delete file \"{Name}\": modifying the files in the folder is not allowed.");
                return;
            }
            innerInfo.Delete();
        }
    }
}

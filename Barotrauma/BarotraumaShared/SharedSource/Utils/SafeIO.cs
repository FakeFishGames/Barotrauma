#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.IO
{
    static class Validation
    {
        private static readonly string[] unwritableDirs = new string[] { "Content" };
        private static readonly string[] unwritableExtensions = new string[]
        {
            ".pdb", ".com", ".scr", ".dylib", ".so", ".a", ".app", //executables and libraries (.exe and .dll handled separately in CanWrite)
            ".bat", ".sh", //shell scripts
            ".json" //deps.json
        };

        /// <summary>
        /// When set to true, the game is allowed to modify the vanilla content in debug builds. Has no effect in non-debug builds.
        /// </summary>
        public static bool SkipValidationInDebugBuilds;

        public static bool CanWrite(string path, bool isDirectory)
        {
            path = System.IO.Path.GetFullPath(path).CleanUpPath();

            if (!isDirectory)
            {
                string extension = System.IO.Path.GetExtension(path).Replace(" ", "");
                if (unwritableExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
                if (!path.StartsWith(System.IO.Path.GetFullPath("Mods/").CleanUpPath(), StringComparison.OrdinalIgnoreCase)
                    && (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
            
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
        public static void SaveSafe(
            this System.Xml.Linq.XDocument doc,
            string path,
            System.Xml.Linq.SaveOptions saveOptions = System.Xml.Linq.SaveOptions.None,
            bool throwExceptions = false)
        {
            if (!Validation.CanWrite(path, false))
            {
                string errorMsg = $"Cannot save XML document to \"{path}\": modifying the files in this folder/with this extension is not allowed.";
                if (throwExceptions)
                {
                    throw new InvalidOperationException(errorMsg);
                }
                else
                {
                    DebugConsole.ThrowError(errorMsg);
                }
                return;
            }
            doc.Save(path, saveOptions);
        }

        public static void SaveSafe(this System.Xml.Linq.XElement element, string path, bool throwExceptions = false)
        {
            if (!Validation.CanWrite(path, false))
            {
                string errorMsg = $"Cannot save XML element to \"{path}\": modifying the files in this folder/with this extension is not allowed.";
                if (throwExceptions)
                {
                    throw new InvalidOperationException(errorMsg);
                }
                else
                {
                    DebugConsole.ThrowError(errorMsg);
                }
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
        public readonly System.Xml.XmlWriter? Writer;

        public XmlWriter(string path, System.Xml.XmlWriterSettings settings)
        {
            if (!Validation.CanWrite(path, false))
            {
                DebugConsole.ThrowError($"Cannot write XML document to \"{path}\": modifying the files in this folder/with this extension is not allowed.");
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

    public static class XmlWriterExtensions
    {
        public static void Save(this System.Xml.Linq.XDocument doc, XmlWriter writer)
        {
            doc.Save(writer.Writer ?? throw new NullReferenceException("Unable to save XML document: XML writer is null."));
        }
    }

    public static class Path
    {
        public static readonly char DirectorySeparatorChar = System.IO.Path.DirectorySeparatorChar;
        public static readonly char AltDirectorySeparatorChar = System.IO.Path.AltDirectorySeparatorChar;

        public static string GetExtension(string path) => System.IO.Path.GetExtension(path);

        public static string GetFileNameWithoutExtension(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

        public static string? GetPathRoot(string? path) => System.IO.Path.GetPathRoot(path);

        public static string GetRelativePath(string relativeTo, string path) => System.IO.Path.GetRelativePath(relativeTo, path);

        public static string GetDirectoryName(ContentPath path) => GetDirectoryName(path.Value)!;
        
        public static string? GetDirectoryName(string path) => System.IO.Path.GetDirectoryName(path);

        public static string GetFileName(string path) => System.IO.Path.GetFileName(path);

        public static string GetFullPath(string path) => System.IO.Path.GetFullPath(path);

        public static string Combine(params string[] s) => System.IO.Path.Combine(s);

        public static string GetTempFileName() => System.IO.Path.GetTempFileName();

        public static bool IsPathRooted(string path) => System.IO.Path.IsPathRooted(path);

        public static IEnumerable<char> GetInvalidFileNameChars() => System.IO.Path.GetInvalidFileNameChars();
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

        public static string[] GetFiles(string path)
        {
            return System.IO.Directory.GetFiles(path);
        }

        public static string[] GetFiles(string path, string pattern, System.IO.SearchOption option = System.IO.SearchOption.AllDirectories)
        {
            return System.IO.Directory.GetFiles(path, pattern, option);
        }

        public static string[] GetDirectories(string path, string searchPattern = "*", System.IO.SearchOption searchOption = System.IO.SearchOption.TopDirectoryOnly)
        {
            return System.IO.Directory.GetDirectories(path, searchPattern, searchOption);
        }

        public static string[] GetFileSystemEntries(string path)
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

        public static System.IO.DirectoryInfo? CreateDirectory(string path)
        {
            if (!Validation.CanWrite(path, true))
            {
                DebugConsole.ThrowError($"Cannot create directory \"{path}\": modifying the contents of this folder/using this extension is not allowed.");
                Validation.CanWrite(path, true);
                return null;
            }
            return System.IO.Directory.CreateDirectory(path);
        }

        public static void Delete(string path, bool recursive=true)
        {
            if (!Validation.CanWrite(path, true))
            {
                DebugConsole.ThrowError($"Cannot delete directory \"{path}\": modifying the contents of this folder/using this extension is not allowed.");
                return;
            }
            //TODO: validate recursion?
            System.IO.Directory.Delete(path, recursive);
        }
    }

    public static class File
    {
        public static bool Exists(ContentPath path) => Exists(path.Value);
        
        public static bool Exists(string path) => System.IO.File.Exists(path);

        public static void Copy(string src, string dest, bool overwrite=false)
        {
            if (!Validation.CanWrite(dest, false))
            {
                DebugConsole.ThrowError($"Cannot copy \"{src}\" to \"{dest}\": modifying the contents of this folder/using this extension is not allowed.");
                return;
            }
            System.IO.File.Copy(src, dest, overwrite);
        }

        public static void Move(string src, string dest)
        {
            if (!Validation.CanWrite(src, false))
            {
                DebugConsole.ThrowError($"Cannot move \"{src}\" to \"{dest}\": modifying the contents of the source folder is not allowed.");
                return;
            }
            if (!Validation.CanWrite(dest, false))
            {
                DebugConsole.ThrowError($"Cannot move \"{src}\" to \"{dest}\": modifying the contents of the destination folder is not allowed");
                return;
            }
            System.IO.File.Move(src, dest);
        }

        public static void Delete(ContentPath path) => Delete(path.Value);
        
        public static void Delete(string path)
        {
            if (!Validation.CanWrite(path, false))
            {
                DebugConsole.ThrowError($"Cannot delete file \"{path}\": modifying the contents of this folder/using this extension is not allowed.");
                return;
            }
            System.IO.File.Delete(path);
        }

        public static DateTime GetLastWriteTime(string path)
        {
            return System.IO.File.GetLastWriteTime(path);
        }

        public static FileStream? Open(
            string path,
            System.IO.FileMode mode,
            System.IO.FileAccess access = System.IO.FileAccess.ReadWrite,
            System.IO.FileShare? share = null)
        {
            switch (mode)
            {
                case System.IO.FileMode.Create:
                case System.IO.FileMode.CreateNew:
                case System.IO.FileMode.OpenOrCreate:
                case System.IO.FileMode.Append:
                case System.IO.FileMode.Truncate:
                    if (!Validation.CanWrite(path, false))
                    {
                        DebugConsole.ThrowError($"Cannot open \"{path}\" in {mode} mode: modifying the contents of this folder/using this extension is not allowed.");
                        return null;
                    }
                    break;
            }
            access =
                !Validation.CanWrite(path, false) ?
                System.IO.FileAccess.Read :
                access;
            var shareVal = share ?? (access == System.IO.FileAccess.Read ? System.IO.FileShare.Read : System.IO.FileShare.None);
            return new FileStream(path, System.IO.File.Open(path, mode, access, shareVal));
        }

        public static FileStream? OpenRead(string path)
        {
            return Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
        }

        public static FileStream? OpenWrite(string path)
        {
            return Open(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write);
        }

        public static FileStream? Create(string path)
        {
            return Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            if (!Validation.CanWrite(path, false))
            {
                DebugConsole.ThrowError($"Cannot write all bytes to \"{path}\": modifying the files in this folder/with this extension is not allowed.");
                return;
            }
            System.IO.File.WriteAllBytes(path, contents);
        }

        public static void WriteAllText(string path, string contents, System.Text.Encoding? encoding = null)
        {
            if (!Validation.CanWrite(path, false))
            {
                DebugConsole.ThrowError($"Cannot write all text to \"{path}\": modifying the files in this folder/with this extension is not allowed.");
                return;
            }
            System.IO.File.WriteAllText(path, contents, encoding ?? System.Text.Encoding.UTF8);
        }

        public static void WriteAllLines(string path, IEnumerable<string> contents, System.Text.Encoding? encoding = null)
        {
            if (!Validation.CanWrite(path, false))
            {
                DebugConsole.ThrowError($"Cannot write all lines to \"{path}\": modifying the files in this folder/with this extension is not allowed.");
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
                if (!Validation.CanWrite(fileName, false)) { return false; }
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
            if (Validation.CanWrite(fileName, false))
            {
                innerStream.Write(buffer, offset, count);
            }
            else
            {
                DebugConsole.ThrowError($"Cannot write to file \"{fileName}\": modifying the files in this folder/with this extension is not allowed.");
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
            if (!Validation.CanWrite(innerInfo.FullName, false))
            {
                DebugConsole.ThrowError($"Cannot delete directory \"{Name}\": modifying the contents of this folder/using this extension is not allowed.");
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
                if (!Validation.CanWrite(innerInfo.FullName, false))
                {
                    DebugConsole.ThrowError($"Cannot set read-only to {value} for \"{Name}\": modifying the files in this folder/with this extension is not allowed.");
                    return;
                }
                innerInfo.IsReadOnly = value;
            }
        }

        public void CopyTo(string dest, bool overwriteExisting = false)
        {
            if (!Validation.CanWrite(dest, false))
            {
                DebugConsole.ThrowError($"Cannot copy \"{Name}\" to \"{dest}\": modifying the contents of the destination folder is not allowed.");
                return;
            }
            innerInfo.CopyTo(dest, overwriteExisting);
        }

        public void Delete()
        {
            if (!Validation.CanWrite(innerInfo.FullName, false))
            {
                DebugConsole.ThrowError($"Cannot delete file \"{Name}\": modifying the files in this folder/with this extension is not allowed.");
                return;
            }
            innerInfo.Delete();
        }
    }
}

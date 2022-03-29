#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public class Md5Hash
    {
        public static class Cache
        {
            private const string cachePath = "Data/hashcache.txt";

            private readonly static List<(string Path, Md5Hash Hash, DateTime DateTime)> Entries
                = new List<(string Path, Md5Hash Hash, DateTime DateTime)>();

            public static void Load()
            {
                if (!File.Exists(cachePath)) { return; }
                var lines = File.ReadAllLines(cachePath);
                if (Version.TryParse(lines[0], out var cacheVersion) && cacheVersion == GameMain.Version)
                {
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string[] split = lines[i].Split('|');
                        string path = split[0].CleanUpPathCrossPlatform();
                        Md5Hash hash = Md5Hash.StringAsHash(split[1]);
                        DateTime? dateTime = null;
                        if (long.TryParse(split[2], out long dateTimeUlong))
                        {
                            dateTime = DateTime.FromBinary(dateTimeUlong);
                        }

                        if (File.Exists(path) && dateTime.HasValue && dateTime >= File.GetLastWriteTime(path))
                        {
                            Entries.Add((path, hash, dateTime.Value));
                        }
                    }
                }
            }

            public static void Add(string path, Md5Hash hash, DateTime dateTime)
            {
                path = path.CleanUpPathCrossPlatform();
                Remove(path);
                Entries.Add((path, hash, dateTime));
            }

            public static void Remove(string path)
            {
                path = path.CleanUpPathCrossPlatform();
                Entries.RemoveAll(e => e.Path == path);
            }
        }

        public static readonly Md5Hash Blank = new Md5Hash(new string('0', 32));

        private static string RemoveWhitespace(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length / 2); // Reserve half the size of the original string because
                                                                // that's probably close enough to the size of the result
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i])) { continue; }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        //thanks to Jlobblet for this regex
        private static readonly Regex stringHashRegex = new Regex(@"^[0-9a-fA-F]{7,32}$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public readonly byte[] ByteRepresentation;
        public readonly string StringRepresentation;
        public readonly string ShortRepresentation;

        private static void CalculateHash(byte[] bytes, out string stringRepresentation, out byte[] byteRepresentation)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] byteHash = md5.ComputeHash(bytes);

                byteRepresentation = byteHash;
                stringRepresentation = ByteRepresentationToStringRepresentation(byteHash);
            }
        }

        private static string ByteRepresentationToStringRepresentation(byte[] byteHash)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < byteHash.Length; i++)
            {
                sb.Append(byteHash[i].ToString("X2"));
            }

            return sb.ToString();
        }

        private static byte[] StringRepresentationToByteRepresentation(string strHash)
        {
            var byteRepresentation = new byte[strHash.Length / 2];
            for (int i = 0; i < byteRepresentation.Length; i++)
            {
                byteRepresentation[i] = Convert.ToByte(strHash.Substring(i * 2, 2), 16);
            }

            return byteRepresentation;
        }

        public static string GetShortHash(string fullHash)
        {
            return fullHash.Length < 7 ? fullHash : fullHash.Substring(0, 7);
        }

        private Md5Hash(string md5Hash)
        {
            StringRepresentation = md5Hash;
            ByteRepresentation = StringRepresentationToByteRepresentation(StringRepresentation);
    
            ShortRepresentation = GetShortHash(md5Hash);
        }

        private Md5Hash(byte[] bytes, bool calculate)
        {
            if (calculate)
            {
                CalculateHash(bytes, out StringRepresentation, out ByteRepresentation);
            }
            else
            {
                StringRepresentation = ByteRepresentationToStringRepresentation(bytes);
                ByteRepresentation = bytes;
            }

            ShortRepresentation = GetShortHash(StringRepresentation);
        }

        public static Md5Hash StringAsHash(string hash)
        {
            if (!stringHashRegex.IsMatch(hash)) { throw new ArgumentException($"{hash} is not a valid hash"); }
            return new Md5Hash(hash);
        }

        public static Md5Hash CalculateForBytes(byte[] bytes)
        {
            return new Md5Hash(bytes, calculate: true);
        }

        public static Md5Hash BytesAsHash(byte[] bytes)
        {
            return new Md5Hash(bytes, calculate: false);
        }

        [Flags]
        public enum StringHashOptions
        {
            BytePerfect = 0,
            IgnoreCase = 0x1,
            IgnoreWhitespace = 0x2
        }

        public static Md5Hash CalculateForFile(string path, StringHashOptions options)
        {
            if (options.HasFlag(StringHashOptions.IgnoreWhitespace) || options.HasFlag(StringHashOptions.IgnoreCase))
            {
                string str = File.ReadAllText(path, Encoding.UTF8);
                return CalculateForString(str, options);
            }
            else
            {
                byte[] bytes = File.ReadAllBytes(path);
                return CalculateForBytes(bytes);
            }
        }

        public static Md5Hash CalculateForString(string str, StringHashOptions options)
        {
            if (options.HasFlag(StringHashOptions.IgnoreCase))
            {
                str = str.ToLowerInvariant();
            }
            if (options.HasFlag(StringHashOptions.IgnoreWhitespace))
            {
                str = RemoveWhitespace(str);
            }
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return CalculateForBytes(bytes);
        }

        public override string ToString()
        {
            return StringRepresentation;
        }

        public override bool Equals(object? obj)
        {
            if (obj is Md5Hash { StringRepresentation: { } otherStr })
            {
                string selfStr = otherStr.Length < StringRepresentation.Length
                       ? StringRepresentation[..otherStr.Length]
                       : StringRepresentation;
                otherStr = StringRepresentation.Length < otherStr.Length
                       ? otherStr[..StringRepresentation.Length]
                       : otherStr;
                return selfStr.Equals(otherStr, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static bool operator ==(Md5Hash? a, Md5Hash? b)
            => (a is null == b is null) && (a?.Equals(b) ?? true);

        public static bool operator !=(Md5Hash? a, Md5Hash? b) => !(a == b);
    }
}

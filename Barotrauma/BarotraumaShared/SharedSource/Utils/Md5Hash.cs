#nullable enable

using Barotrauma.IO;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public class Md5Hash
    {
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

        public override int GetHashCode()
        {
            return ShortRepresentation.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator ==(Md5Hash? a, Md5Hash? b)
            => Equals(a, b);

        public static bool operator !=(Md5Hash? a, Md5Hash? b) => !(a == b);
    }
}

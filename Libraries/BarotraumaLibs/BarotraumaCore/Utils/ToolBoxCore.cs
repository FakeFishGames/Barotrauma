using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma;

public static class ToolBoxCore
{
    public static string ByteArrayToHexString(IReadOnlyList<byte> ba)
    {
        var hex = new StringBuilder(ba.Count * 2);
        foreach (byte b in ba)
        {
            hex.AppendFormat("{0:X2}", b);
        }
        return hex.ToString();
    }

    public static byte[] HexStringToByteArray(string str)
    {
        var byteRepresentation = new byte[str.Length / 2];
        for (int i = 0; i < byteRepresentation.Length; i++)
        {
            byteRepresentation[i] = Convert.ToByte(str.Substring(i * 2, 2), 16);
        }

        return byteRepresentation;
    }

    public static bool IsHexadecimalDigit(this char c)
        => char.IsDigit(c)
            || c is (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    public static bool IsHexString(this string s)
        => !s.IsNullOrEmpty() && s.All(IsHexadecimalDigit);

    public static UInt32 IdentifierToUint32Hash(Identifier id, MD5 md5)
        => StringToUInt32Hash(id.Value.ToLowerInvariant(), md5);

    public static UInt32 StringToUInt32Hash(string str, MD5 md5)
    {
        //calculate key based on MD5 hash instead of string.GetHashCode
        //to ensure consistent results across platforms
        byte[] inputBytes = Encoding.UTF8.GetBytes(str);
        byte[] hash = md5.ComputeHash(inputBytes);

        UInt32 key = (UInt32)((str.Length & 0xff) << 24); //could use more of the hash here instead?
        key |= (UInt32)(hash[hash.Length - 3] << 16);
        key |= (UInt32)(hash[hash.Length - 2] << 8);
        key |= (UInt32)(hash[hash.Length - 1]);

        return key;
    }

    /// <summary>
    /// Convert a HSV value into a RGB value.
    /// </summary>
    /// <param name="hue">Value between 0 and 360</param>
    /// <param name="saturation">Value between 0 and 1</param>
    /// <param name="value">Value between 0 and 1</param>
    /// <see href="https://en.wikipedia.org/wiki/HSL_and_HSV#HSV_to_RGB">Reference</see>
    /// <returns></returns>
    public static Color HSVToRGB(float hue, float saturation, float value)
    {
        float c = value * saturation;

        float h = Math.Clamp(hue, 0, 360) / 60f;

        float x = c * (1 - Math.Abs(h % 2 - 1));

        float r = 0,
            g = 0,
            b = 0;

        if (0 <= h && h <= 1)     { r = c; g = x; b = 0; }
        else if (1 < h && h <= 2) { r = x; g = c; b = 0; }
        else if (2 < h && h <= 3) { r = 0; g = c; b = x; }
        else if (3 < h && h <= 4) { r = 0; g = x; b = c; }
        else if (4 < h && h <= 5) { r = x; g = 0; b = c; }
        else if (5 < h && h <= 6) { r = c; g = 0; b = x; }

        float m = value - c;

        return new Color(r + m, g + m, b + m);
    }

    public static Exception GetInnermost(this Exception e)
    {
        while (e.InnerException != null) { e = e.InnerException; }

        return e;
    }
}

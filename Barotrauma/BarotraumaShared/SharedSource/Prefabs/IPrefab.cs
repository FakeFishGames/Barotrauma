using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Barotrauma
{
    public interface IPrefab
    {
        string OriginalName { get; }
        string Identifier { get; }
        string FilePath { get; }
        ContentPackage ContentPackage { get; }
    }

    public interface IHasUintIdentifier
    {
        uint UIntIdentifier { get; set; }
    }

    public static class PrefabExtensions
    {
        public static void CalculatePrefabUIntIdentifier<T>(this T prefab, PrefabCollection<T> prefabs) where T : class, IPrefab, IHasUintIdentifier, IDisposable
        {
            using (MD5 md5 = MD5.Create())
            {
                prefab.UIntIdentifier = ToolBox.StringToUInt32Hash(prefab.Identifier, md5);

                //it's theoretically possible for two different values to generate the same hash, but the probability is astronomically small
                var collision = prefabs.Find(p => p != prefab && p.UIntIdentifier == prefab.UIntIdentifier);
                if (collision != null)
                {
                    DebugConsole.ThrowError($"Hashing collision when generating uint identifiers for {typeof(T).Name}: {prefab.Identifier} has the same identifier as {collision.Identifier} ({prefab.UIntIdentifier})");
                    collision.UIntIdentifier++;
                }
            }
        }
    }
}

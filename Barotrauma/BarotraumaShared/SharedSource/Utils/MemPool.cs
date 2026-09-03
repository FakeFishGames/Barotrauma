using System;
using System.Buffers;
namespace Barotrauma;
public static class FloatArrayPool
{
    private static readonly ArrayPool<float> Pool = ArrayPool<float>.Shared;

    public static float[] Rent(int size)
    {   
        return Pool.Rent(size);
    }
    public static float[] RentZeroed(int size)
    {
        float[] buffer = Pool.Rent(size);
        Array.Clear(buffer, 0, buffer.Length);
        return buffer;
    }
    public static void Return(float[] buffer)
    {
        Pool.Return(buffer, clearArray: true);
    }
}
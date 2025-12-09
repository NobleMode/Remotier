using System;
using System.Collections.Concurrent;

namespace Remotier.Services.Network
{
    public static class BufferPool
    {
        private static readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private const int BufferSize = 65536; // 64KB, standard UDP max payload usually around this (technically 65507)

        public static byte[] Rent()
        {
            if (_pool.TryTake(out byte[]? buffer))
            {
                return buffer;
            }
            return new byte[BufferSize];
        }

        public static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length != BufferSize) return;
            _pool.Add(buffer);
        }
    }
}

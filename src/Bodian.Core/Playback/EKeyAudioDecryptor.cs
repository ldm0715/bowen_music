using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Bodian.Core.Playback;

/// <summary>按 reverse/findings/09-ekey-decrypt-so.md 的公式实现 EKey / QMC2 解密。</summary>
public sealed class EKeyAudioDecryptor : IDisposable
{
    public const int SegmentSize = 0x1400;
    private readonly byte[] _key;
    private readonly byte[]? _state;
    private readonly uint _hash;

    internal EKeyAudioDecryptor(byte[] key)
    {
        if (key.Length == 0 || key.Length > 4096) { throw new InvalidDataException("音源解密密钥为空"); }
        _key = key.ToArray();
        if (_key.Length <= 300) { return; }
        _state = new byte[_key.Length];
        for (var i = 0; i < _state.Length; i++) { _state[i] = (byte)i; }
        var j = 0;
        for (var i = 0; i < _state.Length; i++)
        {
            j = (j + _state[i] + _key[i]) % _state.Length;
            (_state[i], _state[j]) = (_state[j], _state[i]);
        }
        _hash = CalculateHash(_key);
    }

    public static EKeyAudioDecryptor Create(string? ekey)
    {
        if (string.IsNullOrWhiteSpace(ekey)) { throw new InvalidDataException("该音源未提供解密密钥，继续使用原音质"); }
        if (ekey.Length > 16384) { throw new InvalidDataException("音源密钥长度超出支持范围"); }
        byte[]? bytes = null;
        byte[]? tail = null;
        try
        {
            bytes = Convert.FromBase64String(ekey);
            ReadOnlySpan<byte> prefix = "QQMusic EncV2,Key:"u8;
            if (bytes.AsSpan().StartsWith(prefix))
            {
                var stage1 = DecryptTea(bytes.AsSpan(prefix.Length), "386ZJY!@#*$%^&)("u8);
                try
                {
                    var stage2 = DecryptTea(stage1, "**#!(#$%&^a1cZ,T"u8);
                    try
                    {
                        var inner = Convert.FromBase64String(Encoding.ASCII.GetString(stage2));
                        CryptographicOperations.ZeroMemory(bytes);
                        bytes = inner;
                    }
                    finally { CryptographicOperations.ZeroMemory(stage2); }
                }
                finally { CryptographicOperations.ZeroMemory(stage1); }
            }
            if (bytes.Length < 24) { throw new InvalidDataException("音源解密密钥不完整"); }
            Span<byte> teaKey = stackalloc byte[16];
            ReadOnlySpan<byte> half = [0x69, 0x56, 0x46, 0x38, 0x2b, 0x20, 0x15, 0x0b];
            for (var i = 0; i < 8; i++) { teaKey[i * 2] = half[i]; teaKey[i * 2 + 1] = bytes[i]; }
            try { tail = DecryptTea(bytes.AsSpan(8), teaKey); }
            catch (InvalidDataException)
            {
                // qmc-decoder 的 raw-key 回退没有通过波点实网验证，不能把校验失败当成功。
                CryptographicOperations.ZeroMemory(teaKey);
                throw new InvalidDataException("服务端音源密钥未通过已支持的 EKey 格式校验");
            }
            var key = new byte[8 + tail.Length];
            bytes.AsSpan(0, 8).CopyTo(key);
            tail.CopyTo(key, 8);
            try { return new EKeyAudioDecryptor(key); }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(teaKey); }
        }
        catch (FormatException) { throw new InvalidDataException("音源解密密钥格式无效"); }
        finally
        {
            if (bytes is not null) { CryptographicOperations.ZeroMemory(bytes); }
            if (tail is not null) { CryptographicOperations.ZeroMemory(tail); }
        }
    }

    public void Decrypt(Span<byte> data, long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (_state is null)
        {
            for (var i = 0; i < data.Length; i++)
            {
                var pos = offset + i;
                if (pos > 0x7fff) { pos %= 0x7fff; }
                var index = (int)((pos * pos + 0x1162e) % _key.Length);
                var shift = (index + 4) & 7;
                data[i] ^= (byte)((_key[index] << shift) | (_key[index] >> shift));
            }
            return;
        }
        var consumed = 0;
        if (offset < 0x80)
        {
            var count = (int)Math.Min(0x80 - offset, data.Length);
            for (var i = 0; i < count; i++)
            {
                var pos = offset + i;
                var seed = _key[(int)(pos % _key.Length)];
                var k = SegmentKey(_hash, pos, seed);
                data[i] ^= _key[(int)(k % (ulong)_key.Length)];
            }
            consumed = count;
            offset += count;
        }
        Span<byte> state = stackalloc byte[_state.Length];
        while (consumed < data.Length)
        {
            _state.CopyTo(state);
            var segment = offset / SegmentSize;
            var seed = _key[(int)((segment & 0x1ff) % _key.Length)];
            var skip = (int)(SegmentKey(_hash, segment, seed) & 0x1ff) + (int)(offset % SegmentSize);
            var i = 0;
            var j = 0;
            for (var n = 0; n < skip; n++) { Next(state, ref i, ref j); }
            var count = Math.Min(SegmentSize - (int)(offset % SegmentSize), data.Length - consumed);
            for (var n = 0; n < count; n++) { data[consumed + n] ^= Next(state, ref i, ref j); }
            consumed += count;
            offset += count;
        }
        CryptographicOperations.ZeroMemory(state);
    }

    private static byte Next(Span<byte> state, ref int i, ref int j)
    {
        i = (i + 1) % state.Length;
        j = (j + state[i]) % state.Length;
        (state[i], state[j]) = (state[j], state[i]);
        return state[(state[i] + state[j]) % state.Length];
    }

    internal static uint CalculateHash(ReadOnlySpan<byte> key)
    {
        uint hash = 1;
        foreach (var value in key)
        {
            if (value == 0) { continue; }
            var next = unchecked(hash * value);
            if (next == 0 || next <= hash) { break; }
            hash = next;
        }
        return hash;
    }

    private static ulong SegmentKey(uint hash, long segment, byte seed) => seed == 0
        ? 0 : (ulong)((double)hash / ((segment + 1) * (double)seed) * 100.0);

    // TC-TEA: P[n] = D(C[n] XOR X[n-1]) XOR C[n-1]; X[n] = D(C[n] XOR X[n-1]).
    // 算法及独立测试向量参照 jixunmoe/tc_tea_rust (MIT / Apache-2.0)，未引入原生库。
    internal static byte[] DecryptTea(ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> key)
    {
        if (cipher.Length < 16 || cipher.Length % 8 != 0 || key.Length != 16)
        { throw new InvalidDataException("音源解密密钥长度无效"); }
        var output = new byte[cipher.Length];
        Span<uint> words = stackalloc uint[4];
        for (var i = 0; i < 4; i++) { words[i] = BinaryPrimitives.ReadUInt32BigEndian(key.Slice(i * 4, 4)); }
        ulong previousCipher = 0, previousIntermediate = 0;
        for (var i = 0; i < cipher.Length; i += 8)
        {
            var block = BinaryPrimitives.ReadUInt64BigEndian(cipher.Slice(i, 8));
            var encrypted = block ^ previousIntermediate;
            uint left = (uint)(encrypted >> 32), right = (uint)encrypted, sum = 0xe3779b90;
            unchecked
            {
                for (var round = 0; round < 16; round++)
                {
                    right -= ((left << 4) + words[2]) ^ (left + sum) ^ ((left >> 5) + words[3]);
                    left -= ((right << 4) + words[0]) ^ (right + sum) ^ ((right >> 5) + words[1]);
                    sum -= 0x9e3779b9;
                }
            }
            var intermediate = ((ulong)left << 32) | right;
            BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(i, 8), intermediate ^ previousCipher);
            previousCipher = block;
            previousIntermediate = intermediate;
        }
        var start = 3 + (output[0] & 7);
        var end = output.Length - 7;
        try
        {
            if (start > end || output.AsSpan(end).ContainsAnyExcept((byte)0))
            { throw new InvalidDataException("音源解密密钥校验失败"); }
            return output.AsSpan(start, end - start).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(output); }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        if (_state is not null) { CryptographicOperations.ZeroMemory(_state); }
    }
}

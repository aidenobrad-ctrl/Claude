using System;
using System.Text;
using UnityEngine;

namespace Pulsebreak.Shared.Net
{
    /// <summary>Writes values into a growing byte buffer, little-endian.</summary>
    public sealed class NetWriter
    {
        byte[] _data;
        int _length;

        public NetWriter(int capacity = 256)
        {
            _data = new byte[capacity];
        }

        public byte[] Data => _data;
        public int Length => _length;

        public void Reset() => _length = 0;

        void Ensure(int extra)
        {
            if (_length + extra <= _data.Length) return;
            Array.Resize(ref _data, Math.Max(_data.Length * 2, _length + extra));
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _data[_length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            _data[_length++] = (byte)value;
            _data[_length++] = (byte)(value >> 8);
        }

        public void WriteShort(short value) => WriteUShort((ushort)value);

        public void WriteUInt(uint value)
        {
            Ensure(4);
            _data[_length++] = (byte)value;
            _data[_length++] = (byte)(value >> 8);
            _data[_length++] = (byte)(value >> 16);
            _data[_length++] = (byte)(value >> 24);
        }

        public void WriteInt(int value) => WriteUInt((uint)value);

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteVector2(Vector2 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
        }

        public void WriteVector3(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }

        /// <summary>A string of at most 255 bytes of UTF-8. Longer text is cut short.</summary>
        public void WriteString(string value)
        {
            value = value ?? "";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            while (bytes.Length > 255)
            {
                value = value.Substring(0, value.Length - 1);
                bytes = Encoding.UTF8.GetBytes(value);
            }
            WriteByte((byte)bytes.Length);
            WriteBytes(bytes, 0, bytes.Length);
        }

        /// <summary>Text of up to <paramref name="maxBytes"/> bytes of UTF-8, for the few things longer than a name. Longer text is refused.</summary>
        public void WriteLongString(string value, int maxBytes = 1024)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > maxBytes) bytes = new byte[0];
            WriteUShort((ushort)bytes.Length);
            WriteBytes(bytes, 0, bytes.Length);
        }

        public void WriteBytes(byte[] source, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(source, offset, _data, _length, count);
            _length += count;
        }
    }

    /// <summary>
    /// Reads values back out of a byte buffer. What it reads came off the network, so it
    /// never trusts it: reading past the end, or a number that is not a number, sets
    /// <see cref="Failed"/> and returns zero rather than throwing. Check it when done.
    /// </summary>
    public sealed class NetReader
    {
        byte[] _data;
        int _position, _end;

        public bool Failed { get; private set; }
        public int Remaining => Failed ? 0 : _end - _position;
        public int Position => _position;
        public byte[] Data => _data;

        public void Reset(byte[] data, int offset, int length)
        {
            _data = data;
            _position = offset;
            _end = offset + length;
            Failed = false;
        }

        public void Fail() => Failed = true;

        bool Need(int count)
        {
            if (Failed || count < 0 || _position + count > _end)
            {
                Failed = true;
                return false;
            }
            return true;
        }

        public byte ReadByte() => Need(1) ? _data[_position++] : (byte)0;

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            if (!Need(2)) return 0;
            ushort value = (ushort)(_data[_position] | (_data[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public short ReadShort() => (short)ReadUShort();

        public uint ReadUInt()
        {
            if (!Need(4)) return 0;
            uint value = (uint)(_data[_position] | (_data[_position + 1] << 8) |
                                (_data[_position + 2] << 16) | (_data[_position + 3] << 24));
            _position += 4;
            return value;
        }

        public int ReadInt() => (int)ReadUInt();

        public float ReadFloat()
        {
            float value = BitConverter.Int32BitsToSingle(ReadInt());
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Failed = true;
                return 0f;
            }
            return value;
        }

        public Vector2 ReadVector2() => new Vector2(ReadFloat(), ReadFloat());

        public Vector3 ReadVector3() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());

        public string ReadString()
        {
            int count = ReadByte();
            if (!Need(count)) return "";
            string value = Encoding.UTF8.GetString(_data, _position, count);
            _position += count;
            return value;
        }

        public string ReadLongString(int maxBytes = 1024)
        {
            int count = ReadUShort();
            if (count > maxBytes)
            {
                Failed = true;
                return "";
            }
            if (!Need(count)) return "";
            string value = Encoding.UTF8.GetString(_data, _position, count);
            _position += count;
            return value;
        }

        public void Skip(int count)
        {
            if (Need(count)) _position += count;
        }
    }
}

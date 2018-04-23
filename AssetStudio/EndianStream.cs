﻿using System;
using System.Text;
using System.IO;

namespace UnityStudio
{
    public enum EndianType
    {
        BigEndian,
        LittleEndian
    }

    public class EndianStream : BinaryReader
    {
        public EndianType endian;
        private byte[] a16 = new byte[2];
        private byte[] a32 = new byte[4];
        private byte[] a64 = new byte[8];

        public EndianStream(Stream stream, EndianType endian) : base(stream)
        {
            this.endian = endian;
        }

        ~EndianStream()
        {
            Dispose();
        }

        public long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }

        public override byte ReadByte()
        {
            try
            {
                return base.ReadByte();
            }
            catch
            {
                return 0;
            }
        }

        public override short ReadInt16()
        {
            if (endian == EndianType.BigEndian)
            {
                a16 = ReadBytes(2);
                Array.Reverse(a16);
                return BitConverter.ToInt16(a16, 0);
            }
            return base.ReadInt16();
        }

        public override int ReadInt32()
        {
            if (endian == EndianType.BigEndian)
            {
                a32 = ReadBytes(4);
                Array.Reverse(a32);
                return BitConverter.ToInt32(a32, 0);
            }
            return base.ReadInt32();
        }

        public override long ReadInt64()
        {
            if (endian == EndianType.BigEndian)
            {
                a64 = ReadBytes(8);
                Array.Reverse(a64);
                return BitConverter.ToInt64(a64, 0);
            }
            return base.ReadInt64();
        }

        public override ushort ReadUInt16()
        {
            if (endian == EndianType.BigEndian)
            {
                a16 = ReadBytes(2);
                Array.Reverse(a16);
                return BitConverter.ToUInt16(a16, 0);
            }
            return base.ReadUInt16();
        }

        public override uint ReadUInt32()
        {
            if (endian == EndianType.BigEndian)
            {
                a32 = ReadBytes(4);
                Array.Reverse(a32);
                return BitConverter.ToUInt32(a32, 0);
            }
            return base.ReadUInt32();
        }

        public override ulong ReadUInt64()
        {
            if (endian == EndianType.BigEndian)
            {
                a64 = ReadBytes(8);
                Array.Reverse(a64);
                return BitConverter.ToUInt64(a64, 0);
            }
            return base.ReadUInt64();
        }

        public override float ReadSingle()
        {
            if (endian == EndianType.BigEndian)
            {
                a32 = ReadBytes(4);
                Array.Reverse(a32);
                return BitConverter.ToSingle(a32, 0);
            }
            return base.ReadSingle();
        }

        public override double ReadDouble()
        {
            if (endian == EndianType.BigEndian)
            {
                a64 = ReadBytes(8);
                Array.Reverse(a64);
                return BitConverter.ToUInt64(a64, 0);
            }
            return base.ReadDouble();
        }

        public void AlignStream(int alignment)
        {
            long pos = BaseStream.Position;
            if (pos % alignment != 0)
            {
                BaseStream.Position += alignment - pos % alignment;
            }
        }

        public string ReadAlignedString(int length)
        {
            if (length > 0 && length < BaseStream.Length - BaseStream.Position) //crude failsafe
            {
                byte[] stringData = new byte[length];
                Read(stringData, 0, length);
                string result = Encoding.UTF8.GetString(stringData); //must verify strange characters in PS3

                AlignStream(4);
                return result;
            }
            return "";
        }

        public string ReadStringToNull()
        {
            string result = "";
            for (int i = 0; i < BaseStream.Length; i++)
            {
                char c;
                if ((c = (char) base.ReadByte()) == 0)
                {
                    break;
                }
                result += c.ToString();
            }
            return result;
        }
    }
}

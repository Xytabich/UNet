using System;
using UdonSharp;
using UnityEngine;

namespace UNet
{
	/// <summary>
	/// Reads data structures from byte array
	/// BE byte order.
	/// </summary>
	public class ByteBufferReader : UdonSharpBehaviour
	{
		private const int BIT8 = 8;
		private const int BIT16 = 16;
		private const int BIT24 = 24;
		private const int BIT32 = 32;
		private const int BIT40 = 40;
		private const int BIT48 = 48;
		private const int BIT56 = 56;

		private const uint FLOAT_SIGN_BIT = 0x80000000;
		private const uint FLOAT_EXP_MASK = 0x7F800000;
		private const uint FLOAT_FRAC_MASK = 0x007FFFFF;

		private int[] decimalBuffer = new int[4];
		private byte[] guidBuffer = new byte[16];

		#region common types
		/// <summary>
		/// Reads boolean
		/// </summary>
		/// <remarks>Takes 1 byte</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public bool ReadBool(byte[] buffer, int index)
		{
			return buffer[index] == 1;
		}

		/// <summary>
		/// Reads char
		/// </summary>
		/// <remarks>Takes 2 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public char ReadChar(byte[] buffer, int index)
		{
			return (char)ReadInt16(buffer, index);
		}

		/// <summary>
		/// Reads signed 8-bit integer (<see cref="sbyte"/>)
		/// </summary>
		/// <remarks>Takes 1 byte</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public sbyte ReadSByte(byte[] buffer, int index)
		{
			int value = buffer[index];
			if(value > 0x80) value = value - 0xFF;
			return Convert.ToSByte(value);
		}

		/// <summary>
		/// Reads signed 16-bit integer (<see cref="short"/>)
		/// </summary>
		/// <remarks>Takes 2 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public short ReadInt16(byte[] buffer, int index)
		{
			int value = buffer[index] << BIT8 | buffer[index + 1];
			if(value > 0x8000) value = value - 0xFFFF;
			return Convert.ToInt16(value);
		}

		/// <summary>
		/// Reads unsigned 16-bit integer (<see cref="ushort"/>)
		/// </summary>
		/// <remarks>Takes 2 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public ushort ReadUInt16(byte[] buffer, int index)
		{
			return Convert.ToUInt16(buffer[index] << BIT8 | buffer[index + 1]);
		}

		/// <summary>
		/// Reads signed 32-bit integer (<see cref="int"/>)
		/// </summary>
		/// <remarks>Takes 4 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public int ReadInt32(byte[] buffer, int index)
		{
			int value = 0;
			value |= (int)buffer[index] << BIT24;
			index++;
			value |= (int)buffer[index] << BIT16;
			index++;
			value |= (int)buffer[index] << BIT8;
			index++;
			value |= (int)buffer[index];
			return value;
		}

		/// <summary>
		/// Reads unsigned 32-bit integer (<see cref="uint"/>)
		/// </summary>
		/// <remarks>Takes 4 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public uint ReadUInt32(byte[] buffer, int index)
		{
			uint value = 0;
			value |= (uint)buffer[index] << BIT24;
			index++;
			value |= (uint)buffer[index] << BIT16;
			index++;
			value |= (uint)buffer[index] << BIT8;
			index++;
			value |= (uint)buffer[index];
			return value;
		}

		/// <summary>
		/// Reads signed 64-bit integer (<see cref="long"/>)
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public long ReadInt64(byte[] buffer, int index)
		{
			long value = 0;
			value |= (long)buffer[index] << BIT56;
			index++;
			value |= (long)buffer[index] << BIT48;
			index++;
			value |= (long)buffer[index] << BIT40;
			index++;
			value |= (long)buffer[index] << BIT32;
			index++;
			value |= (long)buffer[index] << BIT24;
			index++;
			value |= (long)buffer[index] << BIT16;
			index++;
			value |= (long)buffer[index] << BIT8;
			index++;
			value |= (long)buffer[index];
			return value;
		}

		/// <summary>
		/// Reads unsigned 64-bit integer (<see cref="ulong"/>)
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public ulong ReadUInt64(byte[] buffer, int index)
		{
			ulong value = 0;
			value |= (ulong)buffer[index] << BIT56;
			index++;
			value |= (ulong)buffer[index] << BIT48;
			index++;
			value |= (ulong)buffer[index] << BIT40;
			index++;
			value |= (ulong)buffer[index] << BIT32;
			index++;
			value |= (ulong)buffer[index] << BIT24;
			index++;
			value |= (ulong)buffer[index] << BIT16;
			index++;
			value |= (ulong)buffer[index] << BIT8;
			index++;
			value |= (ulong)buffer[index];
			return value;
		}

		/// <summary>
		/// Reads single-precision floating-point number
		/// </summary>
		/// <remarks>Takes 4 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public float ReadSingle(byte[] buffer, int index)
		{
			uint value = ReadUInt32(buffer, index);
			if(value == 0 || value == FLOAT_SIGN_BIT) return 0f;

			int exp = (int)((value & FLOAT_EXP_MASK) >> 23);
			int frac = (int)(value & FLOAT_FRAC_MASK);
			bool negate = (value & FLOAT_SIGN_BIT) == FLOAT_SIGN_BIT;
			if(exp == 0xFF)
			{
				if(frac == 0)
				{
					return negate ? float.NegativeInfinity : float.PositiveInfinity;
				}
				return float.NaN;
			}

			bool normal = exp != 0x00;
			if(normal) exp -= 127;
			else exp = -126;

			float result = frac / (float)(2 << 22);
			if(normal) result += 1f;

			result *= Mathf.Pow(2, exp);
			if(negate) result = -result;
			return result;
		}
		#endregion

		#region special types
		/// <summary>
		/// Reads half-precision floating-point number
		/// </summary>
		/// <remarks>Takes 2 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public float ReadHalf(byte[] buffer, int index)
		{
			return Mathf.HalfToFloat(ReadUInt16(buffer, index));
		}

		/// <summary>
		/// Reads <see cref="DateTime"/> structure
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public DateTime ReadDateTime(byte[] buffer, int index)
		{
			return DateTime.FromBinary(ReadInt64(buffer, index));
		}

		/// <summary>
		/// Reads <see cref="TimeSpan"/> structure
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public TimeSpan ReadTimeSpan(byte[] buffer, int index)
		{
			return new TimeSpan(ReadInt64(buffer, index));
		}

		/// <summary>
		/// Reads decimal floating-point number
		/// </summary>
		/// <remarks>Takes 16 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public decimal ReadDecimal(byte[] buffer, int index)
		{
			decimalBuffer[0] = ReadInt32(buffer, index);
			index += 4;
			decimalBuffer[1] = ReadInt32(buffer, index);
			index += 4;
			decimalBuffer[2] = ReadInt32(buffer, index);
			index += 4;
			decimalBuffer[3] = ReadInt32(buffer, index);
			return new decimal(decimalBuffer);
		}

		/// <summary>
		/// Reads <see cref="Guid"/> structure
		/// </summary>
		/// <remarks>Takes 16 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Guid ReadGuid(byte[] buffer, int index)
		{
			for(var i = 0; i < 16; i++)
			{
				guidBuffer[i] = buffer[index + i];
			}
			return new Guid(guidBuffer);
		}

		/// <summary>
		/// Reads a variable-length unsigned 32-bit integer
		/// </summary>
		/// <remarks>Takes from 1 to 5 bytes. To get size of integer use <see cref="GetVarUInt32Size"/>.</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public uint ReadVarUInt32(byte[] buffer, int index)
		{
			uint value = 0;
			uint part;
			int bits = 0;
			do
			{
				part = buffer[index];
				value |= (part & 0x7F) << bits;
				index++;

				bits += 7;
				if(bits > 35)
				{
					Debug.LogError("Variable uint has invalid format");
					return 0;
				}
			}
			while((part & 0x80) != 0);

			return value;
		}

		/// <summary>
		/// Returns the number of bytes used for a variable-length unsigned integer.
		/// </summary>
		public int GetVarUInt32Size(byte[] buffer, int index)
		{
			int i = 0;
			while((buffer[index + i] & 0x80) != 0)
			{
				if(i > 4)
				{
					Debug.LogError("Variable uint has invalid format");
					return 0;
				}
				i++;
			}
			return i + 1;
		}
		#endregion

		#region unity types
		/// <summary>
		/// Reads <see cref="Vector2"/> structure
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector2 ReadVector2(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);

			return new Vector2(x, y);
		}

		/// <summary>
		/// Reads <see cref="Vector3"/> structure
		/// </summary>
		/// <remarks>Takes 12 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector3 ReadVector3(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);
			index += 4;
			float z = ReadSingle(buffer, index);

			return new Vector3(x, y, z);
		}

		/// <summary>
		/// Reads <see cref="Vector4"/> structure
		/// </summary>
		/// <remarks>Takes 16 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector4 ReadVector4(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);
			index += 4;
			float z = ReadSingle(buffer, index);
			index += 4;
			float w = ReadSingle(buffer, index);

			return new Vector4(x, y, z, w);
		}

		/// <summary>
		/// Reads <see cref="Quaternion"/> structure
		/// </summary>
		/// <remarks>Takes 16 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Quaternion ReadQuaternion(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);
			index += 4;
			float z = ReadSingle(buffer, index);
			index += 4;
			float w = ReadSingle(buffer, index);

			return new Quaternion(x, y, z, w);
		}

		/// <summary>
		/// Reads half-precision <see cref="Vector2"/> structure
		/// </summary>
		/// <remarks>Takes 4 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector2 ReadHalfVector2(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);

			return new Vector2(x, y);
		}

		/// <summary>
		/// Reads half-precision <see cref="Vector3"/> structure
		/// </summary>
		/// <remarks>Takes 6 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector3 ReadHalfVector3(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);
			index += 2;
			float z = ReadHalf(buffer, index);

			return new Vector3(x, y, z);
		}

		/// <summary>
		/// Reads half-precision <see cref="Vector4"/> structure
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Vector4 ReadHalfVector4(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);
			index += 2;
			float z = ReadHalf(buffer, index);
			index += 2;
			float w = ReadHalf(buffer, index);

			return new Vector4(x, y, z, w);
		}

		/// <summary>
		/// Reads half-precision <see cref="Quaternion"/> structure
		/// </summary>
		/// <remarks>Takes 8 bytes</remarks>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public Quaternion ReadHalfQuaternion(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);
			index += 2;
			float z = ReadHalf(buffer, index);
			index += 2;
			float w = ReadHalf(buffer, index);

			return new Quaternion(x, y, z, w);
		}
		#endregion

		#region strings
		/// <summary>
		/// Reads ascii string with <see cref="ReadVarUInt32"/> length prefix
		/// </summary>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public string ReadVarASCIIString(byte[] buffer, int index)
		{
			uint length = ReadVarUInt32(buffer, index);
			index += GetVarUInt32Size(buffer, index);
			return ReadASCIIString((int)length, buffer, index);
		}

		/// <summary>
		/// Reads the specified number of bytes and converts it to an ascii string
		/// </summary>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public string ReadASCIIString(int bytesCount, byte[] buffer, int index)
		{
			char[] chars = new char[bytesCount];
			for(var i = 0; i < bytesCount; i++)
			{
				chars[i] = Convert.ToChar(buffer[index + i] & 0x7F);
			}
			return new string(chars);
		}

		/// <summary>
		/// Reads utf-8 encoded string with <see cref="ReadVarUInt32"/> length prefix
		/// </summary>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public string ReadVarUTF8String(byte[] buffer, int index)
		{
			uint length = ReadVarUInt32(buffer, index);
			index += GetVarUInt32Size(buffer, index);
			return ReadUTF8String((int)length, buffer, index);
		}

		/// <summary>
		/// Reads the specified number of bytes and converts it to an utf-8 encoded string
		/// </summary>
		/// <param name="buffer">Target buffer</param>
		/// <param name="index">Index in the buffer where to start reading data</param>
		public string ReadUTF8String(int bytesCount, byte[] buffer, int index)
		{
			string str = "";
			int character = 0;
			int charCounter = 0;
			for(var i = 0; i < bytesCount; i++)
			{
				int value = buffer[index];
				if((value & 0x80) == 0)
				{
					str += (char)value;
					charCounter = 0;
				}
				else if((value & 0xC0) == 0x80)
				{
					if(charCounter > 0)
					{
						character = character << 6 | (value & 0x3F);
						charCounter--;
						if(charCounter == 0) str += char.ConvertFromUtf32(character);
					}
				}
				else if((value & 0xE0) == 0xC0)
				{
					charCounter = 1;
					character = value & 0x1F;
				}
				else if((value & 0xF0) == 0xE0)
				{
					charCounter = 2;
					character = value & 0x0F;
				}
				else if((value & 0xF8) == 0xF0)
				{
					charCounter = 3;
					character = value & 0x07;
				}
				index++;
			}
			return str;
		}
		#endregion
	}
}
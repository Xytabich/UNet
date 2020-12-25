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
		private const uint FLOAT_COEF_MASK = 0x007FFFFF;

		private int[] decimalBuffer = new int[4];
		private byte[] guidBuffer = new byte[16];

		#region common types
		public bool ReadBool(byte[] buffer, int index)
		{
			return buffer[index] == 1;
		}

		public char ReadChar(byte[] buffer, int index)
		{
			return (char)ReadInt16(buffer, index);
		}

		public sbyte ReadSByte(byte[] buffer, int index)
		{
			int value = buffer[index];
			if(value > 0x80) value = value - 0xFF;
			return Convert.ToSByte(value);
		}

		public short ReadInt16(byte[] buffer, int index)
		{
			int value = buffer[index] << BIT8 | buffer[index + 1];
			if(value > 0x8000) value = value - 0xFFFF;
			return Convert.ToInt16(value);
		}

		public ushort ReadUInt16(byte[] buffer, int index)
		{
			return Convert.ToUInt16(buffer[index] << BIT8 | buffer[index + 1]);
		}

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

		public float ReadSingle(byte[] buffer, int index)
		{
			uint value = ReadUInt32(buffer, index);
			if(value == 0 || value == FLOAT_SIGN_BIT) return 0f;

			if((value & FLOAT_EXP_MASK) == FLOAT_EXP_MASK)
			{
				if((value & FLOAT_COEF_MASK) == FLOAT_COEF_MASK) return float.NaN;
				return (value & FLOAT_SIGN_BIT) == FLOAT_SIGN_BIT ? float.NegativeInfinity : float.PositiveInfinity;
			}

			int exp = (int)((value & FLOAT_EXP_MASK) >> 23);
			float coeff = (float)(value & FLOAT_COEF_MASK) / (2 << (21 + (exp > 0 ? 1 : 0)));
			if(exp > 0) coeff += 1f;
			float result = coeff * Mathf.Pow(2, exp - 127);
			if((value & FLOAT_SIGN_BIT) == FLOAT_SIGN_BIT) result = -result;
			return result;
		}
		#endregion

		#region special types
		public float ReadHalf(byte[] buffer, int index)
		{
			return Mathf.HalfToFloat(ReadUInt16(buffer, index));
		}

		public DateTime ReadDateTime(byte[] buffer, int index)
		{
			return DateTime.FromBinary(ReadInt64(buffer, index));
		}

		public TimeSpan ReadTimeSpan(byte[] buffer, int index)
		{
			return new TimeSpan(ReadInt64(buffer, index));
		}

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

		public Guid ReadGuid(byte[] buffer, int index)
		{
			for(var i = 0; i < 16; i++)
			{
				guidBuffer[i] = buffer[index + i];
			}
			return new Guid(guidBuffer);
		}

		/// <summary>
		/// Reads a variable-length unsigned integer.
		/// </summary>
		/// <remarks>To get size of integer use <see cref="GetVarUInt32Size"/></remarks>
		public uint ReadVarUInt32(byte[] buffer, int index)
		{
			uint value = 0;
			uint part;
			int i = 0;
			do
			{
				value <<= 7;
				part = buffer[index];
				value |= part & 0x7F;

				i++;
				if(i > 4)
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
				i++;
				if(i > 4)
				{
					Debug.LogError("Variable uint has invalid format");
					return 0;
				}
			}
			return i + 1;
		}
		#endregion

		#region unity types
		public Vector2 ReadVector2(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);

			return new Vector2(x, y);
		}

		public Vector3 ReadVector3(byte[] buffer, int index)
		{
			float x = ReadSingle(buffer, index);
			index += 4;
			float y = ReadSingle(buffer, index);
			index += 4;
			float z = ReadSingle(buffer, index);

			return new Vector3(x, y, z);
		}

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
		public Vector2 ReadHalfVector2(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);

			return new Vector2(x, y);
		}

		public Vector3 ReadHalfVector3(byte[] buffer, int index)
		{
			float x = ReadHalf(buffer, index);
			index += 2;
			float y = ReadHalf(buffer, index);
			index += 2;
			float z = ReadHalf(buffer, index);

			return new Vector3(x, y, z);
		}

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
		public string ReadVarASCIIString(byte[] buffer, int index)
		{
			uint length = ReadVarUInt32(buffer, index);
			index += GetVarUInt32Size(buffer, index);
			return ReadASCIIString((int)length, buffer, index);
		}

		/// <summary>
		/// Reads the specified number of bytes and converts it to an ascii string
		/// </summary>
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
		/// Reads utf-8 string with <see cref="ReadVarUInt32"/> length prefix
		/// </summary>
		public string ReadVarUTF8String(byte[] buffer, int index)
		{
			uint length = ReadVarUInt32(buffer, index);
			index += GetVarUInt32Size(buffer, index);
			return ReadUTF8String((int)length, buffer, index);
		}

		/// <summary>
		/// Reads the specified number of bytes and converts it to an utf-8 string
		/// </summary>
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
using System;
using UdonSharp;
using UnityEngine;

namespace UNet
{
	/// <summary>
	/// Writes data structures to byte array.
	/// BE byte order.
	/// </summary>
	public class ByteBufferWriter : UdonSharpBehaviour
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

		#region common types
		public int WriteBool(bool value, byte[] buffer, int index)
		{
			if(value) buffer[index] = 1;
			else buffer[index] = 0;
			return 1;
		}

		public int WriteChar(char value, byte[] buffer, int index)
		{
			return WriteInt16((short)value, buffer, index);
		}

		public int WriteSByte(sbyte value, byte[] buffer, int index)
		{
			buffer[index] = (byte)(value < 0 ? (value + 0xFF) : value);
			return 1;
		}

		public int WriteInt16(short value, byte[] buffer, int index)
		{
			int tmp = value < 0 ? (value + 0xFFFF) : value;
			buffer[index] = (byte)(tmp >> BIT8);
			index++;
			buffer[index] = (byte)(tmp & 0xFF);
			return 2;
		}

		public int WriteUInt16(ushort value, byte[] buffer, int index)
		{
			int tmp = Convert.ToInt32(value);
			buffer[index] = (byte)(tmp >> BIT8);
			index++;
			buffer[index] = (byte)(tmp & 0xFF);
			return 2;
		}

		public int WriteInt32(int value, byte[] buffer, int index)
		{
			buffer[index] = (byte)((value >> BIT24) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT16) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT8) & 0xFF);
			index++;
			buffer[index] = (byte)(value & 0xFF);
			return 4;
		}

		public int WriteUInt32(uint value, byte[] buffer, int index)
		{
			buffer[index] = (byte)((value >> BIT24) & 255u);
			index++;
			buffer[index] = (byte)((value >> BIT16) & 255u);
			index++;
			buffer[index] = (byte)((value >> BIT8) & 255u);
			index++;
			buffer[index] = (byte)(value & 255u);
			return 4;
		}

		public int WriteInt64(long value, byte[] buffer, int index)
		{
			buffer[index] = (byte)((value >> BIT56) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT48) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT40) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT32) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT24) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT16) & 0xFF);
			index++;
			buffer[index] = (byte)((value >> BIT8) & 0xFF);
			index++;
			buffer[index] = (byte)(value & 0xFF);
			return 8;
		}

		public int WriteUInt64(ulong value, byte[] buffer, int index)
		{
			buffer[index] = (byte)((value >> BIT56) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT48) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT40) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT32) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT24) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT16) & 255ul);
			index++;
			buffer[index] = (byte)((value >> BIT8) & 255ul);
			index++;
			buffer[index] = (byte)(value & 255ul);
			return 8;
		}

		public int WriteSingle(float value, byte[] buffer, int index)
		{
			uint tmp = 0;
			if(float.IsNaN(value))
			{
				tmp = FLOAT_EXP_MASK | FLOAT_COEF_MASK;
			}
			else if(float.IsInfinity(value))
			{
				tmp = FLOAT_EXP_MASK;
				if(float.IsNegativeInfinity(value)) tmp |= FLOAT_SIGN_BIT;
			}
			else if(value != 0f)
			{
				if(value < 0f)
				{
					value = -value;
					tmp |= FLOAT_SIGN_BIT;
				}

				int exp = 127;
				while(value > 2f)
				{
					value *= 0.5f;
					exp++;
				}
				while(value < 1f)
				{
					value *= 2f;
					exp--;
					if(exp <= 0) break;
				}

				tmp |= Convert.ToUInt32(exp << 23) & FLOAT_EXP_MASK;
				tmp |= Convert.ToUInt32(value * (2 << (21 + (exp > 0 ? 1 : 0)))) & FLOAT_COEF_MASK;
			}
			return WriteUInt32(tmp, buffer, index);
		}
		#endregion

		#region special types
		public int WriteHalf(float value, byte[] buffer, int index)
		{
			return WriteUInt16(Mathf.FloatToHalf(value), buffer, index);
		}

		public int WriteDateTime(DateTime value, byte[] buffer, int index)
		{
			return WriteInt64(value.ToBinary(), buffer, index);
		}

		public int WriteTimeSpan(TimeSpan value, byte[] buffer, int index)
		{
			return WriteInt64(value.Ticks, buffer, index);
		}

		public int WriteDecimal(decimal value, byte[] buffer, int index)
		{
			var tmp = Decimal.GetBits(value);
			WriteInt32(tmp[0], buffer, index);
			index += 4;
			WriteInt32(tmp[1], buffer, index);
			index += 4;
			WriteInt32(tmp[2], buffer, index);
			index += 4;
			WriteInt32(tmp[3], buffer, index);
			return 16;
		}

		public int WriteGuid(Guid value, byte[] buffer, int index)
		{
			var tmp = value.ToByteArray();
			tmp.CopyTo(buffer, index);
			return 16;
		}

		public int WriteVarUInt32(uint value, byte[] buffer, int index)
		{
			int size = 1;
			while(value > 127)
			{
				buffer[index] = (byte)(value & 0x7F);
				value >>= 7;
				size++;
			}
			buffer[index] = (byte)value;
			return size;
		}
		#endregion

		#region unity types
		public int WriteVector2(Vector2 value, byte[] buffer, int index)
		{
			WriteSingle(value.x, buffer, index);
			index += 4;
			WriteSingle(value.y, buffer, index);
			return 8;
		}

		public int WriteVector3(Vector3 value, byte[] buffer, int index)
		{
			WriteSingle(value.x, buffer, index);
			index += 4;
			WriteSingle(value.y, buffer, index);
			index += 4;
			WriteSingle(value.z, buffer, index);
			return 12;
		}

		public int WriteVector4(Vector4 value, byte[] buffer, int index)
		{
			WriteSingle(value.x, buffer, index);
			index += 4;
			WriteSingle(value.y, buffer, index);
			index += 4;
			WriteSingle(value.z, buffer, index);
			index += 4;
			WriteSingle(value.w, buffer, index);
			return 16;
		}

		public int WriteQuaternion(Quaternion value, byte[] buffer, int index)
		{
			WriteSingle(value.x, buffer, index);
			index += 4;
			WriteSingle(value.y, buffer, index);
			index += 4;
			WriteSingle(value.z, buffer, index);
			index += 4;
			WriteSingle(value.w, buffer, index);
			return 16;
		}

		public int WriteHalfVector2(Vector2 value, byte[] buffer, int index)
		{
			WriteHalf(value.x, buffer, index);
			index += 2;
			WriteHalf(value.y, buffer, index);
			return 4;
		}

		public int WriteHalfVector3(Vector3 value, byte[] buffer, int index)
		{
			WriteHalf(value.x, buffer, index);
			index += 2;
			WriteHalf(value.y, buffer, index);
			index += 2;
			WriteHalf(value.z, buffer, index);
			return 6;
		}

		public int WriteHalfVector4(Vector4 value, byte[] buffer, int index)
		{
			WriteHalf(value.x, buffer, index);
			index += 2;
			WriteHalf(value.y, buffer, index);
			index += 2;
			WriteHalf(value.z, buffer, index);
			index += 2;
			WriteHalf(value.w, buffer, index);
			return 8;
		}

		public int WriteHalfQuaternion(Quaternion value, byte[] buffer, int index)
		{
			WriteHalf(value.x, buffer, index);
			index += 2;
			WriteHalf(value.y, buffer, index);
			index += 2;
			WriteHalf(value.z, buffer, index);
			index += 2;
			WriteHalf(value.w, buffer, index);
			return 8;
		}
		#endregion

		#region strings
		public int WriteASCIIString(string str, byte[] buffer, int index)
		{
			int len = str.Length;
			for(var i = 0; i < len; i++)
			{
				buffer[index + i] = (byte)(str[i] & 0x7F);
			}
			return len;
		}

		public int WriteVarASCIIString(string str, byte[] buffer, int index)
		{
			int size = WriteVarUInt32((uint)str.Length, buffer, index);
			index += size;
			size += WriteASCIIString(str, buffer, index);
			return size;
		}

		public int WriteUTF8String(string str, byte[] buffer, int index)
		{
			int byteIndex = 0;
			int len = str.Length;
			for(var i = 0; i < len; i++)
			{
				int value = char.ConvertToUtf32(str, i);
				if(value < 0x80)
				{
					buffer[index + byteIndex] = (byte)value;
				}
				else if(value < 0x0800)
				{
					buffer[index + byteIndex] = (byte)(value >> 6 | 0xC0);
					byteIndex++;
					buffer[index + byteIndex] = (byte)(value & 0x3F | 0x80);
				}
				else if(value < 0x010000)
				{
					buffer[index + byteIndex] = (byte)(value >> 12 | 0xE0);
					byteIndex++;
					buffer[index + byteIndex] = (byte)((value >> 6) & 0x3F | 0x80);
					byteIndex++;
					buffer[index + byteIndex] = (byte)(value & 0x3F | 0x80);
				}
				else
				{
					buffer[index + byteIndex] = (byte)(value >> 18 | 0xF0);
					byteIndex++;
					buffer[index + byteIndex] = (byte)((value >> 12) & 0x3F | 0x80);
					byteIndex++;
					buffer[index + byteIndex] = (byte)((value >> 6) & 0x3F | 0x80);
					byteIndex++;
					buffer[index + byteIndex] = (byte)(value & 0x3F | 0x80);
				}
				byteIndex++;
				if(char.IsSurrogate(str, i)) i++;
			}
			return byteIndex;
		}

		public int WriteVarUTF8String(string str, byte[] buffer, int index)
		{
			int size = GetUTF8StringSize(str);
			size = WriteVarUInt32((uint)size, buffer, index);
			index += size;
			size += WriteUTF8String(str, buffer, index);
			return size;
		}

		public int GetUTF8StringSize(string str)
		{
			int byteIndex = 0;
			int len = str.Length;
			for(var i = 0; i < len; i++)
			{
				int value = char.ConvertToUtf32(str, i);
				if(value >= 0x80)
				{
					if(value < 0x0800)
					{
						byteIndex++;
					}
					else if(value < 0x010000)
					{
						byteIndex += 2;
					}
					else
					{
						byteIndex += 3;
					}
				}
				byteIndex++;
				if(char.IsSurrogate(str, i)) i++;
			}
			return byteIndex;
		}
		#endregion
	}
}
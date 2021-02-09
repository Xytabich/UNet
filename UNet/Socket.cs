using System;
using UdonSharp;
using UnityEngine;

namespace UNet
{
	/// <summary>
	/// Handles network messages on connection
	/// </summary>
	public class Socket : UdonSharpBehaviour
	{
		private const byte MODE_UNRELIABLE = 0;
		private const byte MODE_RELIABLE = 1;
		private const byte MODE_RELIABLE_SEQUENCED = 2;
		private const byte RELIABLE_ACK = 3;

		private const byte TARGET_ALL = 0;
		private const byte TARGET_MASTER = 1 << 2;
		private const byte TARGET_SINGLE = 2 << 2;
		private const byte TARGET_MULTIPLE = 3 << 2;

		private const byte MSG_TYPE_MASK = 3;

		private const byte RELIABLE_ACK_MSG_HEADER = RELIABLE_ACK | TARGET_SINGLE;

		private const int MAX_PACKET_SIZE = 144;

		private const uint MASTER_EXPECT_MASK = 0xFFFFFFFF;

		// 2 bytes are used, because the messages can be targeted.
		// If many messages are sent to only one target, the rest may have an error when calculating the message id.
		// Also in this case it is sometimes necessary to send a message to all clients.
		private const int RELIABLE_IDS_COUNT = 65536;
		private const int RELIABLE_IDS_COUNT_HALF = 32768;
		// Sequenced messages have a smaller counter because they are awaiting confirmation from all clients.
		private const int RELIABLE_SEQUENCES_COUNT = 256;

		private const int RELIABLE_BUFFER_SIZE = 16;
		private const int UNRELIABLE_BUFFER_SIZE = 32;

		private const int RELIABLE_ACK_DATA_LENGTH = 6;//header + id(2 bytes) + mask(2 bytes) + connection

		private NetworkManager manager = null;
		private Connection connection = null;

		#region reliable send
		private int reliableStartId = 0;
		private uint reliableExpectMask = 0;
		private bool updateReliable = false;

		private uint reliableAttemptsMask = 0;
		private int reliableBufferIndex = 0;
		private int reliableBufferedCount = 0;

		private uint[] reliableExpectedAcks;
		private byte[][] reliableBuffer;
		private int[] reliableLengths;
		#endregion

		#region reliable sequenced
		private int reliableSequenceStartIndex = 0;
		private uint reliableSequenceTargets = 0;
		#endregion

		#region reliable ack
		private int[] sendAckStartIds;
		private uint[] sendAckMasks;
		#endregion

		#region reliable sequenced receive
		private int[] receiveSequencedStartIndices;
		private byte[][][] receiveSequencedBuffer;
		#endregion

		#region reliable receive
		private int[] receivedReliableStartIds;
		private uint[] receivedReliableMasks;
		#endregion

		#region unreliable send
		private int unreliableBufferIndex = 0;
		private int unreliableBufferedCount = 0;
		private byte[][] unreliableBuffer;
		private int[] unreliableLengths;
		#endregion

		#region connection variables cache
		private int dataBufferLength;
		private byte[] dataBuffer;
		#endregion

		private byte[] tmpDataBuffer;

		public void Init()
		{
			int connectionsCount = manager.totalConnectionsCount;
			dataBufferLength = 0;
			dataBuffer = new byte[MAX_PACKET_SIZE];
			unreliableBuffer = new byte[UNRELIABLE_BUFFER_SIZE][];
			unreliableLengths = new int[UNRELIABLE_BUFFER_SIZE];

			reliableExpectedAcks = new uint[RELIABLE_BUFFER_SIZE];
			reliableBuffer = new byte[RELIABLE_BUFFER_SIZE][];
			reliableLengths = new int[RELIABLE_BUFFER_SIZE];

			sendAckStartIds = new int[connectionsCount];
			sendAckMasks = new uint[connectionsCount];
			tmpDataBuffer = new byte[RELIABLE_ACK_DATA_LENGTH];

			receivedReliableMasks = new uint[connectionsCount];
			receivedReliableStartIds = new int[connectionsCount];
			for(var i = 0; i < connectionsCount; i++)
			{
				receivedReliableStartIds[i] = -1;
			}

			receiveSequencedStartIndices = new int[connectionsCount];
			receiveSequencedBuffer = new byte[connectionsCount][][];
			for(var i = 0; i < connectionsCount; i++)
			{
				receiveSequencedBuffer[i] = new byte[RELIABLE_BUFFER_SIZE][];
			}
		}

		public void OnConnectionRelease(int connectionIndex)
		{
			uint mask = (1u << connectionIndex) ^ 0xFFFFFFFF;
			for(var i = 0; i < RELIABLE_BUFFER_SIZE; i++)
			{
				reliableExpectedAcks[i] &= mask;
			}
			updateReliable = true;

			receivedReliableStartIds[connectionIndex] = -1;
			receivedReliableMasks[connectionIndex] = 0;
			sendAckStartIds[connectionIndex] = 0;
			sendAckMasks[connectionIndex] = 0;
			receiveSequencedStartIndices[connectionIndex] = 0;
			var buffer = receiveSequencedBuffer[connectionIndex];
			for(var i = 0; i < RELIABLE_BUFFER_SIZE; i++)
			{
				buffer[i] = null;
			}
		}

		#region add to buffer
		public bool SendAll(int mode, byte[] data, int count)
		{
			byte[] buffer = TryAddModeData(mode, manager.connectionsMask, 1, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_ALL);

			return true;
		}

		public bool SendMaster(int mode, byte[] data, int count)
		{
			byte[] buffer = TryAddModeData(mode, MASTER_EXPECT_MASK, 1, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_MASTER);

			return true;
		}

		public bool SendTarget(int mode, byte[] data, int count, int targetConnection)
		{
			byte[] buffer = TryAddModeData(mode, 1u << targetConnection, 2, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_SINGLE);

			int targetIndex = 1;
			if(mode == MODE_RELIABLE) targetIndex += 2;
			if(mode == MODE_RELIABLE_SEQUENCED) targetIndex += 3;
			buffer[targetIndex] = (byte)targetConnection;

			return true;
		}

		public bool SendTargets(int mode, byte[] data, int count, uint connectionsMask)
		{
			int maskSize = manager.connectionsMaskBytesCount;
			byte[] buffer = TryAddModeData(mode, connectionsMask, 1 + maskSize, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_MULTIPLE);

			int targetIndex = 1;
			if(mode == MODE_RELIABLE) targetIndex += 2;
			if(mode == MODE_RELIABLE_SEQUENCED) targetIndex += 3;

			buffer[targetIndex] = (byte)(connectionsMask & 255);
			if(maskSize > 1)
			{
				targetIndex++;
				buffer[targetIndex] = (byte)(connectionsMask >> 8 & 255);
				if(maskSize > 2)
				{
					targetIndex++;
					buffer[targetIndex] = (byte)(connectionsMask >> 16 & 255);
					if(maskSize > 3)
					{
						targetIndex++;
						buffer[targetIndex] = (byte)(connectionsMask >> 24 & 255);
					}
				}
			}

			return true;
		}

		private byte[] TryAddModeData(int mode, uint targets, int addSize, byte[] data, int dataSize)
		{
			if(mode == MODE_UNRELIABLE)
			{
				if(unreliableBufferedCount >= UNRELIABLE_BUFFER_SIZE) return null;

				int fullMsgSize = dataSize + 1 + addSize;
				if(fullMsgSize > MAX_PACKET_SIZE)
				{
					Debug.LogErrorFormat("Message is too long: {0} max size: {1}", fullMsgSize, MAX_PACKET_SIZE);
					return null;
				}

				int index = (unreliableBufferIndex + unreliableBufferedCount) % UNRELIABLE_BUFFER_SIZE;
				var buffer = FillMessageData(unreliableBuffer, index, data, dataSize, addSize);

				unreliableLengths[index] = fullMsgSize;
				unreliableBufferedCount++;
				return buffer;
			}
			else if(mode == MODE_RELIABLE || mode == MODE_RELIABLE_SEQUENCED)
			{
				if(updateReliable) UpdateReliable();

				if(reliableBufferedCount >= RELIABLE_BUFFER_SIZE) return null;

				int reliableHeadSize = 2;
				if(mode == MODE_RELIABLE_SEQUENCED) reliableHeadSize++;

				int fullMsgSize = dataSize + 1 + addSize + reliableHeadSize;
				if(fullMsgSize > MAX_PACKET_SIZE)
				{
					Debug.LogErrorFormat("Message is too long: {0} max size: {1}", fullMsgSize, MAX_PACKET_SIZE);
					return null;
				}

				int index = (reliableBufferIndex + reliableBufferedCount) % RELIABLE_BUFFER_SIZE;
				var buffer = FillMessageData(reliableBuffer, index, data, dataSize, addSize + reliableHeadSize);

				int id = (reliableStartId + reliableBufferedCount) % RELIABLE_IDS_COUNT;
				buffer[1] = (byte)(id >> 8 & 255);
				buffer[2] = (byte)(id & 255);
				reliableExpectMask = reliableExpectMask | (1u << reliableBufferedCount);

				reliableExpectedAcks[index] = manager.connectionsMask;
				if(mode == MODE_RELIABLE_SEQUENCED)
				{
					if(reliableSequenceTargets == 0 || reliableSequenceTargets != targets || reliableSequenceStartIndex >= RELIABLE_BUFFER_SIZE)
					{
						reliableSequenceTargets = targets;
						reliableSequenceStartIndex = 0;
					}
					buffer[3] = (byte)reliableSequenceStartIndex;
					reliableSequenceStartIndex++;
				}

				reliableLengths[index] = fullMsgSize;
				reliableBufferedCount++;
				return buffer;
			}
			return null;
		}

		private byte[] FillMessageData(byte[][] targetBuffer, int targetIndex, byte[] data, int dataLen, int dataIndex)
		{
			var buffer = targetBuffer[targetIndex];
			if(buffer == null)
			{
				buffer = new byte[MAX_PACKET_SIZE];
				targetBuffer[targetIndex] = buffer;
			}
			buffer[dataIndex] = (byte)dataLen;
			data.CopyTo(buffer, dataIndex + 1);
			return buffer;
		}
		#endregion

		#region reliable ack
		public void OnReceivedAck(int connection, int idStart, uint mask)
		{
			if(IdDiff(idStart, reliableStartId) > 0) mask <<= (idStart - reliableStartId + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;
			else mask >>= (reliableStartId - idStart + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;

			int index = reliableBufferIndex;
			while(mask > 0)
			{
				if((mask & 1) == 1)
				{
					uint expect = reliableExpectedAcks[index];
					if(expect == MASTER_EXPECT_MASK)
					{
						if(manager.IsMasterConnection(connection))
						{
							reliableExpectedAcks[index] = 0;
						}
					}
					else if((expect & (1u << connection)) != 0)
					{
						reliableExpectedAcks[index] = expect & ((1u << connection) ^ 0xFFFFFFFF);
					}
				}
				mask >>= 1;
				index = (index + 1) % RELIABLE_BUFFER_SIZE;
			}

			updateReliable = true;
		}

		private void UpdateReliable()
		{
			updateReliable = false;

			int count = 0;
			reliableExpectMask = 0;
			bool pick = true;
			for(var i = 0; i < reliableBufferedCount; i++)
			{
				int index = (reliableBufferIndex + i) % RELIABLE_BUFFER_SIZE;
				if(reliableExpectedAcks[index] == 0)
				{
					if(pick) count++;
				}
				else
				{
					pick = false;
					reliableExpectMask |= 1u << i;
				}
			}
			reliableAttemptsMask &= reliableExpectMask;

			if(count > 0)
			{
				reliableStartId = (reliableStartId + count) % RELIABLE_IDS_COUNT;
				reliableBufferedCount -= count;
				reliableBufferIndex = (reliableBufferIndex + count) % RELIABLE_BUFFER_SIZE;
				reliableAttemptsMask >>= count;
				reliableExpectMask >>= count;
			}
		}
		#endregion

		#region send
		public void PrepareSendStream()
		{
			if(updateReliable) UpdateReliable();

			dataBufferLength = 0;
			int len = sendAckMasks.Length;
			for(var i = 0; i < len; i++)
			{
				uint mask = sendAckMasks[i];
				if(mask != 0)
				{
					int id = sendAckStartIds[i];
					tmpDataBuffer[0] = RELIABLE_ACK_MSG_HEADER;
					tmpDataBuffer[1] = (byte)(id >> 8 & 255);
					tmpDataBuffer[2] = (byte)(id & 255);
					tmpDataBuffer[3] = (byte)(mask >> 8 & 255);
					tmpDataBuffer[4] = (byte)(mask & 255);
					tmpDataBuffer[5] = (byte)i;
					if(TryAddToBuffer(tmpDataBuffer, RELIABLE_ACK_DATA_LENGTH))
					{
						sendAckMasks[i] = 0;
					}
					else break;
				}
			}

			bool sendUnreliable = false;
			int reliableSendIndex = 0;
			int prevSequence = -1;
			bool sendSequenced = true;
			while(unreliableBufferedCount > 0 || reliableSendIndex < reliableBufferedCount)
			{
				if(sendUnreliable && unreliableBufferedCount > 0)
				{
					TryAddToBuffer(unreliableBuffer[unreliableBufferIndex], unreliableLengths[unreliableBufferIndex]);
					unreliableBufferIndex = (unreliableBufferIndex + 1) % UNRELIABLE_BUFFER_SIZE;
					unreliableBufferedCount--;
				}
				else if(reliableSendIndex < reliableBufferedCount)
				{
					uint mask = 1u << reliableSendIndex;
					if((reliableExpectMask & mask) != 0)
					{
						bool sendReliable = true;

						int index = (reliableBufferIndex + reliableSendIndex) % RELIABLE_BUFFER_SIZE;
						var buffer = reliableBuffer[index];
						if((buffer[0] & MSG_TYPE_MASK) == MODE_RELIABLE_SEQUENCED)
						{
							if(sendSequenced)
							{
								int sequence = buffer[3];
								// If the sequence is less than or equal to the previous value, then a new group has started
								// But only one group can be sent at a time 
								if(sequence > prevSequence)
								{
									prevSequence = sequence;
								}
								else sendSequenced = false;
							}
							
							sendReliable = sendSequenced;
						}

						if(sendReliable && (reliableAttemptsMask & mask) == 0)
						{
							TryAddToBuffer(buffer, reliableLengths[index]);
						}
						reliableAttemptsMask |= mask;
					}
					reliableSendIndex++;
				}
				sendUnreliable = !sendUnreliable;
			}
			if(reliableAttemptsMask == reliableExpectMask) reliableAttemptsMask = 0;
			connection.SetProgramVariable("dataBufferLength", dataBufferLength);
			connection.SetProgramVariable("dataBuffer", dataBuffer);
		}

		private bool TryAddToBuffer(byte[] data, int count)
		{
			if(dataBufferLength + count >= MAX_PACKET_SIZE) return false;
			for(var i = 0; i < count; i++)
			{
				dataBuffer[dataBufferLength + i] = data[i];
			}
			dataBufferLength += count;
			return true;
		}
		#endregion

		#region receive
		public void OnReceiveUnreliable(int connectionIndex, byte[] dataBuffer, int index, int len)
		{
			OnDataReceived(connectionIndex, dataBuffer, index, len);
		}

		public void OnReceiveReliable(int connectionIndex, int id, byte[] dataBuffer, int index, int len)
		{
			if(IsNewReliable(connectionIndex, id))
			{
				OnDataReceived(connectionIndex, dataBuffer, index, len);
			}
		}

		public void OnReceiveReliableSequenced(int connectionIndex, int id, int sequence, byte[] dataBuffer, int index, int len)
		{
			if(IsNewReliable(connectionIndex, id))
			{
				int sequenceStartIndex = receiveSequencedStartIndices[connectionIndex];
				if(sequence < sequenceStartIndex) sequenceStartIndex = 0;

				var connectionBuffer = receiveSequencedBuffer[connectionIndex];
				if(sequence == sequenceStartIndex)
				{
					OnDataReceived(connectionIndex, dataBuffer, index, len);
					sequenceStartIndex = sequence + 1;
					while(sequenceStartIndex < RELIABLE_BUFFER_SIZE)
					{
						var buffer = connectionBuffer[sequenceStartIndex];
						if(buffer == null) break;
						connectionBuffer[sequenceStartIndex] = null;
						sequenceStartIndex++;

						OnDataReceived(connectionIndex, buffer, 0, buffer.Length);
					}
				}
				else
				{
					// bad way but there is no better method for copying arrays
					connectionBuffer[sequence] = Convert.FromBase64String(Convert.ToBase64String(dataBuffer, index, len));
				}

				receiveSequencedStartIndices[connectionIndex] = sequenceStartIndex;
			}
		}

		private bool IsNewReliable(int connectionIndex, int id)
		{
			uint mask = sendAckMasks[connectionIndex];
			int minIndex;
			if(mask == 0)
			{
				sendAckStartIds[connectionIndex] = id;
				minIndex = id;
			}
			else
			{
				minIndex = sendAckStartIds[connectionIndex];
				if(IdDiff(id, minIndex) < 0)
				{
					mask <<= minIndex - id;
					minIndex = id;
					sendAckStartIds[connectionIndex] = id;
				}
			}
			uint bit = 1u << IdDiff(id, minIndex);
			sendAckMasks[connectionIndex] = mask | bit;

			int startId = receivedReliableStartIds[connectionIndex];
			if(startId < 0) startId = id;
			int offset = IdDiff(id, startId);
			if(offset > -RELIABLE_BUFFER_SIZE)
			{
				mask = receivedReliableMasks[connectionIndex];
				if(offset < 0)
				{
					mask <<= -offset;
					offset = 0;
					startId = id;
				}
				else if(offset >= RELIABLE_BUFFER_SIZE)
				{
					offset = RELIABLE_BUFFER_SIZE - 1;
					int newStart = (id - offset + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;
					int shift = IdDiff(newStart, startId);
					if(shift < RELIABLE_BUFFER_SIZE) mask >>= shift;
					startId = newStart;
				}

				if((mask & 1u << offset) == 0)
				{
					mask |= 1u << offset;
					receivedReliableMasks[connectionIndex] = mask;
					receivedReliableStartIds[connectionIndex] = startId;
					return true;
				}
			}
			return false;
		}

		private int IdDiff(int id, int reference)
		{
			if(id < RELIABLE_IDS_COUNT_HALF) id += RELIABLE_IDS_COUNT;
			if(reference < RELIABLE_IDS_COUNT_HALF) reference += RELIABLE_IDS_COUNT;
			return id - reference;
		}

		private void OnDataReceived(int connectionIndex, byte[] dataBuffer, int index, int length)
		{
			manager.OnDataReceived(this, connectionIndex, dataBuffer, index, length);
		}
		#endregion
	}
}
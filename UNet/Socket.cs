using UdonSharp;

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

		private const byte ACK_MSG_HEADER = RELIABLE_ACK | TARGET_SINGLE;

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

		private const int ACK_DATA_LENGTH = 6;

		/// <summary>
		/// Flush unreliable data buffers, if data doesn't fit into the packet
		/// </summary>
		public bool flushNotFitUnreliable = false;

		private NetworkManager manager = null;
		private Connection connection = null;

		#region reliable send
		private int reliableStartId = 0;
		private uint reliableExpectMask = 0;
		private bool updateReliable = false;

		private int reliableSequence = 0;
		private uint reliableSendMask = 0;
		private int reliableBufferIndex = 0;
		private int reliableBufferedCount = 0;

		private uint[] reliableExpectedAcks;
		private byte[][] reliableBuffer;
		private int[] reliableLengths;
		#endregion

		#region reliable ack
		private int[] sendAckStartIds;
		private uint[] sendAckMasks;
		private byte[] ackDataBuffer;
		#endregion

		#region reliable receive
		private int[] receiveSequencedBufferIndices;
		private byte[][][] receiveSequencedBuffer;
		private uint[] receiveSequencesIgnoreMask;

		private int[] receivedReliableSequences;
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
			ackDataBuffer = new byte[ACK_DATA_LENGTH];

			receivedReliableMasks = new uint[connectionsCount];
			receivedReliableStartIds = new int[connectionsCount];
			receivedReliableSequences = new int[connectionsCount];
			for(var i = 0; i < connectionsCount; i++)
			{
				receivedReliableStartIds[i] = -1;
				receivedReliableSequences[i] = -1;
			}

			receiveSequencesIgnoreMask = new uint[connectionsCount];
			receiveSequencedBufferIndices = new int[connectionsCount];
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
			receivedReliableSequences[connectionIndex] = -1;
			receivedReliableMasks[connectionIndex] = 0;
			sendAckStartIds[connectionIndex] = 0;
			sendAckMasks[connectionIndex] = 0;
			receiveSequencesIgnoreMask[connectionIndex] = 0;
			receiveSequencedBufferIndices[connectionIndex] = 0;
			var buffer = receiveSequencedBuffer[connectionIndex];
			for(var i = 0; i < RELIABLE_BUFFER_SIZE; i++)
			{
				buffer[i] = null;
			}
		}

		#region add to buffer
		public bool SendAll(int mode, byte[] data, int count)
		{
			byte[] buffer = TryAddModeData(mode, 1, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_ALL);

			if(mode == MODE_RELIABLE || mode == MODE_RELIABLE_SEQUENCED)
			{
				int index = (reliableBufferIndex + reliableBufferedCount - 1) % RELIABLE_BUFFER_SIZE;
				reliableExpectedAcks[index] = manager.connectionsMask;
			}

			return true;
		}

		public bool SendMaster(int mode, byte[] data, int count)
		{
			byte[] buffer = TryAddModeData(mode, 1, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_MASTER);

			if(mode == MODE_RELIABLE || mode == MODE_RELIABLE_SEQUENCED)
			{
				int index = (reliableBufferIndex + reliableBufferedCount - 1) % RELIABLE_BUFFER_SIZE;
				reliableExpectedAcks[index] = mode == MODE_RELIABLE_SEQUENCED ? manager.connectionsMask : MASTER_EXPECT_MASK;
			}

			return true;
		}

		public bool SendTarget(int mode, byte[] data, int count, int targetConnection)
		{
			byte[] buffer = TryAddModeData(mode, 2, data, count);
			if(buffer == null) return false;
			buffer[0] = (byte)(mode | TARGET_SINGLE);

			int targetIndex = 1;
			if(mode == MODE_RELIABLE || mode == MODE_RELIABLE_SEQUENCED)
			{
				targetIndex += 2 + (mode == MODE_RELIABLE_SEQUENCED ? 1 : 0);
				int index = (reliableBufferIndex + reliableBufferedCount - 1) % RELIABLE_BUFFER_SIZE;
				reliableExpectedAcks[index] = mode == MODE_RELIABLE_SEQUENCED ? manager.connectionsMask : (1u << targetConnection);
			}
			buffer[targetIndex] = (byte)targetConnection;

			return true;
		}

		public bool SendTargets(int mode, byte[] data, int count, int[] targetConnections)
		{
			int maskSize = manager.connectionsMaskBytesCount;
			byte[] buffer = TryAddModeData(mode, 1 + maskSize, data, count);
			if(buffer == null) return false;

			buffer[0] = (byte)(mode | TARGET_MULTIPLE);

			uint connectionsMask = 0;
			int len = targetConnections.Length;
			for(var i = 0; i < len; i++)
			{
				connectionsMask |= 1u << targetConnections[i];
			}

			int targetIndex = 1;
			if(mode == MODE_RELIABLE || mode == MODE_RELIABLE_SEQUENCED)
			{
				targetIndex += 2 + (mode == MODE_RELIABLE_SEQUENCED ? 1 : 0);
				int index = (reliableBufferIndex + reliableBufferedCount - 1) % RELIABLE_BUFFER_SIZE;
				reliableExpectedAcks[index] = mode == MODE_RELIABLE_SEQUENCED ? manager.connectionsMask : connectionsMask;
			}

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

		private byte[] TryAddModeData(int mode, int addSize, byte[] data, int dataSize)
		{
			if(mode == MODE_UNRELIABLE)
			{
				if(unreliableBufferedCount >= UNRELIABLE_BUFFER_SIZE) return null;

				int fullMsgSize = dataSize + 1 + addSize;
				if(fullMsgSize > MAX_PACKET_SIZE)
				{
					UnityEngine.Debug.LogErrorFormat("Message is too long: {0} max size: {1}", fullMsgSize, MAX_PACKET_SIZE);
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

				int reliableHeadSize = 2 + (mode == MODE_RELIABLE_SEQUENCED ? 1 : 0);
				int fullMsgSize = dataSize + 1 + addSize + reliableHeadSize;
				if(fullMsgSize > MAX_PACKET_SIZE)
				{
					UnityEngine.Debug.LogErrorFormat("Message is too long: {0} max size: {1}", fullMsgSize, MAX_PACKET_SIZE);
					return null;
				}

				int index = (reliableBufferIndex + reliableBufferedCount) % RELIABLE_BUFFER_SIZE;
				var buffer = FillMessageData(reliableBuffer, index, data, dataSize, addSize + reliableHeadSize);

				int id = (reliableStartId + reliableBufferedCount) % RELIABLE_IDS_COUNT;
				buffer[1] = (byte)(id >> 8 & 255);
				buffer[2] = (byte)(id & 255);
				if(mode == MODE_RELIABLE_SEQUENCED)
				{
					buffer[3] = (byte)reliableSequence;
					reliableSequence = (reliableSequence + 1) % RELIABLE_SEQUENCES_COUNT;
				}
				reliableExpectMask = reliableExpectMask | (1u << reliableBufferedCount);

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
			if(IdGreaterThan(idStart, reliableStartId)) mask <<= (idStart - reliableStartId + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;
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
			reliableSendMask &= reliableExpectMask;

			if(count > 0)
			{
				reliableStartId = (reliableStartId + count) % RELIABLE_IDS_COUNT;
				reliableBufferedCount -= count;
				reliableBufferIndex = (reliableBufferIndex + count) % RELIABLE_BUFFER_SIZE;
				reliableSendMask >>= count;
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
					ackDataBuffer[0] = ACK_MSG_HEADER;
					ackDataBuffer[1] = (byte)(id >> 8 & 255);
					ackDataBuffer[2] = (byte)(id & 255);
					ackDataBuffer[3] = (byte)(mask >> 8 & 255);
					ackDataBuffer[4] = (byte)(mask & 255);
					ackDataBuffer[5] = (byte)i;
					if(TryAddToBuffer(ackDataBuffer, ACK_DATA_LENGTH))
					{
						sendAckMasks[i] = 0;
					}
					else break;
				}
			}

			bool sendUnreliable = true;
			bool sendOrder = false;
			int reliableSendIndex = 0;
			while(true)
			{
				if(sendOrder && sendUnreliable)
				{
					if(unreliableBufferedCount > 0)
					{
						var buffer = unreliableBuffer[unreliableBufferIndex];
						var bufferLength = unreliableLengths[unreliableBufferIndex];
						if(TryAddToBuffer(buffer, bufferLength))
						{
							unreliableBufferIndex = (unreliableBufferIndex + 1) % UNRELIABLE_BUFFER_SIZE;
							unreliableBufferedCount--;
						}
						else sendUnreliable = false;
					}
					else sendUnreliable = false;
				}
				else if(reliableSendIndex < reliableBufferedCount)
				{
					uint mask = 1u << reliableSendIndex;
					if((reliableExpectMask & mask) == mask && (reliableSendMask & mask) == 0)
					{
						int index = (reliableBufferIndex + reliableSendIndex) % RELIABLE_BUFFER_SIZE;
						var buffer = reliableBuffer[index];
						var bufferLength = reliableLengths[index];
						if(TryAddToBuffer(buffer, bufferLength))
						{
							reliableSendMask |= mask;
						}
					}
					reliableSendIndex++;
				}
				else if(!sendUnreliable) break;

				sendOrder = !sendOrder;
			}
			if(reliableSendMask == reliableExpectMask) reliableSendMask = 0;
			connection.SetProgramVariable("dataBufferLength", dataBufferLength);
			connection.SetProgramVariable("dataBuffer", dataBuffer);

			if(flushNotFitUnreliable)
			{
				unreliableBufferIndex = 0;
				unreliableBufferedCount = 0;
			}
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
				int lastSequence = receivedReliableSequences[connectionIndex];
				if(lastSequence < 0 || sequence == (lastSequence + 1) % RELIABLE_SEQUENCES_COUNT)
				{
					OnDataReceived(connectionIndex, dataBuffer, index, len);
					int count = ApplyBufferedSequences(connectionIndex, 0);
					receivedReliableSequences[connectionIndex] = (sequence + count) % RELIABLE_SEQUENCES_COUNT;
				}
				else
				{
					int bufferIndex = (sequence - lastSequence + RELIABLE_SEQUENCES_COUNT) % RELIABLE_SEQUENCES_COUNT;
					bufferIndex = (receiveSequencedBufferIndices[connectionIndex] + bufferIndex) % RELIABLE_BUFFER_SIZE;

					var data = new byte[len];
					for(var i = 0; i < len; i++)
					{
						data[i] = dataBuffer[index + i];
					}
					receiveSequencedBuffer[connectionIndex][bufferIndex] = data;
				}
			}
		}

		public void MarkReliableSequence(int connectionIndex, int id, int sequence)
		{
			if(IsNewReliable(connectionIndex, id))
			{
				int lastSequence = receivedReliableSequences[connectionIndex];
				if(lastSequence < 0 || sequence == (lastSequence + 1) % RELIABLE_SEQUENCES_COUNT)
				{
					int count = ApplyBufferedSequences(connectionIndex, 0);
					receivedReliableSequences[connectionIndex] = (sequence + count) % RELIABLE_SEQUENCES_COUNT;
				}
				else
				{
					receiveSequencesIgnoreMask[connectionIndex] |= 1u << (lastSequence - sequence);
				}
			}
		}

		private int ApplyBufferedSequences(int connectionIndex, int readedCount)
		{
			var buffer = receiveSequencedBuffer[connectionIndex];
			int bufferIndex = receiveSequencedBufferIndices[connectionIndex];
			bufferIndex = (bufferIndex + readedCount) % RELIABLE_BUFFER_SIZE;

			uint mask = receiveSequencesIgnoreMask[connectionIndex];
			mask >>= readedCount;

			uint bit = (mask & 1u);
			byte[] data = null;
			while(bit == 1 || (data = buffer[bufferIndex]) != null)
			{
				if(bit == 0)
				{
					OnDataReceived(connectionIndex, data, 0, data.Length);
					buffer[bufferIndex] = null;
				}
				mask >>= 1;
				bit = (mask & 1u);
				bufferIndex = (bufferIndex + 1) % RELIABLE_BUFFER_SIZE;
				readedCount++;
			}
			receiveSequencedBufferIndices[connectionIndex] = bufferIndex;
			receiveSequencesIgnoreMask[connectionIndex] = mask;
			return readedCount;
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
				if(id < minIndex)
				{
					mask <<= minIndex - id;
					minIndex = id;
					sendAckStartIds[connectionIndex] = id;
				}
			}
			uint bit = 1u << (id - minIndex);
			sendAckMasks[connectionIndex] = mask | bit;

			int startId = receivedReliableStartIds[connectionIndex];
			if(startId < 0 || IdGreaterThan(id, startId))
			{
				mask = receivedReliableMasks[connectionIndex];
				int bitIndex;
				if(startId < 0)
				{
					bitIndex = 0;
					startId = id;
				}
				else
				{
					bitIndex = (id - startId + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;

					if(bitIndex >= RELIABLE_BUFFER_SIZE)
					{
						bitIndex = RELIABLE_BUFFER_SIZE - 1;
						int newStart = (id - bitIndex + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;
						int shift = (newStart - startId + RELIABLE_IDS_COUNT) % RELIABLE_IDS_COUNT;
						if(shift < RELIABLE_BUFFER_SIZE) mask >>= shift;
						startId = newStart;
					}
				}

				if((mask & 1u << bitIndex) == 0)
				{
					mask |= 1u << bitIndex;
					receivedReliableMasks[connectionIndex] = mask;
					receivedReliableStartIds[connectionIndex] = startId;
					return true;
				}
			}
			return false;
		}

		private bool IdGreaterThan(int id, int reference)
		{
			return (id > reference && (id - reference) < RELIABLE_IDS_COUNT_HALF) || (id < reference && ((reference - id) >= RELIABLE_IDS_COUNT_HALF));
		}

		private void OnDataReceived(int connectionIndex, byte[] dataBuffer, int index, int length)
		{
			manager.OnDataReceived(this, connectionIndex, dataBuffer, index, length);
		}
		#endregion
	}
}
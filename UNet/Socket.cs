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
		private const byte TYPE_NORMAL = 0;
		private const byte TYPE_SEQUENCED = 1;
		private const byte TYPE_ACK = 2;

		private const byte TARGET_ALL = 0;
		private const byte TARGET_MASTER = 1 << 2;
		private const byte TARGET_SINGLE = 2 << 2;
		private const byte TARGET_MULTIPLE = 3 << 2;

		private const byte MSG_TARGET_MASK = 3 << 2;
		private const int LENGTH_BYTES_COUNT = 2;

		private const byte ACK_MSG_HEADER = TYPE_ACK | TARGET_SINGLE;

		private const int MAX_MESSAGE_SIZE = 512;
		private const int MAX_PACKET_SIZE = 2048;

		// 2 bytes are used, because the messages can be targeted.
		// If many messages are sent to only one target, the rest may have an error when calculating the message id.
		// Also in this case it is sometimes necessary to send a message to all clients.
		private const int IDS_COUNT = 65536;
		private const int IDS_COUNT_HALF = 32768;

		/// <remarks>The buffer size depends on the size of the masks (or more precisely, the number of bits in the masks)</remarks>
		private const int BUFFER_SIZE = 16;

		private const int ACK_DATA_LENGTH = 6;//header + id(2 bytes) + mask(2 bytes) + connection

		private NetworkManager manager = null;
		private Connection connection = null;

		private int connectionsMaskBytesCount;

		#region QueuedList<MessageSend> sendMessages;
		/// class MessageSend
		/// {
		/// 	public ulong expectedMask;
		/// 	public byte[] data;
		/// 	public int sequenceId;
		/// }
		private int sendMessages_index = 0;
		private int sendMessages_count = 0;
		private ulong[] sendMessages_values_expectedMask;
		private byte[][] sendMessages_values_data;
		private int[] sendMessages_values_sequenceId;
		#endregion

		#region OtherConnection[] otherConnections;
		/// class OtherConnection
		/// {
		/// 	public int ackStartId;
		/// 	public uint ackMessagesMask;
		/// 	public int receiveStartId;
		/// 	public uint receiveMessagesMask;
		/// 	public int sequenceStartIndex;
		/// 	public MessageInfo[] sequenceQueue;
		/// 
		/// 	struct MessageInfo
		/// 	{
		/// 		public int id;
		/// 		public byte[] data;
		/// 	}
		/// }
		private int[] otherConnections_ackStartId;
		private uint[] otherConnections_ackMessagesMask;
		private int[] otherConnections_receiveStartId;
		private uint[] otherConnections_receiveMessagesMask;
		private int[] otherConnections_sequenceStartIndex;
		private int[][] otherConnections_sequenceQueue_id;
		private byte[][][] otherConnections_sequenceQueue_data;
		#endregion

		#region send
		private int messageStartId = 0;
		private uint messagesToSend = 0;
		private uint sendAttemptsMask = 0;

		private int sequenceCounter = 0;
		private ulong lastSequenceGroup = 0ul;
		#endregion

		private byte[] packetFormationBuffer;
		private byte[] sendBufferReference;

		void FixedUpdate()
		{
			if(connection != null) connection.RequestSerialization();
		}

		public void Init(Connection connection, NetworkManager manager, int connectionsCount)
		{
			this.connection = connection;
			this.manager = manager;
			this.connectionsMaskBytesCount = manager.connectionsMaskBytesCount;

			packetFormationBuffer = new byte[MAX_PACKET_SIZE];
			connection.SetDataBuffer(packetFormationBuffer);

			sendMessages_values_expectedMask = new ulong[BUFFER_SIZE];
			sendMessages_values_data = new byte[BUFFER_SIZE][];
			sendMessages_values_sequenceId = new int[BUFFER_SIZE];

			otherConnections_ackStartId = new int[connectionsCount];
			otherConnections_ackMessagesMask = new uint[connectionsCount];

			otherConnections_receiveMessagesMask = new uint[connectionsCount];
			otherConnections_receiveStartId = new int[connectionsCount];
			for(var i = 0; i < connectionsCount; i++)
			{
				otherConnections_receiveStartId[i] = -1;
			}

			otherConnections_sequenceStartIndex = new int[connectionsCount];
			otherConnections_sequenceQueue_data = new byte[connectionsCount][][];
			otherConnections_sequenceQueue_id = new int[connectionsCount][];
			for(var i = 0; i < connectionsCount; i++)
			{
				otherConnections_sequenceQueue_data[i] = new byte[BUFFER_SIZE][];
				otherConnections_sequenceQueue_id[i] = new int[BUFFER_SIZE];
			}
		}

		public void OnConnectionRelease(int connectionIndex)
		{
			ulong mask = ~(1ul << connectionIndex);
			int index = sendMessages_index;
			bool shiftMessages = false;
			for(int i = 0; i < sendMessages_count; i++)
			{
				uint bit = 1u << index;
				if((messagesToSend & bit) != 0)
				{
					var target = sendMessages_values_data[index][0] & MSG_TARGET_MASK;
					if(target != TARGET_MASTER && (sendMessages_values_expectedMask[index] &= mask) == 0ul)
					{
						if(i == 0) shiftMessages = true;
						messagesToSend &= ~bit;
						sendMessages_values_data[index] = null;

						manager.OnSendComplete(this, (messageStartId + i) % IDS_COUNT, target != TARGET_SINGLE);
					}
				}
				index = (index + 1) % BUFFER_SIZE;
			}
			if(shiftMessages) ShiftMessages();
			sendAttemptsMask &= messagesToSend;

			otherConnections_ackStartId[connectionIndex] = 0;
			otherConnections_ackMessagesMask[connectionIndex] = 0;
			otherConnections_receiveStartId[connectionIndex] = -1;
			otherConnections_receiveMessagesMask[connectionIndex] = 0;
			otherConnections_sequenceStartIndex[connectionIndex] = 0;
			var buffer = otherConnections_sequenceQueue_data[connectionIndex];
			for(var i = 0; i < BUFFER_SIZE; i++)
			{
				buffer[i] = null;
			}
		}

		public void OnMasterLeave()
		{
			if(lastSequenceGroup == 0ul) sequenceCounter = 0;

			int index = sendMessages_index;
			bool shiftMessages = false;
			for(int i = 0; i < sendMessages_count; i++)
			{
				uint bit = 1u << index;
				if((messagesToSend & bit) != 0)
				{
					if((sendMessages_values_data[index][0] & MSG_TARGET_MASK) == TARGET_MASTER)
					{
						if(i == 0) shiftMessages = true;
						messagesToSend &= ~bit;
						sendMessages_values_data[index] = null;
						manager.OnSendComplete(this, (messageStartId + i) % IDS_COUNT, false);
					}
				}
				index = (index + 1) % BUFFER_SIZE;
			}
			if(shiftMessages) ShiftMessages();
			sendAttemptsMask &= messagesToSend;
		}

		public void CancelSend(int id)
		{
			int offset = id - messageStartId;
			if(offset < 0 || offset >= sendMessages_count)
			{
				Debug.LogErrorFormat("Message ID {0} is out of range", id);
				return;
			}

			int index = (sendMessages_index + offset) % BUFFER_SIZE;
			uint bit = 1u << index;
			if((messagesToSend & bit) != 0)
			{
				messagesToSend &= ~bit;
				sendMessages_values_data[index] = null;
				if(offset == 0) ShiftMessages();

				manager.OnSendComplete(this, id, false);
			}
			else
			{
				Debug.LogErrorFormat("Unable to cancel sending because message with ID {0} has already been sent", id);
			}
		}

		#region add to buffer
		public int SendAll(bool sequenced, byte[] data, int count)
		{
			int result = TryAddMessage(sequenced, manager.connectionsMask, 1, data, count);
			if(result < 0) return -1;

			sendBufferReference[0] = (byte)((sequenced ? TYPE_SEQUENCED : TYPE_NORMAL) | TARGET_ALL);
			return result;
		}

		public int SendMaster(bool sequenced, byte[] data, int count)
		{
			int result = TryAddMessage(sequenced, 0, 1, data, count);
			if(result < 0) return -1;

			sendBufferReference[0] = (byte)((sequenced ? TYPE_SEQUENCED : TYPE_NORMAL) | TARGET_MASTER);
			return result;
		}

		public int SendTarget(bool sequenced, byte[] data, int count, int targetConnection)
		{
			int result = TryAddMessage(sequenced, 1u << targetConnection, 2, data, count);
			if(result < 0) return -1;

			sendBufferReference[0] = (byte)((sequenced ? TYPE_SEQUENCED : TYPE_NORMAL) | TARGET_SINGLE);
			sendBufferReference[1] = (byte)targetConnection;
			return result;
		}

		public int SendTargets(bool sequenced, byte[] data, int count, ulong connectionsMask)
		{
			int result = TryAddMessage(sequenced, connectionsMask, 1 + connectionsMaskBytesCount, data, count);
			if(result < 0) return -1;

			sendBufferReference[0] = (byte)((sequenced ? TYPE_SEQUENCED : TYPE_NORMAL) | TARGET_MULTIPLE);
			for(int i = 0; i < connectionsMaskBytesCount; i++)
			{
				sendBufferReference[i + 1] = (byte)((connectionsMask >> (i * 8)) & 255ul);
			}
			return result;
		}

		/// <param name="buffer"><see cref="sendBufferReference"></param>
		private int TryAddMessage(bool sequenced, ulong targets, int headerSize, byte[] data, int dataSize/*, out byte[] buffer*/)
		{
			if(sendMessages_count >= BUFFER_SIZE) return -1;

			int messageHeaderSize = 2;
			if(sequenced) messageHeaderSize++;

			int fullMsgSize = headerSize + messageHeaderSize + LENGTH_BYTES_COUNT + dataSize;
			if(fullMsgSize > MAX_MESSAGE_SIZE)
			{
				Debug.LogErrorFormat("Message is too long: {0}, max size: {1}", fullMsgSize, MAX_MESSAGE_SIZE);
				return -1;
			}

			int id = (messageStartId + sendMessages_count) % IDS_COUNT;
			int index = (sendMessages_index + sendMessages_count) % BUFFER_SIZE;

			sendBufferReference = new byte[fullMsgSize];

			int headIndex = headerSize;
			sendBufferReference[headIndex] = (byte)((id >> 8) & 255);
			headIndex++;
			sendBufferReference[headIndex] = (byte)(id & 255);
			headIndex++;

			if(sequenced)
			{
				if(lastSequenceGroup != targets || sequenceCounter >= BUFFER_SIZE)
				{
					lastSequenceGroup = targets;
					sequenceCounter = 0;
				}
				sendBufferReference[headIndex] = (byte)sequenceCounter;
				headIndex++;
				sendMessages_values_sequenceId[index] = sequenceCounter;
				sequenceCounter++;
			}
			else
			{
				sendMessages_values_sequenceId[index] = -1;
			}

			sendBufferReference[headIndex] = (byte)((dataSize >> 8) & 255);
			headIndex++;
			sendBufferReference[headIndex] = (byte)(dataSize & 255);
			headIndex++;
			Array.Copy(data, 0, sendBufferReference, headIndex, dataSize);

			sendMessages_values_expectedMask[index] = targets;
			sendMessages_values_data[index] = sendBufferReference;
			messagesToSend = messagesToSend | (1u << index);
			sendMessages_count++;
			return id;
		}
		#endregion

		#region ack
		public void OnReceivedAck(int connection, int idStart, uint mask)
		{
			if(IdDiff(idStart, messageStartId) > 0) mask <<= (idStart - messageStartId + IDS_COUNT) % IDS_COUNT;
			else mask >>= (messageStartId - idStart + IDS_COUNT) % IDS_COUNT;

			bool shiftMessages = false;
			int i = 0;
			while(mask > 0)
			{
				if((mask & 1) != 0)
				{
					int index = (sendMessages_index + i) % BUFFER_SIZE;
					uint bit = 1u << index;
					if((messagesToSend & bit) != 0)
					{
						bool sendComplete = false;
						if((sendMessages_values_data[index][0] & MSG_TARGET_MASK) == TARGET_MASTER)
						{
							if(manager.IsMasterConnection(connection))
							{
								sendComplete = true;
							}
						}
						else
						{
							ulong expect = sendMessages_values_expectedMask[index];
							ulong expectBit = 1ul << connection;
							if((expect & expectBit) != 0ul)
							{
								expect &= ~expectBit;
								if(expect == 0ul)
								{
									sendComplete = true;
								}
								else
								{
									sendMessages_values_expectedMask[index] = expect;
								}
							}
						}
						if(sendComplete)
						{
							sendMessages_values_data[index] = null;
							messagesToSend &= ~bit;

							if(i == 0) shiftMessages = true;
							manager.OnSendComplete(this, (idStart + i) % IDS_COUNT, true);
						}
					}
				}
				mask >>= 1;
				i++;
			}
			if(shiftMessages) ShiftMessages();
			sendAttemptsMask &= messagesToSend;
		}

		private void ShiftMessages()
		{
			int count = 0;
			int index = sendMessages_index;
			while((messagesToSend & (1u << index)) == 0 && count < sendMessages_count)
			{
				count++;
				index = (index + 1) % BUFFER_SIZE;
			}
			messageStartId = (messageStartId + count) % IDS_COUNT;
			sendMessages_count -= count;
			sendMessages_index = (sendMessages_index + count) % BUFFER_SIZE;
		}
		#endregion

		#region send
		public int PrepareSendStream()
		{
			int dataBufferLength = 0;
			int len = otherConnections_ackMessagesMask.Length;
			for(var i = 0; i < len; i++)
			{
				uint mask = otherConnections_ackMessagesMask[i];
				if(mask != 0)
				{
					if((dataBufferLength + ACK_DATA_LENGTH) < MAX_PACKET_SIZE)
					{
						int id = otherConnections_ackStartId[i];
						packetFormationBuffer[dataBufferLength] = ACK_MSG_HEADER;
						dataBufferLength++;
						packetFormationBuffer[dataBufferLength] = (byte)i;
						dataBufferLength++;
						packetFormationBuffer[dataBufferLength] = (byte)((id >> 8) & 255);
						dataBufferLength++;
						packetFormationBuffer[dataBufferLength] = (byte)(id & 255);
						dataBufferLength++;
						packetFormationBuffer[dataBufferLength] = (byte)((mask >> 8) & 255);
						dataBufferLength++;
						packetFormationBuffer[dataBufferLength] = (byte)(mask & 255);
						dataBufferLength++;
						otherConnections_ackMessagesMask[i] = 0;
					}
					else break;
				}
			}

			int sendIndex = 0;
			int prevSequence = -1;
			bool canSendSequenced = true;
			while(sendIndex < sendMessages_count)
			{
				int index = (sendMessages_index + sendIndex) % BUFFER_SIZE;
				sendIndex++;

				uint bit = 1u << index;
				if((messagesToSend & bit) != 0 && (sendAttemptsMask & bit) == 0)
				{
					int sequence = sendMessages_values_sequenceId[index];
					if(sequence >= 0)
					{
						// If the sequence is less than or equal to the previous value, then a new group has started
						// But only one group can be sent at a time 
						if(canSendSequenced && sequence > prevSequence)
						{
							prevSequence = sequence;
						}
						else
						{
							sendAttemptsMask |= bit;
							continue;
						}
					}

					byte[] msgData = sendMessages_values_data[index];
					int msgLength = msgData.Length;
					if(dataBufferLength + msgLength < MAX_PACKET_SIZE)
					{
						Array.Copy(msgData, 0, packetFormationBuffer, dataBufferLength, msgLength);
						dataBufferLength += msgLength;

						sendAttemptsMask |= bit;
					}
				}
			}
			if(sendAttemptsMask == messagesToSend) sendAttemptsMask = 0;
			return dataBufferLength;
		}
		#endregion

		#region receive
		public void OnReceive(int connectionIndex, int id, byte[] dataBuffer, int index, int len)
		{
			if(IsNewMessage(connectionIndex, id))
			{
				manager.OnDataReceived(this, connectionIndex, dataBuffer, index, len, id);
			}
		}

		public void OnReceiveSequenced(int connectionIndex, int id, int sequence, byte[] dataBuffer, int index, int len)
		{
			if(IsNewMessage(connectionIndex, id))
			{
				int sequenceStartIndex = otherConnections_sequenceStartIndex[connectionIndex];
				if(sequence < sequenceStartIndex) sequenceStartIndex = 0;

				var ids = otherConnections_sequenceQueue_id[connectionIndex];
				var connectionBuffer = otherConnections_sequenceQueue_data[connectionIndex];
				if(sequence == sequenceStartIndex)
				{
					manager.OnDataReceived(this, connectionIndex, dataBuffer, index, len, id);
					sequenceStartIndex = sequence + 1;

					while(sequenceStartIndex < BUFFER_SIZE)
					{
						var buffer = connectionBuffer[sequenceStartIndex];
						if(buffer == null) break;
						connectionBuffer[sequenceStartIndex] = null;
						manager.OnDataReceived(this, connectionIndex, buffer, 0, buffer.Length, ids[sequenceStartIndex]);
						sequenceStartIndex++;
					}
				}
				else
				{
					var buffer = new byte[len];
					Array.Copy(dataBuffer, index, buffer, 0, len);
					connectionBuffer[sequence] = buffer;
					ids[sequence] = id;
				}

				otherConnections_sequenceStartIndex[connectionIndex] = sequenceStartIndex;
			}
		}

		private bool IsNewMessage(int connectionIndex, int id)
		{
			uint ackMask = otherConnections_ackMessagesMask[connectionIndex];
			int ackOffset = 0;
			if(ackMask == 0)
			{
				otherConnections_ackStartId[connectionIndex] = id;
			}
			else
			{
				ackOffset = IdDiff(id, otherConnections_ackStartId[connectionIndex]);
				if(ackOffset < 0)
				{
					ackMask <<= -ackOffset;
					ackOffset = 0;

					otherConnections_ackStartId[connectionIndex] = id;
				}
			}
			uint bit = 1u << ackOffset;
			otherConnections_ackMessagesMask[connectionIndex] = ackMask | bit;

			int startId = otherConnections_receiveStartId[connectionIndex];
			if(startId < 0) startId = id;

			int offset = IdDiff(id, startId);
			uint mask = otherConnections_receiveMessagesMask[connectionIndex];
			if(offset < 0)
			{
				if(offset <= -BUFFER_SIZE) mask = 0;
				else mask <<= -offset;
				offset = 0;
				startId = id;
			}
			else if(offset >= BUFFER_SIZE)
			{
				offset = BUFFER_SIZE - 1;
				int newStart = (id - offset + IDS_COUNT) % IDS_COUNT;
				int shift = IdDiff(newStart, startId);
				if(shift < BUFFER_SIZE) mask >>= shift;
				startId = newStart;
			}

			if((mask & (1u << offset)) == 0)
			{
				mask |= 1u << offset;
				otherConnections_receiveMessagesMask[connectionIndex] = mask;
				otherConnections_receiveStartId[connectionIndex] = startId;
				return true;
			}
			return false;
		}

		private int IdDiff(int id, int reference)
		{
			if(id < IDS_COUNT_HALF) id += IDS_COUNT;
			if(reference < IDS_COUNT_HALF) reference += IDS_COUNT;
			return id - reference;
		}
		#endregion
	}
}
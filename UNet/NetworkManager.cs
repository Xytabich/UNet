using System;
using Katsudon;
using UnityEngine;
using VRC.SDKBase;

namespace UNet
{
	/// <summary>
	/// Manages all network activity
	/// </summary>
	public class NetworkManager : MonoBehaviour
	{
		public const int MAX_MESSAGE_SIZE = 512;

		private const byte TYPE_NORMAL = 0;
		private const byte TYPE_SEQUENCED = 1;
		private const byte TYPE_ACK = 2;

		private const byte TARGET_ALL = 0;
		private const byte TARGET_MASTER = 1 << 2;
		private const byte TARGET_SINGLE = 2 << 2;
		private const byte TARGET_MULTIPLE = 3 << 2;

		private const byte MSG_TYPE_MASK = 3;
		private const byte MSG_TARGET_MASK = 3 << 2;
		private const int LENGTH_BYTES_COUNT = 2;

		[NonSerialized, HideInInspector]
		internal int activeConnectionsCount = 0;
		[NonSerialized, HideInInspector]
		internal ulong connectionsMask = 0ul;
		[NonSerialized, HideInInspector]
		internal int connectionsMaskBytesCount;

		/// <summary>
		/// Called when the network system is fully initialized and you can start sending data
		/// </summary>
		public event OnUNetInit onUNetInit;
		/// <summary>
		/// Called when the connected player is ready to receive messages.
		/// </summary>
		public event OnUNetConnected onUNetConnected;
		/// <summary>
		/// Called when another player has disconnected and resources have been released.
		/// </summary>
		public event OnUNetDisconnected onUNetDisconnected;
		/// <summary>
		/// Called before preparing the package for the next dispatch. Any data added in this callback will also participate in package preparation.
		/// </summary>
		public event OnUNetPrepareSend onUNetPrepareSend;
		/// <summary>
		/// Called when the socket has received a message.
		/// </summary>
		public event OnUNetReceived onUNetReceived;
		/// <summary>
		/// Called when the message has finished sending
		/// </summary>
		public event OnUNetSendComplete onUNetSendComplete;

		[Sync]
		private int masterConnection = -1;

		private int localConnectionIndex;

		private int totalConnectionsCount;

		private Connection[] allConnections;
		private int[] connectionsOwners;

		private int masterId = -1;
		private Socket socket;

		private bool hasLocal = false;
		private bool hasMaster = false;
		private bool isInitComplete = false;

		void Start()
		{
			var playersList = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
			foreach(var player in VRCPlayerApi.GetPlayers(playersList))
			{
				if(player.isMaster)
				{
					masterId = player.playerId;
					break;
				}
			}

			socket = gameObject.GetComponentInChildren<Socket>();
			allConnections = gameObject.GetComponentsInChildren<Connection>();
			totalConnectionsCount = allConnections.Length;
			connectionsMaskBytesCount = (totalConnectionsCount - 1) / 8 + 1;

			connectionsOwners = new int[totalConnectionsCount];
			for(var i = 0; i < totalConnectionsCount; i++)
			{
				connectionsOwners[i] = -1;
				allConnections[i].Init(i, this);
			}
		}

		void OnPlayerJoined(VRCPlayerApi player)
		{
			if(Networking.IsMaster)
			{
				int index = -1;
				if(player.isLocal)
				{
					index = 0;
					hasMaster = true;
					masterConnection = index;
					this.RequestSerialization();
				}
				else
				{
					index = Array.IndexOf(connectionsOwners, -1);
				}
				if(index < 0) Debug.LogError("UNet does not have an unoccupied connection for a new player");
				else
				{
					OnOwnerReceived(index, player.playerId);
					Networking.SetOwner(player, allConnections[index].gameObject);
				}
			}
		}

		void OnPlayerLeft(VRCPlayerApi player)
		{
			if(player == null) return;

			int id = player.playerId;
			int index = Array.IndexOf(connectionsOwners, id);
			if(index >= 0) OnConnectionRelease(index);

			if(id == masterId)
			{
				var playersList = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
				foreach(var playerInfo in VRCPlayerApi.GetPlayers(playersList))
				{
					if(playerInfo.isMaster)
					{
						masterId = playerInfo.playerId;
						if(playerInfo.isLocal)
						{
							masterConnection = localConnectionIndex;
							this.RequestSerialization();
						}
						else
						{
							masterConnection = Array.IndexOf(connectionsOwners, masterId);
						}
						break;
					}
				}
				if(isInitComplete) socket.OnMasterLeave();
			}
		}

		void OnDeserialization()
		{
			if(masterConnection >= 0 && !isInitComplete)
			{
				hasMaster = true;
				OnOwnerReceived(masterConnection, masterId);
			}
		}

		#region public
		/// <summary>
		/// Returns true if the network system is fully initialized and you can send data
		/// </summary>
		public bool IsInitComplete()
		{
			return isInitComplete;
		}

		public bool HasOtherConnections()
		{
			return activeConnectionsCount > 1;
		}

		/// <summary>
		/// Returns max length of message for given options.
		/// </summary>
		/// <param name="sendTargetsCount">Target clients count, for <see cref="NetworkInterface.SendAll"/> and <see cref="NetworkInterface.SendMaster"/> is always 0.</param>
		/// <returns>Max length of message</returns>
		public int GetMaxDataLength(bool sequenced, int sendTargetsCount)
		{
			int len = MAX_MESSAGE_SIZE - 5;//header[byte] + length[ushort] + msg id[ushort]
			if(sequenced) len -= 1;//msg id[ushort] + sequence[byte]
			if(sendTargetsCount == 1) len -= 1;//connection index[byte]
			else if(sendTargetsCount > 1) len -= connectionsMaskBytesCount;
			return len;
		}

		/// <summary>
		/// Cancels the sending of the message with the given id.
		/// This operation cannot affect the message if it has already been delivered to the recipients.
		/// This method must be called before the end of the message delivery (OnUNetSendComplete event), otherwise it may disrupt the sending of other messages.
		/// </summary>
		public void CancelMessageSend(int messageId)
		{
			socket.CancelSend(messageId);
		}

		/// <summary>
		/// Sends message to other clients.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendAll(bool sequenced, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2) return -1;
			return socket.SendAll(sequenced, data, dataLength);
		}

		/// <summary>
		/// Sends message to master client only.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendMaster(bool sequenced, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2 || Networking.IsMaster) return -1;
			return socket.SendMaster(sequenced, data, dataLength);
		}

		/// <summary>
		/// Sends message to target client only.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="targetPlayerId">Target client <see cref="VRCPlayerApi.playerId"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendTarget(bool sequenced, byte[] data, int dataLength, int targetPlayerId)
		{
			if(activeConnectionsCount < 2) return -1;
			int index = Array.IndexOf(connectionsOwners, targetPlayerId);
			if(index < 0) return -1;
			return socket.SendTarget(sequenced, data, dataLength, index);
		}

		/// <summary>
		/// Sends message to target clients only.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="targetPlayerIds">Target clients <see cref="VRCPlayerApi.playerId"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendTargets(bool sequenced, byte[] data, int dataLength, int[] targetPlayerIds)
		{
			if(activeConnectionsCount < 2) return -1;
			if(targetPlayerIds.Length < 1) return -1;
			if(targetPlayerIds.Length == 1)
			{
				return socket.SendTarget(sequenced, data, dataLength, targetPlayerIds[0]);
			}
			uint targetsMask = 0;
			foreach(var playerId in targetPlayerIds)
			{
				int index = Array.IndexOf(connectionsOwners, playerId);
				if(index < 0) return -1;
				targetsMask = 1u << index;
			}
			return socket.SendTargets(sequenced, data, dataLength, targetsMask);
		}
		#endregion

		internal bool IsMasterConnection(int index)
		{
			return connectionsOwners[index] == masterId;
		}

		internal void HandlePacket(int connection, byte[] dataBuffer, int dataBufferLength)
		{
			if(!isInitComplete || connectionsOwners[connection] < 0) return;

			int index = 0;
			while(index < dataBufferLength)
			{
				int header = dataBuffer[index];
				int type = header & MSG_TYPE_MASK;
				int target = header & MSG_TARGET_MASK;

				index++;

				bool isTarget = false;
				switch(target)
				{
					case TARGET_ALL:
						isTarget = true;
						break;
					case TARGET_MASTER:
						isTarget = Networking.IsMaster;
						break;
					case TARGET_SINGLE:
						if(index >= dataBufferLength) return;
						isTarget = dataBuffer[index] == localConnectionIndex;
						index++;
						break;
					case TARGET_MULTIPLE:
						{
							if(index + connectionsMaskBytesCount > dataBufferLength) return;
							ulong mask = 0;
							for(int i = 0; i < connectionsMaskBytesCount; i++)
							{
								mask |= (ulong)dataBuffer[index] << (i * 8);
								index++;
							}
							ulong bit = 1ul << localConnectionIndex;
							isTarget = (mask & bit) == bit;
						}
						break;
				}

				int sequence = 0;
				switch(type)
				{
					case TYPE_NORMAL:
					case TYPE_SEQUENCED:
						if((index + LENGTH_BYTES_COUNT + (type == TYPE_SEQUENCED ? 3 : 2)) > dataBufferLength) return;
						if(isTarget)
						{
							int id = (dataBuffer[index] << 8) | dataBuffer[index + 1];
							index += 2;

							if(type == TYPE_SEQUENCED)
							{
								sequence = dataBuffer[index];
								index++;
							}

							int len = (dataBuffer[index] << 8) | dataBuffer[index + 1];
							index += LENGTH_BYTES_COUNT;

							if(index + len > dataBufferLength) return;

							if(type == TYPE_SEQUENCED)
							{
								socket.OnReceiveSequenced(connection, id, sequence, dataBuffer, index, len);
							}
							else
							{
								socket.OnReceive(connection, id, dataBuffer, index, len);
							}
							index += len;
						}
						else
						{
							index += 2;
							if(type == TYPE_SEQUENCED) index++;
							int len = (dataBuffer[index] << 8) | dataBuffer[index + 1];
							index += LENGTH_BYTES_COUNT;
							index += len;
						}
						break;
					case TYPE_ACK:
						if(index + 4 > dataBufferLength) return;
						if(isTarget)
						{
							int idStart = (dataBuffer[index] << 8) | dataBuffer[index + 1];
							index += 2;
							uint mask = ((uint)dataBuffer[index] << 8) | (uint)dataBuffer[index + 1];
							index += 2;

							socket.OnReceivedAck(connection, idStart, mask);
						}
						else
						{
							index += 4;
						}
						break;
				}
			}
		}

		internal int PrepareSendStream(int index)
		{
			if(!isInitComplete || connectionsOwners[index] < 0) return 0;

			onUNetPrepareSend?.Invoke();
			return socket.PrepareSendStream();
		}

		internal void OnOwnerReceived(int index, int playerId)
		{
			var connection = allConnections[index];
			if(connectionsOwners[index] < 0)
			{
				connectionsOwners[index] = playerId;
				activeConnectionsCount++;

				if(playerId == Networking.LocalPlayer.playerId)
				{
					localConnectionIndex = index;
					socket.Init(connection, this, totalConnectionsCount);

					hasLocal = true;
				}
				else
				{
					connectionsMask |= 1ul << index;
				}

				if(isInitComplete)
				{
					onUNetConnected?.Invoke(playerId);
				}
				else
				{
					if(hasLocal && hasMaster) Init();
				}
			}
		}

		internal void OnDataReceived(Socket socket, int connectionIndex, byte[] dataBuffer, int index, int length, int messageId)
		{
			int playerId = connectionsOwners[connectionIndex];
			onUNetReceived?.Invoke(playerId, dataBuffer, index, length, messageId);
		}

		internal void OnSendComplete(Socket socket, int messageId, bool succeed)
		{
			onUNetSendComplete?.Invoke(messageId, succeed);
		}

		private void Init()
		{
			isInitComplete = true;
			onUNetInit?.Invoke();
			if(onUNetConnected != null)
			{
				int localId = Networking.LocalPlayer.playerId;
				for(var i = 0; i < totalConnectionsCount; i++)
				{
					int owner = connectionsOwners[i];
					if(owner >= 0 && owner != localId)
					{
						onUNetConnected.Invoke(owner);
					}
				}
			}
		}

		private void OnConnectionRelease(int index)
		{
			var connection = allConnections[index];
			int owner = connectionsOwners[index];
			connectionsOwners[index] = -1;
			connectionsMask &= ~(1ul << index);
			socket.OnConnectionRelease(index);
			activeConnectionsCount--;

			onUNetDisconnected?.Invoke(owner);
		}
	}
}
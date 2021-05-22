using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace UNet
{
	/// <summary>
	/// Manages all network activity
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
	public class NetworkManager : UdonSharpBehaviour
	{
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
		public int activeConnectionsCount = 0;
		[NonSerialized, HideInInspector]
		public ulong connectionsMask = 0ul;
		[NonSerialized, HideInInspector]
		public int connectionsMaskBytesCount;

		[UdonSynced]
		private int masterConnection = -1;

		private int localConnectionIndex;

		private int totalConnectionsCount;

		private Connection[] allConnections;
		private int[] connectionsOwners;

		private int masterId = -1;
		private Socket socket;

		private int eventListenersCount = 0;
		private UdonSharpBehaviour[] eventListeners;

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

		public override void OnPlayerJoined(VRCPlayerApi player)
		{
			if(Networking.IsMaster)
			{
				int index = -1;
				if(player.isLocal)
				{
					index = 0;
					hasMaster = true;
					masterConnection = index;
					RequestSerialization();
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

		public override void OnPlayerLeft(VRCPlayerApi player)
		{
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
							RequestSerialization();
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

		public override void OnDeserialization()
		{
			if(masterConnection >= 0 && !isInitComplete)
			{
				hasMaster = true;
				OnOwnerReceived(masterConnection, masterId);
			}
		}

		public bool IsMasterConnection(int index)
		{
			return connectionsOwners[index] == masterId;
		}

		public void HandlePacket(int connection, byte[] dataBuffer, int dataBufferLength)
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

		public int PrepareSendStream(int index)
		{
			if(!isInitComplete || connectionsOwners[index] < 0) return 0;

			for(var i = 0; i < eventListenersCount; i++)
			{
				eventListeners[i].SendCustomEvent("OnUNetPrepareSend");
			}
			return socket.PrepareSendStream();
		}

		public void OnOwnerReceived(int index, int playerId)
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
					if(eventListeners != null)
					{
						for(var i = 0; i < eventListenersCount; i++)
						{
							var listener = eventListeners[i];
							listener.SetProgramVariable("OnUNetConnected_playerId", playerId);
							listener.SendCustomEvent("OnUNetConnected");
						}
					}
				}
				else
				{
					if(hasLocal && hasMaster) Init();
				}
			}
		}

		public void OnDataReceived(Socket socket, int connectionIndex, byte[] dataBuffer, int index, int length, int messageId)
		{
			if(eventListeners != null)
			{
				int playerId = connectionsOwners[connectionIndex];
				for(var i = 0; i < eventListenersCount; i++)
				{
					var listener = eventListeners[i];
					listener.SetProgramVariable("OnUNetReceived_sender", playerId);
					listener.SetProgramVariable("OnUNetReceived_dataBuffer", dataBuffer);
					listener.SetProgramVariable("OnUNetReceived_dataIndex", index);
					listener.SetProgramVariable("OnUNetReceived_dataLength", length);
					listener.SetProgramVariable("OnUNetReceived_messageId", messageId);
					listener.SendCustomEvent("OnUNetReceived");
				}
			}
		}

		public void OnSendComplete(Socket socket, int messageId, bool succeed)
		{
			if(eventListeners != null)
			{
				for(var i = 0; i < eventListenersCount; i++)
				{
					var listener = eventListeners[i];
					listener.SetProgramVariable("OnUNetSendComplete_messageId", messageId);
					listener.SetProgramVariable("OnUNetSendComplete_succeed", succeed);
					listener.SendCustomEvent("OnUNetSendComplete");
				}
			}
		}

		public void AddEventsListener(UdonSharpBehaviour listener)
		{
			if(eventListeners == null)
			{
				eventListeners = new UdonSharpBehaviour[1];
			}
			else if(eventListenersCount >= eventListeners.Length)
			{
				var tmp = new UdonSharpBehaviour[eventListenersCount * 2];
				eventListeners.CopyTo(tmp, 0);
				eventListeners = tmp;
			}

			eventListeners[eventListenersCount] = listener;
			eventListenersCount++;
		}

		public void RemoveEventsListener(UdonSharpBehaviour listener)
		{
			int index = Array.IndexOf(eventListeners, listener);
			if(index >= 0)
			{
				eventListenersCount--;
				Array.Copy(eventListeners, index + 1, eventListeners, index, eventListenersCount - index);
				eventListeners[eventListenersCount] = null;
			}
		}

		public void CancelMessageSend(int messageId)
		{
			socket.CancelSend(messageId);
		}

		public int SendAll(bool sequenced, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2) return -1;
			return socket.SendAll(sequenced, data, dataLength);
		}

		public int SendMaster(bool sequenced, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2 || Networking.IsMaster) return -1;
			return socket.SendMaster(sequenced, data, dataLength);
		}

		public int SendTarget(bool sequenced, byte[] data, int dataLength, int targetPlayerId)
		{
			if(activeConnectionsCount < 2) return -1;
			int index = Array.IndexOf(connectionsOwners, targetPlayerId);
			if(index < 0) return -1;
			return socket.SendTarget(sequenced, data, dataLength, index);
		}

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

		private void Init()
		{
			isInitComplete = true;
			if(eventListeners != null && eventListenersCount > 0)
			{
				for(var i = 0; i < eventListenersCount; i++)
				{
					eventListeners[i].SendCustomEvent("OnUNetInit");
				}

				int localId = Networking.LocalPlayer.playerId;
				for(var i = 0; i < totalConnectionsCount; i++)
				{
					int owner = connectionsOwners[i];
					if(owner >= 0 && owner != localId)
					{
						for(var j = 0; j < eventListenersCount; j++)
						{
							var listener = eventListeners[j];
							listener.SetProgramVariable("OnUNetConnected_playerId", owner);
							listener.SendCustomEvent("OnUNetConnected");
						}
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
			if(eventListeners != null)
			{
				for(var i = 0; i < eventListenersCount; i++)
				{
					var listener = eventListeners[i];
					listener.SetProgramVariable("OnUNetDisconnected_playerId", owner);
					listener.SendCustomEvent("OnUNetDisconnected");
				}
			}
		}
	}
}
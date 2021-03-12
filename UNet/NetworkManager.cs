using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace UNet
{
	/// <summary>
	/// Manages all network activity
	/// </summary>
	public class NetworkManager : UdonSharpBehaviour
	{
		private const byte MODE_UNRELIABLE = 0;
		private const byte MODE_RELIABLE = 1;
		private const byte MODE_RELIABLE_SEQUENCED = 2;
		private const byte RELIABLE_ACK = 3;

		private const byte TARGET_ALL = 0;
		private const byte TARGET_MASTER = 1;
		private const byte TARGET_SINGLE = 2;
		private const byte TARGET_MULTIPLE = 3;

		private const byte MSG_TYPE_MASK = 3;

		[UdonSynced]
		private int masterConnection = -1;

		[HideInInspector]
		public uint connectionsMask = 0;
		[HideInInspector]
		public int activeConnectionsCount = 0;

		private int localConnectionIndex;

		private int totalConnectionsCount;
		private int connectionsMaskBytesCount;

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

				var connection = allConnections[i];
				connection.SetProgramVariable("connectionIndex", i);
				connection.SetProgramVariable("manager", this);
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
						}
						else
						{
							masterConnection = Array.IndexOf(connectionsOwners, masterId);
						}
						break;
					}
				}
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
			if(!isInitComplete) return;

			int index = 0;
			while(index < dataBufferLength)
			{
				int header = dataBuffer[index];
				int type = header & MSG_TYPE_MASK;
				int target = (header >> 2) & 3;

				index++;
				if(type == MODE_UNRELIABLE)
				{
					if(ImTarget(target, dataBuffer, index))
					{
						index += GetTargetHeaderSize(target);

						int len = dataBuffer[index];
						index++;

						socket.OnReceiveUnreliable(connection, dataBuffer, index, len);
						index += len;
					}
					else
					{
						index += GetTargetHeaderSize(target);
						index += dataBuffer[index];
						index += 1;
					}
				}
				else if(type == MODE_RELIABLE)
				{
					if(ImTarget(target, dataBuffer, index + 2))
					{
						int id = dataBuffer[index] << 8 | dataBuffer[index + 1];
						index += 2;
						index += GetTargetHeaderSize(target);

						int len = dataBuffer[index];
						index++;

						socket.OnReceiveReliable(connection, id, dataBuffer, index, len);
						index += len;
					}
					else
					{
						index += 2;
						index += GetTargetHeaderSize(target);
						index += dataBuffer[index];
						index += 1;
					}
				}
				else if(type == MODE_RELIABLE_SEQUENCED)
				{
					if(ImTarget(target, dataBuffer, index + 3))
					{
						int id = dataBuffer[index] << 8 | dataBuffer[index + 1];
						index += 2;
						int sequence = dataBuffer[index];
						index++;
						index += GetTargetHeaderSize(target);

						int len = dataBuffer[index];
						index++;

						socket.OnReceiveReliableSequenced(connection, id, sequence, dataBuffer, index, len);
						index += len;
					}
					else
					{
						index += 3;
						index += GetTargetHeaderSize(target);
						index += dataBuffer[index];
						index += 1;
					}
				}
				else if(type == RELIABLE_ACK)
				{
					if(ImTarget(target, dataBuffer, index + 4))
					{
						int idStart = dataBuffer[index] << 8 | dataBuffer[index + 1];
						index += 2;
						uint mask = (uint)dataBuffer[index] << 8 | (uint)dataBuffer[index + 1];
						index += 2;
						index += GetTargetHeaderSize(target);

						socket.OnReceivedAck(connection, idStart, mask);
					}
					else
					{
						index += 4;
						index += GetTargetHeaderSize(target);
					}
				}
			}
		}

		public bool PrepareSendStream(int index)
		{
			if(!isInitComplete || connectionsOwners[index] < 0) return false;

			for(var i = 0; i < eventListenersCount; i++)
			{
				eventListeners[i].SendCustomEvent("OnUNetPrepareSend");
			}
			socket.PrepareSendStream();
			return true;
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
					connection.SetProgramVariable("socket", socket);
					socket.SetProgramVariable("connection", connection);
					socket.SetProgramVariable("manager", this);
					socket.Init(totalConnectionsCount, connectionsMaskBytesCount);

					hasLocal = true;
				}
				else
				{
					connectionsMask |= 1u << index;
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

		public void OnDataReceived(Socket socket, int connectionIndex, byte[] dataBuffer, int index, int length)
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
					listener.SendCustomEvent("OnUNetReceived");
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

		public bool SendAll(int mode, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2) return true;
			return socket.SendAll(mode, data, dataLength);
		}

		public bool SendMaster(int mode, byte[] data, int dataLength)
		{
			if(activeConnectionsCount < 2 || Networking.IsMaster) return true;
			return socket.SendMaster(mode, data, dataLength);
		}

		public bool SendTarget(int mode, byte[] data, int dataLength, int targetPlayerId)
		{
			if(activeConnectionsCount < 2) return true;
			int index = Array.IndexOf(connectionsOwners, targetPlayerId);
			if(index < 0) return false;
			return socket.SendTarget(mode, data, dataLength, index);
		}

		public bool SendTargets(int mode, byte[] data, int dataLength, int[] targetPlayerIds)
		{
			if(activeConnectionsCount < 2) return true;
			if(targetPlayerIds.Length < 1) return true;
			if(targetPlayerIds.Length == 1)
			{
				return socket.SendTarget(mode, data, dataLength, targetPlayerIds[0]);
			}
			uint targetsMask = 0;
			foreach(var playerId in targetPlayerIds)
			{
				int index = Array.IndexOf(connectionsOwners, playerId);
				if(index < 0) return false;
				targetsMask = 1u << index;
			}
			return socket.SendTargets(mode, data, dataLength, targetsMask);
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
			connectionsMask &= (1u << index) ^ 0xFFFFFFFF;
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

		private bool ImTarget(int type, byte[] dataBuffer, int index)
		{
			if(type == TARGET_ALL) return true;
			if(type == TARGET_MASTER) return Networking.IsMaster;
			if(type == TARGET_SINGLE) return dataBuffer[index] == localConnectionIndex;
			if(type == TARGET_MULTIPLE)
			{
				uint mask = (uint)dataBuffer[index];
				if(connectionsMaskBytesCount > 1)
				{
					index++;
					mask |= (uint)dataBuffer[index] << 8;
					if(connectionsMaskBytesCount > 2)
					{
						index++;
						mask |= (uint)dataBuffer[index] << 16;
						if(connectionsMaskBytesCount > 3)
						{
							index++;
							mask |= (uint)dataBuffer[index] << 24;
						}
					}
				}
				uint bit = 1u << localConnectionIndex;
				return (mask & bit) == bit;
			}
			return false;
		}

		private int GetTargetHeaderSize(int type)
		{
			if(type == TARGET_SINGLE) return 1;
			if(type == TARGET_MULTIPLE) return connectionsMaskBytesCount;
			return 0;
		}
	}
}
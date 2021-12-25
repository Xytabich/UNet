using System;
using UdonSharp;
using UNet;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Xytabich.UNet.Notepad
{
	public class NotepadsManager : UdonSharpBehaviour//TODO: полноситью обеспечивает сетевое взаимодействие с записками(единственное SendAll они сами отправляют)
	{
		private const byte NOTEPAD_NETWORK_MESSAGE = 0x80;
		private const byte REQUEST_NOTEPAD_SPAWN = 0x01;
		private const byte REQUEST_NOTEPAD_INFO = 0x02;
		private const byte NOTEPAD_SYNC = 0X03;
		private const byte NOTEPAD_NOTE = 0X04;
		private const byte NOTEPAD_REMOVE = 0X05;
		private const byte NOTEPAD_REMOVE_ALL = 0X06;
		private const byte REQUEST_NOTEPAD_DESPAWN = 0x07;

		public VRCObjectPool notepadsPool;

		[HideInInspector, NonSerialized]
		public Transform notepadSpawnPoint;

		private Notepad localNotepad;
		private NetworkInterface network = null;
		private ByteBufferWriter writer;
		private ByteBufferReader reader;

		private int recordsCount = 0;
		private int[] players = new int[4];
		private Notepad[] notepads;

		private int notepadRequestSended = -1;
		private bool notepadRequested = false;

		private int requestedSyncsCount = 0;
		private int[] requestedSyncs = new int[4];

#pragma warning disable CS0649
		private int OnUNetReceived_sender;
		private byte[] OnUNetReceived_dataBuffer;
		private int OnUNetReceived_dataIndex;

		private int OnUNetSendComplete_messageId;
		private bool OnUNetSendComplete_succeed;
#pragma warning restore CS0649

		void Start()
		{
			notepads = new Notepad[4];
			notepadSpawnPoint = transform;
			if(network == null) FindNetwork();
		}

		public override void OnPlayerLeft(VRCPlayerApi player)
		{
			if(player != null) DespawnNotepad(player.playerId);
		}

		public void OnUNetReceived()
		{
			if(OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex] == NOTEPAD_NETWORK_MESSAGE)
			{
				OnUNetReceived_dataIndex++;
				var type = OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex];
				switch(type)
				{
					case REQUEST_NOTEPAD_SPAWN:
						CreateNotepadForPlayer(OnUNetReceived_sender);
						break;
					case REQUEST_NOTEPAD_INFO:
						if(localNotepad != null) localNotepad.SendInfoToPlayer(OnUNetReceived_sender);
						else
						{
							if(requestedSyncsCount >= requestedSyncs.Length)
							{
								var tmp = requestedSyncs;
								requestedSyncs = new int[requestedSyncsCount * 2];
								tmp.CopyTo(requestedSyncs, 0);
								requestedSyncs = tmp;
							}
							requestedSyncs[requestedSyncsCount] = OnUNetReceived_sender;
							requestedSyncsCount++;
						}
						break;
					case REQUEST_NOTEPAD_DESPAWN:
						{
							var player = VRCPlayerApi.GetPlayerById(OnUNetReceived_sender);
							if(player != null && player.isMaster)
							{
								OnUNetReceived_dataIndex++;
								int targetPlayer = reader.ReadInt32(OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
								if(targetPlayer == Networking.LocalPlayer.playerId)
								{
									localNotepad = null;
								}
								else
								{
									DespawnNotepad(targetPlayer);
								}
							}
						}
						break;
					case NOTEPAD_SYNC:
					case NOTEPAD_NOTE:
					case NOTEPAD_REMOVE:
					case NOTEPAD_REMOVE_ALL:
						{
							int index = Array.IndexOf(players, OnUNetReceived_sender, 0, recordsCount);
							if(index >= 0)
							{
								OnUNetReceived_dataIndex++;
								notepads[index].OnNetworkMessage(type, OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
							}
						}
						break;
				}
			}
		}

		public void OnUNetSendComplete()
		{
			if(notepadRequested && notepadRequestSended == OnUNetSendComplete_messageId)
			{
				if(!OnUNetSendComplete_succeed)
				{
					notepadRequestSended = -1;
					if(Networking.IsMaster)
					{
						notepadRequested = false;
						SpawnNotepad();
					}
				}
			}
		}

		public void OnUNetPrepareSend()
		{
			if(notepadRequested && notepadRequestSended < 0)
			{
				if(!OnUNetSendComplete_succeed)
				{
					notepadRequestSended = network.SendMaster(false, new byte[] { NOTEPAD_NETWORK_MESSAGE, REQUEST_NOTEPAD_SPAWN }, 2);
				}
			}
			if(localNotepad != null) localNotepad.PrepareSend();
			for(int i = 0; i < recordsCount; i++)
			{
				notepads[i].PrepareSend();
			}
		}

		/// <summary>
		/// (Re)Spawns notepad for local player
		/// </summary>
		public void SpawnNotepad()
		{
			if(network == null || !network.IsInitComplete()) return;
			this.notepadSpawnPoint = transform;

			if(localNotepad == null)
			{
				if(notepadRequested) return;

				notepadRequested = true;
				if(Networking.IsMaster)
				{
					if(notepadsPool.TryToSpawn() == null)
					{
						Debug.LogError("There is no notepad object in the pool");
						return;
					}
				}
				else
				{
					notepadRequestSended = network.SendMaster(false, new byte[] { NOTEPAD_NETWORK_MESSAGE, REQUEST_NOTEPAD_SPAWN }, 2);
				}
			}
			else
			{
				var notepadTransform = localNotepad.transform;
				notepadTransform.position = notepadSpawnPoint.position;
				notepadTransform.rotation = notepadSpawnPoint.rotation;
			}
		}

		public void MasterDespawnNotepad(int playerId)
		{
			if(Networking.IsMaster)
			{
				var data = new byte[6];
				data[0] = NOTEPAD_NETWORK_MESSAGE;
				data[1] = REQUEST_NOTEPAD_DESPAWN;
				writer.WriteInt32(playerId, data, 2);
				notepadRequestSended = network.SendAll(false, data, 6);
				DespawnNotepad(playerId);
			}
		}

		public void SpawnNotepadOnPoint(Transform spawnPoint)
		{
			if(network == null || !network.IsInitComplete()) return;
			this.notepadSpawnPoint = spawnPoint;

			if(localNotepad == null)
			{
				if(notepadRequested) return;

				notepadRequested = true;
				if(Networking.IsMaster)
				{
					if(notepadsPool.TryToSpawn() == null)
					{
						Debug.LogError("There is no notepad object in the pool");
						return;
					}
				}
				else
				{
					notepadRequestSended = network.SendMaster(false, new byte[] { NOTEPAD_NETWORK_MESSAGE, REQUEST_NOTEPAD_SPAWN }, 2);
				}
			}
			else
			{
				var notepadTransform = localNotepad.transform;
				notepadTransform.position = notepadSpawnPoint.position;
				notepadTransform.rotation = notepadSpawnPoint.rotation;
			}
		}

		public void SetNotepadForPlayer(int playerId, Notepad notepad)
		{
			if(Array.IndexOf(players, playerId, 0, recordsCount) >= 0) return;

			if(network == null) FindNetwork();
			if(playerId == Networking.LocalPlayer.playerId)
			{
				notepadRequested = false;
				if(localNotepad == null)
				{
					localNotepad = notepad;
					notepad.Init(network, writer, reader);

					for(int i = 0; i < requestedSyncsCount; i++)
					{
						localNotepad.SendInfoToPlayer(requestedSyncs[i]);
					}
				}
				return;
			}

			if(recordsCount >= players.Length)
			{
				var tmpPlayers = new int[recordsCount * 2];
				players.CopyTo(tmpPlayers, 0);
				players = tmpPlayers;
				var tmpNotepads = new Notepad[recordsCount * 2];
				notepads.CopyTo(tmpNotepads, 0);
				notepads = tmpNotepads;
			}
			players[recordsCount] = playerId;
			notepads[recordsCount] = notepad;
			recordsCount++;

			notepad.Init(network, writer, reader);
		}

		private void DespawnNotepad(int playerId)
		{
			int index = Array.IndexOf(players, playerId, 0, recordsCount);
			if(index < 0) return;

			if(Networking.IsMaster) notepadsPool.Return(notepads[index].gameObject);

			recordsCount--;
			if(index < recordsCount)
			{
				int fromIndex = index + 1;
				int moveCount = recordsCount - index;
				Array.Copy(players, fromIndex, players, index, moveCount);
				Array.Copy(notepads, fromIndex, notepads, index, moveCount);
			}
			notepads[recordsCount] = null;
		}

		private void FindNetwork()
		{
			var obj = GameObject.Find("UNetInstance");
			Debug.Assert(obj == null, "UNetInstance object is not found");
			network = obj.GetComponent<NetworkInterface>();
			writer = obj.GetComponent<ByteBufferWriter>();
			reader = obj.GetComponent<ByteBufferReader>();
			Debug.Assert(network == null, "UNetInstance object is not found");
			network.AddEventsListener(this);
		}

		private void CreateNotepadForPlayer(int playerId)
		{
			var player = VRCPlayerApi.GetPlayerById(playerId);
			if(player == null) return;

			var notepadObj = notepadsPool.TryToSpawn();
			if(notepadObj == null)
			{
				Debug.LogError("There is no notepad object in the pool");
				return;
			}

			Networking.SetOwner(player, notepadObj);
		}
	}
}
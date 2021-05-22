using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace UNet
{
	/// <summary>
	/// It is a bridge between all clients, receives and sends data using UdonSync.
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
	public class Connection : UdonSharpBehaviour
	{
		[UdonSynced]
		private byte[] packet = new byte[0];

		private int connectionIndex = -1;
		private NetworkManager manager;
		private byte[] dataBuffer;

		private byte[] emptyData = new byte[0];

		public void Init(int index, NetworkManager manager)
		{
			this.connectionIndex = index;
			this.manager = manager;
		}

		public void SetDataBuffer(byte[] dataBuffer)
		{
			this.dataBuffer = dataBuffer;
		}

		public override void OnPreSerialization()
		{
			if(connectionIndex < 0) return;

			int dataBufferLength = manager.PrepareSendStream(connectionIndex);
			if(dataBufferLength < 1)
			{
				packet = emptyData;
				return;
			}

			packet = new byte[dataBufferLength];
			Array.Copy(dataBuffer, packet, dataBufferLength);
		}

		public override void OnOwnershipTransferred(VRCPlayerApi player)
		{
			if(connectionIndex < 0) return;

			if(!player.isMaster)
			{
				manager.OnOwnerReceived(connectionIndex, player.playerId);
			}
		}

		public override void OnDeserialization()
		{
			if(connectionIndex < 0) return;

			if(packet.Length > 0)
			{
				manager.HandlePacket(connectionIndex, packet, packet.Length);
			}
			packet = emptyData;
		}
	}
}
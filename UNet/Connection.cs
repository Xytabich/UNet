using System;
using UdonSharp;
using VRC.SDKBase;

namespace UNet
{
	/// <summary>
	/// It is a bridge between all clients, receives and sends data using UdonSync and encoding data in base64.
	/// You can use your own data encoding to increase network bandwidth, but converting to base64 and vice versa is very resource intensive and very fast.
	/// </summary>
	public class Connection : UdonSharpBehaviour
	{
		[UdonSynced]
		private string dataPart1 = "";
		[UdonSynced]
		private string dataPart2 = "";

		public readonly int connectionIndex;
		public readonly int owner = -1;

		private NetworkManager manager = null;
		private Socket socket = null;

		private int dataBufferLength;
		private byte[] dataBuffer;

		public override void OnPreSerialization()
		{
			if(socket == null || owner < 0) return;

			socket.PrepareSendStream();

			if(dataBufferLength < 1)
			{
				dataPart1 = "";
				dataPart2 = "";
				return;
			}

			string data = Convert.ToBase64String(dataBuffer, 0, dataBufferLength);
			int splitIndex = data.Length / 2;
			// The data is split into 2 parts because it allows more data to pass through the network than using one long string
			dataPart1 = data.Substring(0, splitIndex);
			dataPart2 = data.Substring(splitIndex);
		}

		public override void OnOwnershipTransferred()
		{
			if(manager == null) return;

			var player = Networking.GetOwner(gameObject);
			if(!player.isMaster)
			{
				manager.OnOwnerReceived(connectionIndex, player.playerId);
			}
		}

		public override void OnDeserialization()
		{
			if(manager == null) return;

			string data = dataPart1 + dataPart2;
			dataPart1 = "";
			dataPart2 = "";
			if(!string.IsNullOrEmpty(data))
			{
				dataBuffer = Convert.FromBase64String(data);
				dataBufferLength = dataBuffer.Length;
				manager.HandlePacket(connectionIndex, dataBuffer, dataBufferLength);
			}
		}
	}
}

using UdonSharp;
using VRC.SDKBase;

namespace UNet
{
	public class NetworkInterface : UdonSharpBehaviour
	{
		/// <summary>
		/// Method of sending data without guarantee of delivery.
		/// Can be used for constantly updated values, such as the position of an object.
		/// </summary>
		public const int MODE_UNRELIABLE = 0;
		/// <summary>
		/// Method of sending data with guaranteed delivery but without strict order.
		/// Uses most of the connection bandwidth, so it should be used for important messages.
		/// </summary>
		public const int MODE_RELIABLE = 1;
		/// <summary>
		///	Method of sending data with guaranteed delivery in strict order.
		/// Uses even more bandwidth than MODE_RELIABLE.
		/// </summary>
		public const int MODE_RELIABLE_SEQUENCED = 2;

		public const int MAX_PACKET_SIZE = 144;

		private NetworkManager manager;

		void Start()
		{
			manager = GetComponent<NetworkManager>();
		}

		/// <summary>
		/// Adds an unet event listener.
		/// All callbacks parameters must be written in scripts as variables.
		/// Example:
		/// <code>
		/// 	private int OnUNetConnected_playerId;
		/// 	public void OnUNetConnected()
		/// 	{
		/// 		Debug.LogFormat("Connected: {0}", OnUNetConnected_playerId);
		/// 	}
		/// </code>
		/// <remarks>
		/// <list>
		/// Events list:
		/// 	<item>
		/// 		<term>OnUNetInit()</term>
		/// 		<description>called when the network system is fully initialized and you can start sending data</description>
		/// 	</item>
		/// 	<item>
		/// 		<term>OnUNetConnected(int OnUNetConnected_playerId)</term>
		/// 		<description>called when another player has been connected and initialized.</description>
		/// 	</item>
		/// 	<item>
		/// 		<term>OnUNetDisconnected(int OnUNetDisconnected_playerId)</term>
		/// 		<description>called when another player has disconnected and resources have been released.</description>
		/// 	</item>
		/// 	<item>
		/// 		<term>OnUNetReceived(int OnUNetReceived_sender, byte[] OnUNetReceived_dataBuffer, int OnUNetReceived_dataIndex, int OnUNetReceived_dataLength)</term>
		/// 		<description>called when the socket has received a message.</description>
		/// 		<list>
		/// 			<item>
		/// 				OnUNetReceived_sender - <see cref="VRCPlayerApi.playerId"/>
		/// 			</item>
		/// 			<item>
		/// 				OnUNetReceived_dataBuffer - data buffer, contains raw packet data, do not write another data here, as this may break the network
		/// 			</item>
		/// 			<item>
		/// 				OnUNetReceived_dataIndex - index of received data in buffer, the data for this particular message starts with this index
		/// 			</item>
		/// 			<item>
		/// 				OnUNetReceived_dataLength - length of received data
		/// 			</item>
		/// 		</list>
		/// 	</item>
		/// </list>
		/// </remarks>
		/// </summary>
		public void AddEventsListener(UdonSharpBehaviour listener)
		{
			manager.AddEventsListener(listener);
		}

		/// <summary>
		/// Removes an unet event listener.
		/// </summary>
		public void RemoveEventsListener(UdonSharpBehaviour listener)
		{
			manager.RemoveEventsListener(listener);
		}

		/// <summary>
		/// Returns max length of message for given options.
		/// </summary>
		/// <param name="mode">Send mode: <see cref="NetworkInterface.MODE_UNRELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE_SEQUENCED"/></param>
		/// <param name="sendTargetsCount">Target clients count, for <see cref="NetworkInterface.SendAll"/> and <see cref="NetworkInterface.SendMaster"/> is always 0.</param>
		/// <returns>Max length of message</returns>
		public int GetMaxDataLength(int mode, int sendTargetsCount)
		{
			int len = MAX_PACKET_SIZE - 2;//header[byte] + length[byte]
			if(mode == 1) len -= 2;//msg id[ushort]
			else if(mode == 2) len -= 3;//msg id[ushort] + sequence[byte]
			if(sendTargetsCount == 1) len -= 1;//connection index[byte]
			else if(sendTargetsCount > 1) len -= 4;//connections map[uint]
			return len;
		}

		/// <summary>
		/// Sends message to other clients.
		/// </summary>
		/// <param name="mode">Send mode: <see cref="NetworkInterface.MODE_UNRELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE_SEQUENCED"/></param>
		/// <param name="data">Array of data bytes, the length array must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>True if the message has been added to the send buffer.</returns>
		public bool SendAll(int mode, byte[] data, int dataLength)
		{
			return manager.SendAll(mode, data, dataLength);
		}

		/// <summary>
		/// Sends message to master client only.
		/// </summary>
		/// <param name="mode">Send mode: <see cref="NetworkInterface.MODE_UNRELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE_SEQUENCED"/></param>
		/// <param name="data">Array of data bytes, the length array must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>True if the message has been added to the send buffer.</returns>
		public bool SendMaster(int mode, byte[] data, int dataLength)
		{
			return manager.SendMaster(mode, data, dataLength);
		}

		/// <summary>
		/// Sends message to target client only.
		/// </summary>
		/// <param name="mode">Send mode: <see cref="NetworkInterface.MODE_UNRELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE_SEQUENCED"/></param>
		/// <param name="data">Array of data bytes, the length array must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="targetPlayerId">Target client <see cref="VRCPlayerApi.playerId"/></param>
		/// <returns>True if the message has been added to the send buffer.</returns>
		public bool SendTarget(int mode, byte[] data, int dataLength, int targetPlayerId)
		{
			return manager.SendTarget(mode, data, dataLength, targetPlayerId);
		}

		/// <summary>
		/// Sends message to target clients only.
		/// </summary>
		/// <param name="mode">Send mode: <see cref="NetworkInterface.MODE_UNRELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE"/>, <see cref="NetworkInterface.MODE_RELIABLE_SEQUENCED"/></param>
		/// <param name="data">Array of data bytes, the length array must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <param name="targetPlayerIds">Target clients <see cref="VRCPlayerApi.playerId"/></param>
		/// <returns>True if the message has been added to the send buffer.</returns>
		public bool SendTargets(int mode, byte[] data, int dataLength, int[] targetPlayerIds)
		{
			return manager.SendTargets(mode, data, dataLength, targetPlayerIds);
		}
	}
}
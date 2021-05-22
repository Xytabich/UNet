using UdonSharp;
using VRC.SDKBase;

namespace UNet
{
	public class NetworkInterface : UdonSharpBehaviour
	{
		public const int MAX_MESSAGE_SIZE = 512;

		public NetworkManager manager;

		public bool IsInitComplete()
		{
			return (bool)manager.GetProgramVariable("isInitComplete");
		}

		public bool HasOtherConnections()
		{
			return manager.activeConnectionsCount > 1;
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
		/// 		<description>called when the network system is fully initialized and you can start sending data.</description>
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
		/// 		<term>OnUNetPrepareSend()</term>
		/// 		<description>called before preparing the package for the next dispatch. Any data added in this callback will also participate in package preparation.</description>
		/// 	</item>
		/// 	<item>
		/// 		<term>OnUNetSendComplete(int OnUNetSendComplete_messageId, bool OnUNetSendComplete_succeed)</term>
		/// 		<description>called when the message has finished sending.</description>
		/// 		<list>
		/// 			<item>
		/// 				OnUNetSendComplete_succeed:
		/// 					true - the message was delivered to the recipient (but this does not mean that a sequenced message has been applied).
		/// 					false - the send was canceled or the recipient left the room.
		/// 			</item>
		/// 		</list>
		/// 	</item>
		/// 	<item>
		/// 		<term>OnUNetReceived(int OnUNetReceived_sender, byte[] OnUNetReceived_dataBuffer, int OnUNetReceived_dataIndex, int OnUNetReceived_dataLength, int OnUNetReceived_id)</term>
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
		/// 			<item>
		/// 				OnUNetReceived_messageId - received message id
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
		/// <param name="sendTargetsCount">Target clients count, for <see cref="NetworkInterface.SendAll"/> and <see cref="NetworkInterface.SendMaster"/> is always 0.</param>
		/// <returns>Max length of message</returns>
		public int GetMaxDataLength(bool sequenced, int sendTargetsCount)
		{
			int len = MAX_MESSAGE_SIZE - 5;//header[byte] + length[ushort] + msg id[ushort]
			if(sequenced) len -= 1;//msg id[ushort] + sequence[byte]
			if(sendTargetsCount == 1) len -= 1;//connection index[byte]
			else if(sendTargetsCount > 1) len -= manager.connectionsMaskBytesCount;
			return len;
		}

		/// <summary>
		/// Cancels the sending of the message with the given id.
		/// This operation cannot affect the message if it has already been delivered to the recipients.
		/// This method must be called before the end of the message delivery (OnUNetSendComplete event), otherwise it may disrupt the sending of other messages.
		/// </summary>
		public void CancelMessageSend(int messageId)
		{
			manager.CancelMessageSend(messageId);
		}

		/// <summary>
		/// Sends message to other clients.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendAll(bool sequenced, byte[] data, int dataLength)
		{
			return manager.SendAll(sequenced, data, dataLength);
		}

		/// <summary>
		/// Sends message to master client only.
		/// </summary>
		/// <param name="data">Array of data bytes</param>
		/// <param name="dataLength">The length of data, must be less than or equals to <see cref="NetworkInterface.GetMaxDataLength"/></param>
		/// <returns>Message ID or -1 if the message was not added to the buffer</returns>
		public int SendMaster(bool sequenced, byte[] data, int dataLength)
		{
			return manager.SendMaster(sequenced, data, dataLength);
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
			return manager.SendTarget(sequenced, data, dataLength, targetPlayerId);
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
			return manager.SendTargets(sequenced, data, dataLength, targetPlayerIds);
		}
	}
}
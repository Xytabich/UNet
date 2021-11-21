
namespace UNet
{
	/// <summary>
	/// Called when the network system is fully initialized and you can start sending data
	/// </summary>
	public delegate void OnUNetInit();
	/// <summary>
	/// Called when the connected player is ready to receive messages.
	/// </summary>
	/// <param name="playerId">Player id from VRCPlayerApi.playerId</param>
	public delegate void OnUNetConnected(int playerId);
	/// <summary>
	/// Called when another player has disconnected and resources have been released.
	/// </summary>
	/// <param name="playerId">Player id from VRCPlayerApi.playerId</param>
	public delegate void OnUNetDisconnected(int playerId);
	/// <summary>
	/// Called before preparing the package for the next dispatch. Any data added in this callback will also participate in package preparation.
	/// </summary>
	public delegate void OnUNetPrepareSend();
	/// <summary>
	/// Called when the socket has received a message.
	/// </summary>
	/// <param name="sender">Player id from VRCPlayerApi.playerId</param>
	/// <param name="dataBuffer">Data buffer, contains raw packet data, do not write another data here, as this may break the network.</param>
	/// <param name="dataIndex">Index of received data in buffer, the data for this particular message starts with this index.</param>
	/// <param name="dataLength">Length of received data</param>
	/// <param name="messageId">Received message id</param>
	public delegate void OnUNetReceived(int sender, byte[] dataBuffer, int dataIndex, int dataLength, int messageId);
	/// <summary>
	/// Called when the message has finished sending
	/// </summary>
	/// <param name="messageId">Id of message</param>
	/// <param name="succeed">Whether the message was delivered or canceled</param>
	public delegate void OnUNetSendComplete(int messageId, bool succeed);
}
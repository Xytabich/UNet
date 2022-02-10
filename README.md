# UNet
UNet (UDON Networking) is a simple network system that provides reliable binary data transfer and serialization for Udon.
UNet supports message management, sending messages in a strict sequence, targeting messages (to everyone, only the master or players from the list).

The Katsudon version is available [here](https://github.com/Xytabich/UNet/tree/katsudon).

Table of contents:
- Common info
  - [Supported delivery methods](#SupportedDeliveryMethods)
  - [Supported send targets](#SupportedSendTargets)
  - [Requirements](#Requirements)
- [Technical notes](#TechnicalNotes)
  - [Data transfer](#DataTransfer)
  - [System problems](#SystemProblems)
- [System info](#SystemInfo)
  - [Connection](#Connection)
  - [Socket](#Socket)
  - [NetworkManager](#NetworkManager)
  - [Network events](#NetworkEvents)
  - [NetworkInterface](#NetworkInterface)
- [Serialization](#Serialization)
- [Setup](#Setup)

## Common info
Supports up to 64 connections (including own).

Test world (source assets are available in examples): https://www.vrchat.com/home/launch?worldId=wrld_dbb598b1-5a70-4b03-8bd0-fa620e3788ca

### <a name="SupportedDeliveryMethods"></a> Supported delivery methods:
- Normal - data will certainly be delivered, but order is not guaranteed. For example, can be used for chat messages or some actions on the map.
- Sequenced - slower than normal mode, but data will be delivered in strict order.

### <a name="SupportedSendTargets"></a> Supported send targets:
- All - message is delivered to all clients
- Master - message is only delivered to the master client
- Single target - message is delivered to the specified client
- Multiple targets - message is delivered to the specified clients on the list

### <a name="Requirements"></a> Requirements
- [VRCSDK3 + UdonSDK](https://vrchat.com/home/download)
- [UdonSharp](https://github.com/Merlin-san/UdonSharp)

## <a name="TechnicalNotes"></a> Technical notes
### <a name="DataTransfer"></a> Data transfer
UNet uses a synchronized byte array in manual synchronization mode.
Data delivery speed depends on the current network congestion.

A message can have different endpoints (client, player) and different delivery methods.
- Every tick of the network, the messages are collected into a packet and sent.
- The maximum message size is set to 512 bytes, you can change the MAX_MESSAGE_SIZE constants in Socket.cs and NetworkInterface.cs.
- The maximum packet size is set to 2048 bytes, you can change the MAX_PACKET_SIZE constant in Socket.cs if you need to increase or decrease the bandwidth.

### <a name="SystemProblems"></a> System problems:
- Low delivery speed, round trip takes 400-800ms.
- Due to packet loss when the network is heavily loaded, the delivery time may increase significantly
- The volume of messages sent from all behaviors on the scene cannot exceed about 10kb/s, therefore the maximum packet size and the speed of sending messages depends on the number of synchronized objects on the scene.

## <a name="SystemInfo"></a> System info
The system consists of three main components and one auxiliary:
- Connection - is a "client" unit in a network system, responsible for sending data to other clients.
- Socket - used to process messages and form data packets.
- NetworkManager - responsible for assigning connection owners, distributing received messages, and generating network events.
- NetworkInterface - an auxiliary tool through which communication with the internal mechanics of the network goes.

### <a name="Connection"></a> Connection
Each player is given ownership over one connection object, so there must be at least as many connections as there are players.

The connection prepares a byte stream for sending or receiving a message.

### <a name="Socket"></a> Socket
The socket is responsible for handling connection data.

When sending methods are called, data is sent to a socket, where a "network message" is formed, consisting of the message type, delivery targets, its ID, length, and data.
At the next network tick, a packet is formed, consisting of:
- Confirmations for messages from other clients until all confirmations have been sent
- Message data.

The data from the buffer is stored until the message is acknowledged.
After receiving confirmation from all clients to whom the message was intended, the data from the buffer is deleted.
Duplicate messages are automatically discarded.

### <a name="NetworkManager"></a> NetworkManager
Network manager controls general network activity:
- Manages connections: assigns identifiers, makes a list of active connections.
- Processes received packets: checks the type of message and if it is intended for this client, then forwards the data to the appropriate socket.
- Generates network events: network initialization, client connection/disconnection, message receipt and etc.

#### <a name="NetworkEvents"></a> Network events description
Callbacks must be implemented as described, i.e. using public methods and variables as arguments.
```cs
public void OnUNetInit(); // Called when the network system is fully initialized and you can start sending data
```
```cs
private int OnUNetConnected_playerId; // Player id from VRCPlayerApi.playerId

public void OnUNetConnected(); // Called when the connected player is ready to receive messages.
```
```cs
private int OnUNetDisconnected_playerId; // Player id from VRCPlayerApi.playerId

public void OnUNetDisconnected(); // Called when another player has disconnected and resources have been released.
```
```cs
public void OnUNetPrepareSend(); // Called before preparing the package for the next dispatch. Any data added in this callback will also participate in package preparation.
```
```cs
private int OnUNetReceived_sender; // Player id from VRCPlayerApi.playerId
private byte[] OnUNetReceived_dataBuffer; // Data buffer, contains raw packet data, do not write another data here, as this may break the network.
private int OnUNetReceived_dataIndex; // Index of received data in buffer, the data for this particular message starts with this index.
private int OnUNetReceived_dataLength; // Length of received data
private int OnUNetReceived_messageId; // Received message id

public void OnUNetReceived(); // Called when the socket has received a message.
```
```cs
private int OnUNetSendComplete_messageId; // Id of message
private bool OnUNetSendComplete_succeed; // Whether the message was delivered or canceled

public void OnUNetSendComplete(); // Called when the message has finished sending
```

### <a name="NetworkInterface"></a> NetworkInterface
Contains the public API of the networking system.
```cs
// Methods:
public bool IsInitComplete(); // Returns true when network initialization is complete and they can send and receive data

public bool HasOtherConnections(); // Returns true if there are other connections to which data can be sent

void AddEventsListener(UdonSharpBehaviour listener); // Adds target udon behavior as an event listener, all events described earlier can be called on it

void RemoveEventsListener(UdonSharpBehaviour listener); // Removed target behavior from the list of event listeners

int GetMaxDataLength(bool sequenced, int sendTargetsCount); // Returns the maximum data length for the specified parameters

void CancelMessageSend(int messageId); // Will try to cancel sending the message

// Tries to add a message to the send buffer. If successful, returns the message id, otherwise -1
int SendAll(bool sequenced, byte[] data, int dataLength);
int SendMaster(bool sequenced, byte[] data, int dataLength);
int SendTarget(bool sequenced, byte[] data, int dataLength, int targetPlayerId);
int SendTargets(bool sequenced, byte[] data, int dataLength, int[] targetPlayerIds);
```

## <a name="Serialization"></a> Serialization
The system includes the [ByteBufferWriter](https://github.com/Xytabich/UNet/blob/master/UNet/ByteBufferWriter.cs) and [ByteBufferReader](https://github.com/Xytabich/UNet/blob/master/UNet/ByteBufferReader.cs) classes that can help you serialize data.

Supported types:
```cs
bool
char
sbyte
short
ushort
int
uint
long
ulong
float
half (Mathf.FloatToHalf/HalfToFloat)
decimal
System.DateTime
System.TimeSpan
System.Guid
UnityEngine.Vector2/3/4 (+half-precision versions)
UnityEngine.Quaternion (+half-precision version)
ASCII/UTF8 string
variable-length uint
```

Information about type sizes and other additional descriptions can be found in the built-in xml documentation.

## <a name="Setup"></a> Setup
- Download [latest](https://github.com/Xytabich/UNet/releases/tag/2.0.0) unity package for UdonSharp, and unpack it.
- Add `UNetInstance` prefab to the scene.
- Duplicate the `Connection` child element for the room's capacity.
- Add event listeners via NetworkInterface.
- Wait for the `OnUNetInit` event and start sending your messages.
- When a message is received, `OnUNetReceived` is called on all event listeners.

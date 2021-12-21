# UNet
Version for [Katsudon](https://github.com/Xytabich/Katsudon).

Table of contents:
- Common info
- [System info](#SystemInfo)
  - [Connection](#Connection)
  - [Socket](#Socket)
  - [NetworkManager](#NetworkManager)
  - [Network events](#NetworkEvents)
- [Serialization](#Serialization)
- [Setup](#Setup)

## Common info
Supports up to 64 connections (including own).

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
- [Katsudon](https://github.com/Xytabich/Katsudon)

## <a name="SystemInfo"></a> System info
The system consists of three main components:
- Connection - is a "client" unit in a network system, responsible for sending data to other clients.
- Socket - used to process messages and form data packets.
- NetworkManager - responsible for assigning connection owners, distributing received messages, and generating network events.

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

```cs
// Events
public event OnUNetInit onUNetInit;
public event OnUNetConnected onUNetConnected;
public event OnUNetDisconnected onUNetDisconnected;
public event OnUNetPrepareSend onUNetPrepareSend;
public event OnUNetReceived onUNetReceived;
public event OnUNetSendComplete onUNetSendComplete;

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

#### <a name="NetworkEvents"></a> Network events description
Callbacks must be implemented as described, i.e. using public methods and variables as arguments.
```cs
public delegate void OnUNetInit(); // Called when the network system is fully initialized and you can start sending data.

public delegate void OnUNetConnected(int playerId); // Called when the connected player is ready to receive messages.

public delegate void OnUNetDisconnected(int playerId); // Called when another player has disconnected and resources have been released.

public delegate void OnUNetPrepareSend(); // Called before preparing the package for the next dispatch. Any data added in this callback will also participate in package preparation.

public delegate void OnUNetReceived(int sender, byte[] dataBuffer, int dataIndex, int dataLength, int messageId); // Called when the socket has received a message.

public delegate void OnUNetSendComplete(int messageId, bool succeed); // Called when the message has finished sending.
```

## <a name="Serialization"></a> Serialization
The system includes the [ByteBufferWriter](https://github.com/Xytabich/UNet/blob/katsudon/UNet/ByteBufferWriter.cs) and [ByteBufferReader](https://github.com/Xytabich/UNet/blob/katsudon/UNet/ByteBufferReader.cs) classes that can help you serialize data.

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
- Download [latest](https://github.com/Xytabich/UNet/releases/tag/k2.1.0) unity package for Katsudon, and unpack it.
- Add `UNetInstance` prefab to the scene.
- Duplicate the `Connection` child element for the room's capacity.
- Add reference to UNet assembly definition.
- Add event listeners via NetworkManager
- Wait for the `OnUNetInit` event and start sending your messages.
- When a message is received, `OnUNetReceived` is called on all event listeners.

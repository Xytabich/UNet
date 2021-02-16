# UNet
UNet (UDON Networking) is a simple network system that provides binary data transfer for Udon.
UNet supported base features used in network systems: unreliable and reliable delivery methods, targeted messages send, separation into master and common players.

Table of contents:
- Common info
  - [Supported delivery methods](#SupportedDeliveryMethods)
  - [Supported send targets](#SupportedSendTargets)
  - [Requirements](#Requirements)
- [Technical notes](#TechnicalNotes)
  - [Encoding](#Encoding)
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
- [Contact](#Contact)

## Common info
Supports up to 31 connections (including own).
Test world: https://www.vrchat.com/home/launch?worldId=wrld_dbb598b1-5a70-4b03-8bd0-fa620e3788ca

### <a name="SupportedDeliveryMethods"></a> Supported delivery methods:
- Unreliable - simplest mode, low network load, but some data may be lost. It can be used to transfer frequently updated data, for example, the position of an object.
- Reliable - the data will certainly be delivered, but this mode is more loads on the network. Can be used for chat messages or some actions on the map.
- Reliable sequenced - loads the network a little more than reliable mode, but the data will be delivered in a strict order.

### <a name="SupportedSendTargets"></a> Supported send targets:
- All - message is delivered to all clients
- Master - message is only delivered to the master client
- Single target - message is delivered to the specified client
- Multiple targets - message is delivered to the specified clients on the list

### <a name="Requirements"></a> Requirements
- [VRCSDK3 + UdonSDK](https://vrchat.com/home/download)
- [UdonSharp](https://github.com/Merlin-san/UdonSharp)

## <a name="TechnicalNotes"></a> Technical notes
UNet uses syncable variables as a network stream, and only works with it, i.e. does not use SendCustomNetworkEvent and other things.

### <a name="Encoding"></a> Encoding
The stream is 2 string variables with the 'sync' attribute, the maximum total length of which is 192 characters (according to [this article](https://ask.vrchat.com/t/how-to-sync-with-udon/449/6)).

Data is an array of bytes, which are encoded into string variables in base64 format.

It is not efficient in terms of data density, but [system library](https://docs.microsoft.com/en-us/dotnet/api/system.convert.tobase64string) works super fast and is not resource-intensive.

The resulting maximum data size is 144 bytes. You can modify the [Connection](https://github.com/Xytabich/UNet/blob/master/UNet/Connection.cs) class if you want to change the encoding method, but you will need to update the `MAX_PACKET_SIZE` constant in all classes.

### <a name="DataTransfer"></a> Data transfer
Network messages are used to transfer data.
A message can have different endpoints (client, player) and different delivery methods.
Each network tick, messages are packed into a packet (up to 144 bytes) and sent.

### <a name="SystemProblems"></a> System problems:
- Slowness ... This is the main problem, the packet sending rate is 200ms (but actually more).
Considering that the maximum packet size is 144 bytes, you can get a speed of about 720 bytes/s (or 5.76 kbps).
- Packet loss (that's why I created a network system). Since the data is being synchronized over an unreliable channel, data may be lost. In my tests with high loads, the loss was about 60%, which significantly reduces the transfer rate. But in lightly loaded systems, losses are practically absent.
- Delivery speed is the time it takes for a message to reach other players. In my tests, latency time started at 500ms and RTT was 1500-2000ms.

## <a name="SystemInfo"></a> System info
The system consists of three main components and one auxiliary:
- Connection - used to transfer data between clients. 
- Socket - used to process messages and form data packets.
- NetworkManager - responsible for assigning connection owners, distributing received messages, and generating network events.
- NetworkInterface - an auxiliary tool through which communication with the internal mechanics of the network goes.

### <a name="Connection"></a> Connection
Each player is given ownership over one connection object, so there must be at least as many connections as there are players.

The connection prepares a byte stream for sending or receiving a message. The transfer uses synchronization of the two strings and base64 encoding supported by the Convert class.

But due to the specifics of the format, the ratio of the number of bytes to the number of characters is 3/4, which is much lower than if you use 7-bit asci (for which the ratio is 8/9).
Therefore, you can change the conversion in this class if you need a denser data packing.

### <a name="Socket"></a> Socket
The socket is responsible for handling connection data.

When sending methods are called, data is sent to a socket, where it is buffered depending on the delivery method.
At the next network tick, a packet is formed, consisting of:
- Confirmations for messages from other clients until all confirmations are sent
- Reliable and unreliable messages are split 50/50 (a reliable message is always sent first).

Messages from an unreliable buffer will be sent on the first try and then flushed.
You can also enable the `Flush Not Fit Unreliable` option to flush the remaining buffer if it does not fit in the packet.

Data from a reliable buffer is stored until the message is acknowledged.
After receiving acknowledgment from all clients for whom the message was intended, the data from the reliable buffer is removed.
If a reliable message has already been received, it is discarded.

### <a name="NetworkManager"></a> NetworkManager
Network manager controls general network activity:
- Manages connections: assigns identifiers, makes a list of active connections.
- Processes received packets: checks the type of message and if it is intended for this client, then forwards the data to the appropriate socket.
- Generates network events: network initialization, client connection/disconnection, message receipt, preparation for sending.

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

public void OnUNetReceived(); // Called when the socket has received a message.
```

### <a name="NetworkInterface"></a> NetworkInterface
Contains the public API of the networking system.
```cs
// Modes constants:
int MODE_UNRELIABLE = 0;
int MODE_RELIABLE = 1;
int MODE_RELIABLE_SEQUENCED = 2;

// Methods:
void AddEventsListener(UdonSharpBehaviour listener); // Adds target udon behavior as an event listener, all events described earlier can be called on it

void RemoveEventsListener(UdonSharpBehaviour listener); // Removed target behavior from the list of event listeners

int GetMaxDataLength(int mode, int sendTargetsCount); // Returns the maximum data length for the specified parameters

// Tries to add a message to the send buffer, returns true if successful
bool SendAll(int mode, byte[] data, int dataLength);
bool SendMaster(int mode, byte[] data, int dataLength);
bool SendTarget(int mode, byte[] data, int dataLength, int targetPlayerId);
bool SendTargets(int mode, byte[] data, int dataLength, int[] targetPlayerIds);
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
- Download [latest](https://github.com/Xytabich/UNet/releases/latest) unity package, and upack it.
- Add `UNetInstance` prefab to the scene.
- Duplicate the `Connection` child by the number of players.
- Add event listeners via NetworkInterface
- Wait for the `OnUNetInit` event and start sending your messages.
- When a message is received, `OnUNetReceived` is called on all event listeners.

## <a name="Contact"></a> Contact
If you have any questions, suggestions, fixes or bug reports, you can write in Discord `Xytabich#5684`

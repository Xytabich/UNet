# UNet
UNet (UDON Networking) is a simple networking system that provides binary data transfer for Udon.
UNet supported base features used in network systems: unreliable and reliable delivery methods, targeted messages send, separation into master and common players.

Currently, the amount of transmitted data is limited due to the Udon architecture. On average, up to 5 packets are transmitted per second, each about 150 bytes in size.

### Supported delivery methods:
- Unreliable - simplest mode, low network load, but some data may be lost. It can be used to transfer frequently updated data, for example, the position of an object.
- Reliable - the data will certainly be delivered, but this mode is more loads on the network. Can be used for chat messages or some actions on the map.
- Reliable sequenced - loads the network a little more than reliable mode, but the data will be delivered in a strict order.

### Supported send targets:
- All - message is delivered to all clients
- Master - message is only delivered to the master client
- Single target - message is delivered to the specified client
- Multiple targets - message is delivered to the specified clients on the list

### Requirements
- [VRCSDK3 + UdonSDK](https://vrchat.com/home/download)
- [UdonSharp](https://github.com/Merlin-san/UdonSharp)

## How it works
The system consists of three main components and one auxiliary:
- Connection - used to transfer data between clients. 
- Socket - used to process messages and form data packets.
- NetworkManager - responsible for assigning connection owners, distributing received messages, and generating network events.
- NetworkInterface - an auxiliary tool through which communication with the internal mechanics of the network goes.

### Connection
Each player is given ownership over one connection object, so there must be at least as many connections as there are players.

The connection prepares a byte stream for sending or receiving a message. The transfer uses synchronization of the two strings and base64 encoding supported by the Convert class.
Convert.ToBase64 is a very fast and low cost process.

But due to the specifics of the format, the ratio of the number of bytes to the number of characters is 3/4, which is much lower than if you use 7-bit asci (for which the ratio is 8/9).
Therefore, you can change the conversion in this class if you need a denser data packing.

### Socket
The socket is responsible for handling connection data.

When sending methods are called, data is sent to a socket, where it is buffered depending on the delivery method.
At the next network tick, a packet is formed, consisting of:
- Confirmations for messages from other clients until all confirmations are sent
- Reliable and unreliable messages are split 50/50.

Messages from the unreliable buffer are immediately cleared, data from reliable buffer is kept until the message is acknowledged.
After receiving acknowledgment from all clients for whom the message was intended, the data from the safe buffer is removed.
If a reliable message has already been received, it is discarded.

### NetworkManager
Network manager provides general network operation:
- Monitors connections: assigns identifiers, makes a list of active connections
- Processes received packets from connections: checks the type of message and whether it is intended for this client
- Generates network events: network initialized, client connectied/disconnectied, message received

#### Network events description
Callbacks must be implemented as described, i.e. using public methods and variables as arguments.
```cs
public void OnUNetInit(); // Called when the network system is fully initialized and you can start sending data
```
```cs
private int OnUNetConnected_playerId; // Player id from VRCPlayerApi.playerId

public void OnUNetConnected(); // Called when another player has been connected and initialized.
```
```cs
private int OnUNetDisconnected_playerId; // Player id from VRCPlayerApi.playerId

public void OnUNetDisconnected(); // Called when another player has disconnected and resources have been released.
```
```cs
private int OnUNetReceived_sender; // Player id from VRCPlayerApi.playerId
private byte[] OnUNetReceived_dataBuffer; // Data buffer, contains raw packet data, do not write another data here, as this may break the network.
private int OnUNetReceived_dataIndex; // Index of received data in buffer, the data for this particular message starts with this index.
private int OnUNetReceived_dataLength; // Length of received data

public void OnUNetReceived(); // Called when the socket has received a message.
```

### NetworkInterface
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

## Setup
- Download [latest](https://github.com/Xytabich/UNet/releases/latest) unity package, and upack it.
- Add `Network` prefab to the scene.
- Duplicate the `Connection` child by the number of players.
- Wait for the `OnUNetInit` event and start sending your messages.
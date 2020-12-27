using UdonSharp;
using UNet;
using UnityEngine;

namespace Xytabich.UNet.Notepad
{
	public class NotepadSpawner : UdonSharpBehaviour
	{
		private const byte NOTEPAD_NETWORK_MESSAGE = 0x80;
		private const byte SPAWN_CMD = 0x01;

		public GameObject localNotepadPrefab;
		public GameObject remoteNotepadPrefab;

		private NotepadLocal localNotepad;
		private NetworkInterface network;
		private ByteBufferWriter writer;
		private ByteBufferReader reader;

		private int remoteNotepadsCount = 0;
		private NotepadRemote[] remoteNotepads = null;

#pragma warning disable CS0649
		private int OnUNetReceived_sender;
		private byte[] OnUNetReceived_dataBuffer;
		private int OnUNetReceived_dataIndex;
#pragma warning restore CS0649

		void Start()
		{
			var obj = GameObject.Find("UNetInstance");
			Debug.Assert(obj == null, "UNetInstance object is not found");
			network = obj.GetComponent<NetworkInterface>();
			writer = obj.GetComponent<ByteBufferWriter>();
			reader = obj.GetComponent<ByteBufferReader>();
			Debug.Assert(network == null, "UNetInstance object is not found");
			network.AddEventsListener(this);
		}

		void LateUpdate()
		{
			for(var i = 0; i < remoteNotepadsCount; i++)
			{
				remoteNotepads[i].UpdateOffset();
			}
		}

		/// <summary>
		/// (Re)Spawns notepad for local player
		/// </summary>
		public void SpawnNotepad()
		{
			bool init = false;
			if(localNotepad == null)
			{
				var obj = VRCInstantiate(localNotepadPrefab);
				localNotepad = obj.GetComponent<NotepadLocal>();
				init = true;
			}
			var notepadTransform = localNotepad.transform;
			notepadTransform.position = transform.position;
			notepadTransform.rotation = transform.rotation;
			if(init) localNotepad.Init(network, writer);
		}

		public void OnNotepadRemoved(NotepadRemote notepad)
		{
			bool found = false;
			for(var i = 0; i < remoteNotepadsCount; i++)
			{
				if(found)
				{
					remoteNotepads[i - 1] = remoteNotepads[i];
					remoteNotepads[i] = null;
				}
				else if(remoteNotepads[i] == notepad)
				{
					remoteNotepads[i] = null;
					found = true;
				}
			}
			if(found) remoteNotepadsCount--;
		}

		public void OnUNetReceived()
		{
			if(OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex] == NOTEPAD_NETWORK_MESSAGE)
			{
				OnUNetReceived_dataIndex++;
				if(OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex] == SPAWN_CMD)
				{
					OnUNetReceived_dataIndex++;
					var position = reader.ReadVector3(OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
					OnUNetReceived_dataIndex += 12;
					var rotation = reader.ReadHalfQuaternion(OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);

					var obj = VRCInstantiate(remoteNotepadPrefab);
					var notepad = obj.GetComponent<NotepadRemote>();
					notepad.Init(this, network, reader, OnUNetReceived_sender, position, rotation);

					if(remoteNotepads == null)
					{
						remoteNotepads = new NotepadRemote[2];
					}
					else if(remoteNotepadsCount >= remoteNotepads.Length)
					{
						var tmp = new NotepadRemote[remoteNotepadsCount * 2];
						remoteNotepads.CopyTo(tmp, 0);
						remoteNotepads = tmp;
					}
					remoteNotepads[remoteNotepadsCount] = notepad;
					remoteNotepadsCount++;
				}
			}
		}
	}
}
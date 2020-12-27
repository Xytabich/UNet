
using UdonSharp;
using UNet;
using UnityEngine;
using UnityEngine.UI;

namespace Xytabich.UNet.Notepad
{
	public class NotepadRemote : UdonSharpBehaviour
	{
		private const byte NOTEPAD_NETWORK_MESSAGE = 0x80;
		private const byte MESSAGE_CMD = 0x02;
		private const byte TRANSFORM_CMD = 0x03;

		public Text text;
		public int maxTextSize = 1024;//UI Optimization

		private int owner;
		private NotepadSpawner spawner;
		private NetworkInterface network;
		private ByteBufferReader reader;

		private Vector3 targetPosition;
		private Quaternion targetRotation;

#pragma warning disable CS0649
		private int OnUNetDisconnected_playerId;
		private byte[] OnUNetReceived_dataBuffer;
		private int OnUNetReceived_dataIndex;
#pragma warning restore CS0649

		public void Init(NotepadSpawner spawner, NetworkInterface network, ByteBufferReader reader, int owner, Vector3 position, Quaternion rotation)
		{
			this.spawner = spawner;
			this.network = network;
			this.reader = reader;
			this.owner = owner;
			network.AddEventsListener(this);

			transform.position = targetPosition = position;
			transform.rotation = targetRotation = rotation;
		}

		public void UpdateOffset()
		{
			float step = Time.deltaTime * 10f;
			transform.position = Vector3.Lerp(transform.position, targetPosition, step);
			transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, step);
		}

		public void OnUNetDisconnected()
		{
			if(OnUNetDisconnected_playerId == owner)
			{
				network.RemoveEventsListener(this);
				spawner.OnNotepadRemoved(this);
				Destroy(gameObject);
			}
		}

		public void OnUNetReceived()
		{
			if(OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex] == NOTEPAD_NETWORK_MESSAGE)
			{
				OnUNetReceived_dataIndex++;
				byte cmd = OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex];
				if(cmd == MESSAGE_CMD)
				{
					OnUNetReceived_dataIndex++;
					int strsize = OnUNetReceived_dataBuffer[OnUNetReceived_dataIndex];
					OnUNetReceived_dataIndex++;
					string str = reader.ReadUTF8String(strsize, OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
					str = str + "\n" + text.text;
					if(str.Length > maxTextSize) str = str.Substring(0, maxTextSize);
					text.text = str;
				}
				else if(cmd == TRANSFORM_CMD)
				{
					OnUNetReceived_dataIndex++;
					targetPosition = reader.ReadVector3(OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
					OnUNetReceived_dataIndex += 12;
					targetRotation = reader.ReadHalfQuaternion(OnUNetReceived_dataBuffer, OnUNetReceived_dataIndex);
				}
			}
		}
	}
}
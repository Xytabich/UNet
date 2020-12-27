using UdonSharp;
using UNet;
using UnityEngine;
using UnityEngine.UI;

namespace Xytabich.UNet.Notepad
{
	public class NotepadLocal : UdonSharpBehaviour
	{
		private const byte NOTEPAD_NETWORK_MESSAGE = 0x80;
		private const byte SPAWN_CMD = 0x01;
		private const byte MESSAGE_CMD = 0x02;
		private const byte TRANSFORM_CMD = 0x03;

		private const int INFO_MESSAGE_SIZE = 22;//message type + cmd type + Vector3 + HalfQuaternion

		public Text text;
		public int maxTextSize = 1024;//UI Optimization
		public InputField field;
		public string warnTextLength = "<color=red>Input text is too long</color>";

		private NetworkInterface network;
		private ByteBufferWriter writer;

		private Vector3 lastPosition;
		private Quaternion lastRotation;

		private byte[] dataBuffer;

#pragma warning disable CS0649
		private int OnUNetConnected_playerId;
#pragma warning restore CS0649

		public void Init(NetworkInterface network, ByteBufferWriter writer)
		{
			this.network = network;
			this.writer = writer;

			dataBuffer = new byte[network.GetMaxDataLength(1, 1)];

			network.AddEventsListener(this);

			dataBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
			dataBuffer[1] = SPAWN_CMD;
			PrepareTransformBuffer();
			network.SendAll(1, dataBuffer, INFO_MESSAGE_SIZE);
		}

		public void OnUNetConnected()
		{
			dataBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
			dataBuffer[1] = SPAWN_CMD;
			PrepareTransformBuffer();
			network.SendTarget(1, dataBuffer, INFO_MESSAGE_SIZE, OnUNetConnected_playerId);
		}

		public void OnUNetPrepareSend()
		{
			if(transform.position != lastPosition || transform.rotation != lastRotation)
			{
				dataBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
				dataBuffer[1] = TRANSFORM_CMD;
				PrepareTransformBuffer();
				network.SendAll(0, dataBuffer, INFO_MESSAGE_SIZE);
			}
		}

		public void SendMsg()
		{
			string str = field.text;
			if(string.IsNullOrWhiteSpace(str)) return;

			string textStr = text.text;
			int strsize = writer.GetUTF8StringSize(str);
			if(strsize > (network.GetMaxDataLength(1, 0) - 3))
			{
				if(!string.IsNullOrEmpty(warnTextLength))
				{
					textStr = warnTextLength + "\n" + textStr;
				}
			}
			else
			{
				textStr = str + "\n" + textStr;
				field.text = "";

				dataBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
				dataBuffer[1] = MESSAGE_CMD;
				dataBuffer[2] = (byte)strsize;
				writer.WriteUTF8String(str, dataBuffer, 3);
				network.SendAll(1, dataBuffer, strsize + 3);
			}
			if(textStr.Length > maxTextSize) textStr = textStr.Substring(0, maxTextSize);
			text.text = textStr;
		}

		private void PrepareTransformBuffer()
		{
			int index = 2;
			lastPosition = transform.position;
			lastRotation = transform.rotation;
			index += writer.WriteVector3(lastPosition, dataBuffer, index);
			index += writer.WriteHalfQuaternion(lastRotation, dataBuffer, index);
		}
	}
}
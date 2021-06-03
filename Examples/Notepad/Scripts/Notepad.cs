using System;
using UdonSharp;
using UNet;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Xytabich.UNet.Notepad
{
	public class Notepad : UdonSharpBehaviour
	{
		private const int MAX_NOTES_COUNT = 16;

		private const byte NOTEPAD_NETWORK_MESSAGE = 0x80;
		private const byte REQUEST_NOTEPAD_INFO = 0x02;
		private const byte NOTEPAD_SYNC = 0X03;
		private const byte NOTEPAD_NOTE = 0X04;
		private const byte NOTEPAD_REMOVE = 0X05;
		private const byte NOTEPAD_REMOVE_ALL = 0X06;

		public VRC_Pickup pickup;
		public Collider[] colliders;
		public GameObject[] controls;
		public RectTransform notesRect;
		public ScrollRect notesScroll;
		public Toggle autoDeleteOld;
		public GameObject notePrefab;

		public float notesPaddingInControl;
		public float notesPaddingInView;

		[UdonSynced]
		private float scrollValue = 0f;

		private NetworkInterface network;
		private ByteBufferWriter writer;
		private ByteBufferReader reader;

		private bool isLocal;
		private bool syncRequested;
		private int syncRequest = -1;

		private int bufferedIndex;
		private int bufferedCount;
		private int[] bufferedTargets;
		private byte[][] bufferedMessages;

		private int maxTextBytes;
		private byte[] messageBuffer;

		private int notesCount;
		private Text[] notesText;
		private Toggle[] notesRemoveToggle;

		void OnEnable()
		{
			var owner = Networking.GetOwner(gameObject);
			isLocal = owner.isLocal;
			pickup.enabled = isLocal;
			foreach(var collider in colliders)
			{
				collider.enabled = isLocal;
			}
			foreach(var control in controls)
			{
				control.SetActive(isLocal);
			}

			var offset = notesRect.offsetMin;
			offset.y = isLocal ? notesPaddingInControl : notesPaddingInView;
			notesRect.offsetMin = offset;

			notesScroll.enabled = isLocal;
			if(isLocal) scrollValue = notesScroll.verticalNormalizedPosition;
			else notesScroll.verticalNormalizedPosition = scrollValue;

			notesCount = 0;
			notesText = new Text[4];

			var manager = GameObject.Find("UNet-NotepadsManager").GetComponent<NotepadsManager>();
			manager.SetNotepadForPlayer(owner.playerId, this);
		}

		void OnDisable()
		{
			isLocal = false;
			syncRequest = -1;
			syncRequested = false;
			scrollValue = 0f;
			bufferedMessages = null;
			bufferedTargets = null;
			for(int i = 0; i < notesCount; i++)
			{
				if(!object.Equals(notesText[i], null)) Destroy(notesText[i].gameObject);
			}
			notesText = null;
			notesRemoveToggle = null;
			notesCount = 0;
		}

		public override void OnPreSerialization()
		{
			scrollValue = notesScroll.verticalNormalizedPosition;
		}

		public override void OnDeserialization()
		{
			notesScroll.verticalNormalizedPosition = scrollValue;
		}

		public override void OnPlayerLeft(VRCPlayerApi player)
		{
			if(player == null) return;

			if(VRCPlayerApi.GetPlayerCount() < 2)
			{
				bufferedIndex = 0;
				bufferedCount = 0;
				Array.Clear(bufferedTargets, 0, bufferedTargets.Length);
				Array.Clear(bufferedMessages, 0, bufferedMessages.Length);
			}
			else
			{
				int playerId = player.playerId;
				int count = bufferedCount;
				int index = bufferedIndex;
				while(count > 0)
				{
					if(bufferedTargets[index] == playerId)
					{
						int startIndex = index;
						int counter = 1;
						index = (index + 1) % bufferedMessages.Length;
						count--;
						while(count > 0)
						{
							if(index == 0)
							{
								int toIndex = bufferedIndex + counter;
								int moveCount = bufferedMessages.Length - bufferedIndex - counter;
								Array.Copy(bufferedMessages, bufferedIndex, bufferedMessages, toIndex, moveCount);
								Array.Copy(bufferedTargets, bufferedIndex, bufferedTargets, toIndex, moveCount);
								Array.Clear(bufferedMessages, bufferedIndex, counter);
								index = (bufferedIndex + counter) % bufferedMessages.Length;
								bufferedCount -= counter;
								startIndex = 0;
								counter = 0;
							}
							if(bufferedTargets[index] == playerId)
							{
								counter++;
							}
							else break;
							index = (index + 1) % bufferedMessages.Length;
							count--;
						}
						if(counter > 0)
						{
							if(count > 0)
							{
								int fromIndex = startIndex + counter;
								Array.Copy(bufferedMessages, fromIndex, bufferedMessages, startIndex, count);
								Array.Copy(bufferedTargets, fromIndex, bufferedTargets, startIndex, count);
							}
							Array.Clear(bufferedMessages, startIndex + count, counter);
							bufferedCount -= counter;
						}
						break;
					}
					index = (index + 1) % bufferedMessages.Length;
				}
			}
		}

		public void Init(NetworkInterface network, ByteBufferWriter writer, ByteBufferReader reader)
		{
			this.network = network;
			this.writer = writer;
			this.reader = reader;
			if(isLocal)
			{
				bufferedIndex = 0;
				bufferedCount = 0;
				bufferedTargets = new int[4];
				bufferedMessages = new byte[4][];

				notesRemoveToggle = new Toggle[4];

				int bufferSize = network.GetMaxDataLength(true, 1);
				messageBuffer = new byte[bufferSize];
				maxTextBytes = bufferSize - 5;//msg id, msg type, notes count, text length
			}
			else
			{
				syncRequested = true;
				syncRequest = network.SendTarget(false, new byte[] { NOTEPAD_NETWORK_MESSAGE, REQUEST_NOTEPAD_INFO }, 2, Networking.GetOwner(gameObject).playerId);
			}
		}

		public void PrepareSend()
		{
			if(isLocal)
			{
				while(bufferedCount > 0)
				{
					var target = bufferedTargets[bufferedIndex];
					var buffer = bufferedMessages[bufferedIndex];
					if(target < 0)
					{
						if(network.SendAll(true, buffer, buffer.Length) < 0) break;
					}
					else
					{
						if(network.SendTarget(true, buffer, buffer.Length, target) < 0) break;
					}
					bufferedMessages[bufferedIndex] = null;
					bufferedIndex = (bufferedIndex + 1) % bufferedMessages.Length;
					bufferedCount--;
				}
			}
			else
			{
				if(syncRequested && syncRequest < 0)
				{
					syncRequest = network.SendTarget(false, new byte[] { NOTEPAD_NETWORK_MESSAGE, REQUEST_NOTEPAD_INFO }, 2, Networking.GetOwner(gameObject).playerId);
				}
			}
		}

		public void OnNetworkMessage(byte type, byte[] data, int index)
		{
			switch(type)
			{
				case NOTEPAD_SYNC:
					if(!syncRequested) return;
					var count = data[index];
					if(notesCount >= count)
					{
						syncRequested = false;
					}
					if(count > 0)
					{
						index++;
						CreateNote(reader.ReadVarUTF8String(data, index));
					}
					break;
				case NOTEPAD_NOTE:
					CreateNote(reader.ReadVarUTF8String(data, index));
					break;
				case NOTEPAD_REMOVE:
					RemoveNote(data[index]);
					break;
				case NOTEPAD_REMOVE_ALL:
					RemoveAll();
					break;
			}
		}

		public void SendInfoToPlayer(int playerId)
		{
			int msgSize = 3;
			messageBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
			messageBuffer[1] = NOTEPAD_SYNC;
			messageBuffer[2] = (byte)notesCount;
			if(notesCount > 0)
			{
				msgSize += writer.WriteVarUTF8String(notesText[0].text, messageBuffer, 3);
			}

			int index = 1;
			bool bufferSended = false;
			if(bufferedCount < 1)
			{
				if(network.SendTarget(true, messageBuffer, msgSize, playerId) >= 0)
				{
					bufferSended = true;
					while(index < notesCount)
					{
						bufferSended = false;
						msgSize = 3 + writer.WriteVarUTF8String(notesText[index].text, messageBuffer, 3);
						if(network.SendTarget(true, messageBuffer, msgSize, playerId) >= 0)
						{
							bufferSended = true;
						}
						else break;
						index++;
					}
				}
			}
			if(!bufferSended)
			{
				var message = new byte[msgSize];
				Array.Copy(messageBuffer, message, msgSize);
				AddMessageQueue(playerId, message);
				while(index < notesCount)
				{
					msgSize = 3 + writer.WriteVarUTF8String(notesText[index].text, messageBuffer, 3);
					message = new byte[msgSize];
					Array.Copy(messageBuffer, message, msgSize);
					AddMessageQueue(playerId, message);
					index++;
				}
			}
		}

		public void OnRemovePressed()
		{
			if(!isLocal) return;

			for(int i = 0; i < notesCount; i++)
			{
				if(notesRemoveToggle[i].isOn)
				{
					RemoveNote(i);
					break;
				}
			}
		}

		public void RemoveAll()
		{
			for(int i = 0; i < notesCount; i++)
			{
				Destroy(notesText[i].gameObject);
				notesText[i] = null;
				if(isLocal) notesRemoveToggle[i] = null;
			}
			notesCount = 0;

			if(isLocal && network.HasOtherConnections())
			{
				int msgSize = 2;
				messageBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
				messageBuffer[1] = NOTEPAD_REMOVE_ALL;
				if(network.SendAll(true, messageBuffer, msgSize) < 0)
				{
					var message = new byte[msgSize];
					Array.Copy(messageBuffer, message, msgSize);
					AddMessageQueue(-1, message);
				}
			}
		}

		private void RemoveNote(int index)
		{
			Destroy(notesText[index].gameObject);

			notesCount--;
			if(index < notesCount) Array.Copy(notesText, index + 1, notesText, index, notesCount - index);
			notesText[notesCount] = null;

			if(isLocal)
			{
				if(index < notesCount) Array.Copy(notesRemoveToggle, index + 1, notesRemoveToggle, index, notesCount - index);
				notesRemoveToggle[notesCount] = null;

				if(network.HasOtherConnections())
				{
					int msgSize = 3;
					messageBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
					messageBuffer[1] = NOTEPAD_REMOVE;
					messageBuffer[2] = (byte)index;
					if(network.SendAll(true, messageBuffer, msgSize) < 0)
					{
						var message = new byte[msgSize];
						Array.Copy(messageBuffer, message, msgSize);
						AddMessageQueue(-1, message);
					}
				}
			}
		}

		/// <summary>
		/// Tries to add text to list
		/// </summary>
		/// <returns>0 - ok, 1 - too long, 2 - no space, 3 - send error</returns>
		public int AddNote(string str)
		{
			int strsize = writer.GetUTF8StringSize(str);
			if(strsize > maxTextBytes) return 1;

			if(notesCount >= MAX_NOTES_COUNT)
			{
				if(autoDeleteOld.isOn) RemoveNote(0);
				else return 2;
			}

			CreateNote(str);

			if(network.HasOtherConnections())
			{
				int msgSize = 2;
				messageBuffer[0] = NOTEPAD_NETWORK_MESSAGE;
				messageBuffer[1] = NOTEPAD_NOTE;
				msgSize += writer.WriteVarUTF8String(str, messageBuffer, 2);

				if(network.SendAll(true, messageBuffer, msgSize) < 0)
				{
					var message = new byte[msgSize];
					Array.Copy(messageBuffer, message, msgSize);
					AddMessageQueue(-1, message);
				}
			}
			return 0;
		}

		private void AddMessageQueue(int target, byte[] data)
		{
			if(bufferedCount >= bufferedMessages.Length)
			{
				var tmpTargets = bufferedTargets;
				bufferedTargets = new int[bufferedCount * 2];
				Array.Copy(tmpTargets, bufferedIndex, bufferedTargets, bufferedIndex, bufferedTargets.Length - bufferedIndex);
				Array.Copy(tmpTargets, 0, bufferedTargets, 0, bufferedIndex);

				var tmpMessages = bufferedMessages;
				bufferedMessages = new byte[bufferedCount * 2][];
				Array.Copy(tmpMessages, bufferedIndex, bufferedMessages, bufferedIndex, bufferedMessages.Length - bufferedIndex);
				Array.Copy(tmpMessages, 0, bufferedMessages, 0, bufferedIndex);
			}

			bufferedTargets[bufferedIndex] = target;
			bufferedMessages[bufferedIndex] = data;
			bufferedIndex = (bufferedIndex + 1) % bufferedMessages.Length;
			bufferedCount++;
		}

		private void CreateNote(string text)
		{
			if(notesCount >= notesText.Length)
			{
				var tmpTexts = notesText;
				notesText = new Text[notesCount * 2];
				tmpTexts.CopyTo(notesText, 0);
				if(isLocal)
				{
					var tmpToggles = notesRemoveToggle;
					notesRemoveToggle = new Toggle[notesCount * 2];
					tmpToggles.CopyTo(notesRemoveToggle, 0);
				}
			}

			var obj = VRCInstantiate(notePrefab);
			var rt = (RectTransform)obj.transform;
			rt.SetParent(notesScroll.content);
			rt.localPosition = Vector3.zero;
			rt.localRotation = Quaternion.identity;
			rt.localScale = Vector3.one;

			var textComponent = obj.GetComponent<Text>();
			textComponent.text = text;
			notesText[notesCount] = textComponent;

			if(isLocal)
			{
				var toggle = obj.GetComponentInChildren<Toggle>();
				toggle.isOn = false;
				notesRemoveToggle[notesCount] = toggle;
			}
			notesCount++;
			obj.SetActive(true);
		}
	}
}
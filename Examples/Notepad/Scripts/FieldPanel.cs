using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Xytabich.UNet.Notepad
{
	public class FieldPanel : UdonSharpBehaviour
	{
		public InputField field;
		public Notepad notepad;

		public GameObject warnObj;
		public Text warnText;

		public GameObject thisPanel;
		public GameObject otherPanel;

		public void ToggleView()
		{
			thisPanel.SetActive(false);
			warnObj.SetActive(false);
			otherPanel.SetActive(true);
		}

		public void AddNote()
		{
			if(string.IsNullOrEmpty(field.text)) return;
			warnObj.SetActive(false);
			int result = notepad.AddNote(field.text);
			switch(result)
			{
				case 0:
					field.text = "";
					break;
				case 1:
					warnObj.SetActive(true);
					warnText.text = "Text is too long";
					break;
				case 2:
					warnObj.SetActive(true);
					warnText.text = "Too many notes, remove one or enable 'Auto-delete old'";
					break;
				case 3:
					warnObj.SetActive(true);
					warnText.text = "Network error, try send again";
					break;
			}
		}

		public void ClearText()
		{
			field.text = "";
		}

		public void OnInputUpdate()
		{
			if(warnObj.activeSelf)
			{
				warnObj.SetActive(false);
			}
		}
	}
}
using UdonSharp;
using UnityEngine;

namespace Xytabich.UNet.Notepad
{
	public class NotepadSpawnPoint : UdonSharpBehaviour
	{
		public Transform spawnPoint;

		public void SpawnNotepad()
		{
			var manager = GameObject.Find("UNet-NotepadsManager").GetComponent<NotepadsManager>();
			manager.SpawnNotepadOnPoint(spawnPoint);
		}
	}
}
A simple notebook for taking notes or communicating with other players in the world.
Each player has their own notebook that no one can pick up.

You can add and delete almost a large number of records, but the maximum is limited by the constant `MAX_NOTES_COUNT` in the `Notepad.cs` file (note, the number of records cannot exceed 255).

Can be used with both keyboard and VR.
The text is synchronized with all participants in the world, including new joiners.

You can also use [Rich Text](https://docs.unity3d.com/Documentation/Manual/StyledText.html) to decorate your notes.

### Co-autors
- TarotReaderFrog

### Helpers
They were of great help in testing and tuning the keyboard, as well as offering interesting ideas.
- Rianolakas
- Ain
- SleeponSunday
- BohemGrove

### Setup
- Download [latest unitypacke](https://github.com/Xytabich/UNet/blob/master/Examples/Notepad/Notepad-2.1.0.unitypackage) and upack it
- Setup [UNet](https://github.com/Xytabich/UNet)
- Add `UNet-NotepadsManager` to the scene and adjust the position where the tablet will spawn. *Note:* do not rename this object.
- Duplicate child object, depending on the capacity of the world, and put them in the VRC Pool list, which is located on `UNet-NotepadsManager`.
- Add trigger (ui button, for example) that calls "SpawnNotepad" event on `UNet-NotepadsManager`.
- You can also see an example in the UNet test world.

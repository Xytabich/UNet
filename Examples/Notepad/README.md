Notebooks for communication via text.

Each player has its own notepad, and others cannot intercept it.

Created entirely on UNet, i.e. does not use VRChat position synchronization, so there may be small lags of the notepad from the player's hands in other clients.

### Problems:
- Supports only PC keyboard at this moment.
- New players on scene has no text on notepads.
- VRC throws some exceptions when spawning prefabs, but no problems were found.

### Co-autors
TarotReaderFrog

### Setup
- Setup UNet
- Add TabletSpawner to the scene
- Add trigger (button, for example) that calls "SpawnNotepad" event
- You can also see an example in the scene

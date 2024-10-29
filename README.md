# Unused Assets Finder

_This tool is a Unity editor tool._

Unused Assets Finder is a Unity editor tool used to detect and monitor assets that are not used in your project.

## Usage

In the Unity Editor, in the top menu bar, under the "Tools" tab you'll find the "Unused Assets Finder" option.\
This will open the Unused Assets Finder editor window.

![](.Screenshots/Menu.png)

### Popups

When opening the editor window, popups will appear.\
If the tool has already been run, a popup will appear where you can **load the last analysis results** or **start a new analysis**.

![](.Screenshots/Popup1.png)

If the tool has never analyzed this project or if you choose to start a new analysis, a second popup will appear where you can **start a synchronous analysis** or **start an asynchronous analysis**.

![](.Screenshots/Popup2.png)

If the tool is not running an analysis or not showing results, the main menu is showing.

![](.Screenshots/General.png)

### Analysis

The analysis process is the same between the two run modes.\
This tool goes through three separate phases :
1. **Index all assets** added by the user (see limitations in the [support](#Support) section).
parse asset references contained in Unity YAML based files (scenes, prefabs, materials, etc...) as well as code and assets references in C# files before linking those references together.

### Run modes

### Loading last results

### Deletion

## Support

This tool supports all file types natively supported by the Unity Editor. You can find the [full list here](https://docs.unity3d.com/Manual/BuiltInImporters.html).

Several additional file formats are supported :
- TextMeshPro file formats (.fontsettings).
- Shadergraph file formats (.shadergraph, .subshadergraph).

// Don't support refs in shader code.
// Don't support custom packages.

## Known issues

ehebde
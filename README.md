Armok Vision
============

[Forum thread](http://www.bay12forums.com/smf/index.php?topic=146473) | [Screenshots](http://imgur.com/a/bPmeo)

A 3d realtime visualizer for Dwarf Fortress. 

For this restored checkout, follow [Development and verification](Docs/Development.md). The maintenance roadmap is in [CurrentTasks.md](CurrentTasks.md), and the vendored DF client revision is recorded in [Dependency provenance](Docs/ThirdParty.md).

## To Use

1. Install Dwarf Fortress and a current DFHack.
2. Download Armok Vision from the forum thread.
3. Load into a fortress mode map.
4. Start up Armok Vision.
5. Enjoy!

## Controls in the restored project

Keep **Active Input Handling** set to **Input System Package (New)**. Game controls are defined in the **Armok** action map in `Assets/InputSystem_Actions.inputactions`; edit bindings there instead of the legacy Input Manager. Map scenes use `InputSystemUIInputModule` for UI controls.

The restored project's terrain and material shaders use Unity's **Built-in Render Pipeline**. Keep the Render Pipeline Asset set to **None** in both Graphics settings and every Quality preset. A URP override makes these materials render magenta; moving to URP requires porting the custom shaders. This rendering choice works with the modern Input System above.

Startup logs include `Asset loading [stage]: ... ms` timings. These measure elapsed time, including frames yielded to keep the loading UI responsive. The total covers the content-loading pipeline; editor compilation/imports and the initial DF connection happen outside that timer.

Generated creature sprites and their lighting textures are cached under `Cache/CreatureSprites` in Unity's persistent-data directory (Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\ArmokRestored`). The cache refreshes when source images, sprite selection/order, Unity version, or rendering color space/API change. Missing, unreadable, or corrupt entries are regenerated; live DF data is fetched normally. Deleting this cache is safe while the application is closed. Developers changing sprite resampling or bevel generation should bump `SpriteTextureCache.Version`.

| Control | Default binding |
| --- | --- |
| Move camera | WASD or arrow keys |
| Raise / lower camera | E / Q |
| Move faster | Shift |
| Orbit / pan / zoom | Right drag / middle drag / scroll wheel |
| Select | Left mouse button |
| Follow DF camera | F |
| Change camera | V |
| Toggle minimap / UI / DF screen | Tab / F2 / backquote |
| Jump / crouch / grab | Space / C / G |
| Pause displayed time / earlier / later | P / [ / ] |
| Help / refresh / screenshot | F1 / F5 / F9 |
| Export meshes | Ctrl+M |

## To Contribute

Want to help out? We love contributions!

#### Bugs
If you've run into any bugs: [report them!](https://github.com/JapaMala/armok-vision/issues) (You'll need a [github account](https://github.com/).) Make sure to describe the issue in as much detail as you can, and to mention what system & Armok Vision version you're using. Also, check if what you're reporting has been reported before.

#### Artists
If you're an artist and want to contribute 3D models, sounds, concept art:

- There should be a folder called `StreamingAssets` somewhere around the armok vision executable (or inside, if you're on a mac.) If you edit the files inside and restart Armok Vision, it will use your modified assets. Be careful editing the .xml files, they're finnicky. You can post your edited resources in the forum thread and we can try to integrate them with the project.
- Alternatively, load things up in Unity and edit them there (see the following instructions).
- Check the [issues](https://github.com/JapaMala/armok-vision/issues); there may be something open about things that need prettifying.

#### Developers
If you know how to code and want to hack on the engine:

1. Install the Unity Editor version listed in `ProjectSettings/ProjectVersion.txt` (currently Unity 6.6).
2. Non-Windows users: install the [Git LFS](https://git-lfs.github.com/) extension if you haven't already (testable with `git lfs version`).
3. `$ git clone --recurse-submodules --depth 1 https://github.com/JapaMala/armok-vision.git` (or without `--depth 1` if you want the full history, but it's pretty big).
4. Load the `armok-vision` folder in the Unity editor.
5. Missing material texture arrays and definitions are generated automatically before entering Play Mode. Run `Mytools->Build Material Collection` after changing material files to rebuild them.
6. Hack around. Check out the [issues](https://github.com/JapaMala/armok-vision/issues) to find things that need fixing / ideas that could be implemented.
7. Submit a [pull request](https://github.com/JapaMala/armok-vision/pulls) with your changes!

#### Financially
If you want to buy the lead programmer a snack, you can donate on his [Patreon Page](https://www.patreon.com/japamala)

##### Structural Notes
(Some short notes for anyone getting started with the codebase.)

- Armok Vision is an application built with the [Unity engine](https://unity3d.com/). It connects to the [remotefortressreader](https://github.com/DFHack/dfhack/blob/master/plugins/remotefortressreader.cpp) DFHack plugin over TCP and exchanges [protobuf-formatted messages](https://github.com/DFHack/dfhack/blob/master/plugins/proto/RemoteFortressReader.proto). (You don't need to be familiar with DFHack to work with Armok Vision.)
- On the Unity side, the submodule [Assets/RemoteClientDF-Net](https://github.com/JapaMala/armok-vision/tree/master/Assets) contains the generated C# protobuf files, as well as classes for managing the network connection. The script [Assets/Scripts/MapGen/DFConnection.cs](https://github.com/JapaMala/armok-vision/blob/master/Assets/Scripts/MapGen/DFConnection.cs) runs the connection on a separate thread and exposes data collected from DF.
- The script that actually manages the onscreen map is [Assets/Scripts/MapGen/GameMap.cs](https://github.com/JapaMala/armok-vision/blob/master/Assets/Scripts/MapGen/GameMap.cs), which stores the `GameObject`s representing different map chunks. It calls the scripts in [Assets/Scripts/MapGen/Meshing](https://github.com/JapaMala/armok-vision/tree/master/Assets/Scripts/MapGen/Meshing) to build the actual meshes (on separate threads).
- Most assets - textures, 3d models, sprites, etc. - are loaded at runtime from [Assets/StreamingAssets](https://github.com/JapaMala/armok-vision/tree/master/Assets/StreamingAssets), which is copied directly to folder containing the generated app. The script that handles this is [Assets/Scripts/MapGen/ContentLoader.cs](https://github.com/JapaMala/armok-vision/blob/master/Assets/Scripts/MapGen/ContentLoader.cs).

There's a lot of other stuff but hopefully it'll be reasonably self-explanatory. Alternatively, you can ask in the forum thread, or the #dfhack IRC channel on freenode; somebody might be lurking who can help.

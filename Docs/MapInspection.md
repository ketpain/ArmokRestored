# Map inspection

The Map Mode scene uses a small, passive inspection card. Hover over the rendered map to see tile coordinates, material, geological layer, liquids, buildings, and up to two units. Click the world to pin the current tile; click the world again to release it. UI clicks do not change the pinned tile. Unrevealed tiles do not expose their contents, unless the existing Show Hidden Tiles setting is enabled.

Camera navigation remains on the modern Input System: WASD/arrows to move, Q/E to change height, mouse wheel to zoom, right-drag to orbit, middle-drag to pan, and F to follow DF. Escape opens settings. F2 toggles the HUD. The empty minimap shortcut is ignored when the scene has no minimap assigned.

`CursorInspection` on the map camera owns local selection, the target marker, and its generated UI children. `TileInspection` reads the current main-thread map snapshot and formats a short description, refreshed ten times per second. Inspection sends no commands to DF and does not require Draw Debug Info. The card and marker do not intercept pointer events. Their lifetime follows the camera, and selection clears on a new session or when leaving the map view.

The old dwarf/adventure command panels and their command components, clock panel, raw cursor text, log panel/toggle, and Discord button are inactive in Map Mode. The mode switcher is disabled so it cannot reactivate them. Their serialized objects and references remain in the scene for subsequent cleanup. Settings, connection dialogs, and loading progress remain available. Other sample and VR scenes have not been redesigned.

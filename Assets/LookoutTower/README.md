# Fire Lookout Tower (escape room set)

A ~100 ft (30.5 m to the cab floor) timber fire lookout in a night-time forest clearing, built for Unity 6 URP and sized for Quest 3
(the whole tower is ~18k triangles, the cab furniture ~5k).

## Build it

`Tools ▸ Lookout Tower ▸ Build Everything` (Unity also offers to run this the first time the assets import).

It creates, inside `Assets/LookoutTower/`:

| Output | What it is |
|---|---|
| `Materials/` | URP Lit materials using the generated PBR textures (tiling is set per material, UVs are in metres) |
| `Prefabs/LookoutTower.prefab` | The tower: frame, stairs, cab, roof, openables, colliders, cab props, reflection probe, spawn marker |
| `Prefabs/Props/Prop_*.prefab` | Fire finder, cot, wood stove, desk, chair, radio, logbook, binoculars, lantern (with flickering light), map board, cupboard, firewood |
| `Prefabs/Nature/` | 5 pine variants with 3 LODs each, a dead snag, 4 rocks, a stump, a fallen log |
| `Scenes/LookoutTower_ForestClearing.unity` | Night scene: 200 m terrain, ~380 trees, deadfall, path to the tower, moonlight, fog, night sky, post-processing, a desktop player |

Re-running the build updates everything in place.

## Walk around (desktop, before the VR rig)

Open the scene and press Play.

- **WASD** walk, **mouse** look, **Shift** sprint, **C** crouch (you also duck automatically under anything low)
- **E** open/close doors, the trapdoor and windows, or turn the fire finder sight
- **F** flashlight, **T** teleport between the path and the cab, **Esc** frees the cursor

Route: walk in through the open south face of the tower, take the switchback stairs, then the steep last
flight up to the trapdoor in the cab floor (look up at it and press **E** to open it). The cab door opens
onto the catwalk.

`Tools ▸ Lookout Tower ▸ Run Walkability Test` walks a 1.8 m player (and a crouched one) from the ground,
up every flight, through the hatch, round the fire finder to each piece of furniture, out onto the catwalk
and back down, and tells you where it gets stuck. It also runs at the end of Build Everything (results in
`LookoutTowerSource/build_log.txt`). Run it after moving furniture or changing the stairs.

## The tower, in numbers

- Cab floor / catwalk at **30.5 m (100 ft)**. Cab is **7.3 × 7.3 m** (24 ft) outside, 2.75 m ceiling, 1 m wide catwalk, five window bays per wall. Legs taper from 9.6 m apart at the ground to 6.4 m at the top.
- Stairs: twenty 36° switchback flights (8 risers each) with landings every 1.4 m, then a 63° ship's-ladder flight through a 0.82 × 1.6 m hatch.
  The hatch is long because a 1.8 m player's head reaches floor level almost at the foot of that flight;
  `_hatch_south_edge()` in `build_tower.py` works the size out from the player's height.
- Furniture hugs the walls (desk east, stove NE, cot NW, cupboard SE, map board south) so the middle of the cab
  is open for puzzles, with a 0.75 m+ walkway round the fire finder and to every piece.
- Windows: 11 top-hinged sashes (3 per wall, the south middle bay is the door).
- Coordinates: tower origin is the ground at the centre of the tower; +Z is north; the door faces south.

## Scripting the escape room

Every door, window and the trapdoor has `HingedOpenable` (`Openables/` in the prefab):

```csharp
trapdoor.Lock();                       // player can't open it
trapdoor.onLockedAttempt.AddListener(() => radio.Play());
trapdoor.Unlock(); trapdoor.Open();    // e.g. when a puzzle is solved
```

- `openAngle`, `speed`, `locked`, `startOpen`, plus `onOpened` / `onClosed` / `onLockedAttempt` events.
- A swing pauses instead of pushing through a player standing in the way (`stopAtCharacters`), so you
  can't open the trapdoor while standing on it or close it on someone's head.
- The fire finder has `TurnOnInteract` (15° steps, `CurrentAngle`, `onTurned`) for azimuth puzzles.
- `LanternFlicker.sputter` (0–1) makes the lantern gutter for scares.
- **Breath-fog window** (`BreathFogWindow` on `Openables/Window_N2`, the middle north window): lean in close to the
  glass from inside the cab, face it and breathe out. The glass fogs up where the breath lands, and a code someone
  wrote with a fingertip shows up as clear strokes in the fog (lower panes, about chest height). The fog lingers ~4 s,
  then evaporates from the edges in ~10 s.
  - Set the code on the **tower prefab** (`code`, default `2741`), not on the scene instance: Build Everything keeps
    the prefab's settings (and whichever window you moved the component to) but regenerates the scene.
  - For the lock: `fogWindow.Code` / `fogWindow.CheckCode("2741")`; `onRevealed` fires the first time the code is
    readable, `onCodeVisibleChanged(bool)` every time it appears or fades. `FogNow()` fogs it instantly (hints).
  - Breath comes from the microphone via `BreathSensor` (auto-created; add it to any object to tune thresholds, or
    tick `logLevels` to see mic levels). Hold **B** to breathe without a mic, or tick `simulateBreath` in Play mode.
    The Quest asks for microphone permission on first launch (`RECORD_AUDIO` is in the Android manifest).
  - Reach is 0.5 m from the mouth, so the desktop explorer works standing at the wall; in VR players lean in.
  - Move the writing with `writingCentre` / `digitHeight` (the gizmo shows the area when the window is selected);
    after moving the component to another window, use *Fit To Glass* from its context menu.
- Anything implementing `IInteractable` (`Prompt`, `Interact()`) works with the desktop explorer, and a
  VR interactor can call the same `Interact()` later.

## Quest 3 notes

- Switch to the **Mobile** quality level for Quest builds. The lantern and flashlight use realtime soft
  shadows on PC; additional-light shadows are off in the Mobile URP asset, so they just won't cast there.
- For best performance, bake lighting (Window ▸ Rendering ▸ Lighting ▸ Generate). The scene is set up for
  Subtractive mixed lighting; the tower and terrain are lightmap-static, and trees and props use light probes.
- Post-processing (vignette/bloom) is for desktop. Trim it for VR.
- Fog density is 0.032 (exp²). The sky below the horizon is the horizon colour, and the skybox exposure (1.2) is set so it renders exactly the fog colour; change fog colour, sky texture and exposure together.

## Troubleshooting

- **Falling through the ground in Play mode:** the project needs Unity's built-in *Terrain Physics* module
  (`"com.unity.modules.terrainphysics": "1.0.0"` in `Packages/manifest.json`, or Package Manager ▸ Built-in ▸
  Terrain Physics). Without it the terrain renders but its collider does nothing. The tower itself uses mesh
  colliders and doesn't need it. Remember this when moving the forest into the VR horror project.
- **Unity didn't notice regenerated files:** use Assets ▸ Refresh, or restart the editor.
- **Can't get through the hatch / stuck in the cab:** run the walkability test; it names the spot.
  Prop colliders are boxes round each piece (a round capsule for the fire finder), so moved furniture
  needs a ~0.7 m gap for the player to pass.

## Source

The Blender (bpy) scripts that generate the meshes and textures live in `LookoutTowerSource/` at the project
root (outside `Assets/`, so Unity doesn't try to import the .blend files). `LookoutTowerSource/Screenshots/`
is where `Tools ▸ Lookout Tower ▸ Capture Preview Screenshots` writes its images.

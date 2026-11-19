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

Open the scene and press Play. The player starts **locked in the cab** (at `Markers/CabSpawn`), facing the hatch keypad.

- **WASD** walk, **mouse** look, **Shift** sprint, **C** crouch (you also duck automatically under anything low)
- **E** open/close doors, the trapdoor and windows, turn the fire finder sight, or press a keypad button
- **F** flashlight, **Esc** frees the cursor
- **T** teleport between where you started and the cab: off in the escape room (it skips the hatch lock).
  Tick `allowCabTeleport` on the player's `DesktopExplorer` to use it while testing.

### Stair hatch keypad (the way out)

The trapdoor in the cab floor is locked. A keypad on a post just west of the hatch
(`Puzzles/HatchKeypad` in the tower prefab) opens it: enter **1789** and press Enter. The digits come from the
cab puzzles. A wrong code shows *Denied* and clears.

- Change the code on the **tower prefab's** keypad (`Keypad` component, `keypadCombo`), not on the scene instance.
  Build Everything keeps the prefab's code, and also keeps the keypad where you moved it, unless the builder's
  default placement changes (`HatchKeypadLayout`).
- `HatchKeypadLock` (on `HatchKeypad`) locks the trapdoor on start and, when the keypad grants access, unlocks it
  and swings it open (`openOnUnlock`). Listen to `onUnlocked`, or call `Unlock()` for a hint or a debug skip.
- Buttons carry `KeypadButtonInteract` (an `IInteractable`), so they work with **E** on the desktop and with the
  controller trigger through `XRInteractBridge`. The asset's own `KeypadInteractionFPV` isn't used: it reads the
  legacy Input Manager, and this project runs the Input System only.

### Morse lamp puzzle (digit 1)

A signal lamp hangs on a cord from the south ceiling beam, in line with the door (`Puzzles/Prop_MorseLamp`). It
blinks **1** in Morse, `· − − − −`, then goes dark for 3 s and repeats. A paper tag hanging from the shade says
**`X _ _ _`** on both sides: the digit is the first one in the code. A printed *Morse code / numbers* chart
(`Puzzles/Note_MorseGuide`) is pinned on the strip of paneling just east of the door, so from the middle of the cab
the lamp blinks right in front of it. The chart lists 1–6, so the player still has to read the signal.

- `MorseLamp` (on `Prop_MorseLamp`): `message` (digits/letters), `unit` (dot length, 0.35 s; a dash is 3 units, 1 unit
  dark between marks, 3 between characters), `pause`, `litEmission`; `IsLit` / `IsDash`, `LitAt(t)`, `Pattern`,
  `onMark(bool dash)` for a click sound, `alwaysLit` for testing. The bulb uses its own `M_MorseBulb` material, and
  the script switches its emission per renderer, with the `Signal Light` point light switching too.
- The lamp's digit, the tag text and the chart's digits are constants at the top of `PuzzleProps.cs`
  (`MorseLampMessage`, `MorseLampTag`, `MorseGuideDigits`). Both props sit in `Prefabs/Puzzles/`, so rebuilds keep
  them where you move them. The lamp has no colliders: its bulb is 2.1 m up and the tag bottom ~1.98 m.

### Fire finder puzzle (digit 7)

A dispatch note on the logbook says smoke was reported and circled on the wall map (south wall). The player finds
the circle (the north arm of the big lake, east-south-east of the tower triangle), then turns the fire finder sight (**E** /
trigger, 15° a press) until the sight's line runs over the same spot on the finder's table map: **105°**. While the
sight is on that bearing, the ranger's lamp on a radio mast ~90 m out along it signals the digit and its place in the
hatch code: **2 slow flashes** (the 2nd digit), a gap, **7 quick flashes** (the digit), a long pause, and repeat. Nothing
in the cab explains the pattern: working out that the slow flashes are the position is part of the puzzle (the slow
flashes are a little dimmer and warmer to help them read as a separate group). Other puzzles can give their digit's
place the same way.

- `FireFinderSighting` (on `Prop_FireFinder`): `targetBearing` (true north, clockwise), `tolerance`, `CurrentBearing`,
  `IsAligned`, `onAligned` / `onMisaligned`. Change the bearing on the **Prop_FireFinder prefab**: Build Everything
  keeps it and moves the map circle and the lamp to match (keep it a multiple of 15°, and check the circle still lands
  somewhere sensible on the map).
- `SightingBeacon` (scene, `Sighting Beacon (ranger's lamp)`): `slotFlashes` (code position, slow) and `flashes`
  (the digit, quick), set by the builder from `SmokeCodeSlot` / `SmokeCodeDigit`; `slowOn` / `slowOff`, `groupGap`,
  `onTime` / `offTime`, `pause`, `slowTint` / `slowBrightness`; `IsLit` / `IsSlowFlash`; `alwaysOn` for testing. Its glow
  uses the fog-free `LookoutTower/BeaconGlow` shader, so it reads through the night haze; trees and walls still hide it.
  Build Everything keeps a 5 m-wide corridor of the forest clear between the cab and the lamp.
- The table map on the fire finder is turned (UVs, `Prefabs/Props/FireFinder_NorthUp.asset`) so north is up and it
  matches the wall map.

Route: walk in through the open south face of the tower, take the switchback stairs, then the steep last
flight up to the trapdoor in the cab floor (look up at it and press **E** to open it). The cab door opens
onto the catwalk.

`Tools ▸ Lookout Tower ▸ Run Walkability Test` walks a 1.8 m player (and a crouched one) from the ground,
up every flight, through the hatch, round the fire finder to each piece of furniture, out onto the catwalk
and back down, and tells you where it gets stuck. It also runs at the end of Build Everything (results in
`LookoutTowerSource/build_log.txt`). Run it after moving furniture or changing the stairs.

### Escape timer and sound

- **10-minute clock** (`--- Game --- / Escape Timer`: `EscapeTimer` + `WristTimer`). It starts when the scene loads
  and stops when the keypad unlocks the hatch (you escaped). The time shows in the top-right corner on desktop; in
  VR it becomes a small watch face on the left wrist once an XR rig with a left controller is in the scene (found
  by name, or via `XRInteractBridge.leftController`). White, then amber under 3 minutes, pulsing red in the last
  minute, green once you're out.
- **Last minute:** a watch starts ticking (and speeds up) and a heartbeat fades in.
- **At 0:00:** an alarm clock rings, the controls stop, the lanterns gutter out, the view fades to black with
  "You didn't make it out.", and the scene restarts about 8 s later.
- **Winning** (`--- Game --- / Win Screen`: `WinScreen`): once the keypad has unlocked the hatch and the player climbs
  down through it (head 0.4 m below the cab floor), the view fades to black and a panel appears 1.3 m in front of
  them: "You escaped!", the time taken, and a **Play again** button that restarts the game. While it's up the camera
  draws only the panel's layer (UI), so the ladder shaft can't hide it. The button works by pointing a controller at
  it and pulling the trigger (`XRInteractBridge` aims only at the panel while it shows), or by looking at it and
  pressing E; any trigger, E, Enter, Space or a mouse click also plays again (after a 1 s grace period).
- Testing: on the Escape Timer component's ⋮ menu, *Debug: Jump To Last Minute* and *Debug: Run Out Now*; on the
  Win Screen's, *Debug: Show Win Screen*.
- **Ambience** (`--- Game --- / Ambience`: `AmbienceDirector`): gusting wind, timber creaks from the cab walls in
  strong gusts, and a distant owl every 25–60 s. Kept quiet on purpose (measured in the mix: wind about -43 dBFS,
  owl and creaks a few dB above it, a door creaking next to you about -30).
- **Object and puzzle sounds:** doors, the trapdoor (creak, lid landing, a thud when it's locked) and windows; a
  ratchet click per fire finder step; radio static when the sight lines up; paper rustle when you step up to a note or the Morse guide (`ProximitySound`).
- All sounds are CC0 recordings from BigSoundBank in `Audio/` (sources in `Audio/CREDITS.md`), played as trimmed
  `SoundCue`s. `GameAudioSetup.cs` sets every cue and volume; Build Everything calls it, and
  `Tools ▸ Lookout Tower ▸ Apply Timer And Sounds` re-applies it to the current prefab and open scene.

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
  can't open the trapdoor while standing on it or close it on someone's head. The trapdoor only counts someone
  actually in the opening (`onlyCharactersInOpening`), so you can close it from the cab while standing at its edge.
- The fire finder has `TurnOnInteract` (15° steps, `CurrentAngle`, `onTurned`) for azimuth puzzles.
- `LanternFlicker.sputter` (0–1) makes the lantern gutter for scares.
- **Breath-fog window** (`BreathFogWindow` on `Openables/Window_N2`, the middle north window): lean in close to the
  glass from inside the cab, face it and breathe out. The glass fogs up where the breath lands, and finger writing
  shows up as clear strokes in the fog (lower panes, about chest height): `_ _ 8 _`, the third digit of the exit
  code 1789. The fog lingers ~4 s, then evaporates from the edges in ~10 s.
  - `code` is what's written, one slot per character: a digit, `_` for a finger dash, or a space for nothing.
    Set it on the **tower prefab**, not on the scene instance: Build Everything keeps the prefab's settings (and
    whichever window you moved the component to) but regenerates the scene.
  - For the lock: `fogWindow.CheckCode("1789")` is true when an attempt agrees with the digits on the glass (blanks
    match anything); `onRevealed` fires the first time the writing is readable, `onCodeVisibleChanged(bool)` every
    time it appears or fades. `FogNow()` fogs it instantly (hints).
  - **Hint:** a ranger's note pinned at eye height to the wall post just north of the desk (east wall, beside the
    radio, `Puzzles/Note_FogHint`): *"Oct 3. Night watch. The
    north window fogs every time I lean in to look for smoke. Wrote the third number there so I'd stop forgetting
    it."*
  - A brass compass on the desk in front of the radio (`Puzzles/Prop_Compass`) shows which wall is north: its red
    needle (`CompassNeedle`) swings round to world +Z, whichever way the compass is turned. It's placed square to the
    cab, so the dial's N and the needle point at the north wall in the Editor too.
  - Breath comes from the microphone via `BreathSensor`, tuned so casual talking at the glass fogs it as well as
    breathing out (8 dB above the room's noise to start, full strength at 16 dB; the room's noise is a low point of the last 6 s of sound). It's auto-created at Play, so
    Inspector tweaks reset; change the defaults in `BreathSensor.cs`, or add the component to an object in the scene
    to keep your own values. Tick `logLevels` to see mic levels. Hold **B** to breathe without a mic, or tick `simulateBreath` in Play mode.
    The Quest asks for microphone permission on first launch (`RECORD_AUDIO` is in the Android manifest).
  - Reach is 0.5 m from the mouth, so the desktop explorer works standing at the wall; in VR players lean in.
  - Move the writing with `writingCentre` / `digitHeight` (the gizmo shows the area when the window is selected);
    after moving the component to another window, use *Fit To Glass* from its context menu.
- **Puzzle props** live in the tower prefab's `Puzzles` group, alongside the hatch keypad. Build Everything puts
  each instance of a prefab from `Prefabs/Puzzles/` back where you left it (the keypad has its own rebuild code),
  so add new puzzle props there. `Tools ▸ Lookout Tower ▸ Build Puzzle Props`
  regenerates the props in `Prefabs/Puzzles/` (the note's text is in `PuzzleProps.cs`) and resets their placement.
  A rebuild also adds back any default prop (the note, the compass, the Morse lamp and chart) missing from the group.
- **Fuse box** (`Puzzles/FuseBoxPuzzle` in the tower prefab; source `Prefabs/FuseBoxPuzzle.prefab`, which has its own
  builder like the hatch keypad, so it lives outside `Prefabs/Puzzles/`): the power's out.
  The panel is on the wall just east of the cab door, with three screw-in fuse slots, a main switch and a wiring card
  inside its door. Conduits run from the panel to what each slot powers, so players can trace them:
  slot 1 = pendant lamp over the fire finder (15 A, blue), slot 2 = the radio, whose dial stays dark until powered
  (10 A, red), slot 3 = the caged lamp over the door outside (20 A, yellow).
  - Start: slot 1 has a blown 15 A fuse, slot 2 has a 20 A fuse (wrong), slot 3 is empty. The red tin at the front
    of the desk beside the lantern holds a 15 A fuse; the crate on the shelf under north window N1 (next to the fog
    window) holds the 10 A and a 30 A decoy. Both sit at desk/waist height so nobody has to crouch, and clear of other props' colliders.
  - Interact with a fuse to pick it up and with a slot, the tin or the crate to put one in. You carry up to 4: one in
    hand (lower right of the view), the rest in a pocket; **Q** (A/X on a Quest controller) switches which is in hand.
    Taking a fuse from the tin or crate pockets the one in hand; at a panel slot the one in hand is swapped in.
  - Main switch ON: each slot with exactly its rating lights its circuit. A fuse rated too high anywhere sparks and kicks
    the switch back off; one rated too low crackles and stays dead; touching a slot with the main on gives a zap.
    Nothing is ever destroyed, so the puzzle can't soft-lock.
  - **Reward (4th hatch digit):** when all three are live the power surges in: the pendant lamp dips and swells, gets a
    jolt and swings, and light leaking through pinholes punched in the top of its shade spells `_ _ _ 9` on the ceiling
    just in front of it (matching the Morse tag's `X _ _ _` and the fog window's `_ _ 8 _`). It fades in with the surge,
    slides about as the lamp swings and follows the lamp's brightness, so it goes dark if the main switch goes off. A
    hint line appears after a few seconds in case nobody looks up. This is `LightLeakReveal` on `CeilingLamp` (wired to
    `onSolved`) driving an additive decal (`Light Leak (code)`, `M_LightLeak`), not a cookie light, so it looks the same
    on Quest. Its texture `Textures/T_FuseBox_LightLeak.png` is generated by the builder from `LeakCode`.
    To see it without solving, in Play mode use `Tools ▸ Lookout Tower ▸ Fuse Box: Skip To Reveal` and
    `Fuse Box: Toggle Reveal Camera`.
  - Events on `FuseBoxPanel`: `onSolved` (all three live), `onCircuitChanged(index, live)`, `onFault`, `onMainSwitched`;
    each circuit's `PoweredOutput` has `onPowered`/`onUnpowered`. Lock the panel door with its `HingedOpenable`.
  - Rebuild with `Tools ▸ Lookout Tower ▸ Build Fuse Box Puzzle` (makes the prefab and puts it in the tower). Build
    Everything re-attaches the existing prefab, so edits to `FuseBoxPuzzle.prefab` survive; ratings and starting fuses
    are at the top of `FuseBoxPuzzleBuilder.cs`.
  - Models come from `LookoutTowerSource/blender_scripts/build_fusebox.py` (same kit and textures as the props);
    `render_fusebox.py` renders previews.
- Desktop testing: arrow keys look around (Shift for fine aim), **F3** shows what the crosshair ray hits.
- Aiming goes through `InteractionPicker` (desktop crosshair and VR controller rays): an exact hit wins, otherwise the
  interactable closest to the ray within ~6 cm (9 cm inside a container) that is in plain sight, never the player's own
  body and never through walls, so small things don't need pixel-perfect aim.
  `InteractProxy` makes a collider act for another interactable (the crate's sides open its lid).
  `Tools ▸ Lookout Tower ▸ Check Fuse Box Reach` tests every fuse box interactable from standing height and writes
  `LookoutTowerSource/fusebox_reach_report.txt`.
- Anything implementing `IInteractable` (`Prompt`, `Interact()`) works with the desktop explorer, and a
  VR interactor can call the same `Interact()` later.

## Easter egg: gorilla statue

A small stone gorilla perches on the catwalk railing just outside the cab door, two rail posts east of the doorway
(`Easter Eggs/Gorilla Statue`, built from primitives in `GorillaStatue.cs`). It builds itself at Play if the scene
doesn't already have one; to see and move it in the Editor, use `Tools ▸ Lookout Tower ▸ Place Gorilla Statue (Easter Egg)`
(Build Everything adds it too). It's not part of any puzzle.

- Take it (**E** / trigger) and the player gets Gorilla-Tag-style movement (`GorillaMode`, added to the player):
  - **Desktop:** walking is off. **Left / right click** slaps the surface under the crosshair with that hand (1.7 m reach):
    slap the floor ahead to hop forward, high on a wall to go up it. **Hold** the button to grip (gravity almost off),
    so you can climb hand over hand.
  - **VR:** hands stick to whatever they touch and the body moves the other way; let go mid-push to fling. The rig's
    XRI move/teleport providers are paused while it's on (turning stays). Untested until the scene has a VR rig.
  - **G** (or clicking the left thumbstick) toggles it off and on.
- A leash keeps the body over the cab and catwalk while you're up there (`leashHalfExtent` 4.3 m, above `leashMinY`),
  so the gorilla can't vault the railing and skip the hatch. Below the cab floor (after the hatch opens) it's free.
- `DesktopExplorer.externalMovement` is the only hook into the existing player: while it's set, the explorer still
  looks, crouches and interacts but doesn't walk or apply gravity.

## Snow

A light, steady snowfall (`--- Environment ---/Snow`), generated by Build Everything:

- One particle system rides 12 m above the main camera (`SnowFollow`), so it snows around whoever is playing (on
  the path, on the stairs, out the cab windows) without simulating flakes over the whole map. Flakes are simulated
  in world space, so they keep falling where they are as you move; the fog hides the edge of the 30 m volume.
- It never snows inside the cab: `Cab Snow Blocker` is a trigger box over the cab interior, and the particle
  Trigger module kills any flake in it. Every interaction and physics query ignores triggers, so it blocks nothing.
- Flakes fall at a steady ~1.2 m/s, flutter a little (Noise), slowly tumble, and drift gently with the scene's
  WindZone (External Forces, kept low: that WindZone is tuned for trees). `SnowFollow` also nudges the emitter upwind
  every 1.5 s so the flakes at eye height stay centred on the player.
- Flakes aren't discs: `T_SnowflakeSheet` holds four irregular clumps (2×2), each flake picks one at random (Texture
  Sheet Animation) at a random rotation. The material (`M_Snow`, URP Particles/Unlit) is a soft moonlit grey-blue,
  and some flakes start fainter, so the snow doesn't sparkle at night.
- Heavier or lighter: `SnowFollow.intensity` (scales the emission rate), or `SetSnowing(false)` to stop it (flakes in
  the air finish falling). The rate, size and volume are constants at the top of the snow section of the builder.
- Quest: about 260 flakes/s (3.5–7 cm), at most 4500 alive, unlit, no shadows or soft particles.

### Clouds

The night sky (`M_SkyNight`) uses `LookoutTower/NightSkyClouds`: the same star panorama as Skybox/Panoramic (same
sampling, tint and exposure, so the sky below the horizon still matches the fog colour) with a drifting cloud layer
on top: moonlit grey clouds over most of the sky, darker breaks showing stars, fading out towards the horizon.
Two reads of a tileable noise texture (`T_CloudNoise`, generated by the builder), so it's cheap on Quest. Tune it on
the material: `_CloudCoverage`, `_CloudSoftness`, `_CloudColor`, `_CloudOpacity`, `_CloudScale`, `_CloudSpeed`
(drift), `_HorizonFade`; the builder sets these in `BuildMaterials`, so change them there to keep them.

## Play in VR (Quest)

With a headset running (Quest build, Quest Link, or the Meta XR Simulator), `VrRig` builds the VR player at Play
where the desktop player stands and switches the desktop player off. It's built at runtime, so Build Everything and
the scene file don't need to know about it; a scene that already has its own rig (an `XRInteractBridge`) is left alone.

- **Head** tracked (`TrackedPoseDriver`, floor-level tracking), tagged `MainCamera`, so the snow, fog window, fuses,
  timer and win screen all follow the headset. The head is put on the cab spawn, facing the keypad, once tracking starts.
- **Left stick** walks where you look (hold the **left grip** to go faster). **Right stick** snap-turns 45°
  (`VrLocomotion.smoothTurn` for smooth turning). The body follows the headset, so you can also walk and duck for real.
- **Trigger** uses what the controller's laser points at (it turns orange when there's something to use).
  **A / X** swaps fuses, **right stick click** toggles a flashlight on the right controller.
- The Y/B cab jump is off in the escape room, same as T on the desktop (`XRInteractBridge.allowCabJump`).
- Movement stops when the timer runs out or the win screen is up.

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

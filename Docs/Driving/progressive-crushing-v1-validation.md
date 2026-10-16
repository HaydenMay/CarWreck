# Progressive Crushing V1: validation report

Validation of `feature/progressive-crushing-v1` (head `fed6b5e`, three commits on top of `main` `92cf25f`) in Unity 6000.6.0f1 with real PhysX and a local WebGL build.
Nothing was merged or published. Evidence is labelled **measured** (this run) or **derived**.

## A. Repository state

| Item | Finding |
|---|---|
| Branch validated | `fed6b5e` checked out as local `validate/progressive-crushing-v1` (tracking `origin/feature/progressive-crushing-v1`) |
| Based on | `main` `92cf25f`; collider work (`65653d6`) and the enemy driver are in the history |
| Progressive crushing in `main`? | **No.** `fed6b5e` is not an ancestor of `main` |
| Live deployment | `version.json` reports build `20261016-1817-92cf25f`, source `main` @ `92cf25f` (collider fix, **no** progressive crushing). The `gh-pages` history has no progressive-crushing publish. The build the project owner played therefore came from somewhere other than the live site (probably a local build); this run cannot say which revision that was |
| Implementation report | `Docs/Driving/progressive-crushing-v1.md` reviewed. It states that no Unity run had been done; this report supplies it |

Production code reviewed (`DeformablePart`, `VehicleDamageModel`, `ProgressiveCrush`, `DrivingGame`): the damage solve, receivers, hitbox and physics are untouched; progressive display is opt-in (`progressiveCrush.enabled`, on only for arena cars; edit-mode tools see instant dents unless they opt in).
The validator `ProgressiveCrushValidation` uses the real `VehicleRig` / `VehicleDamageModel` / `DeformablePart` on the real car meshes and real PhysX in the DriveArena (not stand-ins), and compares final meshes with an instant replay of the same impact records.

## B. Progressive crush validation (`CarWreck > Validate Progressive Crush`)

First run: 34 checks, **29 pass, 5 fail**, no exceptions. After two test-only corrections (section H): **34 / 34 pass**, 0 exceptions.

The 5 first-run failures were **not production problems**:

| Failure | Cause | Evidence |
|---|---|---|
| A. light frontal: "no damaging hit" | The test's 30 km/h closing speed produces **no damage even without progressive crushing** (below the damage threshold after the momentum split) | probe: 30 km/h, progressive OFF and ON: both "no hit"; 40 km/h gives 5.1 % / 0.1 % |
| C, D, H (pickup): final mesh vs instant replay 1.8e-5 m / 9.5e-7 m / 5.5e-6 m, normals up to 3e-3 (tolerance 1e-6) | The replay is built on a second car 400 m away; float rounding there differs slightly. **Instant mode shows the identical residual** | `ProgressiveEquivalenceProbe`: severe 170 km/h, instant run vs its own replay: pickup 1.8e-5 m, normals 2.9e-3; progressive run: 1.8e-5 m, 2.8e-3. Offset: 9.5e-7 / 1.7e-5 in both |
| H. both cars crush together | Cascade from the H pickup replay residual (8 frames crushed together) | passes after the tolerance correction |

Required scenarios (all PASS after the corrections; "ms" are crush times, "frames" at 50 fps):

| Scenario | Result |
|---|---|
| A. Light frontal (50 km/h closing) | PASS: travel 0.050 m, 75 ms, 4 frames, front third 43 % / back third 2 % at frame 1, exact final (replay 0) |
| B. Moderate frontal (80 km/h) | PASS: 0.174 m, 112 ms, 6 frames, 27 % / 0 %, replay 0 |
| C. Severe frontal (170 km/h) | PASS: 0.650 m, 240 ms, 13 frames, last-frame step 0 %, no backward vertex moves |
| D. Offset (0.9 m) | PASS: 0.338 m, 161 ms; struck corner 0.338 m, other 0.117 m, rear 0 |
| E. Side (T-bone) | PASS: 0.165 m, 110 ms; one side only |
| F. Rear | PASS: 0.163 m, 109 ms; nose untouched |
| G. Repeated / during a crush | PASS: 4 sequences (same region at 40 %, other region at 30 %, three regions, five rapid hits): jump at a hit 0 m, no backward step, final equal to the instant replay (0 m, normals 0), no impact lost; frame rate 20 / 50 / 60 / 144 fps: same final shape, done within one frame of the crush time |
| H. Simultaneous | PASS: both arena cars crush together (8 frames overlap); four standalone cars crush in the same frames, worst difference to instant replays 0 m |
| I. Player and AI | PASS: player (0.290 m, 147 ms) and enemy (0.093 m, 88 ms) both crush progressively, exact final |
| J. Collider compatibility | PASS (section E) |
| K. Reset during a running crush | PASS: meshes, shells restored; crush stopped; still restored 20 frames later; next hit identical to an instant hit |
| L. Performance | PASS (section F) |
| Instant paths | PASS: progressive off and edit-mode tools without opt-in still see instant dents |

## C. Driving regression (`CarWreck > Validate Driving`)

**88 PASS, 0 FAIL** (same count as before progressive crushing: 76 earlier + 12 collider checks). Edit-mode validation uses instant dents, so this confirms the unchanged code path, not progressive display.

## D. Collision Lab regression

`verify`, `collision-verify`, `collision-geometry`, `collision-persist-a/b`, `collision-overflow`, `vehicle-switch`: **52 PASS, 0 FAIL** (7 commands, with graphics). The 13 impact-table rows of `collision-verify` (hits, energies, impulses, dent depths and widths) are **identical** to the run on `main` before the branch (only the timing column differs). Progressive crushing is off in the Lab.

Equivalence of the **final solver result**: the progressive final mesh equals an instant replay of the same impact records to float rounding (see B), and `ProgressiveEquivalenceProbe` shows the same first-hit strengths and records in instant and progressive arena runs (identical strengths in every scenario). Arena physics is **not repeatable run to run** in the editor (two instant runs of the same scenario differ by 2e-3 to 1e-2 m after later contacts), so whole-scenario meshes cannot be compared exactly between runs; the replay comparison is the valid test.

## E. Collider compatibility (`ColliderGapProbe`, now runnable with progressive crushing)

Same probe, same scenarios, with progressive crushing on (`CGP_PROGRESSIVE=1`), refit on, compared with the instant-mode numbers from the collider release:

| Scenario | Instant (collider release) | Progressive |
|---|---|---|
| Moderate 60 km/h, excess gap max (pickup / muscle) | 0.021 / 0.039 | 0.023 / 0.040 |
| Severe 170 km/h | 0.072 / 0.051 | 0.072 / 0.051 |
| Repeated hits, struck car's contact z | 2.350, 2.171, 1.999, 1.921 | 2.350, 2.172, 2.002, 1.923 |
| Offset | 0.043 / 0.046 | 0.043 / 0.047 |
| Two damaged cars pressed together, front-most metal / mid height | -0.210 / +0.072 | -0.208 / +0.072 |
| Core limit after 6 severe hits, largest gap | 0.138 / 0.135 | 0.138 / 0.135 |

* The refit waits for the crush: refit 0.20 s after an 80 km/h hit (crush done at 0.11 s) and at the end of a 170 km/h crush (0.24 s), never inside it (validation J). The shell stays still while the metal moves; the gap **during** the crush is 0.04 to 0.13 m (80 km/h) and 0.08 to 0.50 m (170 km/h) for at most 0.24 s, and after the refit -0.005 to 0.006 m.
* **Pressed against walls (new, measured):** the muscle car into the north wall at 80 km/h and then driven on for 3 s: dent 0.665 m, shell retracted 0.553 m, max angular speed 0.52 rad/s, max vertical speed 1.10 m/s, no NaN, car at rest with its centre at z 68.05 against the wall at z 70. A pickup **pinned between the muscle car (50 km/h then driven on 4 s) and the wall**: both cars at rest, dents 0.045 / 0.190 m, shells 0.012 / 0.142 m, max angular speed 0.73 rad/s, vertical 0.34 m/s, no NaN. So the wedged situation is a stable physical outcome: no invalid colliders, no jitter, no ejection (stuck detection was not touched).
* Existing limitations unchanged: fixed core limit, narrow-dent median fit, 0.2 m upper-edge overlap when two crushed cars are pressed together.

## F. Performance

**Unity Editor (measured, `ProgressiveCrushValidation`, Mono, single-threaded):**

| Case | Result |
|---|---|
| Muscle car, 62,014 vertices | 9 frames, mean 1.75 ms, max 2.20 ms per crush frame |
| Hatchback, 80,108 vertices | 9 frames, mean 2.28 ms, max 2.82 ms |
| Pickup, 114,694 vertices | 8 to 10 frames, mean 2.7 to 3.1 ms, max 3.1 to 3.4 ms |
| Four pickups crushing at once | mean 6.9 ms, max 9.2 ms per frame for all four |
| Managed allocation per crush frame | 0 bytes (Mono heap growth over 7 frames) |
| Impact frame (`AddImpact`) | instant 165 ms vs progressive 177 ms (single editor measurement, JIT included; arena logs show tens of ms) |
| Ramp memory | 52 bytes per vertex, 5.7 MB for a whole muscle car if every part crushes (derived) |

**WebGL, local build `20261016-1941-fed6b5e` (0 errors, 21.9 MB), desktop Chromium in the app's browser pane, software-rendered (measured):**

`pcrushMs` / `tcrushMs` (verified in `VehicleDamageModel.WorstCrushFrameMilliseconds` and `DrivingGame.ReportToPage`): the **slowest single `AdvanceCrush` call** (the ramp loop plus the CPU-side mesh `SetVertices` / `SetNormals`, and on the final frame `FinishRamp`) for that car since the car's impacts were last cleared by a reset. It excludes the impact-frame solve and the GPU upload.

| Scenario | pcrushMs / tcrushMs |
|---|---|
| Muscle car into the parked pickup at 86 km/h | 0.7 / 1.0 |
| Head-on, 129 km/h closing (both cars crushing; hit 85 % / 97 %) | 0.5 / 0.9 |
| Enemy attacks the idle player at 70 km/h | 0.4 / 0.3 |
| About 100 s of sustained enemy combat, repeated rams | 0.6 / 0.8 maximum |

So on desktop WebGL the worst crush frame was **1.0 ms or less** for any car, well under the editor figures (the editor runs unoptimised Mono). The impact-frame deformation time in the HUD was 15 to 43 ms per car, the same order as before progressive crushing (earlier smoke test: 27 / 30 ms). The pane ran at 5 to 36 fps (software rendering, partly throttled when hidden), so **frame-time spikes, visible stutter and GC cannot be assessed from it**.

**Not tested:** any phone or mobile browser, other desktop browsers, WebGL garbage collection, GPU upload cost, frame-time spikes on real hardware, two large cars crushing at once on a phone.

Observed behaviour at low frame rates (derived from the code, seen indirectly): a frame advances the crush by at most 1/30 s, so below 30 fps the crush is stretched in real time (about 8 frames for a heavy crush, so roughly 0.4 s at 20 fps and 1 s at 8 fps) and the collider refit waits for it. This is by design (the report lists it) and is not a defect, but it is the thing to look at on a slow phone.

## G. Bugs and concerns

| # | Issue | Severity | Blocks release? |
|---|---|---|---|
| 1 | The live site does not contain progressive crushing; the revision the owner tested is unknown | Info | No (process: confirm which build was reviewed before publishing) |
| 2 | The shipped validation had a wrong light-hit speed (30 km/h makes no damage) and a replay tolerance tighter than the engine's own float noise | Test bug | No (fixed, section H) |
| 3 | Below 30 fps the crush slows down and delays the refit | Low | No; check on a real phone |
| 4 | `pcrushMs` / `tcrushMs` are worst-since-reset values, not per hit | Info | No |
| 5 | Four large cars crushing together cost about 7 to 9 ms per frame in the editor | Low | No; relevant if combat ever has many simultaneous crushes |
| 6 | Editor arena runs are not repeatable run to run (PhysX), so whole-run mesh comparisons between runs are unreliable | Info | No |

No production bug was found. No physics defect appeared in the wall-pinned or pressed cases.

## H. Files changed (this validation only, on `validate/progressive-crushing-v1`)

| File | Change and reason |
|---|---|
| `Assets/Driving/Editor/ProgressiveCrushValidation.cs` | Test correction: scenario A uses 50 km/h (30 km/h makes no damage); arena replay tolerance 5e-5 m / 1e-2 for normals (named constants with the reason). Standalone replays (G, frame rate, four cars) keep the 1e-6 limit and still pass exactly. Re-run: 34 / 34 |
| `Assets/Driving/Editor/ColliderGapProbe.cs` | Diagnostic: optional progressive crushing (env `CGP_PROGRESSIVE=1`), plus the wall and pinned scenarios (9a, 9b) |
| `Assets/Driving/Editor/ProgressiveEquivalenceProbe.cs` (new) | Diagnostic that runs arena scenarios with progressive crushing on and off and compares records and final meshes with instant replays |
| `Docs/Driving/progressive-crushing-v1-validation.md` | This report |

No production code, scene, collision engine or asset was changed.

## I. Recommendation: CONDITIONALLY READY

Core validation passed (34 / 34 progressive checks, 88 / 88 driving, 52 / 52 Lab, Lab result rows identical), final deformation equals the solver, repeated hits accumulate without snapping, reset is clean, colliders stay consistent and stable (including against walls and when pinned), and the WebGL CPU cost per crush frame is about 1 ms on desktop. Remaining before release: a real-phone check, and publishing a build from the branch that the owner can confirm is the revision they liked.

## J. Next action

Build and publish from the merged branch only after you confirm the reviewed build was this revision, then play two cars crushing head-on on a phone and read `pcrushMs` / `tcrushMs` from the page state (or watch for stutter). If a slow phone shows trouble, the report's mitigation order applies (precompute stagger delays, 30 Hz ramp).

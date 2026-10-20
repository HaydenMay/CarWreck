# Attachment System V1 — Sonnet Implementation Plan

Companion to [`architecture-v1.md`](architecture-v1.md). Status: **proposal, gated on product-owner approval** (architecture §10).
Baseline: `main` @ `3057f05`.

## Decision summary

- Build **Stage 1 (visual-only)** first:
  - data, validator, loadout persistence, mount, lifecycle hooks, a loadout panel and an editor fit tool;
  - **no colliders, no Rigidbody, no mass, no scoring change.**
- A regression test proves physics and scoring are identical with attachments equipped. That test is the gate for each milestone.
- **Stage 2 (proxies)** begins only after Opus delivers the §7.3 interface and M0 baseline numbers exist.
- Ownership:
  - Sonnet: gameplay code, UI and tools.
  - Opus: receiver, damage model, validation-test changes and every physics decision.
  - SOL: item art to the export contract (architecture §6).

## 1. Phases and ownership

| Milestone | Owner | Content | Depends on |
|---|---|---|---|
| **M0 Baseline** | Sonnet (Opus reviews the metrics) | Read-only telemetry: per-car hits and energy by tier, wreck bonus, survival, Showdown result. Editor batch runner, plus CSV/JSON output. No balance changes | Approval |
| **M1 Data + validation** | Sonnet | `AttachmentDefinition`, `VehicleAttachmentFit`, `AttachmentCatalog`, `Loadout`, `LoadoutValidator`, PlayerPrefs persistence. Unit tests | Approval |
| **M2 Visual mount + lifecycle + UI** | Sonnet (art from SOL) | `AttachmentMount`, `BuildCar`/`ResetCar`/wreck-paint hooks, loadout panel, fit-preview tool, 3 items × 3 bodies | M1. Grey-box placeholders allowed until SOL delivers |
| **M3 Opus interface** | **Opus** | `AttachmentProxy`, receiver exclusions and slot fields, damage-model discovery exclusion, validation updates | M2 merged, user authorizes Stage 2 |
| **M4 Proxy items + WebGL check** | Sonnet (Opus reviews) | `ProxySpec` boxes for Push Bar and Ram (front) and Tow Hitch (rear). Attribution and footprint harness. Mobile perf pass | M0 baseline, M3 |

M0 and M1 can run in parallel. Nothing in M0–M2 touches `Assets/DamageLab/**`.

## 2. Touch points, in dependency order

**New** (proposed folder `Assets/Derby/Scripts/Attachments/`):
1. `AttachmentDefinition.cs`, `AttachmentSlot`/`AttachmentRarity` enums, `VehicleAttachmentFit.cs`, `AttachmentCatalog.cs`
2. `Loadout.cs` + `LoadoutStore.cs` (PlayerPrefs JSON, key `carwreck.loadout.v1.<vehicleKey>`)
3. `LoadoutValidator.cs` (pure C#, no `UnityEngine.Object` lookups beyond the catalog)
4. `AttachmentMount.cs` (MonoBehaviour on the car root; owns the `Attachments` child; diff-apply; `ResetVisuals`/`SetWrecked`/`Clear`)
5. `AttachmentPanel` UI (a new panel or step reached from the menu; `DerbyUI.MenuHeight` 640 is full)
6. Editor: `Assets/Derby/Editor/AttachmentFitTool.cs` (fit-preview window and clearance matrix), `AttachmentValidation.cs` (tests)
7. Data assets: `Assets/Derby/Settings/Attachments/` (catalog, 3 fits, items, shared `Attach_Metal`/`Attach_Accent`/`AttachPaint`/wreck materials)

**Edited** (minimal, additive):
- `MatchModel.cs` `Participant` (41): add a `Loadout loadout` field. No rules change.
- `DerbyGame.cs`:
  - `BuildCar` (181): add the mount **after** `Initialize` (192), followed by a comment that marks the ordering as load-bearing.
  - `ResetCar` (617): call `mount.ResetVisuals()`.
  - `ApplyWreckPaint` (267): call `mount.SetWrecked(true)`.
  - `SelectVehicle` (275): restore that vehicle's saved loadout.
  - `StartMatch` (365): call `mount.Apply` when the loadout changed.
  - Serialized `AttachmentCatalog` reference.
- `DerbyUI.cs`: a button or route to the loadout panel. START is disabled while the validator reports errors.

**Do not edit** in M0–M2:
- `VehicleRig`, `VehicleDamageModel`, `VehicleHitbox`, `CollisionDamageReceiver`, `ArcadeCarController`, the AI scripts and `MatchRules` values;
- vehicle definition assets, the scene's physics settings, `DerbyValidation`/`ShowdownValidation` assertions.

## 3. Candidates and validation criteria

| Item | Slot | Cost (hyp.) | M2 | M4 | Validation |
|---|---|---|---|---|---|
| Junk Push Bar | Front | 2 | visual | wide, low front proxy | Fits 3 bodies; ≤5 mm penetration; no wheel-arc overlap; M4: front hits report `ownSlot=Front`, unchanged attribution |
| Spiked Ram Plate | Front | 4 | visual | narrow front proxy | As above, plus M4: the victim's footprint area is smaller than with the push bar; energy is unchanged for an identical scripted hit |
| Roof Cage Rack | Roof | 1 | visual | — (no proxy) | Fits 3 bodies; survives a roof dent with gap ≤160 mm, recorded |
| Tow Hitch Bumper (substitute) | Rear | 2 | visual | rear proxy | As for the push bar, rear |
| Scrap Side Plating (L, R) | Left/Right | 2 each | visual | **deferred** (no protection without an absorption API) | Fits; symmetric; tests the budget |

Validator cases:
- empty loadout;
- exactly at capacity; one over;
- wrong slot; unknown ID; missing prefab;
- unknown save version;
- per-vehicle switch restores the correct build.

## 4. Test matrix

**Unit (EditMode, no physics)**
- Every validator case above.
- Serialization round-trip and version handling.
- Catalog: IDs are unique, every item has a prefab, every fit has 5 sockets.

**Physics harness (editor, deterministic scripted crashes)** — the M2 gate
1. Build each of the 3 vehicles with no loadout, then with a full loadout. Assert identical results for:
   - the `VehicleDamageModel` part count and vertex count;
   - the BoxCollider count, which must be 14;
   - `Body.mass`;
   - the `centerOfMass`.
2. Run the same scripted head-on, T-bone and rear crashes, at fixed speed and a fixed step, with and without attachments. Assert that the `CollisionDamageReceiver.Report` fields match: energy, strength, tier, `other`, `carCollider`, `applied`. Assert identical `MatchModel` points.
3. Lifecycle:
   - Run 50× `ResetCar`, respawn, Showdown prepare, rematch and vehicle switch.
   - Assert the instance count, the renderer count under `Attachments` and the distinct material count are constant.
   - Assert no orphaned objects after `DestroyCar`.
4. Existing suites unchanged and passing: `DerbyValidation`, `ShowdownValidation`, `AiV3Validation`, driving validation.

M4 (after M3):
- A proxy hit reports the slot.
- Attribution goes to the car.
- One event per Rigidbody pair (cooldown).
- The footprint uses the proxy box.
- Proxies are excluded from `ShellBounds`/`FillFace`.
- Showdown placement is unblocked with the largest proxy.

**Visual QA (editor, screenshots to `Previews/Attachments/`)**
- A fit matrix of 3 bodies × all items:
  - from the front, side, top and three-quarter views;
  - after a heavy front crush and a roof dent, which shows the gap;
  - under each paint;
  - under wreck paint.

**Human mobile WebGL** (owner-run; Sonnet prepares a build, but only when the owner authorizes publishing)
- A full-loadout 5-car match.
- Frame time p95 must be within 5% of stock.
- The loadout panel must be readable and tappable at phone width.
- Items must be legible at gameplay distance.

**Reporting.** Every milestone report lists the tests run with PASS/FAIL, and explicitly lists **NOT TESTED** items. Mobile performance is NOT TESTED until the owner runs it.

## 5. Definition of Done and STOP conditions

| Milestone | Done when |
|---|---|
| M0 | Batch runner produces ≥30 AI-only matches per chassis on one difficulty. Report covers the mean and spread of hits, energy and tier distribution, wreck-bonus share, survival and Showdown wins. No gameplay value changed |
| M1 | All unit tests pass. No runtime scene changes |
| M2 | Harness gate (§4.1–4.4) passes. Fit matrix PASS for 3 items × 3 bodies. Screenshots committed. Loadout persists and restores per vehicle. Invalid builds are shown, not dropped |
| M3 | Opus: interface merged with tests. No-proxy behaviour bit-identical in the harness |
| M4 | Proxy tests pass. Perf within guardrail in the editor profiler. Owner mobile test requested |

**STOP and report to Opus or the user if any of the following happens:**
- Any harness comparison differs with Stage 1 attachments on. Possible differences include energy, `carCollider`, part count, box count and mass.
- Mounting needs a collider, a Rigidbody, a mass change or an edit under `Assets/DamageLab/**`.
- An item can only fit a body through nonuniform scaling of its functional shape.
- The validator would need to silently drop or alter a saved loadout.
- Draw calls or GC per reset grow over 50 resets.
- Two different approaches to the same fit or lifecycle bug have failed (AGENTS.md rule 3).
- Any temptation to tune `MatchRules`, tier energies, durability or AI weights.

## 6. Opus handoff requests

- **None are needed for M0–M2.**
- **For M3**, Opus owes the exact interface in architecture §7.3:
  - an `AttachmentProxy` marker;
  - receiver exclusion from `ShellBounds`/`FillFace`/footprint merge;
  - `Report.ownSlot/otherSlot/otherItem`;
  - a discovery exclusion in `VehicleDamageModel.Initialize`;
  - an updated box-count assertion;
  - a spawn and Showdown clearance check for the largest proxy.
- **For the M0 review**, Opus confirms which metrics are trustworthy, and states whether the `ManualStep` batch runner is representative of live play.
- **Future (not V1)**: an additive-mass API, an absorption or adjust field, `panel_follow`.

## 6b. Milestone status

**M1 (data layer): done**, branch `feature/attachments-v1-m1`, `AttachmentValidation` 37/37.

**M2 (visual mount, lifecycle, loadout panel): implemented**, branch `feature/attachments-v1-m2`. Checks, all run in the editor and none on a device:
- `AttachmentParityValidation` (real PhysX, 12 scripted crashes x 4 runs): the kit changes nothing beyond the run-to-run spread of identical runs, and every static check passes (14 boxes, mass, part and vertex count, no colliders or rigidbodies under attachments).
- Lifecycle checks pass: 50 resets, wreck and repair, 5 rematches, paint and vehicle switches, no leaked instances or materials.
- `AttachmentFitTool.RunMatrix`: 12 of 12 item/body pairs fit (report in `m2-fit-report.md`, screenshots in `Previews/Attachments/`).
- **Finding for the owner:** the accepted rigid-mount gap of about 160 mm holds for a firm hit (98-145 mm measured). A maximum synthetic crush (strength 1, depth gain 3) leaves 338-361 mm behind a rigid front or roof item. See the open question in the M2 report.
- `AttachmentUiPlayCheck` (editor play mode, real DerbyUI, clicks the actual buttons): 16 of 16. It covers a saved build with a retired item (flagged, not dropped), FIX LOADOUT, clear problems, cycling and saving per vehicle, switching bodies, START, and no colliders during play.
- Existing suites with M2 in place: `DerbyValidation` 80/80, `ShowdownValidation` 45/45, `AiV3Validation` 19/20. The one failure (A3, a duel-statistics sanity check) fails identically on `main`, so it is not caused by M2.
- Not tested: the loadout panel on a phone or in a WebGL build (touch, layout at phone size), mobile frame time and draw calls, screenshots of the panel (not capturable in batch mode), and any look at SOL's real art (placeholders are grey boxes).

## 7. Scope exclusions and first assignment

**Owner approval recorded** (architecture §10): staged hybrid approved; capacities 8/10/12 are placeholders; AI loadouts are empty in V1; crush gap accepted; Stage 2 interface accepted for after M2. Higher-difficulty AI with different vehicles and attachments is a stated future direction. It is out of scope for V1, but keep `Participant.loadout` working for AI cars, so that a later preset asset needs no refactor.

Not in V1:
- economy and currency;
- unlocks and progression;
- divisions or matchmaking;
- item instances or inventory;
- stacking or more than one item per slot;
- detachment or breakable items;
- deformable attachments;
- `panel_follow`;
- powered, moving or projectile items;
- AI loadout choice;
- networking;
- body rarity;
- rarity-based stats;
- any mass, armor or damage multiplier;
- rebalancing.

**First Sonnet assignment (after approval): M1, with M0 in parallel if Sonnet has capacity.**

> Create branch `feature/attachments-v1-m1` from `main`. Implement the M1 data layer per architecture §5:
> - enums;
> - `AttachmentDefinition`;
> - `VehicleAttachmentFit` (5 sockets, capacity);
> - `AttachmentCatalog`;
> - `Loadout` (version 1, 5 slot IDs);
> - `LoadoutStore` (PlayerPrefs JSON per vehicle);
> - pure `LoadoutValidator`, with error codes `UnknownItem`, `WrongSlot`, `MissingAsset` and `OverBudget`.
>
> Add EditMode unit tests for every validator case in §3 and for the serialization round-trip. Create placeholder data assets: a catalog, 3 fits with capacity 8/10/12 marked "placeholder", and 4 grey-box items.
>
> Do not touch `DerbyGame`, `MatchModel`, any DamageLab script, scenes or vehicle assets. Commit in small steps. Report the files, the test results and anything NOT TESTED. Do not merge.

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

**Owner decisions after M2:** M2 approved to continue; M0 approved; the extreme crush-gap fix is **deferred** (firm hits stay within the accepted 160 mm; the 340-360 mm maximum case is recorded, not fixed); **WebGL/mobile verification is required before M2 is approved for release.** Nothing is published until then.

**M0 (baseline telemetry): done**, branch `feature/attachments-v1-m0` (`BaselineTelemetry`, outputs in `Docs/Attachments/baseline/`). 30 AI-only matches per mode, Standard AI, stock cars, no rules touched. Observations worth the owner's attention (not fixed, not part of this task):
- Points Derby: the pickup won 0 of 50 car-matches (muscle car 34 %, hatchback 26 %, chance 20 %) and deals the least energy per car (1235 kJ vs 1464 hatchback and 1553 muscle car) while taking about as much as the muscle car (1470 vs 1510 kJ).
- Last Car Standing: the muscle car reaches the Final Showdown in 60 % of its car-matches (hatchback 20 %, pickup 32 %) and wins 63 % of its Showdowns.
- 59-94 % of wrecks are finishing blows by a car that did less than half of the victim's damage (hatchback 94 % in Last Car Standing), so the 150-point wreck bonus does go mostly to finishers.
- The seat-0 bot is a slightly different driver from the AI cars; the per-personality tables in the reports show the size of that effect.

**WebGL check of M2 (local build, not published; what could and could not be verified):**
- A local WebGL build of `976d5d5` succeeded (22.1 MB, 0 errors). It loads in a desktop browser, shows the LOADOUT button, opens the panel, cycles items, mounts them live on the shown car, saves them (the build came back with 3/10 after a reload), starts a 5-car match with the kit mounted and reports normal telemetry. The only console error was a 404 for `version.json`, which only exists in the published layout.
- At a phone-landscape viewport (812x375) the menu and panel fit and are readable. **Finding:** the LOADOUT button on the main menu is only about 17 px tall there (the other menu buttons are about 30-40 px), too small for a thumb. Fix options (not done): give it its own row by moving the car description, or open the loadout from the car buttons. Needs a menu relayout, so it was left for the owner's decision.
- **Not verified:** frame time, draw calls and memory on a real phone. The test browser renders in software (fps 34 in the menu, falling to about 8 during a match and running the game clock at about a third of real time), so its numbers say nothing about a phone and were not used for a with/without comparison. Touch input on a device was not tried. The release gate stays open until the owner has run a phone test.

## M3 status: collision-engine interface implemented, Opus-reviewed

Branch `feature/attachments-v1-m3` (not merged, not published). Written by a Sonnet session at the owner's request ("move on to the next part"); it changes `Assets/DamageLab/**`, so it needed an Opus review of the diff before it is merged. No item uses it yet; no effect, mass or scoring change was added.

**Opus review (done; fixes committed on the same branch, `AttachmentProxyValidation` 42/42 after them):**
- *Fixed:* when a proxy that only touched in a later gathered step took over the footprint, the event also took that later step's pose of the other body. The pose now stays the first box contact's, so center, rotation and pose are always from one consistent placement.
- *Fixed:* `Report.ownSlot`/`otherSlot` defaulted to 0 (= Front) when no proxy was involved. They are now -1 unless the matching `ownProxy`/`otherProxy` flag is set.
- *Fixed:* when the deepest contact was on a proxy, `carCollider` was looked up from the uncorrected contact point; it now uses the corrected point like the shell branch.
- *Checked, no change:* `VehicleHitbox` takes its boxes by explicit reference from `VehicleRig`, so it never adopts a proxy.
- *Constraints carried into M4:* create proxy colliders after `StripPhysics` (it would delete them); add the `BoxCollider` before the `AttachmentProxy` component (its `OnEnable` registers the colliders it finds); keep the car's inertia tensor (it is automatic, so extra colliders would change handling); copy the shell's physics material and layer; keep proxies off the ground plane; take a proxy out of the physics scene at once when a slot is cleared (a deferred `Destroy` would leave it for one step).
- **Engine changes (all opt-in; inert when no proxy exists):** `AttachmentProxy` (slot, item id; registers its colliders in a static lookup; `ExecuteAlways` so edit-mode validations register it); `AttachmentRoot` (marker: `VehicleDamageModel.Initialize` skips every mesh below it); in `CollisionDamageReceiver`: proxies are excluded from `ShellBounds` and `FillFace`, a proxy leads the dent footprint by itself (replaces a shell box reported first in the same event; nothing is merged into it), `carCollider` names the nearest shell box when the deepest contact was on a proxy, and `Report` gains `ownProxy`/`ownSlot`/`otherProxy`/`otherSlot`/`otherItem`. `AttachmentMount` puts the `AttachmentRoot` on its node, so mounting order no longer matters. `DerbyValidation` counts shell boxes only (14).
- **Checks:** new `AttachmentProxyValidation` 42/42 (real PhysX: marker registry; per vehicle, rear-end ram with a front proxy, a rear proxy and both; side hit onto a left proxy; shell bounds and box count). `AttachmentParityValidation` 26/26 (its hazard check now proves a Rebuild with items adds no parts, with a stray-mesh control that does add one). `AttachmentValidation` 44/44, `DerbyValidation` 80/80, `ShowdownValidation` 45/45, `AiV3Validation` 19/20 (A3 fails identically on `main`), and the collision-lab suites (`CollisionLabValidation`, `CollisionGeometryValidation`, whole-vehicle) all pass with the engine change in (run through a temporary entry point that was not committed).
- **No hidden multiplier:** the same collider run as a marked proxy and as plain shell gives the same hit energy within 0.1% in every scenario (rear-end for all three cars, with a proxy on the attacker, the victim and both, and a side hit). Attribution to the car root, one applied report per body pair, and scoring are unchanged.
- **Finding for M4 (physics, not the marker):** a proxy's *shape and height* changes how a hit lands, and so the hit energy, because energy follows the closing speed along the contact normal. Against the same scripted hit, a 0.5 m wide front proxy gave -47% (muscle car, pickup: the proxy caught the edge of the other car's rear boxes, oblique contact) and +2% (hatchback, flat contact); a left-side proxy gave +26% against the no-proxy side hit. A narrow proxy also makes a much smaller dent footprint (0.01-0.11 m2 against 0.7-1.0 m2) which the engine already turns into a deeper, more concentrated dent. So proxy sizes and heights in M4 must be measured against the M0 baseline and tuned deliberately; they are not neutral.
- **Spawn and Showdown clearance (arithmetic, not run):** the spawn box in `DerbyGame.Blocked` is 6.4 m long and 3.2 m wide against cars 4.0-5.0 m long and 1.8-2.1 m wide, so each end has at least about 0.7 m of margin and each side about 0.55 m; the largest proposed proxy (the 0.4 m ram plate) fits. `BlockedIgnoring` (7.2 m x 4.0 m) is larger still.
- **Not tested:** a WebGL build with M3 (the code compiles in the editor and the changes are plain C#), mobile cost of the extra colliders, and any real item using a proxy (M4).

## M4 status: proxy items measured against M0 (Opus)

Branch `feature/attachments-v1-m4` (not merged, not published). Proxies: Junk Push Bar front 1.6 x 0.35 x 0.25 m; Spiked Ram Plate front, only the central 0.4 x 0.35 x 0.4 m block collides (the plate wings are visual); Tow Hitch Bumper rear 1.5 x 0.3 x 0.3 m; Roof Cage Rack none. Mounting locks the car's inertia tensor while proxies exist, so handling does not change. No mass, `MatchRules`, tier, durability or AI value was touched.

- **`AttachmentM4Validation` 60/60** (real PhysX, scripted short run-up rams on all three bodies): proxy hits report the slot; attribution goes to the car; one applied report per body pair; the footprint follows the proxy (ram plate 0.04-0.14 m² against push bar 0.33-0.56 m²); centred-hit energy matches bare within 0.3% (noise over 3 runs 0.0%).
- **Harness finding:** the long run-up drifted the attacker off line and the cooldown let it hit twice; the harness now uses a short run-up and cuts throttle after the first report.
- **Seam effect (known issue, not fixed, needs authorization):** a narrow proxy that crosses the victim's centre/side shell-box seam tilts the averaged contact normal. At 0.3 m off centre the hatchback takes 32 kJ from the ram plate vs 65 kJ from the push bar (-50%); muscle car and pickup are unaffected (0.0%, +0.2%). Suggested engine fix: in `CollisionDamageReceiver.Accumulate`, downweight contact normals nearly perpendicular to the relative velocity, or use the dominant face normal for proxy-led events.
- **Baseline (BaselineTelemetry, `BASELINE_LOADOUT=full`: ram plate + tow hitch + roof rack on all five cars, outputs in `baseline-m4/`):** a stock rerun on the current code (`baseline-m4/stock/`) reproduces M0 row for row in both modes, so the no-loadout path is unchanged and the sim is deterministic. Points Derby with the kit: every chassis within 1.3 SE of stock (hatchback energy dealt -4.4%, pickup wins 0 -> 4 of 50). Last Car Standing with the kit: the muscle car drops (score 1219 -> 1063, energy dealt -13%, Showdowns reached 30 -> 21 of 50; about 1.8-1.9 SE), the pickup reaches more Showdowns (16 -> 21). Not tuned; a balance decision for the owner.
- **Editor perf (CPU/physics only):** wall time for 30 Points matches 615 s full kit vs 635 / 664 s stock; Last Car Standing 403 s vs 397 / 409 s. Within run-to-run spread.
- **Regression suites on this branch:** AttachmentProxyValidation 42/0, AttachmentValidation 44/0, AttachmentM4Validation 60/0, DerbyValidation 80/0, ShowdownValidation 45/0, AiV3Validation 19/1 (only the known A3 duel failure, unchanged from main), AttachmentUiPlayCheck exit 0, AttachmentFitTool.RunMatrix 12/12.
- **M2 parity gate:** with the proxies live it fails 7 checks, by design. The static "box colliders unchanged" and "attachments carry no colliders" checks fail on all three cars because proxies are colliders. The 12-crash parity fails in 8 scenarios, mostly 0.3–6 %. The gate is the Stage 1 contract, so it now strips the items' proxies in memory for its run (restored in `finally`, never saved). On that Stage 1 path it passes 26/0, worst kit-vs-none difference 6.8e-3 against a 5.4e-3 run-to-run spread, so the STOP rule ("any harness comparison differs with Stage 1 attachments on") is not triggered.
- **Finding for the owner (not tuned):** with proxies live, the hatchback rear-end parity scenarios differ most. A ram-plate car hitting a tow-hitch car reported 95 % different total energy, and the AI-attacks variant moved the attacker score by 57 %. This is a proxy-on-proxy contact (narrow ram box into the hitch box) and likely the same seam/normal effect as above; the suggested `CollisionDamageReceiver.Accumulate` fix would address both and needs authorization.
- **NOT TESTED:** owner mobile test (full-loadout 5-car match, p95 frame time within 5% of stock), WebGL build, draw calls. **Owner mobile test requested.**

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

# Attachment System V1 — Architecture & Physics Compatibility Review

Owner: Opus (collision/deformation). Status: **proposal, awaiting product-owner approval.**
Inspected: branch `main` @ `3057f05` (via `docs/attachments-v1-architecture`, which equals `main`). Unity 6000.6.0f1, URP, WebGL.
Companion: [`sonnet-implementation-plan-v1.md`](sonnet-implementation-plan-v1.md).

## Decision summary

- **V1 is a staged hybrid.** **Stage 1** is visual-only attachments. They have no colliders, no Rigidbody and no mass change. They are mounted on per-vehicle sockets and kept out of the damage model. A capacity budget, a loadout and a pre-match UI go with them. Sonnet can build all of Stage 1 now with existing APIs. Collision and scoring stay identical, and a regression test enforces that.
- **Stage 2 is attachment contact proxies.** These are BoxColliders on the car's *own* Rigidbody, so attribution is preserved. Each proxy has a stable slot identity in the impact report. They need a **small, opt-in Opus interface** first. Without it, existing code would silently fold the proxies into shell bounds, the face fill, impactor footprints and collider names (§7).
- **No damage boosts, armor or mass in V1.** The only auditable effect channel is geometry: the existing energy and footprint path, applied to a real contact shape. Mass stays off until a baseline audit exists, because it feeds energy, durability and AI in five places.
- **Deferred:** deformable attachments, `panel_follow` mounts, detachment, separate Rigidbodies, powered items and AI loadout choice.
- **Mounting:** each vehicle gets a per-vehicle fit asset holding five socket frames and a capacity value. Each item has a single prefab plus optional per-vehicle *bracket* variants. Nonuniform scaling of functional shapes is never allowed.

## 1. Executive decision and proposed V1 scope

V1 ships three junkyard-style items on all three bodies across the five universal slots. Each slot holds one item. The pre-match loadout is validated against a per-body budget (illustrative 8/10/12) and saved per vehicle. Items mount and reset correctly through every lifecycle path: start, respawn, hulk, Showdown repair, rematch, menu, vehicle switch and paint switch.

The gameplay effect of V1 is **visual identity and build choice only**. The first physical effect comes in Stage 2: a front or rear proxy that changes *where* contact happens. It does not change how energy is computed. Stage 2 starts only after the Opus interface lands and the M0 baseline exists.

A visual-only first iteration is the safe choice under this collision engine. It is not a placeholder for something that cannot work, because every Stage 2 seam is identified below.

## 2. Current-repo findings (cited)

**Rig and hierarchy** — `Assets/DamageLab/Scripts/Collision/VehicleRig.cs`
- `Rig Build(VehicleDefinition def, Transform parent, Vector3 basePosition, VehicleRigSettings s)` (92) instantiates `def.model` as `car`, and `rig.root = car` (104).
- The same GameObject carries the **Rigidbody** (152), `VehicleDamageModel` (162), `CollisionDamageReceiver` (172) and `ImpactFootprintDebug` (177).
- The physics shell is 14 BoxColliders under the child `PhysicsShape` (109–149). That is 12 regions plus the core and the cabin.
- Rigidbody settings:
  - `rb.mass = def.mass` (153);
  - `Interpolate` and `ContinuousDynamic` (156–157);
  - a manual `centerOfMass` (158–159).
- `model.createPickingColliders = false` (167), so the derby has no concave pick colliders. This was verified.
- `Measure` (55–60) runs once in `Build` and computes bounds from MeshRenderers. Attachments added later do not affect geometry.

**Damage model** — `Assets/DamageLab/Scripts/Vehicle/VehicleDamageModel.cs`
- `Initialize()` (461) returns early if already initialized. It then discovers **every `MeshFilter` under the car root** (`GetComponentsInChildren<MeshFilter>(true)`, 482):
  - an `Ignore` rule skips the mesh (486);
  - a `Rigid` rule makes it a rigid follower (490–493);
  - a non-readable mesh produces a warning (495);
  - anything else becomes a `DeformablePart`.
  - Body and Bumper roles define the impact-face bounds (504–509).
- `Rules` (616) is `definition.components` if that is non-empty, else `partRules`. `FindRule` (618) warns on unclassified names.
- `Rebuild()` (599) is `Shutdown()` (570) followed by `Initialize()`. `ResetAll()` (158). `SurfacePointAlong(...)` (228).
- **Consequence:** an attachment mesh that is under the car when `Initialize`/`Rebuild` runs becomes a deforming or rigid part. The derby calls `Initialize` once, in `BuildCar` (`DerbyGame.cs:192`), and never calls `Rebuild`. Grep finds no derby call site. Lab and editor tools do call `Initialize`/`Rebuild` on lab rigs.

**Hitbox** — `Assets/DamageLab/Scripts/Collision/VehicleHitbox.cs`
- It holds explicit `core`, `cabin` and `regions[]` references (42–45). Faces only retract, and the change is applied at `ApplyPending` (185).
- Boxes it does not know about are never refitted. It does not move attachments either, so a crushed face leaves a visible gap behind a rigid item. The prototype below measured 160 mm.

**Receiver** — `Assets/DamageLab/Scripts/Collision/CollisionDamageReceiver.cs`
- `Accumulate` (167):
  - It ignores contacts whose other collider has no Rigidbody, and contacts with itself (170). That covers ground and static geometry. Which arena obstacles carry a Rigidbody, and so can wear a car via `environmentDamageFactor`, was not audited.
  - Cooldown is tracked per other *Rigidbody* (171).
  - The normal is oriented into the car's centre of mass (189).
  - `carCollider` is the name of `thisCollider` at the deepest contact (198).
  - The impactor footprint is built only when `c.collider is BoxCollider` (203–228). Aligned boxes are merged.
- `FillFace` (249–278) adds *all* of the other body's aligned BoxColliders whose `attachedRigidbody == other`.
- `ShellBounds` (290–307) encapsulates *all* own BoxColliders on `body`.
- `Process` (355):
  - μ is computed from both `Rigidbody.mass` values (375–380);
  - `energy = 0.5·μ·vn²` (381), multiplied by `MomentumShare` (338–346, mass-based) (382);
  - the dent is projected onto bodywork with `SurfacePointAlong` (388);
  - `Report.other = other.name`, the *Rigidbody root name* (402);
  - `Report.carCollider` (416), then `model.AddImpact` (425).
- Events: `ContactsReceived` (105) and `ImpactRegistered` (110).

**Derby lifecycle** — `Assets/Derby/Scripts/DerbyGame.cs`
- `BuildCar` (181–233) does the following in order:
  - `Build` (184);
  - `Initialize` (192);
  - `hitbox.Setup` (193);
  - controller (199–202);
  - `root.name = "Car{id} {name}"` (203);
  - receiver config (204–212);
  - subscribe `ImpactRegistered` (214);
  - AI driver (215–229);
  - `ApplyPaint` (230);
  - `Physics.SyncTransforms` (231).
- `RegisterCar` (235) fills `byName[root.name]` and `byBody`. Attribution is `byName.TryGetValue(r.other, …)` in `OnImpact` (691).
- `DestroyCar` (241–251) unsubscribes, calls `Model.Shutdown()`, deactivates the car and destroys it.
- `SelectVehicle` (275) destroys and rebuilds the human car. `SelectPaint` (286).
- `StartMatch` (365–410) reuses existing cars and rebuilds the AI roster only when the count or vehicle changes (375). It **always calls `ResetCar`** (392–393). `Rematch()` is `StartMatch()` (412). `GoToMenu` (415).
- `ResetCar` (617–636) is the **single reset path** used by start, respawn (`OnRespawnDue` 579) and Showdown (`OnShowdownPrepare` 510). In order:
  - `ResetAll`;
  - `SetDamageImmediate(0,0,0,0)`;
  - `hitbox.ApplyPending`;
  - zero velocities and set the pose;
  - `SyncTransforms`;
  - `Receiver.ResetState`;
  - controller and AI reset;
  - `ApplyPaint` (635).
- Spawn and runway checks are `Blocked` (607, OverlapBox 1.6×0.9×3.2), `BlockedIgnoring` (529, 2.0×0.9×3.6) and `RunwayBlocked` (554, SphereCast r 1.1 m, 22 m).
- `ReportToPage` (835) emits WebGL JSON telemetry.

**Match/scoring** — `Assets/Derby/Scripts/MatchModel.cs`
- `Participant` (41) has `vehicleIndex` and `paintIndex` (48). This is the natural home for a loadout reference.
- `MatchRules` (84):
  - `tierEnergy {3000,15000,40000,90000}` (110);
  - points `{10,30,70,120}`;
  - `wreckBonus 150` (113);
  - `environmentDamageFactor 0.4` (107).
- `DurabilityFor(massKg)` (120) is linear in mass. `ResetForMatch` passes **`def.mass`** (212), not `rb.mass`.
- `ApplyDamage`: environment wear (321), Showdown energy (324–331), pair cooldown and the ×1.5 rule (344), wreck-credit window (357–364).

**Other mass readers (verified)**
- `ArcadeCarController` uses `ForceMode.Acceleration` (176/192/203), so drive response does not depend on mass. Collisions do.
- AI: `AiTargeting.cs:275` and `EnemyDriver.cs:726` ShareEdge, and the obstacle test at `EnemyDriver.cs:340` (`rb.mass >= obstacleMinMass`).
- Tests: `AiV3Validation.cs:687` fingerprints `Body.mass`.

**Paint** — `Assets/Derby/Scripts/VehiclePaint.cs`
- `VehiclePainter.Apply(car, …)` (53) walks every MeshRenderer under the car. It swaps only materials whose name ends in **`Paint`** and that have `_PrimaryColor`/`_BaseColor`, using a shared cache with `ClearCache`.
- Attachment materials with any other name are left alone, including during wreck paint.

**Tests that encode today's shape**
- `Assets/Derby/Editor/DerbyValidation.cs:374–375` asserts that `GetComponentsInChildren<BoxCollider>().Length == 14` and that `Body.mass > 500`.
- Mesh and material scans under the root: `DerbyValidation.cs:337/418–428/443/693` and `ShowdownValidation.cs:349–372`. `ShowdownValidation` snapshots every readable mesh and BoxCollider under the root.

**UI** — `Assets/Derby/Scripts/DerbyUI.cs`
- The menu is laid out in a 640-unit-tall, 650-wide panel (`MenuHeight`) that is fully used: title, cars, paint, mode, difficulty, START at −540, keys at −616.
- A loadout UI therefore needs its own panel or step. It cannot be squeezed into this menu.

**Vehicles**
- `Assets/DamageLab/Settings/Vehicles/{MuscleCar,CompactHatchback,CompactPickup}.asset`.
- `DerbyGame.vehicles` order: muscle car, hatchback, pickup.

**Prior art**
- The unmerged branch `origin/feature/attachment-prototype` @ `9351847` (`Prototypes/Attachments/`) models a pushbar, a rack and a scoop. Its results:
  - the rack passes undamaged (0.26 mm);
  - the scoop fails (121.7 mm, severe);
  - the rack leaves a 160 mm gap after a roof dent;
  - Unity and Blender vertex counts differ.
- It recommends excluding attachments from discovery, bounds and the hitbox. That matches this review.

**Discrepancies from the brief:** none material. "DerbyMatch" is `MatchModel`/`DerbyGame` in the current code. The top scoring tier does exist (90 kJ caps at 120 points).

## 3. Requirements, invariants, deferred

**Invariants that V1 must hold, and tests must assert**
1. With no attachments equipped, behaviour is identical: same parts, 14 boxes, same `rb.mass`, same receiver reports.
2. With Stage 1 attachments equipped, behaviour is *also* identical in physics and scoring.
3. All five slots exist on every body. Each slot holds zero or one item, and every item fits every body.
4. The sum of equipped costs is at most the body's capacity. Capacity is not derived from mass, size or rarity.
5. One Rigidbody per car. Attribution goes only through `Report.other` = the car root name.
6. Rarity is metadata only. It never scales energy, points or durability.
7. Resets do not grow allocations: no duplicate instances, components or materials after N resets.

**Deferred:** economy, unlocks, divisions, item instances, stacking, detachment, deformable attachments, powered or moving items, AI loadout choice, networking and body rarity.

## 4. Alternatives and decision table

| Candidate | Feasibility now | Complexity | Visual quality | Scoring fidelity | Reset | WebGL cost | Future detach |
|---|---|---|---|---|---|---|---|
| **1. Visual-only + capacity + loadout** | Ready (existing APIs) | Low | Good (rigid, no gaps beyond the crush gap) | Exact; unchanged | Trivial (nothing to reset) | ≤5 renderers per car | Easy (already separate objects) |
| **2. Rigid same-body proxy colliders with slot identity** | Needs a small Opus interface (§7.3) | Medium | Same as 1 | Auditable if proxies are excluded from shell, face and footprint logic | Simple (static boxes) | +1–5 boxes per car on the same body | Medium (re-parent to a new body later) |
| 3. Deformable modular attachments | Needs research | High | Best | Unknown; dents project onto the body behind the item (388) | Hard | Mesh deformation per item | Hard |

**Decision:** candidate 1 now and candidate 2 as V1 Stage 2. Candidate 3 is deferred.

Per-topic decisions:
- **A** (data): ScriptableObjects plus a per-vehicle loadout saved to PlayerPrefs. Owner: Sonnet. No engine change.
- **B** (mount): per-vehicle fit asset with sockets and bracket variants. Owner: Sonnet, with SOL for art. No engine change.
- **C** (collision): the staged hybrid. Owner: Opus. Stage 2 needs the §7.3 interface.
- **D** (mass): none in V1. Owner: Opus. An additive mass API only after the audit.
- **E** (items): §7.6. Owner: Sonnet. No engine change for Stage 1.
- **F** (lifecycle): a hook in `ResetCar`. Owner: Sonnet. No engine change.
- **G** (performance): shared meshes and materials, no colliders in Stage 1. Owner: Sonnet, with an Opus review in Stage 2.

The "considered" alternatives for each topic are listed with it below.

## 5. Data contracts (A) — pseudocode, not implementation

```text
enum AttachmentSlot { Front, Rear, Left, Right, Roof }           // fixed order; index = (int)slot
enum AttachmentRarity { Common, Uncommon, Rare, Epic, Legendary } // metadata only

ScriptableObject AttachmentDefinition
    string id                // stable, lowercase, never renamed (e.g. "push_bar_junk"); save files reference this
    string displayName
    AttachmentSlot slot      // exactly one slot
    AttachmentRarity rarity  // display only
    int capacityCost         // ≥ 0, playtest-tuned, independent of rarity/mass
    GameObject prefab        // default visual (single asset)
    float designMassKg = 0   // INFORMATIONAL in V1 — never written to Rigidbody (see §7.4)
    ProxySpec[] proxies      // Stage 2 only; empty in Stage 1
    // no compatibility list: every item fits every body (requirement)

struct ProxySpec { Vector3 center; Vector3 size; }   // item-local box, see §6 / §7.3

ScriptableObject VehicleAttachmentFit                // one per VehicleDefinition; does NOT modify the Opus-owned definition asset
    VehicleDefinition vehicle
    int capacity             // illustrative 8 / 10 / 12, user-approved before shipping
    Socket[5] sockets        // indexed by AttachmentSlot
    BracketOverride[] overrides

struct Socket { Vector3 localPosition; Quaternion localRotation; Vector3 envelopeSize; }  // car-root space; envelope = max item box
struct BracketOverride { string itemId; GameObject bracketPrefab; Vector3 offset; float uniformScale /* 0.9..1.1 clamp */ }

ScriptableObject AttachmentCatalog { AttachmentDefinition[] items; VehicleAttachmentFit[] fits; }  // single lookup root

[Serializable] class Loadout                          // persistent SELECTED configuration (no runtime state)
    int version = 1
    string vehicleKey        // VehicleDefinition asset name
    string[5] slotItemIds    // "" = empty

class LoadoutValidator                               // pure, unit-testable
    Result Validate(Loadout l, VehicleAttachmentFit fit, AttachmentCatalog c)
        for each slot s: id empty → ok
            unknown id → Error(UnknownItem, s)        // kept in the loadout and flagged, NOT silently dropped
            def.slot != s → Error(WrongSlot, s)
            def.prefab == null → Error(MissingAsset, s)
        sum(cost) > fit.capacity → Error(OverBudget, total, capacity)
    // UI: invalid slots shown red with reason; START disabled while errors exist; "Clear invalid" button.
    // At match start (defensive): an invalid loadout is applied as EMPTY for that slot and logged once — never crashes.

Persistence: PlayerPrefs key "carwreck.loadout.v1.<vehicleKey>" = JsonUtility(Loadout).
    One loadout per vehicle (switching body restores that body's build). Unknown version → treat as empty + notice.

Participant (MatchModel.cs:41) gains: Loadout loadout   // like vehicleIndex/paintIndex; AI = empty in V1 (§7.7)

MonoBehaviour AttachmentMount (added to car root by DerbyGame.BuildCar, after Initialize)   // RUNTIME condition
    Apply(Loadout, VehicleAttachmentFit, AttachmentCatalog)  // diff per slot; instantiate only on change; reuse otherwise
    ResetVisuals()                                           // called from ResetCar: re-pose, unwreck tint
    SetWrecked(bool)                                         // called alongside ApplyWreckPaint
    Clear()
    ItemIn(AttachmentSlot) → AttachmentDefinition            // for UI/telemetry/tests
```

Alternatives considered:
- Fields on `VehicleDefinition`. This was rejected because it edits Opus-owned vehicle assets and couples the art fit to the physics definition.
- Socket transforms baked into model prefabs. This was rejected because it would require re-exporting the cars.
- A JSON catalog. This was rejected because ScriptableObjects are the project's convention.

**Failure scenario:** a renamed `id` orphans saved loadouts. The validator flags it as `UnknownItem`, and the UI shows a red slot with "Clear invalid".

## 6. Mounting and export contract (B)

**Socket convention.** Sockets live in car-root space, and the Unity axes are:
- +Z = vehicle forward;
- +Y = up;
- +X = vehicle right.

Socket +Z points *outward* from the slot:
- Front faces +Z;
- Rear faces −Z;
- Left faces −X;
- Right faces +X;
- Roof faces +Y, with the socket's up axis being vehicle forward.

Each item is authored once, facing its own +Z outward, so the same prefab mounts on any socket of its slot.

**Seeding sockets.** The editor tool proposes sockets from measured geometry, then a human adjusts them:
- Front and Rear: the centre of the nose and tail faces, at bumper height.
- Sides: mid-door.
- Roof: cabin top.

The measured geometry is the same data `VehicleRig.Measure` produces: `rig.geometry.body`/`cabin`. Sockets are saved into `VehicleAttachmentFit`, so there is no measurement at runtime.

**Fit across bodies.** The three bodies have different bumper widths, hood heights and roof widths.
- The item's *functional* shape is never scaled nonuniformly.
- Each item may ship a **bracket variant** per vehicle: the mounting plate, tubes and offsets that bridge to that body's surface. The functional part is identical, so a variant is still "the same gameplay item".
- `uniformScale` is limited to 0.9–1.1, for cosmetic width only.
- Gaps are hidden by brackets, not by scaling.

**Clearance check (editor).**
- At the rest pose, the item's bounds must sit within the socket envelope.
- Penetration into the body must be at most 5 mm, tested against the BoxCollider shell and the render mesh bounds.
- The item must not overlap a neighbouring slot's envelope.
- It must not cross the wheel arcs (`rig.geometry.wheels`).

**Following deformation.** Every attachment is **rigid to the car root** in V1. A crushed face leaves a gap behind a rigid item; the prototype measured up to 160 mm. That gap is accepted for V1 as "the item stays bolted to the frame".
- `panel_follow` (an item riding a deforming panel) is deferred. It would need a per-frame read of a deforming part's displaced vertices, which is an Opus API.

**SOL export contract (per item)**
- GLB or FBX in metres, 1 unit = 1 m, with Y-up after import and +Z outward.
- The pivot is the **mount plane centre**: the surface contact point on the socket.
- Materials:
  - at most 2 materials, all URP Lit or the project's car shader;
  - names may **not** end in `Paint`, unless the item is meant to take the body colour, in which case use `AttachPaint`;
  - share the `Attach_Metal`/`Attach_Accent` materials across items.
- Triangle budget: 1.5k for Common and Uncommon items, 3k for others. 1 LOD in V1.
- No colliders in the file. Proxy boxes are authored as `ProxySpec` data, reviewed by Opus in Stage 2.
- Object names are prefixed `Attach_`, so a future Ignore rule can match them (§7.3).
- Bracket variants are named `<itemId>__bracket_<vehicleKey>`.
- Meshes are imported **non-readable**, since they are not deformed. This also keeps them out of `ShowdownValidation`'s readable-mesh snapshot.
- Deliver bounds and a front/side/top preview per item.

**Editor tooling (Sonnet)**
- An `Attachments/Fit Preview` window:
  - builds each vehicle through `VehicleRig.Build`;
  - draws socket gizmos and envelopes;
  - equips any item in any slot;
  - runs the clearance check for all 3 bodies × 5 slots × N items;
  - writes a PASS/FAIL table.
- Screenshots go to `Previews/Attachments/`.

**Failure scenario:** a roof rack fits the hatchback but clips the pickup's taller cab. Fix it with a bracket variant and re-run the matrix. Never fix it by scaling the rack.

## 7. Collision, deformation, mass, attribution and reset (C, D, F)

### 7.1 Stage 1: visual-only (ready now)

- The `Attachments` GameObject is a child of the car root, so it inherits the interpolated Rigidbody pose with no follower script. Its children carry MeshFilters and MeshRenderers only: **no Collider, no Rigidbody, no Joint**.
- **Discovery hazard.** Items are mounted **after** `rig.model.Initialize()` (`DerbyGame.cs:192`). Initialize returns early once initialized, and the derby never calls `Rebuild`, so items are never discovered as parts.
  - This ordering is **load-bearing**. Document it at the call site and assert it in a test: part count and vertex count are unchanged with attachments.
  - The robust fix is part of the Stage 2 interface (§7.3).
- Unity callbacks are unaffected: there are no colliders, so contacts, `carCollider`, `ShellBounds`, `FillFace`, the footprint and the hitbox are identical.
- `Physics.OverlapBox` spawn checks ignore renderers. A front item can visually poke past the 1.6×0.9×3.2 m spawn box. That is acceptable, because it is visual only.
- Attribution, closing speed, normals, μ, momentum share and points are all unchanged, so player/AI parity holds trivially.

### 7.2 Why not add colliders through existing APIs

A same-body BoxCollider added today would cause all of the following without any engine change, which makes it **unsafe**:
- It would be counted in this car's `ShellBounds` (290). That widens the face-fill test for every opponent.
- It would be folded into an *opponent's* `FillFace` (259) and footprint merge (221) as if it were shell. A ram would then dent the victim with a footprint the size of the shell.
- It would become `carCollider` when deepest (198). Downstream telemetry and debugging treat that name as a shell region.
- It would never be refitted by `VehicleHitbox` (explicit refs, 42–45). It would stay rigid while the body crushes behind it. That part is fine physically, but dents still project onto the body by `SurfacePointAlong` (388), so an item hit dents the panel behind it.
- It would break `DerbyValidation.cs:374` (`boxes == 14`) and change `ShowdownValidation`'s box snapshot.

A **separate Rigidbody** per item (jointed) is worse:
- `Report.other` would be the item's name, so `byName` misses and the hit is credited as environment (`OnImpact` 691).
- Self-collisions would need ignore pairs.
- Cooldowns would be keyed per Rigidbody.
- It adds solver cost on mobile.

It is rejected.

### 7.3 Stage 2: smallest opt-in Opus interface (Opus implements, Sonnet consumes)

```text
// New, additive. Default behaviour unchanged when no AttachmentProxy exists.
[DisallowMultipleComponent] class AttachmentProxy : MonoBehaviour   // on each proxy BoxCollider's GameObject
    int slot                 // (int)AttachmentSlot
    string itemId

CollisionDamageReceiver changes (Opus):
    ShellBounds / FillFace / footprint merge: skip BoxColliders with AttachmentProxy
        → a proxy is an impactor shape of its own: footprint = proxy box only (not merged with shell boxes)
    Report gains: int ownSlot = -1, int otherSlot = -1, string otherItem
        ownSlot from thisCollider's AttachmentProxy at the deepest contact; otherSlot/otherItem from c.collider
    carCollider keeps reporting the nearest SHELL region name (not the proxy) so existing consumers are unchanged

VehicleDamageModel change (Opus):
    Initialize(): skip MeshFilters under any Transform carrying AttachmentRoot marker   // removes the ordering hazard
    (alternative: built-in Ignore for name token "Attach_" independent of definition.components)

Validation updates (Opus-approved): DerbyValidation box count = 14 + proxies; ShowdownValidation filters proxies.
```

**Attribution.** Proxies are on the same Rigidbody, so `Report.other` is still the car root, and wreck and score credit is unchanged. Cooldown is per Rigidbody, so a ram and its bumper hitting together produce **one** event. There is no duplication.

**Effects without multipliers.** A proxy changes *where and with what shape* the car contacts:
- The receiver's existing energy (381–382) runs unchanged.
- The victim's footprint becomes the proxy's narrow box. This is a real, auditable concentration.
- The item takes contact instead of the shell region behind it. The dent still goes on the host's body via `SurfacePointAlong`, which is acceptable for a rigid bolt-on item.

Any "protect" or "attack" factor beyond geometry stays **deferred**. Candidates would be armor absorption or a spike strength bias. Such a factor would need an Opus-owned, logged, per-event field (`Report.attachmentAdjust`) that is applied before `model.AddImpact` and audited in tests. It is not proposed for V1.

**Crush interactions**
- The host crushing behind a rigid item: the gap is accepted (§6).
- A rigid item pushing into the host when the item is struck: the item is on the same body, so it cannot penetrate its own car. The impulse goes to the host Rigidbody.
- Item against item: both are proxies, so both reports carry slot IDs, and the footprint uses the proxy boxes.
- Walls and obstacles: unchanged. Static contacts are ignored at line 170. Obstacles with a Rigidbody take the environment factor.
- Hulks and eliminated cars: proxies stay enabled with the hulk, the same as its shell. That keeps the hulk the same physical object.

**Failure scenario:** a front ram proxy extends 0.4 m. At Final Showdown, `BlockedIgnoring` (529) or the spawn box could see the proxy as blocking. Opus must check the spawn half-extents (DerbyGame 529/607) against the largest approved proxy before Stage 2 ships.

### 7.4 Mass, capacity, energy and score (D)

These are five separate quantities:
- **Capacity** is a build limit. It lives only in data and the UI.
- **Item mass** is `designMassKg`. It is informational in V1.
- **Car mass and inertia** is `rb.mass` and the manual centre of mass. It stays untouched in V1.
- **Damage energy** is the receiver's `0.5·μ·vn²·share`.
- **Score** is the `MatchModel` tiers, cooldown and wreck window.

Adding mass touches five systems at once:
- μ and momentum share (375–382);
- AI ShareEdge and obstacle choice (`AiTargeting:275`, `EnemyDriver:726/340`);
- durability, which reads `def.mass` (MatchModel 212), so it would go **out of sync** with `rb.mass`;
- collision knockback;
- the centre of mass.

Engine drive uses `ForceMode.Acceleration`, so a heavier car accelerates the same but hits harder and is pushed less. That is a pure buff.

Risk patterns:
1. **Mass raises damage, survivability and knockback for free.** This is real under the current code. **Mitigation:** no mass in V1. Any future mass goes through an Opus API that applies `rb.mass` and the durability input together and is logged.
2. **A heavy ram on the hatchback.** Acceleration-mode drive keeps the hatchback's charge speed while μ rises, which amplifies its known scoring edge. Same mitigation. The M0 baseline must measure the hatchback first.
3. **The pickup's tier cap.** Hits above 90 kJ all score 120 points (MatchModel 110), so the pickup's extra energy is partly wasted on scoring. Attachments must not be used to "fix" this. It is a scoring-audit question.
4. **Defensive stalemates in the 60 s Showdown.** Geometric proxies do not reduce energy, so this cannot happen in V1. Any future absorption must be tested against the Showdown energy accounting (324–331).
5. **Wreck stealing.** The wreck-credit window (357–364) and the 150 bonus reward the finisher regardless of chassis. Attachments do not change this. Measure it in M0 as "wreck bonus share by chassis".

**Baseline instrumentation.** This is M0. It is read-only and needs no balance change.
- Per match and per car, log:
  - chassis and difficulty;
  - hits dealt and taken by tier;
  - energy dealt and taken;
  - points;
  - wreck bonuses;
  - wrecked-by;
  - survival time;
  - Showdown entry and wins;
  - average closing speed.
- Collection:
  - the data is captured from `OnImpact` (685) and `MatchModel` events;
  - it goes out through the existing `ReportToPage` JSON (835);
  - an editor batch runner collects it using the `ManualStep` path (797).
- Fair comparison:
  - same mode and difficulty;
  - AI-only matches for chassis comparisons (5 AI, rotating chassis), at least 30 matches per cell;
  - human data kept separate and labelled by player;
  - report the mean and its spread, never single runs.

### 7.5 Lifecycle and reset (F)

The persistent state is the `Loadout` per vehicle (PlayerPrefs) plus a `Participant.loadout` reference. The runtime state is `AttachmentMount`: its instances and its wreck tint.

| Event | Handling |
|---|---|
| Enter match or countdown | `StartMatch` → `ResetCar` → `mount.ResetVisuals()`. Apply the loadout if `Participant.loadout` changed (diff per slot; reuse instances) |
| Normal damage | Nothing. Items are rigid and outside the damage model |
| Points respawn | `ResetCar` path (579): re-pose and un-tint |
| LCS elimination or hulk | `ApplyWreckPaint` (267) → `mount.SetWrecked(true)`, a shared wreck material swap. Items stay on the hulk |
| Showdown full repair | `ResetCar` path (510): un-tint. Items persist (allowed), with the same instances |
| Rematch | `StartMatch` again; cars are reused, so instances are reused |
| Menu | `GoToMenu` keeps the cars, and the preview shows the current loadout |
| Scene reload | Cars are rebuilt; `BuildCar` → `Initialize` → mount from saved loadouts |
| Vehicle switch | `SelectVehicle` destroys the car (`DestroyCar` 241 destroys its children). The new car mounts that vehicle's saved loadout |
| Paint switch | `ApplyPaint`. Only `*Paint` materials change; `AttachPaint` follows the body colour |
| Load saved loadout | Validate, then `mount.Apply`. Invalid slots are empty, flagged and logged once |
| AI or human | Same `AttachmentMount`, same validator. In V1 the AI gets **empty loadouts** (default) or a fixed preset (§10) |

No allocation churn:
- Instances are created only when a slot's item changes.
- Materials are shared assets, with no `renderer.material` access.
- The wreck swap uses a cached material array per renderer, allocated once.
- `mount.Apply` with an identical loadout is a no-op. Test it with 50 resets: instance and material counts must stay the same.

### 7.6 Candidate items (E)

These are hypothetical and need playtesting. All are Common, junkyard style.

| Item | Slot | Art | Cost (hyp.) | Mass | Playstyle / downside | Stage 1 tier | Stage 2 tier | Real decision? |
|---|---|---|---|---|---|---|---|---|
| **Junk Push Bar** | Front | welded pipe frame | 2 | 0 (design 40 kg) | Signals "rammer". Cheap filler | visual | simple protective geometry (wide low proxy) | Stage 1: build identity only. Stage 2: yes, it moves contact forward and wide |
| **Spiked Ram Plate** | Front | plate + homemade spikes | 4 | 0 (design 70 kg) | Concentrated front hits. It is costly, so it competes with side and roof items | visual | geometry (narrow proxy → smaller footprint, same energy) | Yes in Stage 2: it trades capacity for shape. It must be measured, because a "spike" here only concentrates the dent |
| **Scrap Side Plating** | Left/Right (two separate items) | riveted sheet panels | 2 each | 0 | Defensive look. A full set costs 4 of 8 on the hatchback | visual | engine extension required for any protection (a proxy only moves the contact surface) | Weak until absorption is approved. It tests the capacity tradeoff and symmetry |

Substitutes:
- **Roof Cage Rack** (Roof, cost 1, visual only). It tests the roof socket, is the lowest risk, and the prototype rack passed.
- **Tow Hitch Bumper** (Rear, cost 2). It tests the rear socket and becomes a rear proxy in Stage 2.

The prototype "scoop" is excluded because it failed fit at 121.7 mm.

Honest assessment: in Stage 1, no item changes the outcome of a match. The decision is build expression and budget practice. Real gameplay decisions start in Stage 2 with front and rear proxies.

### 7.7 Parity

AI and human use the same mount code, validator and contact path, and no code branches on `isHuman`. The V1 AI loadout default is empty, so the 5-car baseline stays comparable. A fixed preset per difficulty is a user decision (§10).

## 8. Risks and mitigations

| # | Risk | Sev. | Owner | Mitigation |
|---|---|---|---|---|
| R1 | Attachment meshes discovered as parts if mounted before `Initialize` or after a `Rebuild` | High | Sonnet (ordering), Opus (marker) | Mount after Initialize; regression test on part and vertex count; §7.3 marker |
| R2 | Colliders added without the §7.3 interface corrupt ShellBounds, FillFace, footprint and carCollider | Critical | Opus | Stage 1 forbids colliders; a test asserts 14 boxes per car |
| R3 | `rb.mass` change desyncs from `def.mass` durability and AI | High | Opus | No mass in V1; future single API |
| R4 | Gap behind a rigid item after a crush (≤160 mm measured) | Medium | SOL/Sonnet | Brackets with depth; accepted for V1; visual QA per body |
| R5 | Items clip the body or wheels on one chassis | Medium | SOL/Sonnet | Fit matrix tool; bracket variants; no scaling |
| R6 | Instance or material leaks over resets and rematches | Medium | Sonnet | Diffing `Apply`; 50-reset test |
| R7 | Items stay bright on hulks | Low | Sonnet | `SetWrecked` shared material |
| R8 | Draw calls: up to 25 extra renderers in a 5-car match | Medium | Sonnet | Shared materials; single mesh per item; measure in M4 |
| R9 | Menu has no room | Low | Sonnet | Separate loadout panel |
| R10 | Stage 2 proxy trips spawn or Showdown overlap checks | Medium | Opus | Check half-extents before Stage 2 |
| R11 | Renamed item IDs orphan saves | Low | Sonnet | Stable IDs; validator flags `UnknownItem` |
| R12 | Separate Rigidbody per item breaks attribution | Critical | Opus | Rejected; documented here |

## 9. Performance (G)

Predictions, not measurements:
- Stage 1 adds **0 colliders and 0 Rigidbodies**. It adds up to 5 MeshRenderers per car, so 25 in a 5-car match. Shared materials allow SRP Batcher batching.
- About 1.5–3k triangles per item, so at most about 75k extra triangles worst case, with typical builds far lower. There is no deformation work, no refit and no callback change.
- Stage 2 adds at most 5 BoxColliders per car, on the same body, as compound children. The broadphase entry count is unchanged, because a compound counts as one body. Contact pair work grows only when touching.
- The receiver's per-contact cost gains one `TryGetComponent<AttachmentProxy>`. Cache it in a dictionary keyed by collider so it allocates nothing per hit.

Measurement plan (M0 to M4), all in the same WebGL build on the same phone:
- frame time p50/p95;
- draw calls and SetPass calls (Frame Debugger, editor);
- GC allocations per reset (Profiler);
- `WorstHitchCount` (`OnImpact` 689);
- the FPS EMA in `ReportToPage`.

Guardrail: a 5-car match with full loadouts must stay within 5% of the stock p95 frame time, with no new GC allocations per hit.

Unanswered:
- Which arena obstacles carry Rigidbodies (environment damage path)? This affects Stage 2 wall tests only.
- Real triangle and draw-call headroom on the target phone. Nothing has been measured.
- Whether the `ManualStep` batch runner reproduces live AI matches closely enough for M0 statistics. To check in M0.

## 10. Decisions (owner responses recorded)

| # | Decision | Outcome |
|---|---|---|
| 1 | Staged hybrid, Stage 1 visual-only first | **Approved** |
| 2 | Capacities 8/10/12 | **Approved as placeholders**, to be retuned after playtests |
| 3 | AI loadouts | **V1 = empty AI loadouts** (keeps the M0 baseline clean). **Direction from the owner:** higher difficulties should eventually field different vehicles *with* attachments. See "Future: difficulty-scaled AI loadouts" below |
| 4 | Rigid-item crush gap (up to about 160 mm) | **Accepted for V1** |
| 5 | Opus Stage 2 interface (§7.3) | **Accepted**, to be started after M2 (a separate go-ahead from the owner starts the Opus work) |

| 6 | Crush gap behind rigid items (found in M2) | Firm hits leave 98-145 mm (within the accepted 160 mm); a maximum synthetic crush leaves 338-361 mm. **Fix deferred by the owner**; candidates when revisited: a visible strut or bracket behind the item, or items that follow the panel (`panel_follow`) |
| 7 | Release gate | **WebGL/mobile verification is required before M2 is approved for release**; nothing is published meanwhile |

### Future: Home screen with Play and Garage (owner direction, not V1)

Long term, a production build should open on a home screen where the player chooses between **Play** (match setup: mode, opponents, START) and **Garage** (vehicle, paint, loadout), the way Rocket League and similar games separate them. How this relates to what exists:
- The M2 loadout panel is a stopgap reached from the match menu, and its small LOADOUT button is the weak point found in the phone-landscape check. The Garage replaces both: the main menu would lose the car, paint and loadout rows and gain a Garage entry, which also frees the space the current menu lacks.
- The data and mounting layers do not depend on any of this: `Loadout`, `LoadoutValidator`, `LoadoutStore` and `AttachmentMount` take choices from any screen. A Garage is a new view over the same calls (`SetHumanLoadoutItem`, `HumanLoadoutResult`).
- Garage needs that V1 does not cover: a live 3D preview with orbit, an item list or grid per slot showing cost, rarity and a short description, a capacity bar, a clear reason when an item cannot be equipped, and large touch targets (at least about 44 px on a phone). Saved builds per vehicle already exist; named presets would be new.
- Natural order when it is built: home screen shell and navigation first, then move car and paint into the Garage, then the loadout, then polish (previews, rarity styling). The shell also gives later features (stats, settings, unlocks) a place to live without crowding match setup.
- Not started; it is a menu-wide change, so it needs its own plan and the owner's go-ahead.

### Future: difficulty-scaled AI loadouts (not V1)

V1 already contains everything this needs, so nothing is built for it now:
- `Participant.loadout` (§5) applies to AI cars as well as the human car. Both use the same `AttachmentMount` and `LoadoutValidator`.
- A later `AiLoadoutPreset` data asset could map difficulty plus vehicle to a `Loadout` (and an optional vehicle roster), validated by the same capacity rules, so the AI is never given a build a player could not make.
- **Gate:** once Stage 2 proxies exist, AI builds change contact geometry and therefore difficulty balance. Presets must wait for the M0 baseline, and should be measured against it with the same AI-only protocol (§7.4). Stage 1 (visual-only) presets are safe at any time, because they do not change physics or scoring.
- Open for later: whether higher difficulties may exceed player capacity. This would be a balance decision for the owner and is not assumed.

1. **Approve the staged hybrid**, with Stage 1 visual-only shipping first and no physical effect.
2. **Capacities for V1 testing:** keep 8/10/12 as placeholders, or set other values?
3. **AI loadouts in V1:** empty (recommended, keeps the baseline clean) or a fixed preset per difficulty?
4. **Accept the rigid-item crush gap** (up to about 160 mm) for V1, instead of `panel_follow`?
5. **Authorize the Opus Stage 2 interface (§7.3)** after M2, with Opus implementing it on its own branch.

# Car-vs-car dent width (resolved)

**Status:** fixed on `fix/car-vs-car-impact-width`, not merged.

In the owner's phone playtests, car-vs-car hits left a narrow dent in the middle of the front, while wall hits dented the full width. Before the fix, car-vs-car dents covered 21–37% of the car's width; wall dents covered 94–100%.

## Root cause

Confirmed with the per-callback log in `ImpactWidthProbe`.

1. **How contacts are collected.** `OnCollisionEnter` fires once per collider pair. A car's physics shell is tiled into several boxes:
   - Front/Rear Left, Centre and Right boxes;
   - side boxes;
   - `BodyCoreBox` and `CabinBox`.

   So a head-on hit produces several callbacks in one physics step, for example `FrontLeftBox<-FrontCentreBox` and `FrontCentreBox<-FrontRightBox`.
2. **How events are grouped.** `Accumulate` merges every callback from the same other Rigidbody into one `Pending` event. `Step` processes that event after the physics step.
3. **How the impact region is calculated.** The dent footprint is the impactor box projected onto the car. The impactor box was the `BoxCollider` of the event's first callback.
4. **The first segment decided the footprint.** `if (!p.box.valid && c.collider is BoxCollider bc …)` kept only the first box. That box is one third of the shell's face, 0.44–0.68 m wide.
5. **Later segments were ignored for the dent's shape.** Their contact points still moved the average point and normal, but their boxes were dropped.
6. **How collision geometry affects width.** A wall, or the Collision Lab sled, is one wide box, so its dent is full width. That is why the lab never showed the problem. A vehicle's shell has three boxes across its face, so its dent covered about one third.
7. **Side and rear hits had the same problem.** Struck side: 0.56–0.65 m. Striker: 11–21% of its width. Rear: 22–32%.

**A second, smaller cause.** Shell segments do not always touch in the same physics step. In the Muscle→Hatch rear hit, `RearCentreBox` touched one step before the outer boxes. That first, off-centre touch also yawed both cars slightly. As a result, the later boxes no longer lined up with the first box *in this car's frame*.

## Fix

All changes are in `CollisionDamageReceiver` and apply only to box impactors. Single-box walls produce exactly the same footprint as before.

- **`combineImpactorBoxes` (default on).** Every box of the other body that touches during the event joins the footprint.
  - The footprint is the union bounds of those boxes. The union is built in the other body's own frame, where its shell boxes are fixed and exactly aligned (checked to 0.5°).
  - The union is then placed on this car using the other body's pose at the first box contact.
  - Only boxes that actually touched join, so an offset hit stays local.
  - A box that is not aligned with the first box (a different body part) is not merged. The footprint keeps the first box, as before.
- **`vehicleGatherSteps` (default 1, vehicle-vs-vehicle only).** An event against another damageable vehicle waits one extra physics step (20 ms), so segments that touch a step later still join the footprint.
  - Contacts from that extra step only add impactor boxes.
  - The strength and energy inputs (point, normal, relative velocity, impulse and penetration) still come from the first step only, exactly as before.
  - Wall and obstacle hits are processed immediately, as before.

**Why this over the alternatives.**

- **(B) Computing the actual overlap region** of the two shells would need a geometric overlap solver running for every hit, for an equivalent result. The union of the boxes that touched *is* the overlap, at the shell's resolution.
- **(C) Using the other vehicle's whole front face** would artificially widen offset and small hits, which the brief forbids.
- **Allocation:** none per step. `ready` is a preallocated list, and `Pending` objects were already allocated per event. The number of events is unchanged.

## Before / after (`CarWreck.Driving.EditorTools.ImpactWidthProbe.Run`, 20 m/s, walls at 19 m/s)

**How to read the table.**

- **Width** is how far the dent outline spans across the struck face.
- **Expected** is how much the two faces overlap geometrically. For side hits it is the striker's width.
- **Ratio** is width ÷ expected.

| Scenario | Car | Before width (ratio) | After width (ratio) | % of car width after | Matches contact? |
|---|---|---|---|---|---|
| Muscle → wall | muscle | 1.93 (0.94) | 1.93 (0.94) | 94% | yes, unchanged |
| Hatch → wall | hatch | 1.79 (0.97) | 1.79 (0.97) | 97% | yes, unchanged |
| Pickup → wall | pickup | 1.99 (1.00) | 1.99 (1.00) | 100% | yes, unchanged |
| Muscle → Muscle head-on | struck / striker | 0.64 (0.31) / 0.44 (0.21) | 1.93 (0.94) / 1.51 (0.73) | 94% / 73% | yes / partly |
| Muscle → Hatch head-on | struck / striker | 0.55 (0.30) / 0.44 (0.24) | 1.79 (0.97) / 1.46 (0.79) | 97% / 71% | yes / partly |
| Muscle → Pickup head-on | struck / striker | 0.68 (0.34) / 0.65 (0.33) | 1.95 (0.98) / 1.50 (0.75) | 98% / 73% | yes / partly |
| Hatch → Muscle head-on | struck / striker | 0.57 (0.31) / 0.44 (0.24) | 1.72 (0.93) / 1.58 (0.85) | 84% / 85% | yes |
| Hatch → Hatch head-on | struck / striker | 0.61 (0.33) / 0.44 (0.24) | 1.74 (0.94) / 1.51 (0.82) | 94% / 82% | yes |
| Hatch → Pickup head-on | struck / striker | 0.57 (0.31) / 0.62 (0.34) | 1.76 (0.95) / 1.51 (0.82) | 89% / 82% | yes |
| Pickup → Muscle head-on | struck / striker | 0.61 (0.31) / 0.66 (0.33) | 1.86 (0.93) / 1.97 (0.99) | 90% / 99% | yes |
| Pickup → Hatch head-on | struck / striker | 0.66 (0.36) / 0.55 (0.30) | 1.79 (0.97) / 1.74 (0.94) | 97% / 88% | yes |
| Pickup → Pickup head-on | struck / striker | 0.61 (0.30) / 0.62 (0.31) | 1.88 (0.94) / 1.85 (0.93) | 94% / 93% | yes |
| Muscle → Hatch offset 1.02 m | struck / striker | 0.22 (0.23) / 0.55 (0.59) | 0.86 (0.92) / 0.61 (0.65) | 46% / 29% | yes, local |
| Hatch → Pickup offset 1.02 m | struck / striker | 0.57 (0.63) / 0.58 (0.64) | 0.86 (0.95) / 0.72 (0.80) | 43% / 39% | yes, local |
| Pickup → Muscle offset 1.09 m | struck / striker | 0.61 (0.65) / 0.64 (0.69) | 0.82 (0.88) / 0.88 (0.95) | 40% / 44% | yes, local |
| Muscle → Hatch side | struck side / striker | 0.65 (0.31) / 0.31 (0.15) | 1.94 (0.94) / 1.44 (0.70) | 48% of length / 70% | yes |
| Hatch → Pickup side | struck side / striker | 0.56 (0.30) / 0.20 (0.11) | 1.75 (0.95) / 1.51 (0.82) | 37% of length / 82% | yes |
| Pickup → Muscle side | struck side / striker | 0.61 (0.30) / 0.42 (0.21) | 1.86 (0.93) / 1.94 (0.98) | 37% of length / 98% | yes |
| Muscle → Hatch rear | struck / striker | 0.55 (0.30) / 0.43 (0.23) | 1.79 (0.96) / 1.14 (0.61) | 96% / 55% | yes / partly |
| Hatch → Pickup rear | struck / striker | 0.57 (0.31) / 0.41 (0.22) | 1.76 (0.95) / 1.51 (0.82) | 89% / 82% | yes |
| Pickup → Muscle rear | struck / striker | 0.66 (0.33) / 0.62 (0.31) | 1.87 (0.94) / 1.94 (0.98) | 91% / 98% | yes |
| Muscle → Hatch, second hit on already-dented cars | struck / striker | 0.68 (0.37) / 0.61 (0.33) | 1.79 (0.97) / 1.56 (0.85) | 97% / 76% | yes |

**Energy and strength are identical before and after** (within PhysX run-to-run noise, at most 0.2%). For example, the Muscle→Muscle head-on target takes 64.0 kJ at 54.2% strength both ways. The momentum split is unchanged: in the probe, the striker takes about 15% and the struck car 85% (11.3 / 64.0 kJ).

**Depth.** A wider footprint spreads the same energy over more area. The existing area factor, `Clamp((refArea/area)^0.35, 0.6, 1.6)`, now gives 0.71–0.90 for head-on hits instead of 1.08–1.34. That is the same range as a wall hit (0.81). Peak crush travel drops accordingly. For example, the Muscle→Muscle target goes from 0.258 m to 0.170 m, against 0.201 m when the muscle car hits the wall. This is the existing rule applied to the correct area, and damage was not increased.

## Tolerances

These are proposed, measured with the probe.

| Measure | Tolerance | After |
|---|---|---|
| Struck car, full face overlap (head-on, rear; side: against the striker's width) | width ≥ 0.85 × expected overlap | 0.93–0.98, **PASS** |
| Offset hits, both cars | width within −35% to +15% of expected overlap | 0.65–0.95, **PASS** (Muscle→Hatch striker 0.65 is at the bound) |
| Wall hits | within ±1% of the previous width | identical, **PASS** |
| Energy and strength | identical to before (PhysX noise ≤ 0.2%) | **PASS** |
| Striker, full overlap | informational only (see remaining issues) | 0.61–0.99 |

## Regression results

Run after the fix, on the feature branch.

- **Collision Lab verify and geometry (`collision-verify;collision-geometry`): PASS, 15 of 15.** This covers the wide/narrow, corner, glancing, speed, mass, accumulation, saturation, reset and repeatability checks.
- **Vehicle integration (`vehicle 0;vehicle 1;vehicle 2`): PASS, 20 of 20.** This covers saturation, part-fold outliers, rebuild optimisation, UVs and Reset All.
- **`DrivingValidation`: ALL CHECKS PASSED.** This includes:
  - the 15/85 split for all nine vehicle pairs (energy ratio 5.7, expected 5.7);
  - persistence and reset;
  - the moving-target scenarios. The head-on result is identical to the documented baseline: closing 132 km/h, strengths 87% / 98%, target 39 → −32 km/h.

  On the first run three moving-target checks failed. The test treated contact as starting when the receiver *registered an event*. With the one-step gather, an event now registers one step after the first touch, so the "before impact" speeds were sampled after the hit. The test now detects the first physical car-to-car contact through `ContactsReceived`. The collision outcomes themselves were unchanged.
- **`PickupCheckpointCExtensionValidation`:** ran to completion with no errors. This run only captures evidence and has no pass/fail checks. It uses the single-box lab sled, so this change does not affect it.

To reproduce the width table, run `CarWreck.Driving.EditorTools.ImpactWidthProbe.Run` in batch mode. It prints BEFORE (first box only), UNION (combine, no gather) and AFTER (combine plus gather) for every scenario.

## Remaining issues

- **The striker's dent is narrower than the struck car's** on curved fronts: 71–73% for the muscle car, and 55% for the Muscle→Hatch rear striker.
  - The striker's crush is shallow because it takes only a 15% energy share.
  - Where the shell box reaches only the most forward part of a curved nose, the dent outline covers only the area the box actually reaches.
  - This is not a box-selection problem: all three boxes join (see the callback log).
- **Vehicle hits register 20 ms later** because of `vehicleGatherSteps = 1`. The delay is not perceptible. Setting the value to 0 restores the old timing but also loses the late-segment fix.
- **Boxes from different body parts are not merged**, for example a front box and a side box in a corner hit. The first box is used, as before.

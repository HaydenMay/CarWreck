---
name: addacar
description: Create a new destructible CarWreck vehicle (Blender generator, deformation-ready geometry, vehicle.json, asset QA, Unity import and destruction tests). Use only when the user invokes $addacar. Starts by asking what vehicle to build and for reference images, then waits.
---

# addacar

Create a new destructible vehicle for CarWreck through the shared vehicle engine: configuration and validation,
never vehicle-specific destruction code.

The process is defined in `Docs/VehicleProduction/addacar-workflow.md` (canonical; shared with the other agent's
`addacar` skill). Follow it exactly.

## On invocation — do this first, then stop

1. Read `Docs/VehicleProduction/vehicle-asset-spec.md`.
2. Read `Docs/VehicleProduction/vehicle-qa-checklist.md`.
3. Read `Docs/VehicleProduction/addacar-workflow.md`.
4. Review the existing generators `Blender/Scripts/build_compact_hatchback.py` and `Blender/Scripts/build_muscle_car.py`
   and the look of the existing cars (`Previews/CompactHatchback/StageC/*.png`, `Screenshots/70_*`).
5. Ask the user, in one message:
   - **What kind of vehicle would you like to create?**
   - **Do you have any reference images?**
6. **Wait for the reply.** Do not create branches, scripts, Blender scenes or files, and do not start modelling,
   until the user has answered. Do not ask the user for topology, Unity import or damage-configuration details.

## After the user answers

Continue with Phase 1 of the workflow document: visual direction, an isolated `feature/vehicle-<slug>` branch,
a blockout with preview renders and an approval gate, production geometry, `vehicle.json`, Blender asset QA, Unity
import and destruction tests where the environment allows, then the report and a push to the feature branch.

Rules that always apply:
- Never merge a vehicle into `main` without the user's approval.
- Any vehicle shape and size; no muscle-car dimensions, no fixed vertex counts.
- Report asset QA and Unity destruction tests separately; never claim destruction results that were not run.
- Blender-side socket checks do not prove Unity attachment behaviour.

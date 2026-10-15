---
name: addacar
description: "CarWreck vehicle creation workflow (addacar). Use ONLY when the user explicitly asks to start, use or follow it: /addacar, $addacar, 'addacar', 'the addacar skill/process', 'the CarWreck vehicle creation (or production) workflow'. Do not use for questions about the existing cars, the damage engine or the Collision Lab, or for casual talk about possible future vehicles. On start it reads the project docs, asks what vehicle to create and for reference images, then waits."
argument-hint: "[optional vehicle idea]"
---

# addacar

Create a new destructible vehicle for CarWreck through the shared vehicle engine: configuration and validation,
never vehicle-specific destruction code.

The process is defined in `Docs/VehicleProduction/addacar-workflow.md` (canonical; shared with the other agent's
`addacar` skill). Follow it exactly.

Start it only when the user explicitly asks for this workflow (the skill command, or plain language such as "Follow
the addacar skill from the repository", "Use the CarWreck vehicle creation workflow", "Start the addacar process").

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

Continue with Phase 1 of the workflow document: reference intake (use every relevant view, side and top proportions
before ¾ views, measured proportion ratios, ask about missing design-critical features), an isolated
`feature/vehicle-<slug>` branch, then the three approval checkpoints:

- **A — blockout:** front ¾, side and rear ¾ renders compared against the references. Wait for approval.
- **B — detailed model:** compared against the references and the approved blockout. Wait for approval.
- **C — final validation:** final renders, comparison, QA results, Unity destruction screenshots when available,
  known limitations. Wait for approval; never merge without it.

At every checkpoint act as the first visual reviewer: matched-angle renders, a comparison sheet, a proportion table
measured from the geometry, a feature checklist, discrepancies fixed or listed before asking. Never claim a perfect
match without evidence.

At each checkpoint, commit and push the review images/report to the feature branch and provide hosted gallery links.
After approval, clean temporary review images when they are no longer needed, preserving comparison baselines and
final evidence as specified in the canonical workflow's "Checkpoint image delivery and cleanup" section.

Rules that always apply:
- Never merge a vehicle into `main` without the user's approval.
- Any vehicle shape and size; no muscle-car dimensions, no fixed vertex counts.
- Report asset QA and Unity destruction tests separately; never claim destruction results that were not run.
- Blender-side socket checks do not prove Unity attachment behaviour.

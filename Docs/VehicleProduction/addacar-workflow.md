# addacar — vehicle production workflow (canonical)

The single source of truth for adding a destructible vehicle to CarWreck. The Claude Code skill
(`.claude/skills/addacar/SKILL.md`, invoked as `/addacar`) and the Codex skill (`.agents/skills/addacar/SKILL.md`,
invoked as `$addacar`) are thin entry points onto this document; change the process here, not in the skills.

Technical standard: [vehicle-asset-spec.md](vehicle-asset-spec.md). Checks: [vehicle-qa-checklist.md](vehicle-qa-checklist.md).

---

## Phase 0 — Start (every invocation)

1. Read `Docs/VehicleProduction/vehicle-asset-spec.md` and `Docs/VehicleProduction/vehicle-qa-checklist.md`.
2. Review the existing generators for conventions and style:
   - `Blender/Scripts/build_compact_hatchback.py` — the newer pattern: argparse CLI, materials + `palette.json`,
     `StripeProjection` UVs for the two-colour paint, `finalize()` (apply modifiers, clean meshes, ray-cast trims onto
     the body), `inspection()`, `export()`, preview renders.
   - `Blender/Scripts/build_muscle_car.py` — the even 3–4 cm lofted body grid and wheel construction.
3. Look at the art style: `Previews/CompactHatchback/StageC/*.png`, `Screenshots/70_*` (both cars undamaged and
   damaged). Stylised, clean, sporty miniature vehicles; bold paint with optional twin stripes; black bumpers; no logos.
4. Ask the user, in one message, exactly:

   > **What kind of vehicle would you like to create?**
   > **Do you have any reference images?**

5. **Stop and wait for the answer.** Do not create a branch, a script, a Blender scene or any file before the user
   replies. Do not ask the user about topology, Unity import settings or damage configuration — this workflow owns them.

## Phase 1 — Visual direction (after the answer)

- Restate the vehicle in a few lines: type, era/style, proportions (length, width, height, wheelbase in metres),
  silhouette features, colours and stripe treatment. Any shape and size is fine; never default to muscle-car proportions.
- Use the reference images for proportions and features only; never copy logos, badges or trademarks.
- Confirm the direction with the user if anything material is ambiguous.

## Phase 2 — Isolated branch

- Create `feature/vehicle-<slug>` from the current `main` (a separate worktree if the environment supports it).
- Never work on `main`. Never merge a vehicle into `main` without the user's explicit approval.

## Phase 3 — Blockout and approval gate

- Write `Blender/Scripts/build_<slug>.py` following the hatchback generator's structure.
- Produce a blockout and render `Previews/<Vehicle>/StageA/` (front ¾, side, rear ¾).
- Present the renders. **Wait for approval** before detailed modelling. Iterate on feedback.

## Phase 4 — Production geometry

Meet [the asset spec](vehicle-asset-spec.md) §1–§6:
- even body quad grid (p90 edge ≤ 6 cm), wheel wells in the grid, no booleans/n-gons on the shell;
- recesses with real depth (no thin double skins), dense small parts (no long slivers), trims following the body;
- four wheel roots at the wheel centres; stable, token-friendly component names;
- `UVMap` plus `StripeProjection` on painted parts; `palette.json`; stripe mask texture;
- deformable vertices ≤ 90k recommended.

## Phase 5 — Configuration

- Classify every component (role table in the spec §2) and write `Assets/Vehicles/<Vehicle>/vehicle.json`
  (spec §7): identity, model/palette/paint paths, mass, rules, `cabinLine` 0 (measured), `shell` 0 (automatic),
  `expected` (dimensions and wheel centres from the generator), and optional `sockets` (spec §9; omit unsupported groups,
  `runtime_ready: false`).
- No vehicle-specific C#. If something seems to need engine code, stop and ask.

## Phase 6 — Asset QA (Blender)

```text
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b Blender/<Vehicle>.blend --factory-startup \
  --python Blender/Scripts/vehicle_asset_qa.py -- --manifest Assets/Vehicles/<Vehicle>/vehicle.json --out Previews/<Vehicle>/asset_qa.json
```

Fix every mandatory failure; fix or justify every recommendation. Re-run the generator after each fix.

## Phase 7 — Export and Unity

Unity 6000.6.0f1, batch bridge (`-executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "<cmds>"`):

1. `vehicle-import Assets/Vehicles/<Vehicle>/vehicle.json dry` → must PASS (classification, wheels, orientation, size).
2. `vehicle-import Assets/Vehicles/<Vehicle>/vehicle.json` → import settings, materials, definition and profile.
3. `collision-build` → the vehicle appears in the selector (index = after the shipped vehicles).
4. Destruction tests (checklist §7): `vehicle <i>`, `collision-phase4 <i>`, `collision-overflow <i>`,
   `collision-random <i>`, `vehicle-switch`. Review the **PART FOLD OUTLIERS** line and the `70_<Vehicle>_*` screenshots.
5. If possible, `collision-webgl` and a browser check (the vehicle, Random Launch, a Chaos Run, paint).

Batch runs re-serialize a few unrelated files (`Assets/DamageLab/Materials/*.mat`,
`ProjectSettings/DynamicsManager.asset`, `ProjectSettings/ProjectSettings.asset` product name): revert them before
committing.

If the environment cannot run Unity or Blender, say so plainly and list which phases were not performed.

## Phase 8 — Report and commit

Present the vehicle (renders, damaged screenshots) and a report with these sections:

1. **Vehicle** — description, dimensions, mass, component count, deformable vertices.
2. **Asset QA (Blender)** — mandatory results, recommendations and how each was handled.
3. **Configuration** — `vehicle-import` result, warnings.
4. **Destruction tests (Unity)** — which commands ran, key numbers, fold outliers. If none ran, write
   "Destruction tests: NOT RUN" — never infer them from asset QA.
5. **Sockets** — groups provided (metadata only, not runtime-validated).
6. **Known issues and what was not done.**

Commit and push `feature/vehicle-<slug>` (Git LFS for binaries). Ask before any merge into `main`.

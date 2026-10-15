# addacar — vehicle production workflow (canonical)

The single source of truth for adding a destructible vehicle to CarWreck. The Claude Code skill
(`.claude/skills/addacar/SKILL.md`) and the Codex skill (`.agents/skills/addacar/SKILL.md`) are thin entry points onto
this document; change the process here, not in the skills.

Technical standard: [vehicle-asset-spec.md](vehicle-asset-spec.md). Checks: [vehicle-qa-checklist.md](vehicle-qa-checklist.md).

**How it starts.** `/addacar` (Claude Code), `$addacar` (Codex), or an explicit request in plain language to use this
workflow, for example "Follow the addacar skill from the repository", "Use the CarWreck vehicle creation workflow",
"Start the addacar process". It never starts on its own from a conversation that did not ask for it: questions about
the existing cars, the damage engine, or musing about future vehicles are answered normally.

**The agent is the first visual reviewer; the user keeps final creative approval.** Three approval checkpoints
(A blockout, B detailed model, C final validation) each present renders compared against the references, with
measured proportions and an honest list of differences, and then wait for the user.

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

## Phase 1 — Visual direction and reference intake (after the answer)

1. **Catalogue every reference image**: its view (front, rear, side, top, front ¾, rear ¾, detail), whether it is
   close to orthographic or has strong perspective, and which features it shows clearly.
2. **Use all relevant views, ranked for proportions**: side and top (or plan) views first, then front and rear, then
   ¾ views. A ¾ view shows character and surfacing but distorts lengths; never let a good-looking ¾ render override
   the side and top proportions.
3. **Measure proportions from the references** wherever a near-orthographic view exists, as ratios to overall length
   (side/top) or overall width (front/rear), from pixel positions on the image:
   wheelbase, front and rear overhang, overall height, wheel diameter and wheel-centre height, beltline height,
   hood length (nose to windscreen base), cabin/glasshouse length and roof length, windscreen and rear-glass rake,
   track and body width (front/top), and the positions of distinctive features. Record each value with its source
   image and an uncertainty (perspective, lens, partial view). Estimates from ¾ views are marked as estimates.
4. **Ask instead of inventing** when a design-critical feature is obscured or missing from every reference (for
   example no rear view, an unclear roofline or door count, a hidden front end). State what is unknown, propose an
   option, and wait. Small details may be designed in the house style and listed as assumptions.
5. **Restate the direction**: type, era/style, overall dimensions in metres (length, width, height, wheelbase),
   the measured proportion table, silhouette features, colours and stripe treatment, and the list of assumptions.
   Any shape and size is fine; never default to muscle-car proportions. Use references for proportions and
   features only; never copy logos, badges or trademarks.

Without reference images, the proportion table comes from the user's description and typical real vehicles of that
type (marked as targets, not measurements), and the comparisons below are against that table.

## Phase 2 — Isolated branch

- Create `feature/vehicle-<slug>` from the current `main` (a separate worktree if the environment supports it).
- Never work on `main`. Never merge a vehicle into `main` without the user's explicit approval.

## Reference self-review (used at every checkpoint)

Before presenting anything, the agent reviews its own renders against the references and fixes what it can.

1. **Matched views.** Render the model from cameras that match the references: orthographic side, front, rear and
   top for the near-orthographic references; for ¾ and perspective references, a perspective camera at a similar
   height, angle and focal length. Use the same framing for model and reference.
2. **Comparison sheet.** Put each reference beside (or overlaid on, at matched scale) its matching render, in
   `Previews/<Vehicle>/<Stage>/comparison_*.png`.
3. **Proportion table.** For every ratio measured in Phase 1, list reference value, model value (measured from the
   generator's geometry, not eyeballed from the render) and the difference in %. Flag differences above 5 % (and any
   wheel placement or wheelbase difference above 3 %).
4. **Feature checklist.** Evaluate and write one line each for: overall silhouette, vehicle proportions, hood and
   cabin dimensions, wheelbase and wheel placement, front design, rear design, fenders and body curvature, and every
   major distinctive feature from Phase 1.
5. **Fix, then report.** Fix significant discrepancies before asking for approval and re-run the review. Report the
   differences that remain, deliberate stylisation, and what could not be judged (no reference for that view).
6. **Evidence only.** Never claim a perfect or exact match. Every "matches" needs a measurement or a side-by-side
   image; otherwise say "not verified". Mark estimates as estimates.

## Checkpoint image delivery and cleanup (A, B and C)

Before asking for checkpoint approval, commit and push the current vehicle's review package to its
`feature/vehicle-<slug>` branch: required renders, reference comparisons, measurements, report and the source/scripts
needed to reproduce them. Keep images under `Previews/<Vehicle>/<Stage>/` (Git LFS for binaries); add a short
`README.md` gallery with relative image links so the checkpoint can be reviewed on the repository host.
Stage only the relevant vehicle and workflow files; leave unrelated working changes out of the commit.

Confirm the remote branch contains the commit and the image content is available, then provide browser links to
the hosted gallery, comparison sheets and report in the checkpoint message. Local file links and inline chat images
are supplementary. If the configured remote or authentication is unavailable, report the publishing limitation and
provide the local files; do not claim the images were uploaded or use a different/public host without authorization.
Pushing review evidence does not approve the checkpoint or authorize a merge.

Keep images for the active checkpoint until the user has approved it. After approval, remove superseded drafts and
redundant temporary images once they are no longer needed for the next comparison. Preserve the approved Stage A
baseline through the Stage B comparison and the approved Stage B baseline through final validation. When all
checkpoints are approved, clean intermediate review uploads from the branch, retaining the source/generators,
reference catalogue, measurements/reports and a compact final render/comparison/QA evidence set. Update gallery and
report links when removing images. Use ordinary follow-up deletion commits, not Git/LFS history rewriting; list
what was cleaned and push the cleanup. Do not clean another vehicle's evidence or the current pending review.

## Phase 3 — Blockout → Checkpoint A (initial blockout)

- Write `Blender/Scripts/build_<slug>.py` following the hatchback generator's structure; produce the blockout.
- Render `Previews/<Vehicle>/StageA/`: front ¾, side, rear ¾, plus the matched reference views.
- Run the reference self-review. Present: the three renders, the comparison sheet, the proportion table, the feature
  checklist and the remaining differences.
- Publish the checkpoint package and hosted gallery links using the delivery rules above.
- **CHECKPOINT A — WAIT FOR USER APPROVAL.** Iterate on feedback (re-run the self-review each time). No detailed
  modelling before approval.

## Phase 4 — Production geometry → Checkpoint B (detailed model)

Meet [the asset spec](vehicle-asset-spec.md) §1–§6:
- even body quad grid (p90 edge ≤ 6 cm), wheel wells in the grid, no booleans/n-gons on the shell;
- recesses with real depth (no thin double skins), dense small parts (no long slivers), trims following the body;
- four wheel roots at the wheel centres; stable, token-friendly component names;
- `UVMap` plus `StripeProjection` on painted parts; `palette.json`; stripe mask texture;
- deformable vertices ≤ 90k recommended.

Then render `Previews/<Vehicle>/StageB/` (same views as Stage A) and run the self-review **against the references and
against the approved blockout**: the proportion table shows reference, Stage A and Stage B values; any drift from the
approved blockout above 2 % is called out and explained or corrected.

Publish the checkpoint package and hosted gallery links using the delivery rules above.

**CHECKPOINT B — WAIT FOR USER APPROVAL** before configuration, QA and Unity work.

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

Fix every mandatory failure; fix or justify every recommendation. Re-run the generator after each fix. If a fix
changes the visible shape, re-run the self-review.

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

## Phase 8 — Checkpoint C (final validation), commit

Present the finished vehicle and a report:

1. **Vehicle** — description, dimensions, mass, component count, deformable vertices; final renders (Stage C).
2. **Reference comparison** — final comparison sheet and proportion table (reference, approved Stage B, final),
   feature checklist, remaining differences and deliberate stylisation.
3. **Asset QA (Blender)** — mandatory results, recommendations and how each was handled.
4. **Configuration** — `vehicle-import` result, warnings.
5. **Destruction tests (Unity)** — which commands ran, key numbers, fold outliers, damaged screenshots. If none ran,
   write "Destruction tests: NOT RUN" — never infer them from asset QA.
6. **Sockets** — groups provided (metadata only, not runtime-validated).
7. **Known limitations and what was not done.**

Commit and push `feature/vehicle-<slug>` (Git LFS for binaries), including the final checkpoint gallery and evidence
links as described above. After approval, perform the review-image cleanup described above before merging.

**CHECKPOINT C — WAIT FOR USER APPROVAL.** Never merge into `main` without the user's explicit approval.

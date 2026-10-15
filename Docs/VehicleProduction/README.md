# Vehicle production

How new destructible vehicles are made for CarWreck. Shared by people and by both coding agents.

| Document | Purpose |
|---|---|
| [vehicle-asset-spec.md](vehicle-asset-spec.md) | The technical standard: units/orientation, component roles and names, cabin protection, deformation-ready geometry, paint and stripes, export/import, `vehicle.json`, attachment sockets. MUST vs SHOULD. |
| [vehicle-qa-checklist.md](vehicle-qa-checklist.md) | Every check, tagged mandatory / recommended / automated in Blender / automated in Unity / visual / Unity-only, with how to run it. |
| [addacar-workflow.md](addacar-workflow.md) | The canonical step-by-step workflow behind the `addacar` skill. |
| [examples/](examples) | Reference manifests for the two shipped vehicles (`vehicle-import-check` proves the hatchback one reproduces its hand-written configuration). |
| [tools/check-addacar-skills.mjs](tools/check-addacar-skills.mjs) | Keeps the two skill entry points consistent (`node Docs/VehicleProduction/tools/check-addacar-skills.mjs`). |

## Entry points

| Agent | Skill file | Invoke |
|---|---|---|
| Claude Code | `.claude/skills/addacar/SKILL.md` | `/addacar`, or a plain-language request |
| OpenAI Codex | `.agents/skills/addacar/SKILL.md` (+ `agents/openai.yaml`) | `$addacar`, `/skills`, or a plain-language request |

Plain-language requests work in both agents, for example *"Follow the addacar skill from the repository"*, *"Use the
CarWreck vehicle creation workflow"*, *"Start the addacar process"*. The skill description restricts automatic use to
explicit requests for this workflow; questions about the existing cars or casual talk about future vehicles do not
start it. Both entry points have the same description and body and defer to `addacar-workflow.md`. On invocation the
agent reads the spec, the checklist and the workflow, reviews the existing generators, asks *"What kind of vehicle
would you like to create?"* and *"Do you have any reference images?"*, and waits.

The workflow makes the agent the first visual reviewer: at three checkpoints (A blockout, B detailed model, C final
validation) it compares matched-angle renders and measured proportions against the references, fixes or lists the
differences, and waits for the user's approval.

## Tools

| Tool | Where | What |
|---|---|---|
| Blender asset QA | `Blender/Scripts/vehicle_asset_qa.py` | Static checks on a vehicle `.blend` against its manifest. |
| Manifest importer | bridge `vehicle-import <manifest> [dry]` | Import settings, materials, `VehicleDefinition` + damage profile, orientation/scale/wheel checks. |
| Importer checks | bridge `vehicle-import-check`, `vehicle-import-roundtrip` | Reference manifests vs the hand-written configurations; manifest-built hatchback replays identically. |
| Destruction tests | bridge `vehicle <i>`, `collision-phase4 <i>`, `collision-overflow <i>`, `collision-random <i>`, `vehicle-switch` | Live engine tests in Unity (batch). |

# Attachment proof of concept implementation plan

> For agentic workers: use superpowers:executing-plans for inline implementation.

Goal: Demonstrate three simple accessories, repeatable surface following and visible
failure limits in Blender, without Unity integration.
Architecture: Semantic sockets and attachment-owned feet connected by explicit adapters.
Four cached barycentric samples drive the scoop's rigid pose. All files remain under
Prototypes/Attachments on feature/attachment-prototype.
Tech stack: Blender 5.2 Python, mathutils, bundled NumPy; Pillow for preview contact sheets.
Spec: architecture-proposal.md, approved by the user on 2026-10-14.

## Global constraints

- Preserve the verified hatchback/muscle source assets, all C# and both labs.
- Roof-foot separation is a demonstrated limitation requiring future mounting/detachment
  handling, never acceptable final gameplay behavior.
- Small fixed-case metadata and diagnostics only; no customization framework.
- Show moderate and severe hood deformation with identifiable tracked anchors.
- Commit/push this branch only; no merge and no Unity integration.

## Review focus

1. Imported versus authored coordinate frames; source topology identity.
2. Folded, collinear, missing, nonfinite or stale anchor data must fail explicitly.
3. Invalid pose freeze must not conceal the current surface/foot disagreement.
4. Same rack geometry across vehicles; adaptation must not stretch the rack.
5. Repeat build/repair results must come from the original vertices.

## Task 1: Surface binding and rigid pose

Files: scripts/mount_math.py, scripts/test_mount_math.py.
Interfaces: bind_anchors(vertices, triangles, seeds) -> binding list; mesh_revision(...) -> hash;
sample_anchor(vertices, binding, revision) -> point, normal;
fit_rigid(local, targets) -> 4x4 matrix; assess(...) -> diagnostic dict.
- [ ] Write tests for a known triangle's barycentric motion, rigid rotation/translation,
  warped-foot residuals, stale cache, invalid index/degeneracy and unsupported adapter.
- [ ] Run the new tests; verify the missing implementation fails.
- [ ] Implement the minimum functions, rerun tests, commit the tested math.

## Task 2: Models and repeatable Blender demonstration

Files: scripts/models.py, scripts/build_demo.py, scripts/scene_views.py.
Consumes Task 1 functions and the verified production sources.
Produces models/, demonstration.blend, metadata.json, evidence/results.json and raw renders.
- [ ] Write end-to-end acceptance checks before the build exists.
- [ ] Create black four-foot/two-crossbar rack, cyan shallow scoop, steel tube push bar with
  two bracket assemblies and a rest-up constraint.
- [ ] Bind real source surfaces, generate rest/moderate/severe/repair cases from originals.
- [ ] Save multi-scene demonstration with readable markers, diagnostic connectors and cameras.
- [ ] Record numeric checks including rack reuse, source hashes, invalid cases, repair,
  attachment exports and deterministic geometry/bindings across two builds.
- [ ] Render modest technical views and inspect them visually.

## Task 3: Results, review and handoff

Files: validation-report.md, integration-notes.md, README.md, preview boards, execution notes.
- [ ] Assemble labelled matched-view boards without altering render content.
- [ ] Report measured results, physical limits, untested Unity/runtime work and seven answers.
- [ ] Rerun tests and end-to-end checks; verify only prototype paths are staged.
- [ ] Obtain one fresh whole-branch code/evidence review; address material findings.
- [ ] Commit and push final artifacts to feature/attachment-prototype, no merge.

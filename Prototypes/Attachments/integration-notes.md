# Minimum integration handoff for Claude

This is a Blender research handoff, **not implemented Unity functionality**.
The approved design is in architecture-proposal.md; actual measurements are in
validation-report.md. Keep the unfinished engine branch independent.

## Small contract demonstrated

`metadata.json` is generated, concrete Blender-side data for three accessories and four
explicit vehicle/accessory fits. `metadata-example.json` remains the earlier design
example; it is not the current binding data. There is no generic schema framework or UI.

Each accessory has an ID, category, mode, failure capability, identity local frame,
named local feet, geometry hash and unit-scale requirement. Each vehicle adapter has a
stable vehicle ID, named socket group, source mesh role, source SHA256, ray/region seeds,
four source-triangle bindings, rest pose, normal clearance and alignment offsets.

Stable socket identity is the tuple (vehicle_id, socket_group, socket_id), for example
(carwreck.compact_hatchback, hood.scoop, H4). H4 does not mean a global FBX child index.
Authored strings remain stable when the generator is rerun. Cached triangle references
are revision-specific and must be regenerated if topology/triangulation changes.

All JSON coordinates are Blender vehicle-local metres: +X right, +Y forward, +Z up.
Quaternion arrays are xyzw. Public asset frames and alignment offsets use identity
quaternions; fitted adapter poses are explicit 4x4 matrices.
Unity points/directions map as (x,z,y). For rotations use S R inverse(S), then construct
a quaternion in Unity. Do not swap Euler angles or quaternion components.

## Minimum Unity work

1. Resolve a unique vehicle frame and named mesh roles once. Account for FBX import-root
   transforms rather than assuming identity or copying a hierarchy path blindly.
2. Bind the constrained rest-space samples to the **actual imported Unity Mesh**. Prefer
   an editor/import-time bake; a one-time initialization search is a reasonable first
   fallback. Cache mesh identity/signature, submesh, three vertex indices and three weights.
   Reject missing, ambiguous or out-of-region matches.
3. After final deformation/repair rebuild, fetch only the cached vertices for changed
   support meshes. A four-anchor accessory reads at most twelve vertices, plus a small
   rigid fit. Avoid repeated nearest-surface searches or full mesh-array allocations per
   attachment. This is a complexity estimate, not measured Unity performance.
4. Compute a proper rotation and translation without scale/shear. Evaluate residuals,
   spacing distortion, support degeneracy and normal consistency before accepting a pose.
   Coordinate-handedness and frame-normal orientation need Unity tests.
5. Invalidate caches when source import settings, topology/order, mesh replacement or
   model identity changes. Repair with unchanged topology retains bindings and restores
   pose. Do not confuse current vertex-position changes with topology revisions.
6. Exclude attachment meshes and debug helpers from automatic body-mesh discovery, damage
   face bounds and hitbox fitting unless intentionally integrated.

Main at 2a9ca8b has VehicleDamageModel.Deformed, Parts and
DeformablePart.DeformedVehiclePosition(int). Treat these as possible seams; this prototype
does not assume Claude's unfinished design retains their names or ownership.

## Resolve the demonstrated failures

- **Rack:** Its fixed chassis frame stays stable, but a roof dent produces a 160 mm gap.
  That is unacceptable final gameplay behavior. Choose support motion consistent with
  the intended load path, compliant/bending brackets, or detachment. Holding the old pose
  and allowing visible disconnected feet is only this experiment's diagnostic.
- **Scoop:** Moderate following is plausible but imperfect (12.8 mm maximum error).
  A severe fold has 121.7 mm best-fit error. Freezing the previous pose exposes the fault;
  it is not a production response. Define allowed residual/compliance and the response
  when a supporting panel ceases to support the accessory.
- **Push bar:** Bumper skin samples are proxies. Real chassis mounting needs structural
  reference metadata; an asymmetric crush cannot be satisfied by fixed bracket spacing.
  Force transfer, breakage and detached-body ownership belong to the runtime engine.

The proof of concept deliberately leaves force_limit_N and torque_limit_Nm null. Millimetre
residuals are not forces and should never be used as calibrated strengths. The diagnostic
25/60 mm, 25% spacing and 55-degree normal-spread/orientation limits are initial authoring thresholds only.

## Future /addacar asset requirements

For every new car, require:
- Stable vehicle ID/revision, metre scale and verified asymmetric +X/right, +forward and up
  markers through the actual export/import pipeline.
- Unique semantic body/cabin/front structural mesh-role mapping, an identifiable vehicle
  frame, source/export identity and topology revision.
- Optional named socket groups such as roof.rack, hood.scoop and front.push_bar. Missing
  groups mean unsupported, not automatic fallback to a guessed location.
- Structural versus deformable support semantics and local rest frames. Panel sockets
  need bounded rest-space seeds, expected normals and permitted surface regions.
- Explicit per-accessory fit/adapters with local foot mapping, allowed orientation offsets,
  clearance and compatibility restrictions. Permit a separately authored bracket adapter
  where appropriate; do not silently stretch an accessory.
- Source-binding regeneration after topology changes, rest fit and severe-distortion
  evidence, exact repair/reset checks, and a separate Unity-imported binding validation.

The existing car generators were not changed. A later /addacar workflow can emit these
small records beside its production assets once the runtime contract has been agreed.

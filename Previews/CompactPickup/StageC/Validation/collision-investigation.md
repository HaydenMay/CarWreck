# Pickup collision investigation

Status: unresolved mandatory Phase 4 coverage failure. No shared runtime or validation code, thresholds, cabin protection, or damage resistance was changed.

## Automatic rig

The final manifest keeps automatic shell depths and measured cabin protection. The 1.99 by 4.70 m Body/Bumper envelope produces 0.658 m nose/tail sections and a 0.438 m side shell. The cabin collider ends at approximately local Z=0.88625 m; its lower face starts at the measured 1.34149 m beltline.

The front-side region centres are Z=1.128 m. A 0.50 m wide narrow impactor reaches rearward to Z=0.878 m, overlapping the legitimate cabin by about **8.25 mm**. Its 1.10 m height at the 0.923 m impact centre also overlaps the cabin vertically. After the first fender hit retracts the shell, the second identical launch contacts the fixed CabinBox. It does deepen, but its averaged contact is more than 60 mm from the retracted shell face. Both front-side targets fail the unchanged assertion in `CollisionWholeVehicleValidation.cs`.

This does not demonstrate misplaced cabin protection. Shrinking the cabin to clear the test would reduce legitimate passenger protection. The measured cab remains separate from the open bed.

[Automatic baseline report](phase4_automatic_baseline.txt). The final automatic-rig report is [phase4.txt](phase4.txt).

## Rejected data-only experiment

A supported `shell.frontDepth=0.60` override was tested on the preceding C geometry (114,694 imported deformable vertices; 228,330 triangles), before the planar-lens experiment. This moved the core front plane from 1.692 to 1.750 m and front-side target centre to Z=1.17633 m. The impactor then cleared the cabin by approximately 40.08 mm. The change also reduced maximum nose-box retraction from about 0.598 to 0.540 m; it was a real rig change, not merely a test adjustment.

Both front-side tests passed. However, front-left/right targets failed: swept second-touch positions were 2.080/2.069 m and correctly followed the retracted hitboxes, while averaged post-step PhysX contact points were 1.982/1.979 m. Those points failed the same 60 mm follows-contact assertion. All other sections, locality, saturation, 84/84 long-run hits and repair passed.

The candidate therefore **did not resolve mandatory coverage** and was rejected. The production manifest and definition restore zero/automatic shell values. No manual target overrides or thresholds were changed; automatic target centres moved with the region layout. [Candidate report](phase4_frontshell_candidate.txt).

## Permission boundary

The next justified scope is a shared first-contact validation investigation: distinguish swept first touch, post-step solver contact, and intentional protected-cabin contact. This evidence does not yet prove a runtime physics defect or justify weakening the tolerance. Any correction needs explicit user authorization, unchanged meaningful tolerances, and muscle/hatch regression evidence. The final vehicle is not accepted while this mandatory check fails.

The open-bed solid-core limitation is separate. The supplemental physical probe records actual contact above the visual liner; the geometry has not been filled or reshaped to conceal it.

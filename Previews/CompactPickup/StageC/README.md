# Compact Pickup — Checkpoint C

**Review package; NOT ACCEPTED.** Mandatory adaptive-contact coverage, part-fold criteria and damaged-lamp appearance still fail. Browser loading, impacts, 50-shot Chaos and repair checks pass. No merge or shared destruction-engine changes have been made.

[Full validation report](checkpoint-c-report.md) · [Measured reference/B/final proportions](proportion_comparison.md) · [Machine-readable results](Validation/validation_summary.json)

## Final appearance and references

![Reference versus final: front three-quarter, side, rear three-quarter, directly above](checkpoint_c_overview.png)

![Hood, recessed grille, wheels/clearance and open-bed details](checkpoint_c_details.png)

![Approved B versus final, identical cameras](approved_b_vs_final_overview.png)

Individual final renders: [front three-quarter](stage_c_front34.png), [side](stage_c_side.png), [rear three-quarter](stage_c_rear34.png), [top](stage_c_top.png), [front](stage_c_front.png), [rear](stage_c_rear.png), [nose from above](stage_c_nose_top.png), [bed](stage_c_bed.png), [clearance](stage_c_clearance.png), [roof color diagnostic](stage_c_roof_color.png).

## Actual Unity evidence

![Undamaged, single front hit, nine accumulated impacts and 50-impact Chaos Run](unity_destruction_overview.png)

![Independent Color A/Color B edits and paint on deformed metal](unity_paint_comparison.png)

- Remaining defects: [front close-up](Unity/front_15ms_no_overlay.png), [front suspension](Unity/front_suspension_close.png), [undamaged lamp depth](Unity/lamps_undamaged_oblique.png).
- Local deformation: [hood](Unity/hood_dent.png), [bed side](Unity/bed_side_dent.png), [tailgate](Unity/tailgate_dent.png), [liner](Unity/liner_dent.png).
- Extreme damage: [50-impact front](Unity/chaos_50_front.png), [50-impact bed](Unity/chaos_50_bed.png), [84-hit top](Unity/CompactPickup_66_P4_LongRun_84_Top.png).
- Physics overlays: [undamaged](Unity/CompactPickup_60_P4_Hitbox_Undamaged_FrontLeft.png), [25 repeated front hits](Unity/CompactPickup_62_P4_FrontCentre_x25_Hitbox.png), [84 hits](Unity/CompactPickup_64_P4_LongRun_84_FrontLeft.png).
- [Physical cargo resting above the liner](Unity/open_bed_collision_cargo.png), [green pigment under neutral diagnostic lighting](Unity/paint_neutral_lighting_diagnostic.png).
- WebGL: [browser evidence and limitations](Validation/webgl.md).

## Reports and retained approvals

[Blender QA](asset_qa.json) · [Import](Validation/import.txt) · [Integration](Validation/integration.txt) · [Adaptive hitboxes](Validation/phase4.txt) · [Overflow](Validation/overflow.txt) · [Chaos](Validation/chaos.txt) · [Switching](Validation/switching.txt) · [Collision investigation](Validation/collision-investigation.md) · [Independent review](Validation/independent-review.md) · [Regression integrity](Validation/regression_integrity.json) · [Image/source hashes](review_manifest.json)

Approved [Checkpoint A](../StageA/README.md) and [Checkpoint B](../StageB/README.md) remain available. Their protected source and images are unchanged. This pending C package and rejected experiment evidence remain available for review. Cleanup after all approvals removes only unapproved drafts; approved checkpoint evidence is retained through merge.

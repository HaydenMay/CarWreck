# Independent review and disposition

Reviewed commit be389ed against main 2a9ca8b on 2026-10-14, read-only.

The reviewer confirmed scope isolation, preserved source identities, explicit coordinate
handling, exact repair/repeatability, unscaled rack reuse and clearly exposed mounting
limitations. No critical issues were found.

## Material finding fixed

Pairwise normal spread alone could accept support triangles that invert together while
their anchors remain in place. The new check transports each rest surface normal with
the fitted accessory rotation and compares it with the current triangle normal. It is
not a fixed world-up test. Chassis-fixed mounts also retain explicit invalid status when
the orientation check fails.

A four-triangle reflection fixture reproduced the original "valid" misclassification
with effectively zero positional residual. After the fix it is invalid with about
180 degrees of orientation error. A coherent rigid rotation beyond 90 degrees remains
valid. The numerical suite passed all nine tests after this regression fix.

## Report precision

The reviewer noted three table differences of a few micrometres between the text and
current results.json. The report table and repeat-build signature were refreshed directly
from the final JSON during the same evidence regeneration, avoiding outdated measurements.

## Scope exclusions retained

Unity binding/cache implementation, runtime performance, collision integration, force
transfer, breakage, multiplayer and player builds remain untested as the user requested.
Production compliance, full-body intersection clearance, real structural load paths and
a general-purpose cache lifecycle are not established by this fixed Blender experiment.
These are documented limits, not inferred successes.

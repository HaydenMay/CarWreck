# Modular vehicle attachment proof of concept

**Completed Blender-only prototype.** Three simple accessories demonstrate semantic
sockets, attachment-owned feet, explicit adapters and cached surface following.
No Unity integration, physics behavior or main merge is included.

Start with the [hood scoop comparison](previews/hood_scoop_comparison.png):
the moderate case follows 88.4 mm of translation and 9.35 degrees of rotation; the severe
fold exposes a rigid-footprint failure. Yellow anchors remain visibly tracked beneath
the scoop through the labelled X-ray overlay.

- [Validation report and seven mission answers](validation-report.md)
- [Roof rack comparison / both vehicles](previews/roof_rack_comparison.png)
- [Push bar mounting and failure](previews/push_bar_comparison.png)
- [All three installed on the undamaged hatchback](previews/01_hatch_installed.png)
- [Claude integration and future vehicle requirements](integration-notes.md)
- [Approved architecture](architecture-proposal.md)
- [Actual generated mounting metadata](metadata.json)
- [Measured results](evidence/results.json) and [acceptance checks](evidence/acceptance.json)

Open `demonstration.blend` in Blender 5.2 and select numbered scenes 01-10.
Each scene has its own camera; use camera view for the matching render.
The three `Asset_*` scenes contain only the accessory models.
`models/` also contains a standalone .blend and FBX for each accessory.

The roof-foot gap, severe scoop separation and push-bar support disagreement are
**demonstrated limitations**. They require future mounting behavior or detachment;
retaining disconnected parts is not approved final gameplay behavior.

## Reproduce

From the repository root (requires the verified Git LFS vehicle files):
```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Prototypes/Attachments/scripts/test_mount_math.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Prototypes/Attachments/scripts/build_demo.py
# Run build_demo.py a second time to validate the deterministic signature.
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Prototypes/Attachments/scripts/build_demo.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup Prototypes/Attachments/demonstration.blend --python-exit-code 1 --python Prototypes/Attachments/scripts/render_demo.py
python Prototypes/Attachments/scripts/compose_previews.py
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Prototypes/Attachments/scripts/validate_demo.py -- --renders
```

The compositor needs Pillow and Windows Segoe UI fonts. The scripts only generate files
under this prototype folder and inspect the production vehicles; no C# is executed.
The original Blender sources must match their verified SHA256 identities. A changed
vehicle intentionally requires a reviewed rebind rather than silently reusing old anchors.

Baseline: main `2a9ca8bebb296557ed75513c9ad892c8f5153d92`.
Branch: `feature/attachment-prototype`. The interactive Blender session and Claude's
vehicle-engine-wip checkout are not used for implementation.

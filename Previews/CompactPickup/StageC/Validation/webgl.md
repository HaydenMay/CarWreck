# Final WebGL browser validation

Final `collision-webgl` build: **Succeeded, 21.2 MB, zero build errors, 66 seconds**. [Build report](webgl_build.txt), [observed browser results](webgl_validation.json).

The rebuilt player was loaded from a local static server in the Codex in-app Chromium browser. Muscle car and pickup both rendered. Pickup selection showed 52 deforming parts, 114,694 vertices, 14 colliders and zero validation warnings. Green/cream two-tone paint and the open bed were visible, with the same blue reflection cast as the editor.

![Final player loaded with the pickup](../Unity/webgl_pickup_loaded.jpg)

A manual front shot at 20 m/s, 2500 kg and a Random Launch visibly deformed the vehicle. Paint remained on the dented panels. Independent color-property edits were tested in the editor; this browser UI has no paint editor.

![Front impact](../Unity/webgl_front_impact.jpg)

![Random Launch](../Unity/webgl_random_launch.jpg)

After Reset All and re-entering seed **2026**, the 50-impact Chaos Run used 20 m/s, 2500 kg and random block width. Its completion status displayed **50 launched · 50 hit · 0 missed**. Repair Car then restored the original visible geometry. These browser functional results do not waive the final model's known lamp/fold failures or prove artifact-free extreme damage.

![Browser Chaos completion](../Unity/webgl_chaos_50.jpg)

![Repair restored the pickup](../Unity/webgl_repaired.jpg)

The application tab's console returned **zero errors and zero warnings** after the final run. The automation host separately emitted telemetry network errors; those did not originate in the local Unity player and were not counted as application messages.

An earlier build/session had a blank 3D viewport for both muscle car and pickup. Rebuilding and reloading resolved the observed problem; its cause is not established. No shared rendering or runtime code was changed. The [initial screenshot](../Unity/webgl_pickup_blank_viewport.jpg) is retained as labelled investigation evidence, not the final browser result.

No mobile, other-browser, standalone-player, memory or frame-budget certification was performed. The temporary browser tab and server are closed after evidence collection.

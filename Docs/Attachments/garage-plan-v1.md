# Home screen and Garage: plan (owner direction, approved to start)

Goal: replace the single match menu with a home screen where the player chooses **Play** or **Garage**, as in Rocket League-style games. Today's menu is full (title through key hints fit in 640 units), the M2 loadout panel is a stopgap behind a 17 px button on a phone, and car, paint, loadout and match setup are all mixed on one page.

## Decision summary

- Three screens in the existing left panel, all built in `DerbyUI` like the rest of the interface:
  - **Home:** PLAY, GARAGE, a one-line summary of the current car, colour and loadout cost.
  - **Play:** game mode, opponents, START, BACK. START is blocked (and says so) while the loadout has problems.
  - **Garage:** two tabs, **VEHICLE** (car and paint) and **LOADOUT** (the five slots, capacity, problems), BACK to Home.
- Touch targets are sized for a phone held sideways: the panel scales by `height / 640`, so every tappable control is at least about 76 units tall (about 44 px at 375 px screen height).
- No change to the game rules, the attachment data layer, the mounting code, saved loadouts or the match flow. `DerbyGame` calls the UI through the same methods (`ShowMenu`, `OnMatchStarted`); the menu opens on Home.

## Milestones

| | Scope | Not included |
|---|---|---|
| **G1 (this branch)** | Home, Play and Garage pages and navigation; existing choices moved into them; touch-sized controls; play-mode UI check updated; looked at in a local WebGL build | Orbit preview, item info cards, presets, settings, stats |
| G2 | Garage polish: orbit and zoom on the preview car, an item detail card (name, cost, rarity, short text), capacity bar, equip/unequip clarity, named presets | New items or art |
| G3 | Room for what comes next: settings, stats or unlocks entries on Home; transitions; gamepad / keyboard navigation | Economy |

## Acceptance for G1

- Opens on Home; PLAY leads to match setup and START starts a match as before; GARAGE leads to the two tabs; BACK works on every page; pause, results and menu round trips come back to Home.
- A saved loadout with a retired item is flagged in the Garage, blocks START with a reason, and CLEAR PROBLEMS empties only the bad slot (as in M2).
- Nothing about physics, scoring or AI changes; the M2 parity and lifecycle validation and the existing Derby and Showdown suites still pass.
- Checked in a local WebGL build at desktop and phone-landscape size. Frame time and touch on a real phone are still the owner's test.

## G1 status

Implemented on `feature/garage-g1`: `DerbyUI` now builds Home, Play and Garage (Vehicle and Loadout tabs) in the existing left panel; the old LOADOUT button and separate loadout panel are gone. Controls are 70-120 units tall (about 40 px or more at 375 px screen height). Checks (all editor or local-browser, none on a phone):
- Play-mode UI check rewritten for the new flow: 27 of 27 (Home first, broken build routes START to the Garage loadout tab, clear problems, cycling, per-vehicle builds, paint, mode and opponent choices, START, quit returns to Home).
- `AttachmentParityValidation` 25/25, `DerbyValidation` 80/80, `ShowdownValidation` 45/45 on this branch.
- Local WebGL build (22.1 MB, 0 errors) looked at in a desktop browser at phone-landscape size: Home, Garage (both tabs) and Play fit the screen and are readable.
- Not verified: touch on a real phone, frame time, and the preview car's visibility next to the panel on narrow phones.

## G2 status: orbit camera done (item cards and presets still to do)

Branch `feature/garage-g2`. In the menu view (`ChaseCamera`) the player now has "grab and turn" orbit: a drag on the empty part of the screen turns the view (a transparent `MenuOrbitPad` sits behind the panel), a drag down looks from higher up (clamped 6-65 degrees), the mouse wheel and a two-finger pinch zoom (distance x0.6 to x1.5). Home and Play keep the slow showcase spin; the Garage holds the view still, entering the Vehicle tab resets to the three-quarter view, and editing a loadout slot swings to that slot (front, rear, left, right, roof).
- Checks: play-mode UI check 37/37 (including drag direction and size, clamps, wheel, slot swing, spin on Home and off in the Garage); `DerbyValidation` 80/80; local WebGL build looked at in a desktop browser: dragging right brought the car's front round to the right, the wheel zoomed in.
- Not verified: pinch and touch drag on a real phone; the driving arena's menu uses the same camera with the original behaviour (spin on) but was not re-run.
- Still to do in G2: item detail card, capacity bar, equip/unequip clarity, named presets.

## Risks

- The menu code is the largest piece of `DerbyUI`; a rewrite can regress something small (mode highlight, difficulty text). Mitigation: keep each widget's behaviour and move it, extend the play-mode UI check.
- Camera focus in the menu (`SetMenuFocus`) is shared by all three screens; the preview must stay visible next to the panel on a phone.
- The panel is 650 units wide; on a narrow phone the preview is the remaining strip. G2 may need a layout that gives the car more room.

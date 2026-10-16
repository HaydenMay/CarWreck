# Flag for Opus: dent width is only a fraction of the car's width in car-vs-car hits

Reported by the owner from phone playtests (moving-target scenarios): in car-vs-car hits the damage appears as a narrow patch in the middle of the hood/front,
not across the full width of the impacted front. Wall hits damage the full width. Not fixed here (collision engine is Opus's); documented with data.

## Evidence (editor simulation, `CarWreck.Driving.EditorTools.ImpactWidthProbe.Run`, ~72 km/h, fronts meeting head-on or front into the crash wall)

| Hit | Car | Dent patch (width x height) | Car body width | Share of width | Contacts |
|---|---|---|---|---|---|
| muscle front into crash wall | muscle | 1.93 x 0.50 m | 2.05 m | 94% | 12 |
| hatch front into crash wall | hatch | 1.79 x 0.62 m | 1.85 m | 97% | 12 |
| pickup front into crash wall | pickup | 1.99 x 0.71 m | 1.99 m | 100% | 12 |
| muscle vs muscle head-on | player / target | 0.49 / 0.68 m | 2.05 m | 24% / 33% | 28 |
| hatch vs pickup head-on | hatch / pickup | 0.44 / 0.60 m | 1.85 / 1.99 m | 24% / 30% | 20 |
| pickup vs hatch head-on | pickup / hatch | 0.60 / 0.77 m | 1.99 / 1.85 m | 30% / 42% | 24 |

The full front faces overlap in a head-on hit (20-28 contact points reported across the front), but the footprint covers only about a quarter to a third of the width. Strength and depth are normal.

## Likely cause (code reading, not yet proven)

`CollisionDamageReceiver.Accumulate` takes the impactor's shape from the **first** reported contact only: `if (!p.box.valid && c.collider is BoxCollider bc && geometryAwareFootprint)` and uses that one
`BoxCollider` as the dent footprint. Against a wall or the lab sled that collider is one wide box, so the dent is full width. Against another vehicle the collider is one of that vehicle's
physics-shell regions (the shell is split into FrontLeft / FrontCentre / FrontRight thirds, each about 0.6-0.7 m wide), so the footprint is a single third even when all three touch in the same event.
The Collision Lab never shows it because its impactor is one full-width box.

## Suggested checks (not implemented)

* Confirm by logging which collider each contact belongs to in one head-on event (expect several FrontLeft/Centre/Right contacts, one box used).
* Options for Opus to weigh: merge the boxes touching in the same event into one footprint (union/bounds), or use the other vehicle's whole front face when it is a vehicle. Either changes shared footprint code, so it needs the lab regression run.
* Side hits probably have the same limitation (side shell regions are also thirds).

No change was made to `Assets/DamageLab`.

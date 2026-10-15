using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Random Launch and Chaos Run. A random shot comes from any horizontal bearing around the car, aimed through the
    /// body centre shifted sideways by a random fraction of the body's apparent width. The block is swept from well
    /// outside the car against the car's CURRENT colliders (the damage-aware hitbox) and parked <see cref="startGap"/>
    /// before its first touch, so it never starts inside the car and always aims at the car as it is now.
    /// A Chaos Run fires a seeded sequence, one block at a time: each shot is finished (contact processed, block
    /// separated or at rest, hitbox update applied, block reset) before the next launch. The car is never repaired.
    /// </summary>
    public partial class CollisionLabController
    {
        [Header("Random testing")]
        public int randomSeed = 2026;
        public RandomImpactOptions randomOptions = new RandomImpactOptions();
        [Tooltip("Chaos Run sizes offered by the count buttons.")]
        public int[] chaosCounts = { 10, 25, 50 };
        public int chaosCountIndex;
        [Tooltip("Physics steps without new contact events (and with the block separated or slow) before a shot counts as finished.")]
        public int chaosQuietSteps = 15;

        [Header("Random testing UI")]
        public Button randomLaunchButton;
        public Button chaosButton;
        public Button stopButton;
        public Button[] chaosCountButtons;
        public Button newSeedButton;
        public Text seedText;
        public InputField seedField;
        public Toggle varyWidthToggle;

        /// <summary>Outcome of one shot of a Chaos Run (or a single Random Launch).</summary>
        public enum ShotOutcome { Impact, ContactOnly, Miss }

        public struct ShotRecord
        {
            public RandomShot shot;
            public bool predictedContact;
            public Vector3 aimPoint;          // car space: where the swept block first touches the current hitbox
            public ShotOutcome outcome;
            public string region, carCollider;
            public Vector3 contactPoint;      // car space (footprint centre when the impact was applied)
            public float strength, deformMs, hitboxMs, crush;
            public int steps;
        }

        /// <summary>Where and how the last random block was aimed (null after a manual launch).</summary>
        public ShotRecord? LastRandom { get; private set; }
        public int NextShot { get; private set; }

        // ------------------------------------------------------------------ chaos state
        enum ChaosState { Idle, Flying }
        ChaosState chaosState;
        int chaosTotal, chaosDone, chaosSteps, chaosQuiet, chaosTimeout;
        bool chaosStopRequested, shotApplied, shotContact;
        CollisionDamageReceiver.Report shotReport;
        ShotRecord current;
        public readonly List<ShotRecord> ChaosRecords = new List<ShotRecord>();
        public bool ChaosRunning => chaosState != ChaosState.Idle;
        public int ChaosTotal => chaosTotal;
        public int ChaosAttempted => chaosDone;
        public int ChaosImpacts { get; private set; }
        public int ChaosContactOnly { get; private set; }
        public int ChaosMisses { get; private set; }
        public float ChaosDeformMsTotal { get; private set; }
        readonly Dictionary<string, int> chaosRegions = new Dictionary<string, int>();
        readonly HashSet<string> chaosColliders = new HashSet<string>();
        public IReadOnlyDictionary<string, int> ChaosRegions => chaosRegions;
        public int ChaosCollidersStruck => chaosColliders.Count;

        // ------------------------------------------------------------------ public controls

        /// <summary>One random block from the current seed's next shot, with the selected speed and mass.</summary>
        public void RandomLaunch()
        {
            if (ChaosRunning) { SetStatus("Chaos run in progress: press STOP first"); return; }
            var s = RandomImpact.Plan(randomSeed, NextShot++, randomOptions, narrowImpactor);
            current = PlaceRandom(s);
            LastRandom = current;
            FireParked(speed * s.speedScale);
            SetStatus($"Random launch: {s.approachDeg:0}°, {(s.narrow ? "narrow block" : "wide sled")}, {speed * s.speedScale:0.0} m/s");
            RefreshRandomUI();
            RefreshDiagnostics();
        }

        /// <summary>Start a seeded Chaos Run of <paramref name="count"/> shots (shots 0..count-1 of the current seed).</summary>
        public void StartChaos(int count)
        {
            if (ChaosRunning) return;
            if (speed < 0.5f) { SetStatus("Chaos run needs an impact speed above 0"); return; }
            chaosTotal = Mathf.Max(1, count);
            chaosDone = 0; ChaosImpacts = 0; ChaosContactOnly = 0; ChaosMisses = 0; ChaosDeformMsTotal = 0f;
            chaosRegions.Clear(); chaosColliders.Clear(); ChaosRecords.Clear();
            chaosStopRequested = false;
            NextShot = 0;
            if (receiver != null) receiver.ImpactRegistered += OnChaosEvent;
            BeginChaosShot();
        }

        public void StartChaosSelected() => StartChaos(chaosCounts[Mathf.Clamp(chaosCountIndex, 0, chaosCounts.Length - 1)]);

        /// <summary>STOP: no further launches. A block already in flight finishes its impact first (so its damage and
        /// hitbox update complete); <paramref name="immediate"/> ends at once (vehicle switch, repair, reset).
        /// The car is never repaired or reset by stopping.</summary>
        public void StopChaos(bool immediate = false)
        {
            if (!ChaosRunning) return;
            if (immediate) { EndChaos("Chaos run stopped"); return; }
            chaosStopRequested = true;
            SetStatus($"Stopping after the current impact ({chaosDone + 1}/{chaosTotal})…");
        }

        public void NewSeed()
        {
            if (ChaosRunning) return;
            randomSeed = RandomImpact.NewSeed();
            NextShot = 0;
            RefreshRandomUI();
        }

        /// <summary>Forget the last random launch and chaos-run summary (a new vehicle or a reset experiment starts clean;
        /// the seed and the next shot index are kept).</summary>
        void ClearRandomDiagnostics()
        {
            LastRandom = null;
            chaosTotal = chaosDone = 0;
            ChaosImpacts = ChaosContactOnly = ChaosMisses = 0; ChaosDeformMsTotal = 0f;
            chaosRegions.Clear(); chaosColliders.Clear(); ChaosRecords.Clear();
            RefreshRandomUI();
        }

        /// <summary>Runtime sanity after a run: non-finite vertices in the deformed meshes and invalid hitbox boxes.</summary>
        public string SanitySummary()
        {
            int bad = 0, verts = 0;
            if (model != null)
                foreach (var p in model.Parts)
                {
                    if (p.RuntimeMesh == null) continue;
                    foreach (var v in p.RuntimeMesh.vertices)
                    {
                        verts++;
                        if (float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)) bad++;
                    }
                }
            int badBoxes = 0, boxes = 0;
            if (hitbox != null)
                foreach (var r in hitbox.regions)
                {
                    boxes++;
                    Vector3 c = r.box.center, s = r.box.size;
                    if (float.IsNaN(c.x + c.y + c.z + s.x + s.y + s.z) || s.x <= 0.01f || s.y <= 0.01f || s.z <= 0.01f) badBoxes++;
                }
            return $"non-finite vertices {bad} of {verts:N0}, invalid hitbox boxes {badBoxes} of {boxes}";
        }

        /// <summary>Whether the parked block overlaps any of the car's colliders (validation).</summary>
        public bool BlockOverlapsCar() => SledOverlapsCar();

        public void SetSeed(int seed) { if (ChaosRunning) return; randomSeed = seed; NextShot = 0; RefreshRandomUI(); }
        public void SetChaosCountIndex(int i) { chaosCountIndex = Mathf.Clamp(i, 0, chaosCounts.Length - 1); RefreshRandomUI(); }
        public void SetVaryWidth(int on) { randomOptions.varyWidth = on != 0; if (varyWidthToggle != null) varyWidthToggle.SetIsOnWithoutNotify(on != 0); }

        // ------------------------------------------------------------------ placement

        /// <summary>Park the block for a random shot against the car's current collision geometry (no launch).</summary>
        public ShotRecord PlaceRandom(RandomShot s)
        {
            if (hitbox != null) hitbox.ApplyPending();
            if (narrowImpactor != s.narrow) { narrowImpactor = s.narrow; RefreshDirectionButtons(); }
            ApplySledWidth();
            Physics.SyncTransforms();
            Bounds b = Geometry.body;
            Vector3 d = s.Direction, p = Vector3.Cross(Vector3.up, d).normalized;   // p: the impactor's right
            float halfAcross = Mathf.Abs(p.x) * b.extents.x + Mathf.Abs(p.z) * b.extents.z;
            float halfAlong = Mathf.Abs(d.x) * b.extents.x + Mathf.Abs(d.z) * b.extents.z;
            Vector3 linePoint = new Vector3(b.center.x, 0f, b.center.z) + p * (s.lateral * halfAcross);

            var bc = sled.GetComponent<BoxCollider>();
            Vector3 half = Vector3.Scale(bc.size, sled.transform.lossyScale) * 0.5f;
            float far = b.extents.magnitude + half.z + 2f + startGap;   // well outside the car whatever the bearing

            Vector3 fwd = car.transform.forward; fwd.y = 0f;
            Quaternion yaw = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : carStartRot;
            Vector3 basePos = new Vector3(car.transform.position.x, carStartPos.y, car.transform.position.z);
            Vector3 dirW = yaw * d; dirW.y = 0f; dirW.Normalize();
            Quaternion rot = Quaternion.LookRotation(dirW, Vector3.up);
            Vector3 start = basePos + yaw * (linePoint - d * far);
            start.y = carStartPos.y + sledHeight;
            Vector3 centreOffset = rot * Vector3.Scale(bc.center, sled.transform.lossyScale);

            // Sweep the block along its path against the car's current colliders.
            float touch = float.MaxValue;
            foreach (var h in Physics.BoxCastAll(start + centreOffset, half, dirW, rot, 2f * far, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance > 0f && h.collider.attachedRigidbody == car) touch = Mathf.Min(touch, h.distance);
            var rec = new ShotRecord { shot = s, predictedContact = touch < float.MaxValue };
            float travel;
            if (rec.predictedContact)
            {
                travel = Mathf.Max(0f, touch - startGap);
                rec.aimPoint = car.transform.InverseTransformPoint(start + dirW * (touch + half.z) + centreOffset);
            }
            else
            {
                // No contact predicted (the line passes the car's current shape): park as if the body were a box, let it fly.
                travel = Mathf.Max(0f, far - halfAlong - half.z - startGap);
                rec.aimPoint = linePoint - d * halfAlong;
            }
            Vector3 pos = start + dirW * travel;
            sled.isKinematic = true;
            sled.position = pos; sled.rotation = rot;
            sled.transform.SetPositionAndRotation(pos, rot);
            // Never start inside the car: back off if the parked block overlaps it (e.g. a free car that has moved).
            for (int k = 0; k < 20 && SledOverlapsCar(); k++)
            {
                pos -= dirW * 0.25f;
                sled.position = pos; sled.transform.position = pos;
            }
            Physics.SyncTransforms();
            if (receiver != null) receiver.ClearCooldowns();
            launched = false;
            chaosTimeout = Mathf.CeilToInt((travel + 2f * halfAlong + 2f * half.z + 2f) / Mathf.Max(0.5f, speed * s.speedScale) / Mathf.Max(1e-3f, Time.fixedDeltaTime)) + 150;
            return rec;
        }

        void FireParked(float v)
        {
            sled.mass = mass;
            sled.isKinematic = false;
            sled.linearVelocity = sled.transform.forward * v;
            sled.angularVelocity = Vector3.zero;
            launched = true;
            launchTime = Time.time;
        }

        // ------------------------------------------------------------------ chaos run

        void BeginChaosShot()
        {
            var s = RandomImpact.Plan(randomSeed, chaosDone, randomOptions, narrowImpactor);
            NextShot = chaosDone + 1;
            current = PlaceRandom(s);
            LastRandom = current;
            shotApplied = shotContact = false;
            chaosSteps = 0; chaosQuiet = 0;
            chaosState = ChaosState.Flying;
            FireParked(speed * s.speedScale);
            SetStatus($"Chaos run {chaosDone + 1}/{chaosTotal}: {s.approachDeg:0}°, {(s.narrow ? "narrow" : "wide")}");
            RefreshRandomUI();
        }

        void OnChaosEvent(CollisionDamageReceiver.Report r)
        {
            if (chaosState != ChaosState.Flying) return;
            chaosQuiet = 0;
            if (r.applied && !shotApplied) { shotApplied = true; shotReport = r; }
            else if (!shotApplied && r.contacts > 0) { shotContact = true; shotReport = r; }
        }

        /// <summary>Advance the Chaos Run by one physics step. Called from FixedUpdate in play mode and by the edit-mode
        /// validation after each Physics.Simulate (after the receiver and the hitbox safe point).</summary>
        public void ChaosTick()
        {
            if (chaosState != ChaosState.Flying) return;
            chaosSteps++; chaosQuiet++;
            Vector3 rel = sled.linearVelocity - car.linearVelocity;
            bool finished;
            if (shotApplied || shotContact)
            {
                // Collision complete: no new contact for a while, and the block is slow or moving away from the car.
                Vector3 toCar = car.worldCenterOfMass - sled.worldCenterOfMass;
                bool separatingOrSlow = rel.magnitude < settleSpeed || Vector3.Dot(rel, toCar) < 0f;
                finished = chaosQuiet >= chaosQuietSteps && separatingOrSlow;
            }
            else
            {
                // Miss: past the car, stopped short, or out of time.
                Vector3 toCar = car.worldCenterOfMass - sled.worldCenterOfMass; toCar.y = 0f;
                bool past = Vector3.Dot(toCar, sled.transform.forward) < -(Geometry.body.extents.magnitude + 1f);
                bool stopped = chaosSteps > 25 && rel.magnitude < 0.05f;
                finished = past || stopped;
            }
            if (!finished && chaosSteps < chaosTimeout) return;
            FinishChaosShot();
        }

        void FinishChaosShot()
        {
            var rec = current;
            rec.steps = chaosSteps;
            rec.outcome = shotApplied ? ShotOutcome.Impact : shotContact ? ShotOutcome.ContactOnly : ShotOutcome.Miss;
            if (shotApplied || shotContact)
            {
                rec.region = shotReport.region; rec.carCollider = shotReport.carCollider;
                rec.contactPoint = shotReport.localPoint; rec.strength = shotReport.strength; rec.deformMs = shotReport.deformMilliseconds;
                rec.crush = shotReport.footprint.valid ? shotReport.footprint.travel : 0f;
            }
            // Hitbox update for this impact (the block has stopped pressing), then reset the block.
            int updates = hitbox != null ? hitbox.UpdateCount : 0;
            if (hitbox != null) hitbox.ApplyPending();
            if (hitbox != null && hitbox.UpdateCount != updates) rec.hitboxMs = hitbox.LastUpdateMilliseconds;
            switch (rec.outcome)
            {
                case ShotOutcome.Impact:
                    ChaosImpacts++; ChaosDeformMsTotal += rec.deformMs;
                    chaosRegions.TryGetValue(rec.region ?? "?", out int n); chaosRegions[rec.region ?? "?"] = n + 1;
                    if (!string.IsNullOrEmpty(rec.carCollider)) chaosColliders.Add(rec.carCollider);
                    break;
                case ShotOutcome.ContactOnly: ChaosContactOnly++; break;
                default: ChaosMisses++; break;
            }
            ChaosRecords.Add(rec);
            Debug.Log($"[CollisionLab] Chaos shot {chaosDone + 1}/{chaosTotal} (seed {rec.shot.seed}, shot {rec.shot.shot}): approach {rec.shot.approachDeg:0.0}°, offset {rec.shot.lateral:+0.00;-0.00}, {(rec.shot.narrow ? "narrow" : "wide")} -> {rec.outcome}" +
                      (rec.outcome == ShotOutcome.Miss ? "" : $" at {rec.region} [{rec.carCollider}] strength {rec.strength * 100f:0}, deform {rec.deformMs:0.0} ms, hitbox {rec.hitboxMs:0.0} ms") + $", impacts on car {model.TotalImpactCount} ({model.ImpactCount} stored, {model.MergedImpactCount} merged)");
            LastRandom = rec;
            chaosDone++;
            ResetBlock();
            chaosState = ChaosState.Idle;
            if (chaosStopRequested || chaosDone >= chaosTotal) EndChaos(chaosDone >= chaosTotal ? "Chaos run complete" : "Chaos run stopped");
            else { RefreshDiagnostics(); BeginChaosShot(); }
        }

        void EndChaos(string status)
        {
            if (receiver != null) receiver.ImpactRegistered -= OnChaosEvent;
            if (chaosState == ChaosState.Flying) { if (hitbox != null) hitbox.ApplyPending(); ResetBlock(); }
            chaosState = ChaosState.Idle;
            chaosStopRequested = false;
            SetStatus($"{status}: {chaosDone} launched · {ChaosImpacts} hit · {ChaosContactOnly + ChaosMisses} missed");
            Debug.Log($"[CollisionLab] {status}: seed {randomSeed}, {chaosDone}/{chaosTotal} launched, {ChaosImpacts} impacts, {ChaosContactOnly} contact only, {ChaosMisses} missed, " +
                      $"{chaosRegions.Count} regions / {chaosColliders.Count} hitbox boxes struck, deformation {(ChaosImpacts > 0 ? ChaosDeformMsTotal / ChaosImpacts : 0f):0.0} ms avg, " +
                      $"impacts on car {model.TotalImpactCount} ({model.ImpactCount} stored, {model.MergedImpactCount} merged); {SanitySummary()}");
            RefreshRandomUI();
            RefreshDiagnostics();
        }

        // ------------------------------------------------------------------ UI

        void BindRandomUI()
        {
            if (randomLaunchButton != null) randomLaunchButton.onClick.AddListener(RandomLaunch);
            if (chaosButton != null) chaosButton.onClick.AddListener(StartChaosSelected);
            if (stopButton != null) stopButton.onClick.AddListener(() => StopChaos());
            if (newSeedButton != null) newSeedButton.onClick.AddListener(NewSeed);
            if (seedField != null)
                seedField.onEndEdit.AddListener(t => { if (int.TryParse(t, out int s) && s > 0) SetSeed(Mathf.Min(s, 999999)); else RefreshRandomUI(); });
            if (chaosCountButtons != null)
                for (int i = 0; i < chaosCountButtons.Length; i++)
                {
                    int k = i;
                    if (chaosCountButtons[i] != null) chaosCountButtons[i].onClick.AddListener(() => SetChaosCountIndex(k));
                }
            if (varyWidthToggle != null)
            {
                varyWidthToggle.SetIsOnWithoutNotify(randomOptions.varyWidth);
                varyWidthToggle.onValueChanged.AddListener(on => randomOptions.varyWidth = on);
            }
            RefreshRandomUI();
        }

        void RefreshRandomUI()
        {
            if (seedText != null)
                seedText.text = ChaosRunning ? $"Chaos run impacts · shot {Mathf.Min(chaosDone + 1, chaosTotal)} of {chaosTotal}" : $"Chaos run impacts · seed {randomSeed}, next shot {NextShot}";
            if (seedField != null && !seedField.isFocused) seedField.SetTextWithoutNotify(randomSeed.ToString());
            if (seedField != null) seedField.interactable = !ChaosRunning;
            if (chaosCountButtons != null)
                for (int i = 0; i < chaosCountButtons.Length; i++)
                    if (chaosCountButtons[i] != null && chaosCountButtons[i].targetGraphic != null)
                        chaosCountButtons[i].targetGraphic.color = i == chaosCountIndex ? selectedColor : unselectedColor;
            if (chaosButton != null && chaosButton.targetGraphic != null) chaosButton.targetGraphic.color = ChaosRunning ? selectedColor : unselectedColor;
        }

        /// <summary>Random-launch and chaos lines for the diagnostics panel ("" for a manual launch with no chaos run).</summary>
        string RandomDiagnostics()
        {
            var sb = new StringBuilder();
            if (LastRandom.HasValue)
            {
                var r = LastRandom.Value; var s = r.shot;
                sb.Append($"Random launch        seed {s.seed} · shot {s.shot} · approach {s.approachDeg:0.0}°\n");
                sb.Append($"  offset {s.lateral:+0.00;-0.00} of half-width · {(s.narrow ? "narrow block" : "wide sled")} · {speed * s.speedScale:0.0} m/s\n");
                sb.Append($"  aim {(r.predictedContact ? "first touch" : "no contact predicted")} {r.aimPoint.ToString("F2")}\n");
            }
            if (ChaosRunning || chaosDone > 0)
            {
                sb.Append($"Chaos run            {chaosDone}/{chaosTotal}{(ChaosRunning ? " (running)" : "")} · impacts {ChaosImpacts} · contact only {ChaosContactOnly} · missed {ChaosMisses}\n");
                sb.Append($"  {chaosRegions.Count} regions · {chaosColliders.Count} hitbox boxes struck · deform {(ChaosImpacts > 0 ? ChaosDeformMsTotal / ChaosImpacts : 0f):0} ms avg\n");
            }
            return sb.ToString();
        }
    }
}

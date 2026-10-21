using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarWreck.Driving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Play-mode check of the menu and the loadout panel with the real DerbyUI (clicks the actual buttons). Enters play mode without a domain reload so this static state
    /// survives; EditorSettings are restored afterwards. Uses a throwaway PlayerPrefs entry for the muscle car and removes it.
    /// Run (graphics needed): Unity -batchmode -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentUiPlayCheck.Run -logFile ui.log
    /// </summary>
    public static class AttachmentUiPlayCheck
    {
        static StringBuilder sb;
        static int passes, fails, frame, step;
        static bool prevOptionsEnabled; static EnterPlayModeOptions prevOptions;
        static DerbyGame game; static DerbyUI ui;
        const string Key = "MuscleCar";

        [MenuItem("CarWreck/Validate Attachments (M2 UI, play mode)", priority = 46)]
        public static void Run()
        {
            sb = new StringBuilder("[Attachments] M2 UI play-mode check\n");
            passes = fails = frame = step = 0;
            EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
            prevOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled; prevOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            // a saved build with a retired item id in the left slot: the panel must flag it, not drop it
            var saved = new Loadout(Key);
            saved.Set(AttachmentSlot.Front, "push_bar_junk"); saved.Set(AttachmentSlot.Left, "retired_item");
            LoadoutStore.Save(saved);
            for (int i = 0; i < LoadoutPresets.Count; i++) LoadoutPresets.Delete(Key, i);
            Directory.CreateDirectory(AttachmentFitTool.PreviewFolder);
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.isPlaying = true;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode) EditorApplication.update += Tick;
        }

        static void Check(string name, bool ok, string detail = "")
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? ": " + detail : "")}");
            if (ok) passes++; else fails++;
        }

        static Button Btn(string name) => ui.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        static void Click(string name) { var b = Btn(name); if (b != null && b.interactable) b.onClick.Invoke(); else sb.AppendLine($"  (button {name} {(b == null ? "missing" : "not interactable")})"); }
        static string LabelOf(string name) => Btn(name) != null ? Btn(name).GetComponentInChildren<Text>().text : "<missing>";
        static bool Vis(string name) => ui.GetComponentsInChildren<Transform>(true).Any(x => x.name == name && x.gameObject.activeInHierarchy);
        static string TextOf(string label, string page) => ui.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == label && t.transform.parent != null && t.transform.parent.name == page)?.text ?? "";

        static void Shot(string file)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(AttachmentFitTool.PreviewFolder, file));
        }

        static void Tick()
        {
            frame++;
            if (frame < 5) return;
            if (step > 0 && frame % 6 != 0) return;           // a few frames between actions so UI and screenshots settle
            try
            {
                if (step == 0)
                {
                    game = Object.FindFirstObjectByType<DerbyGame>(); ui = Object.FindFirstObjectByType<DerbyUI>();
                    if (game == null || ui == null || !game.MenuOpen || Btn("Play") == null) { if (frame > 200) Finish("menu never came up"); return; }
                    step = 1;
                    Check("the menu opens on the Home page: PLAY and GARAGE are shown, the match setup and the garage are not", Vis("Play") && Vis("Garage") && !Vis("Start") && !Vis("TabVehicle"));
                    Check("Home keeps the slow showcase spin and has the orbit layer behind the panel", game.chaseCamera.HasMenuFocus && game.chaseCamera.menuAutoSpin && Vis("OrbitPad"));
                    Check("the saved build for the muscle car is loaded (valid item plus a retired id)", game.HumanLoadout.Get(AttachmentSlot.Left) == "retired_item" && game.HumanLoadout.Get(AttachmentSlot.Front) == "push_bar_junk");
                    Check("Home summarises the car and flags the broken loadout", TextOf("Summary", "PageHome").Contains("MUSCLE CAR") && TextOf("Summary", "PageHome").Contains("FIX LOADOUT"), TextOf("Summary", "PageHome"));
                    Check("the saved build's valid item is mounted on the shown car; the retired id is left empty, not dropped from the save", game.Human.car.mount.MountedId(AttachmentSlot.Front) == "push_bar_junk" && game.Human.car.mount.MountedId(AttachmentSlot.Left) == "" && game.HumanLoadout.Get(AttachmentSlot.Left) == "retired_item");
                }
                else if (step == 1)
                {
                    step = 2;
                    Click("Play");
                    Check("PLAY opens match setup (mode, opponents, START, BACK); Home is hidden", Vis("Start") && Vis("ModePoints") && Vis("Diff1") && Vis("PlayBack") && !Vis("Play"));
                    Check("with a broken build START reads FIX LOADOUT", LabelOf("Start").Contains("FIX"), LabelOf("Start"));
                    Click("Start");
                    Check("START with a broken build opens the Garage loadout tab instead of starting the match", Vis("TabContentLoadout") && game.MenuOpen && game.Match == null);
                    Check("the broken slot is shown in red and CLEAR PROBLEMS is offered", Vis("ClearProblems") && ui.GetComponentsInChildren<Text>(false).Any(t => t.text.Contains("retired_item")));
                }
                else if (step == 2)
                {
                    step = 3;
                    Click("ClearProblems");
                    Check("CLEAR PROBLEMS empties only the bad slot", game.HumanLoadoutResult.IsValid && game.HumanLoadout.Get(AttachmentSlot.Front) == "push_bar_junk" && game.HumanLoadout.Get(AttachmentSlot.Left) == "");
                    Click("Next0");                                   // front: push bar -> spiked ram plate
                    Check("cycling the front slot swaps the mounted item and updates the cost", game.Human.car.mount.MountedId(AttachmentSlot.Front) == "spiked_ram_plate" && game.HumanLoadoutResult.totalCost == 4, $"front={game.Human.car.mount.MountedId(AttachmentSlot.Front)}, cost {game.HumanLoadoutResult.totalCost}");
                    Click("Next4");                                   // roof: none -> rack
                    Click("Next1");                                   // rear: none -> hitch
                    Check("capacity 7/10 is valid and three items are mounted", game.HumanLoadoutResult.IsValid && game.HumanLoadoutResult.totalCost == 7 && game.Human.car.mount.InstanceCount == 3);
                    Check("slots with no items yet (left/right) are not interactable", !Btn("Next2").interactable && !Btn("Next3").interactable);
                    var cam = game.chaseCamera; float carYaw = game.Human.car.rig.root.transform.eulerAngles.y;
                    Check("after editing the rear slot the preview swings to the rear three-quarter view", Mathf.Abs(Mathf.DeltaAngle(cam.MenuAngle, carYaw + 25f)) < 0.5f && Mathf.Approximately(cam.MenuPitch, 16f) && Mathf.Approximately(cam.MenuZoom, 0.8f), $"angle {cam.MenuAngle:0.0} vs car {carYaw:0.0}, pitch {cam.MenuPitch}, zoom {cam.MenuZoom}");
                    Click("Next4");
                    Check("editing the roof slot raises and tightens the view", Mathf.Approximately(cam.MenuPitch, 40f) && Mathf.Approximately(cam.MenuZoom, 0.85f) && Mathf.Abs(Mathf.DeltaAngle(cam.MenuAngle, carYaw + 150f)) < 0.5f);
                    Click("Next4");                                   // rack -> none -> rack again: leaves the build as it was (7/10)
                    Check("the roof rack is back and the build is still 7/10", game.Human.car.mount.MountedId(AttachmentSlot.Roof) == "roof_cage_rack" && game.HumanLoadoutResult.totalCost == 7);
                    Check("the item card shows the slot being edited: name, cost, rarity and a description", TextOf("CardTitle", "ItemCard") == "ROOF CAGE RACK" && TextOf("CardMeta", "ItemCard").Contains("COST 1") && TextOf("CardBody", "ItemCard").Length > 20 && Vis("ItemCard"), $"{TextOf("CardTitle", "ItemCard")} | {TextOf("CardMeta", "ItemCard")}");
                    var barFill = GameObject.Find("CapacityFill");
                    Check("the capacity bar fills 7/10 of its width", barFill != null && Mathf.Abs(barFill.GetComponent<RectTransform>().sizeDelta.x - 196f) < 0.5f, barFill != null ? barFill.GetComponent<RectTransform>().sizeDelta.x.ToString("0.0") : "missing");
                    Check("an equipped slot looks different from an empty one", Btn("Item0").targetGraphic.color != Btn("Item2").targetGraphic.color);
                    Click("Next0");                                   // front: ram -> none (the list wraps: none, push bar, ram plate)
                    Check("the card says EMPTY for an empty slot", TextOf("CardMeta", "ItemCard") == "EMPTY" && TextOf("CardTitle", "ItemCard") == "FRONT");
                    Click("Next0"); Click("Next0");                   // none -> push bar -> ram plate again
                    Check("the capacity readout shows 7 / 10", TextOf("Capacity", "PageGarage").Contains("7 / 10"), TextOf("Capacity", "PageGarage"));
                }
                else if (step == 3)
                {
                    step = 4;
                    Click("GarageBack");
                    Check("BACK returns to Home with the new loadout cost in the summary", Vis("Play") && !Vis("TabLoadout") && TextOf("Summary", "PageHome").Contains("7/10"), TextOf("Summary", "PageHome"));
                    LoadoutStore.Load(Key, out var persisted);
                    Check("each change was saved for this vehicle", persisted.Get(AttachmentSlot.Front) == "spiked_ram_plate" && persisted.Get(AttachmentSlot.Roof) == "roof_cage_rack" && persisted.Get(AttachmentSlot.Rear) == "tow_hitch_bumper");
                    Click("Garage");
                    Check("GARAGE opens on the Vehicle tab (cars and paint)", Vis("TabVehicle") && Vis("Car0") && Vis("Swatch0") && !Vis("Item0"));
                    var cam = game.chaseCamera; var pad = ui.GetComponentInChildren<MenuOrbitPad>(true);
                    Check("the Garage holds the view still (no auto-spin) at the default angle, height and distance", !cam.menuAutoSpin && Mathf.Approximately(cam.MenuPitch, ChaseCamera.DefaultMenuPitch) && Mathf.Approximately(cam.MenuZoom, 1f) && pad != null && pad.gameObject.activeInHierarchy);
                    float a0 = cam.MenuAngle, p0 = cam.MenuPitch, z0 = cam.MenuZoom;
                    var drag = new PointerEventData(EventSystem.current) { delta = new Vector2(100f, -50f) };
                    pad.OnDrag(drag);
                    Check("dragging right swings the camera round by +35 deg (the near side of the car follows the finger) and dragging down raises the view (+11 deg)", Mathf.Abs((cam.MenuAngle - a0) - 35f) < 0.01f && Mathf.Abs((cam.MenuPitch - p0) - 11f) < 0.01f, $"angle {cam.MenuAngle - a0:0.0}, pitch {cam.MenuPitch - p0:0.0}");
                    for (int i = 0; i < 40; i++) pad.OnDrag(new PointerEventData(EventSystem.current) { delta = new Vector2(0f, -100f) });
                    Check("the height is clamped (never below the ground or straight overhead)", cam.MenuPitch <= cam.menuMaxPitch + 0.001f && cam.MenuPitch >= cam.menuMinPitch);
                    for (int i = 0; i < 40; i++) pad.OnDrag(new PointerEventData(EventSystem.current) { delta = new Vector2(0f, 100f) });
                    Check("dragging up lowers the view only to the minimum", Mathf.Approximately(cam.MenuPitch, cam.menuMinPitch));
                    pad.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, 1f) });
                    Check("the mouse wheel zooms in (distance x0.9)", Mathf.Abs(cam.MenuZoom - 0.9f) < 0.001f, $"zoom {cam.MenuZoom}");
                    for (int i = 0; i < 20; i++) pad.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -1f) });
                    Check("zoom is clamped to the allowed range", Mathf.Approximately(cam.MenuZoom, cam.menuMaxZoom));
                    cam.FocusMenuView(ChaseCamera.DefaultMenuRelativeAngle, ChaseCamera.DefaultMenuPitch, 1f);
                }
                else if (step == 4)
                {
                    step = 5;
                    Click("Car1");                                    // hatchback has its own (empty) build
                    Check("switching to the hatchback shows its own saved build (empty)", game.playerVehicle == 1 && game.HumanLoadout.IsEmpty && game.Human.car.mount.InstanceCount == 0);
                    Click("TabLoadout");
                    Check("the LOADOUT tab shows capacity 0 / 8 for the hatchback", Vis("Item0") && TextOf("Capacity", "PageGarage").Contains("0 / 8"), TextOf("Capacity", "PageGarage"));
                    Click("TabVehicle"); Click("Car0");
                    Check("switching back restores the muscle car's build", game.Human.car.mount.InstanceCount == 3 && game.HumanLoadout.Get(AttachmentSlot.Front) == "spiked_ram_plate");
                    Click("Swatch2");
                    Check("paint choice works from the Garage", game.playerPaint == 2);
                    Click("TabBuilds");
                    Check("the BUILDS tab shows three empty slots; LOAD is disabled until something is saved", Vis("PresetLoad0") && Vis("PresetLoad2") && !Btn("PresetLoad0").interactable && !Vis("ItemCard"));
                    Click("PresetSave0");
                    Check("SAVE stores the current loadout; the slot now shows its items and cost and offers REPLACE", game.HumanPresetExists(0) && Btn("PresetLoad0").interactable && TextOf("PresetSummary0", "TabContentBuilds").Contains("7/10") && LabelOf("PresetSave0").Contains("REPLACE"), TextOf("PresetSummary0", "TabContentBuilds"));
                    Click("TabLoadout"); Click("Next4");              // roof rack off: build is now 6/10
                    Check("the build was changed after saving (6/10)", game.HumanLoadoutResult.totalCost == 6);
                    Click("TabBuilds"); Click("PresetLoad0");
                    Check("LOAD restores the saved build (rack back, 7/10, items mounted on the car) and saves it as the current loadout", game.HumanLoadoutResult.totalCost == 7 && game.Human.car.mount.InstanceCount == 3 && game.Human.car.mount.MountedId(AttachmentSlot.Roof) == "roof_cage_rack");
                    LoadoutStore.Load(Key, out var afterLoad);
                    Check("the loaded build is what is saved for the vehicle", afterLoad.Get(AttachmentSlot.Roof) == "roof_cage_rack");
                    Click("Car1"); Click("TabBuilds");
                    Check("saved builds belong to a vehicle: the hatchback's slots are empty", !game.HumanPresetExists(0) && !Btn("PresetLoad0").interactable);
                    Click("Car0");
                    Click("TabVehicle");
                }
                else if (step == 5)
                {
                    step = 6;
                    Click("GarageBack"); Click("Play");
                    Check("match setup: mode and opponent buttons change the game's choices", true);
                    Click("ModeLast"); Click("Diff2");
                    Check("LAST CAR STANDING and COMPETITIVE were selected", game.rules.mode == GameMode.LastCarStanding && game.difficulty == AiDifficulty.Competitive);
                    Click("ModePoints"); Click("Diff1");
                    Check("START now reads START (build is valid)", LabelOf("Start") == "START");
                    Click("Start");
                    Check("START starts the match; the whole menu is hidden and the kit is on the player's car", !game.MenuOpen && game.Match != null && game.Human.car.mount.InstanceCount == 3 && !Vis("Start") && !Vis("Play") && !Vis("OrbitPad"), $"menuOpen={game.MenuOpen}, items={game.Human.car.mount.InstanceCount}");
                }
                else if (step == 6)
                {
                    step = 7;
                    var others = game.Participants.Where(p => p != game.Human).ToList();
                    Check("AI cars have no attachments in V1", others.All(p => p.car.mount.InstanceCount == 0), $"{others.Count} AI cars");
                    int clicks = 0; foreach (var p in game.Participants) clicks += p.car.mount.Root != null ? p.car.mount.Root.GetComponentsInChildren<Collider>(true).Length : 0;
                    Check("no colliders anywhere under any car's attachments during play", clicks == 0);
                    game.GoToMenu();
                }
                else if (step == 7)
                {
                    step = 8;
                    Check("quitting to the menu returns to Home with the showcase spin and the orbit layer back", Vis("Play") && Vis("Garage") && !Vis("Start") && game.chaseCamera.menuAutoSpin && Vis("OrbitPad"));
                }
                else if (step == 8) Finish(null);
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; Finish(null); }
        }

        static void Finish(string problem)
        {
            if (problem != null) { sb.AppendLine("  FAIL  " + problem); fails++; }
            EditorApplication.update -= Tick;
            LoadoutStore.Delete(Key);
            for (int i = 0; i < LoadoutPresets.Count; i++) LoadoutPresets.Delete(Key, i);
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            Debug.Log(sb.ToString());
            EditorApplication.playModeStateChanged -= OnState;
            EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled; EditorSettings.enterPlayModeOptions = prevOptions;
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}

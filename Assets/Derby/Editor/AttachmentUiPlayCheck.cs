using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarWreck.Driving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
                    Check("START starts the match; the whole menu is hidden and the kit is on the player's car", !game.MenuOpen && game.Match != null && game.Human.car.mount.InstanceCount == 3 && !Vis("Start") && !Vis("Play"), $"menuOpen={game.MenuOpen}, items={game.Human.car.mount.InstanceCount}");
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
                    Check("quitting to the menu returns to Home", Vis("Play") && Vis("Garage") && !Vis("Start"));
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
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            Debug.Log(sb.ToString());
            EditorApplication.playModeStateChanged -= OnState;
            EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled; EditorSettings.enterPlayModeOptions = prevOptions;
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}

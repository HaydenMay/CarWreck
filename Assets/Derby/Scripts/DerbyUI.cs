using System.Collections.Generic;
using CarWreck.Driving;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarWreck.Derby
{
    /// <summary>
    /// The derby's whole interface, built in code like the driving arena's: the start menu (vehicle, paint, mode), the countdown, the in-match HUD (score or cars left,
    /// timer, durability, standings, feed, name plates), the pause panel and the results screen, plus the same on-screen touch controls as the arena.
    /// The canvas scales with the screen (1280x720 reference, matched halfway between width and height) so buttons stay finger-sized on phones.
    /// It only reads the game and the match (and calls the game's menu actions): there is no game state in here.
    /// </summary>
    public class DerbyUI : MonoBehaviour
    {
        public DriveInput input;
        public DerbyGame game;

        Canvas canvas;
        RectTransform root;
        Font font;
        Sprite round, triangle;
        LookDragPad lookPad;
        GameObject touchRoot, cornerRoot, hudRoot, menuRoot, pauseRoot, resultsRoot, namePlateRoot;
        TouchHoldButton left, right, gas, brake;
        Text touchToggleLabel, hintText;
        bool touchVisible, userToggled, menuBuilt, resultsBuilt, pauseBuilt;

        // HUD
        Text scoreText, subText, timerText, modeText, bannerText, bannerSub, pointsText, feedText, flipText;
        Image durFill, durBack;
        Text durLabel;
        Text[] boardName, boardScore; Image[] boardSwatch;
        const int BoardRows = 6;
        float bannerUntil, pointsUntil, hudTimer;
        string bannerCurrent = "";
        readonly List<(string text, float time)> feed = new List<(string, float)>();
        readonly List<Participant> sorted = new List<Participant>();

        class Plate { public RectTransform rect; public Text name; public Image barBack, bar; public Participant who; public int alive = -1; }
        readonly List<Plate> plates = new List<Plate>();

        // menu
        Button[] carButtons, modeButtons, swatchButtons;
        Image[] swatchRing;
        Text carDescription, modeDescription;

        // results
        Text resultTitle, resultSub;
        Text[][] resultCells;
        const int ResultRows = 8;

        public bool TouchVisible => touchVisible;

        static readonly Color Dark = new Color(0.16f, 0.18f, 0.2f, 0.92f), Chosen = new Color(0.95f, 0.48f, 0.1f, 0.95f), Panel = new Color(0.04f, 0.05f, 0.06f, 0.82f);

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            round = MakeRoundedSprite(64, 22);
            triangle = MakeTriangleSprite(64);
            Build();
        }

        void Start() => SetTouch(Application.isMobilePlatform, false);

        public void SetTouch(bool on, bool byUser)
        {
            if (byUser) userToggled = true;
            touchVisible = on;
            bool inMatch = game != null && !game.MenuOpen && game.Match != null;
            if (touchRoot != null) touchRoot.SetActive(on && inMatch);
            if (hintText != null) hintText.gameObject.SetActive(!on && inMatch);
            if (touchToggleLabel != null) touchToggleLabel.text = on ? "TOUCH: ON" : "TOUCH: OFF";
            if (!on) ReleaseTouchInputs();
        }

        void ReleaseTouchInputs()
        {
            if (input == null) return;
            input.touchGas = input.touchBrake = input.touchLeft = input.touchRight = false;
        }

        // ------------------------------------------------------------------ actions called by the game

        public void ShowMenu(bool on)
        {
            if (on) { EnsureMenu(); RefreshMenu(); }
            if (menuRoot != null) menuRoot.SetActive(on);
            if (on) { if (resultsRoot != null) resultsRoot.SetActive(false); if (pauseRoot != null) pauseRoot.SetActive(false); }
            ApplyVisibility();
            if (on) ReleaseTouchInputs();
        }

        public void ShowPause(bool on)
        {
            if (on) EnsurePause();
            if (pauseRoot != null) pauseRoot.SetActive(on);
            if (on) ReleaseTouchInputs();
        }

        public void OnMatchStarted()
        {
            feed.Clear(); if (feedText != null) feedText.text = "";
            bannerUntil = pointsUntil = 0f;
            if (menuRoot != null) menuRoot.SetActive(false);
            if (resultsRoot != null) resultsRoot.SetActive(false);
            if (pauseRoot != null) pauseRoot.SetActive(false);
            ApplyVisibility();
        }

        public void OnStateChanged(MatchState old, MatchState now) { ApplyVisibility(); }

        public void Banner(string text, float seconds) { bannerCurrent = text; bannerUntil = Time.unscaledTime + seconds; }

        public void ShowPoints(int points, string tier)
        {
            if (pointsText == null) return;
            pointsText.text = $"+{points}  {tier}";
            pointsUntil = Time.unscaledTime + 1.3f;
        }

        public void PushFeed(string s)
        {
            feed.Add((s, Time.unscaledTime));
            while (feed.Count > 4) feed.RemoveAt(0);
        }

        public void ShowResults()
        {
            EnsureResults();
            var m = game.Match;
            resultsRoot.SetActive(true);
            bool humanWon = m.Winner == game.Human;
            resultTitle.text = m.IsDraw ? "DRAW" : humanWon ? "YOU WIN!" : m.Winner != null ? m.Winner.name + " WINS" : "MATCH OVER";
            resultTitle.color = humanWon ? new Color(1f, 0.82f, 0.25f) : m.IsDraw ? Color.white : new Color(1f, 0.55f, 0.45f);
            string reason = m.ResultText.Contains("  -  ") ? m.ResultText.Substring(m.ResultText.IndexOf("  -  ") + 5) : "";
            resultSub.text = $"{(m.rules.mode == GameMode.PointsDerby ? "POINTS DERBY" : "LAST CAR STANDING")}   -   YOUR PLACE  {game.Human.placement} / {m.participants.Count}" + (reason.Length > 0 ? $"\n<size=20>{reason}</size>" : "");
            // up to ResultRows rows in rank order; the player's row always shows
            var rows = new List<Participant>(m.Ranking);
            if (rows.Count > ResultRows) { int hi = rows.IndexOf(game.Human); rows.RemoveRange(ResultRows, rows.Count - ResultRows); if (hi >= ResultRows) rows[ResultRows - 1] = game.Human; }
            for (int i = 0; i < ResultRows; i++)
            {
                bool has = i < rows.Count;
                for (int c = 0; c < resultCells[i].Length; c++) resultCells[i][c].gameObject.SetActive(has);
                if (!has) continue;
                var p = rows[i];
                var cells = resultCells[i];
                Color tint = p == game.Human ? new Color(1f, 0.85f, 0.4f) : Color.white;
                cells[0].text = p.placement.ToString();
                cells[1].text = p.name; cells[1].color = tint;
                cells[2].text = p.score.ToString("N0");
                cells[3].text = p.wrecksDealt.ToString();
                cells[4].text = p.timesWrecked.ToString();
                cells[5].text = m.rules.mode == GameMode.LastCarStanding ? (p.IsActive ? "ALIVE" : "OUT") : p.hitsDealt + " hits";
            }
            ApplyVisibility();
        }

        // ------------------------------------------------------------------ visibility

        void ApplyVisibility()
        {
            if (game == null) return;
            bool inMatch = !game.MenuOpen && game.Match != null;
            bool results = inMatch && game.Match.State == MatchState.Results;
            if (hudRoot != null) hudRoot.SetActive(inMatch && !results);
            if (cornerRoot != null) cornerRoot.SetActive(inMatch && !results);
            if (namePlateRoot != null) namePlateRoot.SetActive(inMatch && !results);
            if (touchRoot != null) touchRoot.SetActive(touchVisible && inMatch && !results);
            if (hintText != null) hintText.gameObject.SetActive(!touchVisible && inMatch && !results);
            if (resultsRoot != null && !results) resultsRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (lookPad != null && lookPad.cam == null && game != null) lookPad.cam = game.chaseCamera;
            if (input != null && input.TouchSeen && !touchVisible && !userToggled) SetTouch(true, false);
            if (game == null || game.MenuOpen || game.Match == null) return;
            UpdateNamePlates();
            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer > 0f) return;
            hudTimer = 0.1f;
            RefreshHud();
        }

        void RefreshHud()
        {
            var m = game.Match; var h = game.Human;
            bool points = m.rules.mode == GameMode.PointsDerby;
            int active = m.State == MatchState.Countdown ? m.participants.Count : m.ActiveCount;
            scoreText.text = points ? $"SCORE  {h.score:N0}" : $"CARS LEFT  {active}";
            subText.text = points ? $"RANK {game.HumanRank()} / {m.participants.Count}     ·     {Mathf.Max(0, m.participants.Count - 1)} OPPONENTS" : $"SCORE {h.score:N0}     ·     RANK {game.HumanRank()} / {m.participants.Count}";
            // timer line
            Color tc = Color.white; string t;
            switch (m.State)
            {
                case MatchState.Countdown: t = points ? FormatTime(m.rules.pointsDuration) : "GET READY"; break;
                case MatchState.FinalShowdown: t = "SHOWDOWN  " + FormatTime(m.ShowdownRemaining); tc = new Color(1f, 0.4f, 0.3f); break;
                case MatchState.Results: t = "FINISHED"; break;
                default:
                    if (points) { t = FormatTime(m.TimeRemaining); if (m.TimeRemaining < 15f) tc = new Color(1f, 0.55f, 0.35f); }
                    else t = "LAST CAR STANDING";
                    break;
            }
            timerText.text = t; timerText.color = tc;
            modeText.text = points ? "POINTS DERBY" : (m.State == MatchState.FinalShowdown ? "WINNER: HIGHEST SCORE IF TIME RUNS OUT" : "NO RESPAWNS");
            // durability
            float d = h.DurabilityFraction;
            durFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, d), 1f);
            durFill.color = d > 0.6f ? new Color(0.35f, 0.85f, 0.35f) : d > 0.3f ? new Color(0.98f, 0.8f, 0.2f) : new Color(0.95f, 0.3f, 0.25f);
            durLabel.text = h.IsActive || m.State == MatchState.Countdown ? $"ARMOUR {Mathf.CeilToInt(d * 100f)}%" : "WRECKED";
            // standings
            sorted.Clear(); sorted.AddRange(m.participants);
            sorted.Sort(points ? CompareByScore : CompareByAlive);
            for (int i = 0; i < BoardRows; i++)
            {
                bool has = i < sorted.Count;
                boardName[i].gameObject.SetActive(has); boardScore[i].gameObject.SetActive(has); boardSwatch[i].gameObject.SetActive(has);
                if (!has) continue;
                var p = sorted[i];
                boardName[i].text = $"{i + 1}  {p.name}";
                boardName[i].color = p == h ? new Color(1f, 0.85f, 0.4f) : p.IsActive || points || p.status == ParticipantStatus.Waiting ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                boardScore[i].text = !points && !p.IsActive && p.status != ParticipantStatus.Waiting ? "OUT" : p.score.ToString("N0");
                boardScore[i].color = boardName[i].color;
                boardSwatch[i].color = game.palette != null && game.palette.Count > 0 ? game.palette.Get(p.paintIndex).body : Color.white;
            }
            // banner: countdown number, WRECK!, player wreck/respawn text, custom banners
            string banner = ""; string sub = "";
            if (m.State == MatchState.Countdown) banner = Mathf.CeilToInt(m.CountdownRemaining).ToString();
            else if (m.State == MatchState.Active && m.Clock < 0.9f) banner = "WRECK!";
            else if (Time.unscaledTime < bannerUntil) banner = bannerCurrent;
            if (!h.IsActive && m.InPlay)
            {
                if (h.status == ParticipantStatus.Wrecked) sub = $"RESPAWN IN {Mathf.Max(0, Mathf.CeilToInt(h.respawnAt - m.Clock))}";
                else if (h.status == ParticipantStatus.Out) sub = "YOU ARE OUT  -  WATCHING THE REST";
                if (banner == "") banner = h.status == ParticipantStatus.Wrecked ? "WRECKED" : "OUT";
            }
            bannerText.text = banner; bannerSub.text = sub;
            pointsText.gameObject.SetActive(Time.unscaledTime < pointsUntil);
            if (flipText != null) flipText.gameObject.SetActive(game.HumanFlipped && h.IsActive);
            // feed
            float now = Time.unscaledTime;
            feed.RemoveAll(f => now - f.time > 6f);
            var sbd = new System.Text.StringBuilder();
            foreach (var f in feed) sbd.AppendLine(f.text);
            feedText.text = sbd.ToString();
        }

        static int CompareByScore(Participant a, Participant b)
        {
            int c = b.score.CompareTo(a.score);
            return c != 0 ? c : a.id.CompareTo(b.id);
        }

        static int CompareByAlive(Participant a, Participant b)
        {
            int c = (b.IsActive ? 1 : 0).CompareTo(a.IsActive ? 1 : 0);
            if (c == 0) c = b.score.CompareTo(a.score);
            return c != 0 ? c : a.id.CompareTo(b.id);
        }

        static string DurationText(float s) => s < 90f ? $"{Mathf.RoundToInt(s)} seconds" : $"{Mathf.RoundToInt(s / 60f)} minutes";

        static string FormatTime(float s) { int t = Mathf.CeilToInt(Mathf.Max(0f, s)); return $"{t / 60}:{t % 60:00}"; }

        // ------------------------------------------------------------------ name plates over the cars

        void UpdateNamePlates()
        {
            var cam = Camera.main;
            var list = game.Participants;
            while (plates.Count < list.Count) plates.Add(MakePlate());
            for (int i = 0; i < plates.Count; i++)
            {
                var pl = plates[i];
                var p = i < list.Count ? list[i] : null;
                bool show = p != null && p != game.Human && p.car != null && p.status != ParticipantStatus.Waiting && cam != null;
                Vector3 sp = Vector3.zero;
                if (show)
                {
                    sp = cam.WorldToScreenPoint(p.car.Body.position + Vector3.up * 2.3f);
                    show = sp.z > 1f && sp.z < 90f;
                    sp.x = Mathf.Clamp(sp.x, 90f * canvas.scaleFactor, Screen.width - 90f * canvas.scaleFactor);
                    sp.y = Mathf.Clamp(sp.y, 20f * canvas.scaleFactor, Screen.height - 60f * canvas.scaleFactor);
                }
                pl.rect.gameObject.SetActive(show);
                if (!show) continue;
                if (pl.who != p)
                {
                    pl.who = p; pl.alive = -1;
                    pl.name.color = game.palette != null && game.palette.Count > 0 ? Color.Lerp(game.palette.Get(p.paintIndex).body, Color.white, 0.25f) : Color.white;
                }
                bool alive = p.IsActive;
                pl.barBack.gameObject.SetActive(alive); pl.bar.gameObject.SetActive(alive);
                int aliveFlag = alive ? 1 : 0;
                if (pl.alive != aliveFlag) { pl.alive = aliveFlag; pl.name.text = alive ? p.name : p.name + " (WRECKED)"; }
                float f = p.DurabilityFraction;
                pl.bar.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, f), 1f);
                pl.bar.color = f > 0.6f ? new Color(0.35f, 0.85f, 0.35f) : f > 0.3f ? new Color(0.98f, 0.8f, 0.2f) : new Color(0.95f, 0.3f, 0.25f);
                float scale = Mathf.Clamp(1.15f - sp.z / 160f, 0.65f, 1f);
                pl.rect.localScale = new Vector3(scale, scale, 1f);
                pl.rect.anchoredPosition = new Vector2(sp.x, sp.y) / canvas.scaleFactor;
            }
        }

        Plate MakePlate()
        {
            var go = new GameObject("Plate", typeof(RectTransform));
            go.transform.SetParent(namePlateRoot.transform, false);
            var rt = (RectTransform)go.transform;
            Anchor(rt, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(240f, 40f));
            var pl = new Plate { rect = rt };
            pl.name = Label("Name", rt, "", 20, TextAnchor.LowerCenter, FontStyle.Bold);
            Anchor(pl.name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(240f, 26f));
            var back = new GameObject("BarBack", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(rt, false);
            Anchor((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(90f, 8f));
            pl.barBack = back.GetComponent<Image>(); pl.barBack.color = new Color(0f, 0f, 0f, 0.6f); pl.barBack.raycastTarget = false;
            var fill = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var fr = (RectTransform)fill.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(1, 1); fr.offsetMax = new Vector2(-1, -1);
            pl.bar = fill.GetComponent<Image>(); pl.bar.raycastTarget = false;
            return pl;
        }

        // ------------------------------------------------------------------ layout: HUD, touch controls

        void Build()
        {
            var cgo = new GameObject("DerbyCanvas");
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)cgo.transform;

            var padGO = new GameObject("LookPad", typeof(RectTransform), typeof(Image));
            padGO.transform.SetParent(root, false);
            var prt = (RectTransform)padGO.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
            padGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            lookPad = padGO.AddComponent<LookDragPad>();

            namePlateRoot = new GameObject("NamePlates", typeof(RectTransform));
            Stretch((RectTransform)namePlateRoot.transform, root);
            namePlateRoot.SetActive(false);

            BuildHud();

            cornerRoot = new GameObject("CornerButtons", typeof(RectTransform));
            var cr = (RectTransform)cornerRoot.transform;
            Stretch(cr, root);
            var menuBtn = SmallButton("Menu", cr, "PAUSE", new Vector2(-20, -20), new Vector2(180, 64), 26);
            menuBtn.OnChanged = held => { if (held) input.RequestMenu(); };
            var toggleBtn = SmallButton("TouchToggle", cr, "TOUCH: OFF", new Vector2(-20, -20 - 72), new Vector2(180, 64), 20);
            toggleBtn.OnChanged = held => { if (held) SetTouch(!touchVisible, true); };
            touchToggleLabel = toggleBtn.GetComponentInChildren<Text>();
            var flipBtn = SmallButton("Unflip", cr, "RIGHT CAR", new Vector2(-20, -20 - 144), new Vector2(180, 64), 20);
            flipBtn.OnChanged = held => { if (held) input.RequestReset(); };
            cornerRoot.SetActive(false);

            touchRoot = new GameObject("TouchControls", typeof(RectTransform));
            var tr = (RectTransform)touchRoot.transform;
            Stretch(tr, root);
            const float pad = 36f, big = 210f, gap = 26f;
            left = Pedal("SteerLeft", tr, new Vector2(0, 0), new Vector2(pad, pad), new Vector2(big, big), 90f, "", v => input.touchLeft = v);
            right = Pedal("SteerRight", tr, new Vector2(0, 0), new Vector2(pad + big + gap, pad), new Vector2(big, big), -90f, "", v => input.touchRight = v);
            gas = Pedal("Gas", tr, new Vector2(1, 0), new Vector2(-pad, pad), new Vector2(big, big * 1.45f), 0f, "GAS", v => input.touchGas = v);
            brake = Pedal("Brake", tr, new Vector2(1, 0), new Vector2(-pad - big - gap, pad), new Vector2(big, big), 180f, "BRAKE\nREVERSE", v => input.touchBrake = v);
            touchRoot.SetActive(false);
        }

        void BuildHud()
        {
            hudRoot = new GameObject("Hud", typeof(RectTransform));
            var hr = (RectTransform)hudRoot.transform;
            Stretch(hr, root);
            // top-left block: score / cars left, rank, durability, standings, feed
            scoreText = Label("Score", hr, "", 44, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(scoreText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -14), new Vector2(520, 54));
            subText = Label("Sub", hr, "", 22, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(subText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -66), new Vector2(520, 28));
            durBack = Img("DurBack", hr, new Color(0f, 0f, 0f, 0.6f));
            Anchor(durBack.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -100), new Vector2(330, 26));
            durFill = Img("DurFill", durBack.transform, Color.green);
            var fr = durFill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(2, 2); fr.offsetMax = new Vector2(-2, -2);
            durLabel = Label("DurLabel", durBack.transform, "", 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            var lr = durLabel.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
            boardName = new Text[BoardRows]; boardScore = new Text[BoardRows]; boardSwatch = new Image[BoardRows];
            for (int i = 0; i < BoardRows; i++)
            {
                float y = -134f - i * 26f;
                boardSwatch[i] = Img("Swatch" + i, hr, Color.white);
                Anchor(boardSwatch[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, y - 4), new Vector2(14, 14));
                boardName[i] = Label("Name" + i, hr, "", 20, TextAnchor.UpperLeft, FontStyle.Bold);
                Anchor(boardName[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, y), new Vector2(230, 26));
                boardScore[i] = Label("Score" + i, hr, "", 20, TextAnchor.UpperRight, FontStyle.Bold);
                Anchor(boardScore[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(250, y), new Vector2(100, 26));
            }
            feedText = Label("Feed", hr, "", 20, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(feedText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -134f - BoardRows * 26f - 10f), new Vector2(440, 120));
            feedText.color = new Color(1f, 1f, 1f, 0.9f);
            // top centre: timer and mode
            timerText = Label("Timer", hr, "", 48, TextAnchor.UpperCenter, FontStyle.Bold);
            Anchor(timerText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(100, -14), new Vector2(620, 58));
            modeText = Label("Mode", hr, "", 20, TextAnchor.UpperCenter, FontStyle.Bold);
            Anchor(modeText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(100, -68), new Vector2(620, 26));
            modeText.color = new Color(1f, 1f, 1f, 0.7f);
            // centre: banner (countdown, WRECK!, wrecked ...) and its sub line, points popup
            bannerText = Label("Banner", hr, "", 120, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1100, 150));
            bannerText.color = new Color(1f, 0.72f, 0.2f);
            bannerText.resizeTextForBestFit = true; bannerText.resizeTextMinSize = 40; bannerText.resizeTextMaxSize = 120;
            bannerSub = Label("BannerSub", hr, "", 36, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1000, 50));
            pointsText = Label("Points", hr, "", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(pointsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(900, 54));
            pointsText.color = new Color(0.5f, 1f, 0.5f);
            pointsText.gameObject.SetActive(false);
            flipText = Label("Flipped", hr, "Flipped?  Press R  (or tap RIGHT CAR)", 32, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(flipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(900, 56));
            flipText.color = new Color(1f, 0.85f, 0.3f);
            flipText.gameObject.SetActive(false);
            hintText = Label("Hint", hr, "W / S  accelerate - brake / reverse     A / D  steer     R  right car     ESC  pause     drag  look around", 20, TextAnchor.LowerCenter, FontStyle.Normal);
            Anchor(hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(1100, 36));
            hudRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ start menu

        void EnsureMenu()
        {
            if (menuBuilt || game == null) return;
            menuBuilt = true;
            menuRoot = new GameObject("Menu", typeof(RectTransform), typeof(Image));
            var mr = (RectTransform)menuRoot.transform;
            mr.SetParent(root, false);
            Anchor(mr, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(650, 0));
            menuRoot.GetComponent<Image>().color = Panel;

            var title = Label("Title", mr, "CARWRECK", 52, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -12), new Vector2(580, 60));
            title.color = Chosen;
            var tag = Label("Tagline", mr, "DRIVE.  RAM.  WRECK.  WIN.", 22, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -70), new Vector2(580, 28));

            var cl = Label("CarLabel", mr, "YOUR CAR", 20, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(cl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -106), new Vector2(580, 24));
            int n = game.vehicles.Length;
            carButtons = new Button[n];
            string[] shortNames = { "MUSCLE CAR", "HATCHBACK", "PICKUP" };
            float cw = (578f - 10f * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                string nm = i < shortNames.Length ? shortNames[i] : game.vehicles[i].displayName.ToUpperInvariant();
                carButtons[i] = MenuButton("Car" + i, mr, $"<b>{nm}</b>\n<size=18>{game.vehicles[i].mass:N0} kg</size>", 22, TextAnchor.MiddleCenter, new Vector2(36 + i * (cw + 10f), -132), new Vector2(cw, 76), () => { game.SelectVehicle(idx); RefreshMenu(); });
            }
            carDescription = Label("CarDesc", mr, "", 19, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(carDescription.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -214), new Vector2(580, 44));

            var pl = Label("PaintLabel", mr, "PAINT", 20, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(pl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -262), new Vector2(580, 24));
            int pc = game.palette != null ? game.palette.Count : 0;
            swatchButtons = new Button[pc]; swatchRing = new Image[pc];
            float sw = Mathf.Min(70f, (578f - 8f * (pc - 1)) / Mathf.Max(1, pc));
            for (int i = 0; i < pc; i++)
            {
                int idx = i;
                float x = 36f + i * (sw + 8f);
                var ring = Img("Ring" + i, mr, Color.clear);
                ring.sprite = round; ring.type = UnityEngine.UI.Image.Type.Sliced;
                Anchor(ring.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x - 4f, -288f + 4f), new Vector2(sw + 8f, sw + 8f));
                swatchRing[i] = ring;
                var go = new GameObject("Swatch" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(mr, false);
                Anchor((RectTransform)go.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -288f), new Vector2(sw, sw));
                var img = go.GetComponent<Image>(); img.sprite = round; img.type = UnityEngine.UI.Image.Type.Sliced; img.color = game.palette.Get(i).body;
                var b = go.GetComponent<Button>(); b.targetGraphic = img; b.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white; colors.pressedColor = new Color(0.75f, 0.75f, 0.75f); colors.selectedColor = Color.white; b.colors = colors;
                b.onClick.AddListener(() => { game.SelectPaint(idx); RefreshMenu(); });
                swatchButtons[i] = b;
            }

            var ml = Label("ModeLabel", mr, "GAME MODE", 20, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(ml.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -376), new Vector2(580, 24));
            modeButtons = new Button[2];
            modeButtons[0] = MenuButton("ModePoints", mr, "<b>POINTS DERBY</b>\n<size=17>timed - respawn</size>", 22, TextAnchor.MiddleCenter, new Vector2(36, -402), new Vector2(284, 76), () => { game.SelectMode(GameMode.PointsDerby); RefreshMenu(); });
            modeButtons[1] = MenuButton("ModeLast", mr, "<b>LAST CAR STANDING</b>\n<size=17>no respawns - final showdown</size>", 20, TextAnchor.MiddleCenter, new Vector2(330, -402), new Vector2(284, 76), () => { game.SelectMode(GameMode.LastCarStanding); RefreshMenu(); });
            modeDescription = Label("ModeDesc", mr, "", 19, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(modeDescription.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -484), new Vector2(580, 50));

            var start = MenuButton("Start", mr, "START", 46, TextAnchor.MiddleCenter, new Vector2(36, -548), new Vector2(578, 92), () => game.StartMatch());
            start.targetGraphic.color = Chosen;
            var colors2 = start.colors; colors2.normalColor = colors2.highlightedColor = Color.white; colors2.pressedColor = new Color(0.8f, 0.8f, 0.8f); colors2.selectedColor = Color.white; start.colors = colors2;
            var keys = Label("Keys", mr, "W / S  gas - brake      A / D  steer      R  right car      ESC  pause", 17, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(keys.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -652), new Vector2(580, 26));
            keys.color = new Color(1f, 1f, 1f, 0.55f);
        }

        void RefreshMenu()
        {
            if (!menuBuilt) return;
            for (int i = 0; i < carButtons.Length; i++) carButtons[i].targetGraphic.color = i == game.playerVehicle ? Chosen : Dark;
            for (int i = 0; i < swatchRing.Length; i++) swatchRing[i].color = i == game.playerPaint ? Color.white : Color.clear;
            modeButtons[0].targetGraphic.color = game.rules.mode == GameMode.PointsDerby ? Chosen : Dark;
            modeButtons[1].targetGraphic.color = game.rules.mode == GameMode.LastCarStanding ? Chosen : Dark;
            carDescription.text = game.driveProfiles != null && game.playerVehicle < game.driveProfiles.Length ? game.driveProfiles[game.playerVehicle].description : "";
            modeDescription.text = game.rules.mode == GameMode.PointsDerby
                ? $"{game.participantCount} cars, {DurationText(game.rules.pointsDuration)}. Hard hits score points. Wrecked cars respawn. Highest score wins."
                : $"{game.participantCount} cars, one life each. Last car running wins. With two left, a {Mathf.RoundToInt(game.rules.showdownDuration)} s final showdown: highest score wins if time runs out.";
        }

        // ------------------------------------------------------------------ pause and results

        void EnsurePause()
        {
            if (pauseBuilt) return;
            pauseBuilt = true;
            pauseRoot = new GameObject("Pause", typeof(RectTransform), typeof(Image));
            var pr = (RectTransform)pauseRoot.transform;
            pr.SetParent(root, false);
            Anchor(pr, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 430));
            pauseRoot.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.06f, 0.92f);
            var t = Label("Title", pr, "PAUSED", 50, TextAnchor.UpperCenter, FontStyle.Bold);
            Anchor(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(480, 64));
            var resume = MenuButton("Resume", pr, "RESUME", 36, TextAnchor.MiddleCenter, new Vector2(30, -100), new Vector2(460, 84), () => game.TogglePause());
            resume.targetGraphic.color = Chosen;
            MenuButton("Restart", pr, "RESTART MATCH", 30, TextAnchor.MiddleCenter, new Vector2(30, -200), new Vector2(460, 84), () => game.Rematch());
            MenuButton("Quit", pr, "QUIT TO MENU", 30, TextAnchor.MiddleCenter, new Vector2(30, -300), new Vector2(460, 84), () => game.GoToMenu());
            pauseRoot.SetActive(false);
        }

        void EnsureResults()
        {
            if (resultsBuilt) return;
            resultsBuilt = true;
            resultsRoot = new GameObject("Results", typeof(RectTransform), typeof(Image));
            var rr = (RectTransform)resultsRoot.transform;
            rr.SetParent(root, false);
            Anchor(rr, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 640));
            resultsRoot.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.06f, 0.93f);
            resultTitle = Label("Title", rr, "", 60, TextAnchor.UpperCenter, FontStyle.Bold);
            Anchor(resultTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(860, 70));
            resultSub = Label("Sub", rr, "", 26, TextAnchor.UpperCenter, FontStyle.Bold);
            resultSub.supportRichText = true;
            Anchor(resultSub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -86), new Vector2(860, 74));
            string[] heads = { "#", "DRIVER", "SCORE", "WRECKS", "WRECKED", "" };
            float[] xs = { 30f, 90f, 420f, 540f, 660f, 770f }, ws = { 50f, 320f, 110f, 110f, 110f, 110f };
            TextAnchor[] al = { TextAnchor.MiddleLeft, TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };
            for (int c = 0; c < heads.Length; c++)
            {
                var h = Label("Head" + c, rr, heads[c], 18, al[c], FontStyle.Bold);
                Anchor(h.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(xs[c], -172), new Vector2(ws[c], 26));
                h.color = new Color(1f, 1f, 1f, 0.55f);
            }
            resultCells = new Text[ResultRows][];
            for (int i = 0; i < ResultRows; i++)
            {
                resultCells[i] = new Text[heads.Length];
                for (int c = 0; c < heads.Length; c++)
                {
                    var t = Label($"Cell{i}_{c}", rr, "", 24, al[c], FontStyle.Bold);
                    Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(xs[c], -202 - i * 36f), new Vector2(ws[c], 34));
                    resultCells[i][c] = t;
                }
            }
            var again = MenuButton("Rematch", rr, "REMATCH", 36, TextAnchor.MiddleCenter, new Vector2(40, -534), new Vector2(400, 84), () => game.Rematch());
            again.targetGraphic.color = Chosen;
            MenuButton("Menu", rr, "MENU", 36, TextAnchor.MiddleCenter, new Vector2(460, -534), new Vector2(400, 84), () => game.GoToMenu());
            resultsRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ widget helpers

        Button MenuButton(string name, Transform parent, string text, int fontSize, TextAnchor anchor, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeft, size);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = round; img.type = UnityEngine.UI.Image.Type.Sliced; img.color = Dark;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white; colors.pressedColor = new Color(0.75f, 0.75f, 0.75f); colors.selectedColor = Color.white; colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            var t = Label("Label", go.transform, text, fontSize, anchor, FontStyle.Normal);
            t.supportRichText = true;
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(12, 4); rt.offsetMax = new Vector2(-12, -4);
            return b;
        }

        UnityEngine.UI.Image Img(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = color; img.raycastTarget = false;
            return img;
        }

        Text Label(string name, Transform parent, string text, int size, TextAnchor anchor, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor; t.text = text;
            t.color = Color.white; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        static void Stretch(RectTransform r, Transform parent)
        {
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        }

        TouchHoldButton SmallButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var corner = new Vector2(1, 1);
            Anchor((RectTransform)go.transform, corner, corner, corner, pos, size);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = round; img.type = UnityEngine.UI.Image.Type.Sliced;
            var b = go.AddComponent<TouchHoldButton>();
            b.face = img; b.idle = new Color(0.08f, 0.09f, 0.1f, 0.62f); b.pressed = new Color(1f, 0.55f, 0.1f, 0.9f);
            img.color = b.idle;
            var t = Label("Label", go.transform, label, fontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return b;
        }

        TouchHoldButton Pedal(string name, Transform parent, Vector2 corner, Vector2 pos, Vector2 size, float arrowRotation, string label, System.Action<bool> changed)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, corner, corner, corner, pos, size);
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(corner.x, corner.y);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = round; img.type = UnityEngine.UI.Image.Type.Sliced;
            var b = go.AddComponent<TouchHoldButton>();
            b.face = img;
            b.idle = new Color(1f, 1f, 1f, 0.26f);
            b.pressed = new Color(1f, 0.6f, 0.12f, 0.8f);
            img.color = b.idle;
            b.OnChanged = changed;
            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            arrow.transform.SetParent(go.transform, false);
            var art = (RectTransform)arrow.transform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, string.IsNullOrEmpty(label) ? 0.5f : 0.62f);
            art.sizeDelta = new Vector2(size.x * 0.42f, size.x * 0.42f);
            art.localRotation = Quaternion.Euler(0f, 0f, arrowRotation);
            var ai = arrow.GetComponent<UnityEngine.UI.Image>();
            ai.sprite = triangle; ai.color = new Color(1f, 1f, 1f, 0.9f); ai.raycastTarget = false;
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label("Label", go.transform, label, 30, TextAnchor.LowerCenter, FontStyle.Bold);
                var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(0, 14); lrt.offsetMax = Vector2.zero;
            }
            return b;
        }

        // ------------------------------------------------------------------ procedural sprites

        static Sprite MakeRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeTriangleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float halfWidth = (1f - (y + 0.5f) / size) * 0.5f * size;
                    float d = halfWidth - Mathf.Abs(x + 0.5f - size * 0.5f);
                    float a = Mathf.Clamp01(d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}

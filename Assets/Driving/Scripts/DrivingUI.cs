using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarWreck.Driving
{
    /// <summary>A button that stays "on" while any pointer (mouse or finger) holds it. Each finger is its own pointer, so
    /// steering and the pedals can be held at the same time.</summary>
    public class TouchHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        public System.Action<bool> OnChanged;
        public Image face;
        public Color idle = new Color(1f, 1f, 1f, 0.28f), pressed = new Color(1f, 0.62f, 0.15f, 0.85f);
        readonly System.Collections.Generic.HashSet<int> pointers = new System.Collections.Generic.HashSet<int>();
        public bool Held => pointers.Count > 0;

        public void OnPointerDown(PointerEventData e) { pointers.Add(e.pointerId); Apply(); }
        public void OnPointerUp(PointerEventData e) { pointers.Remove(e.pointerId); Apply(); }
        // Sliding a finger off the button releases it; sliding another finger on keeps working because each finger is a pointer.
        public void OnPointerExit(PointerEventData e) { if (pointers.Remove(e.pointerId)) Apply(); }
        public void OnPointerEnter(PointerEventData e) { }

        void OnDisable() { pointers.Clear(); Apply(); }

        void Apply()
        {
            if (face != null) face.color = Held ? pressed : idle;
            OnChanged?.Invoke(Held);
        }
    }

    /// <summary>
    /// Builds the whole driving overlay in code: speed read-out, last-impact line, key hints, the on-screen touch
    /// controls (steer left / right bottom-left, gas / brake bottom-right, reset top-right) and a touch on/off toggle.
    /// The canvas scales with the screen (1280×720 reference, matched to height) so the buttons stay finger-sized.
    /// </summary>
    public class DrivingUI : MonoBehaviour
    {
        public DriveInput input;
        public DrivingGame game;

        Canvas canvas;
        GameObject touchRoot;
        GameObject menuRoot, cornerRoot;
        Button[] playerButtons, targetButtons;
        bool menuBuilt;
        Text speedText, impactText, hintText, flipText, touchToggleLabel;
        bool touchVisible;
        bool userToggled;
        Font font;
        Sprite round, triangle;
        TouchHoldButton left, right, gas, brake;

        public bool TouchVisible => touchVisible;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            round = MakeRoundedSprite(64, 22);
            triangle = MakeTriangleSprite(64);
            Build();
        }

        void Start()
        {
            // Phones and tablets get the touch controls straight away (the page also reports coarse-pointer devices);
            // desktops (including touch laptops) get them on the first real touch or via the TOUCH toggle.
            SetTouch(Application.isMobilePlatform, false);
        }

        public void SetTouch(bool on, bool byUser)
        {
            if (byUser) userToggled = true;
            touchVisible = on;
            bool menu = game != null && game.MenuOpen;
            if (touchRoot != null) touchRoot.SetActive(on && !menu);
            if (hintText != null) hintText.gameObject.SetActive(!on && !menu);
            if (touchToggleLabel != null) touchToggleLabel.text = on ? "TOUCH: ON" : "TOUCH: OFF";
            if (!on) ReleaseTouchInputs();
        }

        void ReleaseTouchInputs()
        {
            if (input == null) return;
            input.touchGas = input.touchBrake = input.touchLeft = input.touchRight = false;
        }

        void Update()
        {
            if (input != null && input.TouchSeen && !touchVisible && !userToggled) SetTouch(true, false);
            if (game == null) return;
            if (speedText != null) speedText.text = $"{Mathf.Abs(game.SpeedKmh):0}<size=26> km/h</size>";
            if (flipText != null) flipText.gameObject.SetActive(game.Flipped);
        }

        // ------------------------------------------------------------------ vehicle selection menu (also the pause screen)

        public void ShowMenu(bool on)
        {
            if (on) { EnsureMenu(); RefreshMenu(); }
            if (menuRoot != null) menuRoot.SetActive(on);
            if (cornerRoot != null) cornerRoot.SetActive(!on);
            if (speedText != null) speedText.gameObject.SetActive(!on);
            if (impactText != null) impactText.gameObject.SetActive(!on);
            SetTouch(touchVisible, false);
            if (on) ReleaseTouchInputs();
        }

        void EnsureMenu()
        {
            if (menuBuilt || game == null) return;
            menuBuilt = true;
            var root = (RectTransform)canvas.transform;
            menuRoot = new GameObject("Menu", typeof(RectTransform), typeof(Image));
            var mr = (RectTransform)menuRoot.transform;
            mr.SetParent(root, false);
            Anchor(mr, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(650, 0));
            var bg = menuRoot.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.06f, 0.8f);

            var title = Label("Title", mr, "CHOOSE YOUR CAR", 32, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -22), new Vector2(580, 44));

            int n = game.vehicles.Length;
            playerButtons = new Button[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var p = game.profiles[i];
                string text = $"<b>{p.displayName.ToUpperInvariant()}</b>   {game.vehicles[i].mass:N0} kg\n<size=20>{p.description}</size>";
                playerButtons[i] = MenuButton("Car" + i, mr, text, 24, TextAnchor.MiddleLeft, new Vector2(36, -72 - i * 98), new Vector2(578, 90), () => { game.SelectPlayer(idx); RefreshMenu(); });
            }

            var tl = Label("TargetLabel", mr, "TARGET VEHICLE  (parked: crash into it)", 22, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(tl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -370), new Vector2(580, 28));
            targetButtons = new Button[n];
            string[] shortNames = { "MUSCLE CAR", "HATCHBACK", "PICKUP" };
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                targetButtons[i] = MenuButton("Target" + i, mr, i < shortNames.Length ? shortNames[i] : game.vehicles[i].displayName, 22, TextAnchor.MiddleCenter,
                    new Vector2(36 + i * 196, -400), new Vector2(186, 72), () => { game.SelectTarget(idx); RefreshMenu(); });
            }

            var drive = MenuButton("Drive", mr, "DRIVE", 44, TextAnchor.MiddleCenter, new Vector2(36, -486), new Vector2(578, 96), () => game.CloseMenu());
            drive.targetGraphic.color = new Color(0.95f, 0.48f, 0.1f, 0.95f);
            var colors = drive.colors; colors.normalColor = colors.highlightedColor = Color.white; colors.pressedColor = new Color(0.8f, 0.8f, 0.8f); colors.selectedColor = Color.white; drive.colors = colors;
        }

        static readonly Color Dark = new Color(0.16f, 0.18f, 0.2f, 0.92f), Chosen = new Color(0.95f, 0.48f, 0.1f, 0.95f);

        void RefreshMenu()
        {
            if (!menuBuilt) return;
            for (int i = 0; i < playerButtons.Length; i++) playerButtons[i].targetGraphic.color = i == game.playerIndex ? Chosen : Dark;
            for (int i = 0; i < targetButtons.Length; i++) targetButtons[i].targetGraphic.color = i == game.targetIndex ? Chosen : Dark;
        }

        Button MenuButton(string name, Transform parent, string text, int fontSize, TextAnchor anchor, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeft, size);
            var img = go.GetComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced; img.color = Dark;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, 1f, 1f); colors.pressedColor = new Color(0.75f, 0.75f, 0.75f); colors.selectedColor = Color.white; colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(onClick);
            var t = Label("Label", go.transform, text, fontSize, anchor, FontStyle.Normal);
            t.supportRichText = true;
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(22, 4); rt.offsetMax = new Vector2(-12, -4);
            return b;
        }

        public void SetImpact(string s) { if (impactText != null) impactText.text = s; }

        // ------------------------------------------------------------------ layout

        void Build()
        {
            var cgo = new GameObject("DrivingCanvas");
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;       // between width and height: fits 4:3 tablets and 20:9 phones
            cgo.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)cgo.transform;

            // -------------------------------------------------------------- HUD
            speedText = Label("Speed", root, "0", 64, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(speedText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(360, 90));
            speedText.supportRichText = true;
            impactText = Label("Impact", root, "Drive into something solid to see the damage.", 22, TextAnchor.UpperLeft, FontStyle.Normal);
            Anchor(impactText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -96), new Vector2(500, 120));
            impactText.fontSize = 19;
            hintText = Label("Hint", root, "W / S  accelerate - brake / reverse     A / D  steer     R  reset     M  menu", 22, TextAnchor.LowerCenter, FontStyle.Normal);
            Anchor(hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(1100, 40));
            flipText = Label("Flipped", root, "Flipped?  Press R  (or tap RESET)", 34, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(flipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 60));
            flipText.color = new Color(1f, 0.85f, 0.3f);
            flipText.gameObject.SetActive(false);

            // -------------------------------------------------------------- always-available corner buttons (mouse + touch)
            cornerRoot = new GameObject("CornerButtons", typeof(RectTransform));
            var cr = (RectTransform)cornerRoot.transform;
            cr.SetParent(root, false);
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            var resetBtn = Button("Reset", cr, "RESET", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(160, 84), 28);
            resetBtn.OnChanged = held => { if (held) input.RequestReset(); };
            var changeBtn = Button("ChangeCar", cr, "CHANGE CAR", new Vector2(1, 1), new Vector2(-24 - 160 - 12 - 170 - 12, -24), new Vector2(190, 84), 22);
            changeBtn.OnChanged = held => { if (held) input.RequestMenu(); };
            var toggleBtn = Button("TouchToggle", cr, "TOUCH: OFF", new Vector2(1, 1), new Vector2(-24 - 160 - 12, -24), new Vector2(170, 84), 20);
            toggleBtn.OnChanged = held => { if (held) SetTouch(!touchVisible, true); };
            touchToggleLabel = toggleBtn.GetComponentInChildren<Text>();

            // -------------------------------------------------------------- driving buttons
            touchRoot = new GameObject("TouchControls", typeof(RectTransform));
            var tr = (RectTransform)touchRoot.transform;
            tr.SetParent(root, false);
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;

            const float pad = 36f, big = 210f, gap = 26f;
            left = Pedal("SteerLeft", tr, new Vector2(0, 0), new Vector2(pad, pad), new Vector2(big, big), 90f, "", v => input.touchLeft = v);
            right = Pedal("SteerRight", tr, new Vector2(0, 0), new Vector2(pad + big + gap, pad), new Vector2(big, big), -90f, "", v => input.touchRight = v);
            gas = Pedal("Gas", tr, new Vector2(1, 0), new Vector2(-pad, pad), new Vector2(big, big * 1.45f), 0f, "GAS", v => input.touchGas = v);
            brake = Pedal("Brake", tr, new Vector2(1, 0), new Vector2(-pad - big - gap, pad), new Vector2(big, big), 180f, "BRAKE\nREVERSE", v => input.touchBrake = v);
        }

        Text Label(string name, Transform parent, string text, int size, TextAnchor anchor, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor; t.text = text;
            t.color = Color.white; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        /// <summary>Small labelled corner button.</summary>
        TouchHoldButton Button(string name, Transform parent, string label, Vector2 corner, Vector2 pos, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, corner, corner, corner, pos, size);
            var img = go.GetComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced;
            var b = go.AddComponent<TouchHoldButton>();
            b.face = img; b.idle = new Color(0.08f, 0.09f, 0.1f, 0.62f); b.pressed = new Color(1f, 0.55f, 0.1f, 0.9f);
            img.color = b.idle;
            var t = Label("Label", go.transform, label, fontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return b;
        }

        /// <summary>Large driving button with an arrow (rotation in degrees: 0 = up) and an optional label.</summary>
        TouchHoldButton Pedal(string name, Transform parent, Vector2 corner, Vector2 pos, Vector2 size, float arrowRotation, string label, System.Action<bool> changed)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, corner, corner, corner, pos, size);
            // Pivot follows the corner so the offsets read as margins from the screen edge.
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(corner.x, corner.y);
            var img = go.GetComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced;
            var b = go.AddComponent<TouchHoldButton>();
            b.face = img;
            b.idle = new Color(1f, 1f, 1f, 0.26f);
            b.pressed = new Color(1f, 0.6f, 0.12f, 0.8f);
            img.color = b.idle;
            b.OnChanged = changed;

            var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrow.transform.SetParent(go.transform, false);
            var art = (RectTransform)arrow.transform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, string.IsNullOrEmpty(label) ? 0.5f : 0.62f);
            art.sizeDelta = new Vector2(size.x * 0.42f, size.x * 0.42f);
            art.localRotation = Quaternion.Euler(0f, 0f, arrowRotation);
            var ai = arrow.GetComponent<Image>();
            ai.sprite = triangle; ai.color = new Color(1f, 1f, 1f, 0.9f); ai.raycastTarget = false;

            if (!string.IsNullOrEmpty(label))
            {
                var t = Label("Label", go.transform, label, 30, TextAnchor.LowerCenter, FontStyle.Bold);
                var lrt = t.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(0, 14); lrt.offsetMax = Vector2.zero;
            }
            return b;
        }

        // ------------------------------------------------------------------ procedural sprites (no art assets needed)

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
                    // Upward triangle: apex at the top centre, base along the bottom.
                    float halfWidth = (1f - (y + 0.5f) / size) * 0.5f * size;    // width at this row shrinks towards the top
                    float d = halfWidth - Mathf.Abs(x + 0.5f - size * 0.5f);
                    float a = Mathf.Clamp01(d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));   // row 0 is the bottom: widest
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}

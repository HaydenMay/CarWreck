using DamageLab.Deformation;
using DamageLab.Inspection;
using DamageLab.UI;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.EditorTools
{
    /// <summary>Builds the lab's uGUI layout: top bar, right damage panel, controls hint and rotation bar.</summary>
    public static class DamageLabUIBuilder
    {
        // Palette: neutral greys + orange accent.
        static readonly Color Bar = Hex("1F2124");
        static readonly Color PanelBg = Hex("1A1C1F");
        static readonly Color Card = Hex("26292D");
        static readonly Color CardBorder = Hex("33373C");
        static readonly Color Well = Hex("131416");
        static readonly Color TextMain = Hex("ECEEF0");
        static readonly Color TextDim = Hex("A3A8AE");
        static readonly Color TextFaint = Hex("6E747B");
        static readonly Color Accent = Hex("F27A1A");
        static readonly Color AccentDark = Hex("C95F0E");
        static readonly Color Good = Hex("5FD27A");

        const float PanelWidth = 470f;
        const float TopBarHeight = 64f;

        static Font font;

        public static DamageLabUI Build(Camera cam, VehicleDamageModel model, ModelTurntable turntable, InspectionCameraController camController)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("LabCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            canvas.pixelPerfect = false;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            var root = canvasGO.transform;

            var ui = canvasGO.AddComponent<DamageLabUI>();
            ui.model = model;
            ui.turntable = turntable;
            ui.cameraController = camController;

            BuildTopBar(root);
            BuildRightPanel(root, ui);
            BuildControlsHint(root, ui);
            BuildRotateBar(root, ui, turntable);
            return ui;
        }

        // ------------------------------------------------------------------ sections

        static void BuildTopBar(Transform root)
        {
            var bar = Img("TopBar", root, Bar, null);
            Stretch(bar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, TopBarHeight));
            var line = Img("AccentLine", bar.transform, Hex("2E3135"), null);
            Stretch(line.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 1));

            var badge = Img("Badge", bar.transform, Accent, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            Place(badge.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(42, 42), new Vector2(0, 0.5f));
            var icon = Img("Icon", badge.transform, Color.white, DamageLabAssets.GetSprite("IconCar"));
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));

            var title = Label("Title", bar.transform, "Vehicle Damage Lab", 27, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(300, 40), new Vector2(0, 0.5f));
            float x = 80 + title.preferredWidth + 26;
            foreach (var s in new[] { "Unity 6 URP Prototype", "Vehicle Deformation System" })
            {
                var div = Img("Divider", bar.transform, Hex("4A4F55"), null);
                Place(div.rectTransform, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(2, 26), new Vector2(0, 0.5f));
                var t = Label("Crumb", bar.transform, s, 18, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
                Place(t.rectTransform, new Vector2(0, 0.5f), new Vector2(x + 20, 0), new Vector2(320, 30), new Vector2(0, 0.5f));
                x += 20 + t.preferredWidth + 22;
            }
        }

        static void BuildRightPanel(Transform root, DamageLabUI ui)
        {
            var panel = Img("DamagePanel", root, PanelBg, null);
            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(1, 0); prt.anchorMax = new Vector2(1, 1); prt.pivot = new Vector2(1, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, 0); prt.anchoredPosition = Vector2.zero;
            var edge = Img("Edge", panel.transform, Hex("2E3135"), null);
            Stretch(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(1, 0));

            // Header
            var header = Img("Header", panel.transform, Hex("202226"), null);
            Stretch(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            var wrench = Img("Wrench", header.transform, Accent, DamageLabAssets.GetSprite("IconWrench"));
            Place(wrench.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(58, 58), new Vector2(0, 0.5f));
            var underline = Img("Underline", header.transform, Accent, null);
            Place(underline.rectTransform, new Vector2(0, 0), new Vector2(16, 0), new Vector2(72, 4), new Vector2(0, 0));
            var htitle = Label("Title", header.transform, $"Vehicle <color=#{ColorUtility.ToHtmlStringRGB(Accent)}>Damage Lab</color>", 32, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(htitle.rectTransform, new Vector2(0, 0.5f), new Vector2(100, 14), new Vector2(360, 44), new Vector2(0, 0.5f));
            var hsub = Label("Subtitle", header.transform, "D E F O R M A T I O N    T E S T I N G    T O O L", 13, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Place(hsub.rectTransform, new Vector2(0, 0.5f), new Vector2(102, -20), new Vector2(360, 22), new Vector2(0, 0.5f));

            // Scrollable content column (fits 1080p without scrolling; layout groups keep it tidy).
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(panel.transform, false);
            content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(1, 1);
            content.offsetMin = new Vector2(16, 14); content.offsetMax = new Vector2(-16, -TopBarHeight - 112);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12; vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;

            // Header sits below the top bar.
            header.rectTransform.anchoredPosition = new Vector2(0, -TopBarHeight);

            // Intro card
            var intro = CardBox("IntroCard", content, 84);
            var it = Label("Heading", intro.transform, "Directional Deformation Test", 22, TextMain, FontStyle.Bold, TextAnchor.UpperLeft);
            Stretch(it.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-36, 30));
            var id = Label("Body", intro.transform, "Drag a slider to crush that side of the car. Every value is rebuilt from the original mesh, so 0 always restores it exactly.", 14, TextDim, FontStyle.Normal, TextAnchor.UpperLeft);
            Stretch(id.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(-36, 40));

            // Sliders card
            var sliderCard = CardBox("DamageSliders", content, 4 * 80 + 20);
            var sv = sliderCard.gameObject.AddComponent<VerticalLayoutGroup>();
            sv.padding = new RectOffset(18, 18, 10, 10); sv.spacing = 0;
            sv.childControlHeight = true; sv.childControlWidth = true; sv.childForceExpandHeight = false; sv.childForceExpandWidth = true;
            var sliders = new DamageSliderControl[4];
            string[] names = { "Front Damage", "Rear Damage", "Left Damage", "Right Damage" };
            DamageDirection[] dirs = { DamageDirection.Front, DamageDirection.Rear, DamageDirection.Left, DamageDirection.Right };
            for (int i = 0; i < 4; i++) sliders[i] = SliderRow(sliderCard.transform, names[i], dirs[i], i < 3);
            ui.sliders = sliders;

            // Reset All
            var reset = Img("ResetAllButton", content, Accent, DamageLabAssets.RoundedRectSprite, sliced: true);
            Layout(reset.gameObject, 54);
            var rb = reset.gameObject.AddComponent<Button>();
            rb.targetGraphic = reset;
            rb.colors = ButtonColors();
            var rIcon = Img("Icon", reset.transform, Color.white, DamageLabAssets.GetSprite("IconReset"));
            Place(rIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-66, 0), new Vector2(30, 30));
            var rLabel = Label("Label", reset.transform, "Reset All", 22, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(rLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(16, 0), new Vector2(200, 40));
            ui.resetAllButton = rb;

            // Wireframe toggle
            var wf = CardBox("WireframeRow", content, 58);
            var toggle = wf.gameObject.AddComponent<Toggle>();
            var box = Img("Box", wf.transform, Well, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            Place(box.rectTransform, new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(30, 30), new Vector2(0, 0.5f));
            var check = Img("Checkmark", box.transform, Accent, DamageLabAssets.GetSprite("Check"));
            Place(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = false;
            var wl = Label("Label", wf.transform, "Wireframe", 19, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(wl.rectTransform, new Vector2(0, 0.5f), new Vector2(62, 9), new Vector2(300, 24), new Vector2(0, 0.5f));
            var wd = Label("Hint", wf.transform, "Show the live deformed mesh topology (debug)", 13, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Place(wd.rectTransform, new Vector2(0, 0.5f), new Vector2(62, -12), new Vector2(360, 20), new Vector2(0, 0.5f));
            ui.wireframeToggle = toggle;

            // Quick info
            var info = CardBox("QuickInfo", content, 236);
            var iv = info.gameObject.AddComponent<VerticalLayoutGroup>();
            iv.padding = new RectOffset(18, 18, 10, 8); iv.spacing = 0;
            iv.childControlHeight = true; iv.childControlWidth = true; iv.childForceExpandHeight = false; iv.childForceExpandWidth = true;
            var ih = Label("Heading", info.transform, "Quick Info", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(ih.gameObject, 28);
            InfoRow(info.transform, "Vehicle", "Muscle Car (Prototype)", out _);
            InfoRow(info.transform, "Triangles", "-", out ui.trianglesText);
            InfoRow(info.transform, "Deformable verts", "-", out ui.verticesText);
            InfoRow(info.transform, "Material", "PBR (URP Lit)", out _);
            InfoRow(info.transform, "Damage System", "Procedural crush + folds", out _);
            InfoRow(info.transform, "Mesh rebuild", "-", out ui.deformTimeText);
            InfoRow(info.transform, "Status", "Undamaged", out ui.statusText, last: true);
            ui.statusText.color = Good;
        }

        static DamageSliderControl SliderRow(Transform parent, string title, DamageDirection dir, bool divider)
        {
            var row = new GameObject(title.Replace(" ", "") + "Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            Layout(row.gameObject, 80);

            var label = Label("Label", row, title, 19, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, -22), new Vector2(260, 28), new Vector2(0, 0.5f));

            // numeric box
            var fieldBg = Img("Value", row, Well, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            Place(fieldBg.rectTransform, new Vector2(1, 1), new Vector2(0, -22), new Vector2(70, 34), new Vector2(1, 0.5f));
            var fieldText = Label("Text", fieldBg.transform, "0", 20, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(fieldText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fieldText.supportRichText = false;
            var field = fieldBg.gameObject.AddComponent<InputField>();
            field.textComponent = fieldText;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 3;
            field.targetGraphic = fieldBg;
            field.text = "0";
            field.caretColor = Accent;
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.4f);

            // slider
            var sliderGO = new GameObject("Slider", typeof(RectTransform));
            var srt = sliderGO.GetComponent<RectTransform>();
            srt.SetParent(row, false);
            Stretch(srt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -54), new Vector2(-12, 26));
            var track = Img("Track", srt, Well, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            Stretch(track.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 10));
            var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(srt, false);
            Stretch(fillArea, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 10));
            var fill = Img("Fill", fillArea, Accent, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            fill.rectTransform.sizeDelta = new Vector2(12, 0);
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(srt, false);
            Stretch(handleArea, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var handle = Img("Handle", handleArea, Accent, DamageLabAssets.GetSprite("Circle"));
            handle.rectTransform.sizeDelta = new Vector2(26, 0);
            var dot = Img("Dot", handle.transform, Color.white, DamageLabAssets.GetSprite("Circle"));
            Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            var slider = sliderGO.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true; slider.value = 0;
            slider.colors = ButtonColors();

            var min = Label("Min", row, "0", 13, TextFaint, FontStyle.Normal, TextAnchor.MiddleLeft);
            Place(min.rectTransform, new Vector2(0, 1), new Vector2(0, -72), new Vector2(40, 16), new Vector2(0, 0.5f));
            var max = Label("Max", row, "100", 13, TextFaint, FontStyle.Normal, TextAnchor.MiddleRight);
            Place(max.rectTransform, new Vector2(1, 1), new Vector2(0, -72), new Vector2(40, 16), new Vector2(1, 0.5f));
            if (divider)
            {
                var d = Img("Divider", row, CardBorder, null);
                Stretch(d.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 1));
            }

            var control = row.gameObject.AddComponent<DamageSliderControl>();
            control.direction = dir;
            control.slider = slider;
            control.valueField = field;
            return control;
        }

        static void InfoRow(Transform parent, string key, string value, out Text valueText, bool last = false)
        {
            var row = new GameObject(key.Replace(" ", "") + "Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            Layout(row.gameObject, 28);
            var k = Label("Key", row, key, 15, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(k.rectTransform, new Vector2(0, 0), new Vector2(0.45f, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            valueText = Label("Value", row, value, 15, TextMain, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(valueText.rectTransform, new Vector2(0.45f, 0), new Vector2(1, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            if (!last)
            {
                var d = Img("Divider", row, CardBorder, null);
                Stretch(d.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 1));
            }
        }

        static void BuildControlsHint(Transform root, DamageLabUI ui)
        {
            var card = Img("ControlsHint", root, new Color(0.11f, 0.12f, 0.13f, 0.86f), DamageLabAssets.RoundedRectSprite, sliced: true);
            Place(card.rectTransform, new Vector2(0, 1), new Vector2(20, -TopBarHeight - 20), new Vector2(250, 176), new Vector2(0, 1));
            (string icon, string name, string keys)[] rows =
            {
                ("IconOrbit", "Orbit", "LMB / RMB drag"),
                ("IconPan", "Pan", "MMB / Shift + drag"),
                ("IconZoom", "Zoom", "Scroll wheel"),
            };
            float y = -16;
            foreach (var r in rows)
            {
                var ic = Img(r.icon, card.transform, Accent, DamageLabAssets.GetSprite(r.icon));
                Place(ic.rectTransform, new Vector2(0, 1), new Vector2(16, y), new Vector2(24, 24), new Vector2(0, 1));
                var n = Label("Name", card.transform, r.name, 16, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
                Place(n.rectTransform, new Vector2(0, 1), new Vector2(52, y + 2), new Vector2(70, 28), new Vector2(0, 1));
                var k = Label("Keys", card.transform, r.keys, 13, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
                Place(k.rectTransform, new Vector2(0, 1), new Vector2(112, y + 2), new Vector2(140, 28), new Vector2(0, 1));
                y -= 38;
            }
            var btn = Img("ResetViewButton", card.transform, Hex("33373C"), DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            Place(btn.rectTransform, new Vector2(0, 1), new Vector2(14, y - 4), new Vector2(222, 36), new Vector2(0, 1));
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = btn;
            b.colors = ButtonColors();
            var bi = Img("Icon", btn.transform, Accent, DamageLabAssets.GetSprite("IconTarget"));
            Place(bi.rectTransform, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(22, 22), new Vector2(0, 0.5f));
            var bl = Label("Label", btn.transform, "Reset View  (F)", 15, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(bl.rectTransform, new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(170, 28), new Vector2(0, 0.5f));
            ui.resetViewButton = b;
        }

        static void BuildRotateBar(Transform root, DamageLabUI ui, ModelTurntable turntable)
        {
            // Centered in the free viewport area (screen minus the right panel).
            var bar = Img("RotateModelBar", root, new Color(0.11f, 0.12f, 0.13f, 0.9f), DamageLabAssets.RoundedRectSprite, sliced: true);
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(420, 84);
            rt.anchoredPosition = new Vector2((1920 - PanelWidth) * 0.5f, 24);
            // Keep the bar centered in the viewport area at any aspect ratio.
            var anchorFix = bar.gameObject.AddComponent<ViewportCenter>();
            anchorFix.rightInset = PanelWidth;

            var left = ArrowButton(bar.transform, "RotateLeft", "ArrowLeft", new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(0, 0.5f));
            var right = ArrowButton(bar.transform, "RotateRight", "ArrowRight", new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(1, 0.5f));
            var cap = Label("Caption", bar.transform, "ROTATE MODEL", 15, TextDim, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(cap.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(220, 22));
            var yaw = Label("Yaw", bar.transform, "0°", 24, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(yaw.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(220, 30));
            ui.yawText = yaw;
            turntable.rotateLeftButton = left;
            turntable.rotateRightButton = right;
        }

        static HoldButton ArrowButton(Transform parent, string name, string sprite, Vector2 anchor, Vector2 pos, Vector2 pivot)
        {
            var bg = Img(name, parent, Accent, DamageLabAssets.RoundedRectSprite, sliced: true);
            Place(bg.rectTransform, anchor, pos, new Vector2(84, 60), pivot);
            var hb = bg.gameObject.AddComponent<HoldButton>();
            hb.targetGraphic = bg;
            hb.colors = ButtonColors();
            var icon = Img("Arrow", bg.transform, Color.white, DamageLabAssets.GetSprite(sprite));
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38, 38));
            return hb;
        }

        // ------------------------------------------------------------------ helpers

        static Image CardBox(string name, Transform parent, float height)
        {
            var img = Img(name, parent, Card, DamageLabAssets.RoundedRectSprite, sliced: true);
            img.pixelsPerUnitMultiplier = 1.6f;
            Layout(img.gameObject, height);
            return img;
        }

        static void Layout(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        static ColorBlock ButtonColors()
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            c.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            c.selectedColor = Color.white;
            c.fadeDuration = 0.08f;
            return c;
        }

        static Image Img(string name, Transform parent, Color color, Sprite sprite, bool sliced = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            if (sliced) img.pixelsPerUnitMultiplier = 2f;
            return img;
        }

        static Text Label(string name, Transform parent, string text, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = anchor;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}

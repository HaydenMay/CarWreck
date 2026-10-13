using DamageLab.Inspection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DamageLab.UI
{
    /// <summary>
    /// Adapts the lab layout to the device: bigger UI on phones/small screens, touch control hints,
    /// a "rotate to landscape" overlay in portrait, and keeps the car centred in the free viewport area.
    /// </summary>
    public class ResponsiveLabLayout : MonoBehaviour
    {
        public CanvasScaler scaler;
        public RectTransform sidePanel;
        public GameObject portraitOverlay;
        public InspectionCameraController cameraController;

        public Vector2 desktopReference = new Vector2(1920, 1080);
        [Tooltip("Smaller reference = larger UI. Used on phones / short screens; the side panel scrolls.")]
        public Vector2 compactReference = new Vector2(1100, 600);
        public float compactBelowHeight = 700f;

        [Header("Control hints")]
        public Text[] hintTexts;
        public string[] touchHints = { "One-finger drag", "Two-finger drag", "Pinch" };

        void Start()
        {
            bool touch = Application.isMobilePlatform || Touchscreen.current != null;
            if (touch && hintTexts != null)
                for (int i = 0; i < hintTexts.Length && i < touchHints.Length; i++)
                    if (hintTexts[i] != null) hintTexts[i].text = touchHints[i];
        }

        void LateUpdate()
        {
            bool compact = Application.isMobilePlatform || Screen.height < compactBelowHeight;
            if (scaler != null) scaler.referenceResolution = compact ? compactReference : desktopReference;
            if (portraitOverlay != null)
            {
                bool portrait = Screen.height > Screen.width * 1.05f;
                if (portraitOverlay.activeSelf != portrait) portraitOverlay.SetActive(portrait);
            }
            if (cameraController != null && sidePanel != null && scaler != null)
            {
                float canvasWidth = ((RectTransform)scaler.transform).rect.width;
                if (canvasWidth > 1f) cameraController.screenOffset = 0.5f * sidePanel.rect.width / canvasWidth;
            }
        }
    }
}

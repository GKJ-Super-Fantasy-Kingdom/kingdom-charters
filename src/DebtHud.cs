using SuperFantasyKingdom;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomCharters
{
    internal sealed class DebtHud : MonoBehaviour
    {
        private GameObject root;
        private CanvasScaler scaler;
        private TextMeshProUGUI label;
        private int shownDebt, language;

        private void LateUpdate()
        {
            var run = Plugin.Instance.Run;
            bool show = run != null && run.debt > 0 && run.openingComplete && Supplies.HasMaterialPair &&
                GameManager.Instance != null && GameManager.Instance.IsGameLoaded() &&
                UIManager.Instance != null && !UIManager.Instance.IsHidden() && !UIManager.Instance.IsHiddenByUser() &&
                OverlayManager.Instance != null && !OverlayManager.Instance.IsOpen() &&
                (PauseMenuManger.Instance == null || !PauseMenuManger.Instance.InMenu());
            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;
            if (root == null) Create();
            scaler.scaleFactor = SettingsManager.Instance.GetUIScaling();
            int currentLanguage = SettingsManager.Instance.GetLanguage();
            if (shownDebt == run.debt && language == currentLanguage) return;
            shownDebt = run.debt;
            language = currentLanguage;
            label.text = $"Charter Due: {run.debt} {Supplies.OwedName}";
        }

        private void Create()
        {
            // Keep the corners available for the game's dawn log and input prompts.
            root = new GameObject("Kingdom Charters debt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = root.GetComponent<CanvasScaler>();
            var plate = new GameObject("Balance", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)plate.transform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 9);
            rect.sizeDelta = new Vector2(210, 29);
            var image = plate.GetComponent<Image>();
            image.color = new Color32(23, 26, 35, 230);
            image.raycastTarget = false;
            var text = new GameObject("Debt", typeof(RectTransform), typeof(TextMeshProUGUI));
            label = text.GetComponent<TextMeshProUGUI>();
            var bounds = label.rectTransform;
            bounds.SetParent(rect, false);
            bounds.anchorMin = Vector2.zero;
            bounds.anchorMax = Vector2.one;
            bounds.offsetMin = new Vector2(6, 2);
            bounds.offsetMax = new Vector2(-6, -2);
            var native = UIManager.Instance.GetComponentInChildren<TMP_Text>(true);
            if (native != null) label.font = native.font;
            label.fontSize = 21;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14;
            label.fontSizeMax = 21;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(255, 241, 212, 255);
            label.raycastTarget = false;
            shownDebt = -1;
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root);
        }
    }
}

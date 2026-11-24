using System.Collections.Generic;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
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
        private Image materialIcon;
        private ResourceType shownSlot;
        private RectTransform plate;
        private Transform unitRow;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly List<Graphic> cardGraphics = new List<Graphic>();
        private static readonly AccessTools.FieldRef<UIUnitButtonManager, Transform> UnitRow =
            AccessTools.FieldRefAccess<UIUnitButtonManager, Transform>("unitButtonContainer");

        private void LateUpdate()
        {
            var run = Plugin.Instance?.Run;
            bool show = run != null && run.debt > 0 && run.openingComplete && Supplies.HasMaterialPair &&
                GameManager.Instance != null && GameManager.Instance.IsGameLoaded() &&
                UIManager.Instance != null && !UIManager.Instance.IsHidden() && !UIManager.Instance.IsHiddenByUser() &&
                OverlayManager.Instance != null && !OverlayManager.Instance.IsOpen() &&
                (PauseMenuManger.Instance == null || !PauseMenuManger.Instance.InMenu());
            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;
            if (root == null) Create();
            scaler.scaleFactor = SettingsManager.Instance.GetUIScaling();
            PositionBadge();
            int currentLanguage = SettingsManager.Instance.GetLanguage();
            var slot = Supplies.OwedSlot;
            if (shownDebt == run.debt && language == currentLanguage && shownSlot == slot) return;
            shownSlot = slot;
            materialIcon.sprite = RaceManager.Instance.GetResourceIcon(slot);
            materialIcon.enabled = materialIcon.sprite != null;
            label.rectTransform.offsetMin = new Vector2(materialIcon.enabled ? 32 : 6, 2);
            shownDebt = run.debt;
            language = currentLanguage;
            label.text = $"Charter Due: {run.debt} {Supplies.OwedName}";
        }

        private void PositionBadge()
        {
            // Native units can grow taller when selected or upgraded. Measure the
            // painted parts of the current row in screen pixels, including its focus.
            float scale = Screen.height / ((RectTransform)root.transform).rect.height;
            if (scale <= 0 || float.IsInfinity(scale)) return;
            float bottom = 0;
            if (unitRow != null && unitRow.gameObject.activeInHierarchy)
            {
                float halfWidth = plate.rect.width * scale / 2;
                unitRow.GetComponentsInChildren(false, cardGraphics);
                foreach (var graphic in cardGraphics)
                {
                    if (!graphic.isActiveAndEnabled || graphic.canvasRenderer.cull) continue;
                    graphic.rectTransform.GetWorldCorners(corners);
                    var canvas = graphic.canvas;
                    var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    Vector2 left = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    Vector2 right = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    if (right.x >= Screen.width * .5f - halfWidth && left.x <= Screen.width * .5f + halfWidth)
                        bottom = Mathf.Max(bottom, right.y);
                }
            }
            plate.anchoredPosition = new Vector2(0, bottom / scale + 9);
        }

        private void Create()
        {
            // Keep the corners available for the game's dawn log and input prompts.
            root = new GameObject("Kingdom Charters debt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = root.GetComponent<CanvasScaler>();
            var manager = Object.FindObjectOfType<UIUnitButtonManager>(true);
            unitRow = manager == null ? null : UnitRow(manager);
            var panel = new GameObject("Balance", typeof(RectTransform), typeof(Image));
            var rect = plate = (RectTransform)panel.transform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 9);
            rect.sizeDelta = new Vector2(210, 29);
            var image = panel.GetComponent<Image>();
            image.color = new Color32(23, 26, 35, 230);
            image.raycastTarget = false;
            var icon = new GameObject("Owed material", typeof(RectTransform), typeof(Image));
            materialIcon = icon.GetComponent<Image>();
            materialIcon.raycastTarget = false;
            materialIcon.preserveAspect = true;
            var iconRect = materialIcon.rectTransform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0, .5f);
            iconRect.anchoredPosition = new Vector2(8, 0);
            iconRect.sizeDelta = new Vector2(22, 22);
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

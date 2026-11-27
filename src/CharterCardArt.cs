using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KingdomCharters
{
    internal static class CharterCardArt
    {
        private static readonly Sprite[] Frames = new Sprite[CharterState.Names.Length];

        internal static Sprite For(int index)
        {
            if (Frames[index] != null) return Frames[index];
            using var stream = typeof(CharterCardArt).Assembly.GetManifestResourceStream($"KingdomCharters.Art.{(Charter)index}Frame");
            using var image = new System.IO.MemoryStream();
            stream.CopyTo(image);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(texture, image.ToArray(), true);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return Frames[index] = sprite;
        }
    }

    internal sealed class CharterButton : Button, IPointerMoveHandler
    {
        private CharterPicker picker;
        private GameObject edge;
        private bool card, pointerInside, navigationShown, selectionShown;
        private SelectionState? shownState;

        private bool IsEventSystemSelection => EventSystem.current?.currentSelectedGameObject == gameObject;

        internal void Configure(CharterPicker owner, Sprite selectedPlate, float pixelsPerUnit, bool card = false)
        {
            picker = owner;
            this.card = card;
            if (selectedPlate != null)
            {
                edge = new GameObject(card ? "Focus Ring" : "Focus Edge", typeof(RectTransform), typeof(Image));
                var image = edge.GetComponent<Image>();
                image.sprite = selectedPlate;
                image.type = Image.Type.Sliced;
                image.fillCenter = false;
                image.pixelsPerUnitMultiplier = pixelsPerUnit;
                image.raycastTarget = false;
                var rect = image.rectTransform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                // Keep the artwork inside the perimeter of the interaction edge.
                var outset = card ? new Vector2(4, 4) : Vector2.zero;
                rect.offsetMin = -outset;
                rect.offsetMax = outset;
            }
            DoStateTransition(currentSelectionState, true);
        }

        public override void OnSelect(BaseEventData data)
        {
            if (data is AxisEventData) picker?.KeyboardNavigation();
            base.OnSelect(data);
        }

        public override void OnPointerEnter(PointerEventData data)
        {
            pointerInside = true;
            base.OnPointerEnter(data);
        }

        public override void OnPointerExit(PointerEventData data)
        {
            pointerInside = false;
            base.OnPointerExit(data);
        }

        public override void OnPointerDown(PointerEventData data)
        {
            picker?.PointerNavigation();
            base.OnPointerDown(data);
        }

        public void OnPointerMove(PointerEventData data)
        {
            if (data.delta.sqrMagnitude > 0f) picker?.PointerNavigation();
        }

        private void LateUpdate()
        {
            if (picker != null && (navigationShown != picker.ShowNavigationFocus ||
                (navigationShown && selectionShown != IsEventSystemSelection)))
                DoStateTransition(currentSelectionState, true);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (picker != null)
            {
                navigationShown = picker.ShowNavigationFocus;
                selectionShown = IsEventSystemSelection;
                // Unity retains the clicked object as its submit target. In pointer
                // mode only the control under the pointer should look active. After
                // a page or input change, use the EventSystem's current selection.
                if (state != SelectionState.Disabled &&
                    !(state == SelectionState.Pressed && (navigationShown ? selectionShown : pointerInside)))
                {
                    if (navigationShown)
                        state = selectionShown ? SelectionState.Selected : SelectionState.Normal;
                    else
                        state = pointerInside ? SelectionState.Highlighted : SelectionState.Normal;
                }
            }
            if (shownState == state && !instant) return;
            shownState = state;
            base.DoStateTransition(state, instant);
            if (edge != null)
            {
                // The native hover and press sprites keep their fill and need the
                // outside edge. The selected footer sprite already includes it.
                edge.SetActive(state == SelectionState.Highlighted || state == SelectionState.Pressed ||
                    (card && state == SelectionState.Selected));
            }
        }
    }
}

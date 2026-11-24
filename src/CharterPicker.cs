using System;
using System.Collections.Generic;
using SuperFantasyKingdom;
using SuperFantasyKingdom.TitleScreen;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KingdomCharters
{
    internal sealed class CharterPicker : MonoBehaviour
    {
        private readonly List<Button> buttons = new List<Button>();
        private TitleScreenHeroSelectionManager screen;
        private CanvasGroup underlying;
        private GameObject play, root;
        private RectTransform panel, page;
        private TMP_FontAsset font;
        private Sprite buttonSkin;
        private CanvasScaler scaler;
        private Image backIcon;
        private RectTransform focusMarker;
        private TMP_Text backHint;
        private string scheme;
        private bool review, interactable, raycasts, waitForRelease;
        private int selected, changedFrame = -1, backFrame = -1;
        internal bool IsOpen => root != null;
        internal bool HandledBackThisFrame => backFrame == Time.frameCount;
        private static InputSystemUIInputModule Module => EventSystem.current?.currentInputModule as InputSystemUIInputModule;
        private static readonly Color Ink = new Color32(26, 32, 36, 255);
        private static readonly Color Paper = new Color32(245, 234, 204, 255);

        internal void Open(TitleScreenHeroSelectionManager owner, CanvasGroup group, GameObject opener)
        {
            screen = owner;
            underlying = group;
            play = opener;
            interactable = group.interactable;
            raycasts = group.blocksRaycasts;
            selected = 0;
            review = false;
            var nativeText = play.GetComponentInChildren<TMP_Text>(true) ?? screen.GetComponentInChildren<TMP_Text>(true);
            font = nativeText != null ? nativeText.font : TMP_Settings.defaultFontAsset;
            buttonSkin = (play.GetComponent<Button>()?.targetGraphic as Image)?.sprite;
            root = new GameObject("Kingdom Charters", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            ScaleToScreen();
            var shade = Box(root.transform, "Backdrop", new Color(0.03f, 0.04f, 0.05f, 0.69f));
            Stretch(shade);
            panel = Box(shade, "Charter menu", Ink);
            panel.sizeDelta = new Vector2(852, 644);
            group.interactable = false;
            group.blocksRaycasts = false;
            Draw();
        }

        private void ScaleToScreen()
        {
            float preferred = SettingsManager.Instance != null ? SettingsManager.Instance.GetUIScaling() : 1f;
            scaler.scaleFactor = Mathf.Min(Mathf.Max(preferred, 1f), Screen.width / 876f, Screen.height / 668f);
        }

        private void Draw()
        {
            changedFrame = Time.frameCount;
            if (page != null) { page.gameObject.SetActive(false); Destroy(page.gameObject); }
            buttons.Clear();
            page = Child(panel, "Choices");
            Stretch(page);
            Label(page, review ? "Review Your Charter" : "Kingdom Charters", 30, 16, 780, 52, 36);
            if (review)
            {
                Label(page, CharterState.Names[selected], 34, 78, 780, 55, 32);
                Label(page, selected == (int)Charter.Merchant ? "Caravan Schedule" : "Starting Supplies",
                    36, 150, 780, 34, 22).color = new Color32(255, 211, 126, 255);
                Label(page, Benefit(selected), 36, 187, 776, 58, 25);
                string cost = CharterState.Costs[selected];
                if (cost.Length != 0)
                {
                    Label(page, selected == (int)Charter.Merchant ? "Selection Fee" : "Repayment",
                        36, 253, 780, 34, 22).color = new Color32(255, 211, 126, 255);
                    Label(page, cost, 36, 289, 776, 67, 25);
                }
                AddButton("Start Run", 365, 70, Commit);
                AddButton("Back to Charters", 450, 65, Back);
            }
            else
            {
                for (int i = 0; i < CharterState.Names.Length; i++)
                {
                    int index = i;
                    AddButton(CharterState.Names[i] + "\n" + Terms(i), 78 + i * 101, 92, () => Inspect(index));
                }
                AddButton("Back to Hero Selection", 491, 55, Back);
            }
            WireNavigation();
            backIcon = Icon(34);
            backHint = Label(page, "", 79, 570, 720, 47, 19);
            scheme = null;
            RefreshPrompts();
            focusMarker = Box(page, "Focused choice", new Color32(255, 208, 83, 255));
            focusMarker.GetComponent<Image>().raycastTarget = false;
            Focus(review ? 0 : selected);
        }

        private string Terms(int index)
        {
            if (index == (int)Charter.Merchant && Plugin.DailyCaravan)
                return "Unavailable while Daily Caravan sets the schedule.";
            var cost = CharterState.Costs[index];
            return cost.Length == 0 ? Benefit(index) : Benefit(index) + " " + cost;
        }

        private static string Benefit(int index) =>
            index == (int)Charter.Merchant && Merchant.OtherSchedule
                ? "Another mod controls arrivals."
                : CharterState.Benefits[index];

        private void Inspect(int index)
        {
            if (Time.frameCount == changedFrame) return;
            selected = index;
            if (index == (int)Charter.Merchant && Plugin.DailyCaravan) { Focus(index); return; }
            review = true;
            waitForRelease = Module?.submit?.action?.IsPressed() == true;
            Draw();
        }

        private void Commit()
        {
            if (!review || waitForRelease || changedFrame == Time.frameCount) return;
            if (selected == (int)Charter.Merchant && Plugin.DailyCaravan) { Back(); return; }
            var owner = screen;
            var choice = (Charter)selected;
            Close();
            Plugin.Instance.StartRun(owner, choice);
        }

        internal void Back()
        {
            if (!IsOpen || changedFrame == Time.frameCount || HandledBackThisFrame) return;
            backFrame = Time.frameCount;
            if (review) { review = false; Draw(); }
            else Close();
        }

        private void Close()
        {
            if (underlying != null)
            {
                underlying.interactable = interactable;
                underlying.blocksRaycasts = raycasts;
            }
            root.SetActive(false);
            Destroy(root);
            root = null;
            if (play != null && EventSystem.current != null)
            {
                GamepadManager.Instance.SetGameObjectToSelect(play, true);
                EventSystem.current.SetSelectedGameObject(play);
            }
        }

        private void OnDestroy() { if (IsOpen) Close(); }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            if (screen == null) { Close(); return; }
            ScaleToScreen();
            var module = Module;
            var keyboard = Keyboard.current;
            if (waitForRelease && module?.submit?.action?.IsPressed() != true) waitForRelease = false;
            if (changedFrame == Time.frameCount) return;
            if (module?.cancel?.action?.WasPerformedThisFrame() == true ||
                keyboard?.escapeKey.wasPressedThisFrame == true) { Back(); return; }
            // Use the UI module for normal submission and movement. Some title-screen schemes
            // expose only gamepad navigation, so retain keyboard access in those schemes.
            if (keyboard != null && module?.move?.action?.WasPerformedThisFrame() != true)
            {
                if (keyboard.downArrowKey.wasPressedThisFrame) Step(1);
                else if (keyboard.upArrowKey.wasPressedThisFrame) Step(-1);
            }
            if (keyboard?.enterKey.wasPressedThisFrame == true && module?.submit?.action?.WasPerformedThisFrame() != true)
            {
                int index = FocusIndex();
                if (index >= 0) buttons[index].onClick.Invoke();
            }
            var focused = FocusIndex();
            if (focused < 0) Focus(review ? 0 : selected);
            else
            {
                PlaceFocusMarker(focused);
                if (!review && focused < CharterState.Names.Length) selected = focused;
                var pad = GamepadManager.Instance;
                if (pad != null && pad.GetGameObjectToSelect() != buttons[focused].gameObject)
                    pad.SetGameObjectToSelect(buttons[focused].gameObject, true);
            }
            RefreshPrompts();
        }

        private void RefreshPrompts()
        {
            var input = GamepadManager.Instance?.GetPlayerInput();
            string current = input?.currentControlScheme ?? "";
            if (scheme == current) return;
            scheme = current;
            string back = Prompt(Module?.cancel?.action, current, backIcon, "Esc");
            backHint.text = backIcon.enabled ? "Back" : string.IsNullOrEmpty(back) ? "" : $"{back}  Back";
            Position(backHint.rectTransform, backIcon.enabled ? 79 : 34, 570, 720, 47);
        }

        private static string Prompt(InputAction action, string group, Image image, string keyboardFallback)
        {
            image.enabled = false;
            if (action == null || string.IsNullOrEmpty(group)) return keyboardFallback;
            int index = action.GetBindingIndex(group);
            if (index < 0) return GamepadManager.Instance.IsGamepadInUse() ? "" : keyboardFallback;
            var binding = action.bindings[index];
            string path = binding.effectivePath;
            if (!string.IsNullOrEmpty(path))
            {
                image.sprite = GamepadManager.Instance.GetButtonPrompt(path);
                image.enabled = image.sprite != null;
            }
            return action.GetBindingDisplayString(index);
        }

        private int FocusIndex()
        {
            var obj = EventSystem.current?.currentSelectedGameObject;
            return buttons.FindIndex(b => b.gameObject == obj);
        }

        private void Step(int delta) => Focus((Math.Max(0, FocusIndex()) + delta + buttons.Count) % buttons.Count);
        private void Focus(int index)
        {
            PlaceFocusMarker(index);
            var target = buttons[index].gameObject;
            GamepadManager.Instance?.SetGameObjectToSelect(target, true);
            EventSystem.current?.SetSelectedGameObject(target);
        }

        private void PlaceFocusMarker(int index)
        {
            if (focusMarker == null) return;
            var row = (RectTransform)buttons[index].transform;
            Position(focusMarker, 32, Mathf.RoundToInt(-row.anchoredPosition.y) + 12, 5,
                Mathf.RoundToInt(row.sizeDelta.y) - 24);
        }

        private void WireNavigation()
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i + buttons.Count - 1) % buttons.Count],
                    selectOnDown = buttons[(i + 1) % buttons.Count] };
        }

        private void AddButton(string caption, int y, int height, Action action)
        {
            var rect = Box(page, caption.Split('\n')[0], Color.white);
            Position(rect, 28, y, 796, height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var face = (Image)button.targetGraphic;
            if (buttonSkin != null)
            {
                face.sprite = buttonSkin;
                face.type = Image.Type.Sliced;
            }
            var colors = button.colors;
            colors.normalColor = new Color32(182, 196, 207, 255);
            colors.highlightedColor = colors.selectedColor = new Color32(255, 190, 77, 255);
            colors.pressedColor = new Color32(235, 160, 62, 255);
            button.colors = colors;
            int split = caption.IndexOf('\n');
            bool choice = split >= 0;
            if (choice)
                Label(rect, caption.Substring(0, split), 22, 6, 752, 33, 25).color = Color.white;
            var captionLabel = choice
                ? Label(rect, caption.Substring(split + 1), 22, 39, 752, height - 45, 20)
                : Label(rect, caption, 20, 7, 752, height - 14, 23);
            captionLabel.color = Color.white;
            captionLabel.enableAutoSizing = true;
            captionLabel.fontSizeMin = 17;
            captionLabel.fontSizeMax = choice ? 20 : 23;
            button.onClick.AddListener(() => action());
            buttons.Add(button);
        }

        private Image Icon(int x)
        {
            var rt = Box(page, "Input prompt", Color.white);
            Position(rt, x, 578, 32, 32);
            var img = rt.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private TMP_Text Label(Transform parent, string value, int x, int y, int w, int h, int size)
        {
            var rt = Child(parent, "Label");
            Position(rt, x, y, w, h);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.font = font;
            label.fontSize = size;
            label.color = Paper;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Box(Transform parent, string name, Color color)
        {
            var rt = Child(parent, name);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt;
        }

        private static RectTransform Child(Transform parent, string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Position(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}

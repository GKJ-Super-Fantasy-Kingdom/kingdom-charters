using System;
using System.Collections.Generic;
using SuperFantasyKingdom;
using SuperFantasyKingdom.TitleScreen;
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
        private Font font;
        private Image submitIcon, backIcon;
        private Text submitHint, backHint;
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
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            root = new GameObject("Kingdom Charters", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1000, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var shade = Box(root.transform, "Backdrop", new Color(0.03f, 0.04f, 0.05f, 0.90f));
            Stretch(shade);
            panel = Box(shade, "Charter menu", Ink);
            panel.sizeDelta = new Vector2(852, 644);
            group.interactable = false;
            group.blocksRaycasts = false;
            Draw();
        }

        private void Draw()
        {
            changedFrame = Time.frameCount;
            if (page != null) { page.gameObject.SetActive(false); Destroy(page.gameObject); }
            buttons.Clear();
            page = Child(panel, "Choices");
            Stretch(page);
            Label(page, review ? "Review your charter" : "Kingdom Charters", 30, 16, 780, 52, 36);
            if (review)
            {
                Label(page, CharterState.Names[selected], 30, 111, 790, 64, 34);
                Label(page, Terms(selected), 30, 185, 780, 132, 25);
                AddButton("Start Run", 365, 70, Commit);
                AddButton("Back to charters", 450, 65, Back);
            }
            else
            {
                for (int i = 0; i < CharterState.Names.Length; i++)
                {
                    int index = i;
                    AddButton(CharterState.Names[i] + "\n" + Terms(i), 78 + i * 101, 92, () => Inspect(index));
                }
                AddButton("Back to hero selection", 491, 55, Back);
            }
            WireNavigation();
            submitIcon = Icon(34);
            backIcon = Icon(420);
            submitHint = Label(page, "", 79, 570, 335, 47, 19);
            backHint = Label(page, "", 465, 570, 340, 47, 19);
            scheme = null;
            RefreshPrompts();
            Focus(review ? 0 : selected);
        }

        private string Terms(int index) => index == (int)Charter.Merchant && Plugin.DailyCaravan
            ? "Unavailable with Daily Caravan. " + CharterState.Terms[index] : CharterState.Terms[index];

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
            string ok = Prompt(Module?.submit?.action, current, submitIcon, "Enter");
            string back = Prompt(Module?.cancel?.action, current, backIcon, "Esc");
            submitHint.text = $"{ok}  {(review ? "Confirm" : "Inspect choice")}";
            backHint.text = $"{back}  Back";
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
            var target = buttons[index].gameObject;
            GamepadManager.Instance?.SetGameObjectToSelect(target, true);
            EventSystem.current?.SetSelectedGameObject(target);
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
            var colors = button.colors;
            colors.normalColor = new Color32(56, 65, 67, 255);
            colors.highlightedColor = colors.selectedColor = new Color32(102, 87, 48, 255);
            colors.pressedColor = new Color32(132, 105, 55, 255);
            button.colors = colors;
            var captionLabel = Label(rect, caption, 18, 1, 758, height - 2, 21);
            captionLabel.resizeTextForBestFit = true;
            captionLabel.resizeTextMinSize = 16;
            captionLabel.resizeTextMaxSize = 21;
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

        private Text Label(Transform parent, string value, int x, int y, int w, int h, int size)
        {
            var rt = Child(parent, "Label");
            Position(rt, x, y, w, h);
            var label = rt.gameObject.AddComponent<Text>();
            label.text = value;
            label.font = font;
            label.fontSize = size;
            label.color = Paper;
            label.alignment = TextAnchor.MiddleLeft;
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

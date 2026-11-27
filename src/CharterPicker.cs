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
        private Button footerTemplate;
        private Canvas canvas;
        private Image backIcon;
        private TMP_Text backLabel;
        private Button backButton;
        private Button startButton;
        private int footerReturn;
        private bool review, interactable, raycasts, waitForRelease, keyboardNavigation;
        private int selected, changedFrame = -1, backFrame = -1;
        internal bool IsOpen => root != null;
        internal bool HandledBackThisFrame => backFrame == Time.frameCount;
        private static bool GamepadInUse
        {
            get
            {
                var input = GamepadManager.Instance;
                var scheme = input?.GetPlayerInput()?.currentControlScheme;
                return string.IsNullOrEmpty(scheme) ? input?.IsGamepadInUse() == true : scheme == "Gamepad";
            }
        }
        internal bool ShowNavigationFocus => GamepadInUse || keyboardNavigation;
        internal void KeyboardNavigation() { if (!GamepadInUse) keyboardNavigation = true; }
        internal void PointerNavigation() { if (!GamepadInUse) keyboardNavigation = false; }
        private static InputSystemUIInputModule Module => EventSystem.current?.currentInputModule as InputSystemUIInputModule;
        private const int CardWidth = 280, CardHeight = 420, Gap = 24, CardTop = 92;
        private const int PanelWidth = 4 * CardWidth + 3 * Gap + 64, ReviewWidth = 732, PanelHeight = 592;
        private const int FooterTop = 530, FooterHeight = 36, FooterWidth = 230, FooterGap = 16;
        private static readonly Color Paper = new Color32(245, 234, 204, 255);

        internal void Open(TitleScreenHeroSelectionManager owner, CanvasGroup group, GameObject opener)
        {
            screen = owner;
            underlying = group;
            play = opener;
            interactable = group.interactable;
            raycasts = group.blocksRaycasts;
            selected = footerReturn = 0;
            review = false;
            keyboardNavigation = false;
            var nativeText = play.GetComponentInChildren<TMP_Text>(true) ?? screen.GetComponentInChildren<TMP_Text>(true);
            font = nativeText != null ? nativeText.font : TMP_Settings.defaultFontAsset;
            // This title-safe prefab has the same cancel plate as Unit and Relic Selection.
            var message = Resources.Load<GameObject>("UI/OverlayMessage");
            footerTemplate = message?.transform.Find("Buttons/Cancel")?.GetComponent<Button>();
            root = new GameObject("Kingdom Charters", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            ScaleToScreen();
            SettingsManager.OnResolutionChange += OnResolutionChange;
            var shield = Box(root.transform, "Modal input", Color.clear);
            Stretch(shield);
            var shade = Box(shield, "Backdrop", new Color(0.03f, 0.04f, 0.05f, 0.78f));
            Stretch(shade);
            shade.offsetMax = new Vector2(0, -52); // Leave the title screen's resource row visible.
            shade.GetComponent<Image>().raycastTarget = false;
            panel = Child(shield, "Charter menu");
            group.interactable = false;
            group.blocksRaycasts = false;
            Draw();
        }

        private void ScaleToScreen()
        {
            float preferred = SettingsManager.Instance != null ? SettingsManager.Instance.GetUIScaling() : 1f;
            canvas.scaleFactor = Mathf.Min(Mathf.Max(preferred, 1f), Screen.width / (PanelWidth + 24f), Screen.height / (PanelHeight + 24f));
        }

        private void OnResolutionChange(int _) => ScaleToScreen();

        private void Draw()
        {
            changedFrame = Time.frameCount;
            if (page != null) { page.gameObject.SetActive(false); Destroy(page.gameObject); }
            buttons.Clear();
            startButton = null;
            page = Child(panel, "Choices");
            Stretch(page);
            int width = review ? ReviewWidth : PanelWidth;
            panel.sizeDelta = new Vector2(width, PanelHeight);
            var heading = Box(page, "Chooser heading", new Color32(18, 22, 28, 220));
            Position(heading, (width - 356) / 2, 24, 356, 47);
            heading.GetComponent<Image>().raycastTarget = false;
            CenterLabel(heading, review ? "Review Your Charter" : "Kingdom Charters", 8, 0, 340, 47, 33);
            if (review)
            {
                AddCard(selected, (width - CardWidth) / 2, false);
                int left = (width - 2 * FooterWidth - FooterGap) / 2;
                startButton = Footer("Start Run", left + FooterWidth + FooterGap, Commit, out _);
                buttons.Add(startButton);
                backButton = AddBack("Back to Charters", left);
            }
            else
            {
                for (int i = 0; i < CharterState.Names.Length; i++)
                    buttons.Add(AddCard(i, 32 + i * (CardWidth + Gap), true));
                backButton = AddBack("Back to Selection", (PanelWidth - FooterWidth) / 2);
            }
            buttons.Add(backButton);
            WireNavigation();
            RefreshPrompts();
            Focus(review ? startButton : buttons[selected]);
        }

        private static readonly Color Gold = new Color32(255, 211, 126, 255);

        private static string AvailabilityReason(int index, RaceManager race)
        {
            if (index == (int)Charter.Merchant && Plugin.DailyCaravan)
                return "Daily Caravan sets the schedule.";
            if (index >= (int)Charter.Forester && race != null && !Supplies.TryMaterials(race, out _, out _))
                return "This kingdom needs two distinct stored materials.";
            return null;
        }

        private static string UnavailableReason(int index)
        {
            // The base game creates RaceManager with the kingdom scene. A provider
            // may already expose it here. Never use a previously loaded kingdom's data.
            var race = RaceManager.Instance;
            if (race != null && MainManager.Instance != null && race.GetRace() != MainManager.Instance.GetSelectedKingdom()) race = null;
            return AvailabilityReason(index, race);
        }

        private static string Benefit(int index) =>
            index == (int)Charter.Merchant && Merchant.OtherSchedule
                ? "Another mod controls arrivals."
                : CharterState.Benefits[index];

        private void Inspect(int index)
        {
            if (Time.frameCount == changedFrame) return;
            if (UnavailableReason(index) != null) return;
            selected = footerReturn = index;
            review = true;
            waitForRelease = Module?.submit?.action?.IsPressed() == true;
            Draw();
        }

        private void Commit()
        {
            if (!review || waitForRelease || changedFrame == Time.frameCount) return;
            if (UnavailableReason(selected) != null) { Back(); return; }
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
            SettingsManager.OnResolutionChange -= OnResolutionChange;
            root.SetActive(false);
            Destroy(root);
            root = null;
            if (play != null && EventSystem.current != null)
            {
                GamepadManager.Instance?.SetGameObjectToSelect(play, true);
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
            var mouse = Mouse.current;
            if (GamepadInUse || (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0f ||
                mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)))
                keyboardNavigation = false;
            if (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame ||
                keyboard.rightArrowKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.downArrowKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame))
                KeyboardNavigation();
            if (waitForRelease && module?.submit?.action?.IsPressed() != true) waitForRelease = false;
            if (changedFrame == Time.frameCount) return;
            if (module?.cancel?.action?.WasPerformedThisFrame() == true ||
                keyboard?.escapeKey.wasPressedThisFrame == true) { Back(); return; }
            // Use the UI module for normal submission and movement. Some title-screen schemes
            // expose only gamepad navigation, so retain keyboard access in those schemes.
            if (keyboard != null && module?.move?.action?.WasPerformedThisFrame() != true)
            {
                var focus = FocusedButton();
                if (focus != null)
                {
                    if (keyboard.leftArrowKey.wasPressedThisFrame) Focus(focus.FindSelectableOnLeft() as Button);
                    else if (keyboard.rightArrowKey.wasPressedThisFrame) Focus(focus.FindSelectableOnRight() as Button);
                    else if (keyboard.upArrowKey.wasPressedThisFrame) Focus(focus.FindSelectableOnUp() as Button);
                    else if (keyboard.downArrowKey.wasPressedThisFrame) Focus(focus.FindSelectableOnDown() as Button);
                }
            }
            var focused = FocusedButton();
            if (keyboard?.enterKey.wasPressedThisFrame == true && module?.submit?.action?.WasPerformedThisFrame() != true)
                focused?.onClick.Invoke();
            if (changedFrame == Time.frameCount || !IsOpen) return;
            if (focused == null || !focused.IsInteractable()) Focus(review ? startButton : buttons[selected]);
            else
            {
                if (!review && focused != backButton)
                {
                    selected = footerReturn = buttons.IndexOf(focused);
                    UpdateBackNavigation();
                }
                var pad = GamepadManager.Instance;
                if (pad != null && pad.GetGameObjectToSelect() != focused.gameObject)
                    pad.SetGameObjectToSelect(focused.gameObject, true);
            }
            RefreshPrompts();
        }

        private void RefreshPrompts()
        {
            var input = GamepadManager.Instance?.GetPlayerInput();
            bool hadIcon = backIcon.enabled;
            string back = Prompt(Module?.cancel?.action, input?.currentControlScheme ?? "", backIcon, "Esc");
            string caption = backButton.name;
            if (!backIcon.enabled && !string.IsNullOrEmpty(back)) caption = $"{back}  {caption}";
            if (backLabel.text == caption && hadIcon == backIcon.enabled) return;
            backLabel.text = caption;
            int left = backIcon.enabled ? 44 : 10;
            Position(backLabel.rectTransform, left, 2, FooterWidth - left - 10, FooterHeight - 4);
        }

        private static string Prompt(InputAction action, string group, Image image, string keyboardFallback)
        {
            image.enabled = false;
            if (action == null || string.IsNullOrEmpty(group)) return keyboardFallback;
            int index = action.GetBindingIndex(group);
            if (index < 0) return GamepadManager.Instance?.IsGamepadInUse() == true ? "" : keyboardFallback;
            var binding = action.bindings[index];
            string path = binding.effectivePath;
            if (!string.IsNullOrEmpty(path))
            {
                image.sprite = GamepadManager.Instance.GetButtonPrompt(path);
                image.enabled = image.sprite != null;
            }
            return !string.IsNullOrEmpty(path) ? action.GetBindingDisplayString(index)
                : GamepadManager.Instance?.IsGamepadInUse() == true ? "" : keyboardFallback;
        }

        private Button FocusedButton()
        {
            var current = EventSystem.current?.currentSelectedGameObject;
            return buttons.Find(button => button.gameObject == current);
        }

        private void Focus(Button button)
        {
            if (button == null || !button.IsInteractable()) return;
            GamepadManager.Instance?.SetGameObjectToSelect(button.gameObject, true);
            EventSystem.current?.SetSelectedGameObject(button.gameObject);
        }

        private void WireNavigation()
        {
            if (review)
            {
                Navigate(startButton, backButton, backButton, backButton, backButton);
                Navigate(backButton, startButton, startButton, startButton, startButton);
                return;
            }
            for (int i = 0; i < CharterState.Names.Length; i++)
            {
                var card = buttons[i];
                if (!card.interactable) continue;
                Navigate(card, Neighbor(i, -1), Neighbor(i, 1), backButton, backButton);
            }
            if (!buttons[selected].interactable) selected = footerReturn = 0;
            UpdateBackNavigation();
        }

        private Button Neighbor(int index, int direction)
        {
            for (int i = index + direction; i >= 0 && i < CharterState.Names.Length; i += direction)
                if (buttons[i].interactable) return buttons[i];
            return null;
        }

        private void UpdateBackNavigation()
        {
            var card = buttons[footerReturn];
            Navigate(backButton, null, null, card, card);
        }

        private static void Navigate(Button button, Button left, Button right, Button up, Button down) =>
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnLeft = left, selectOnRight = right, selectOnUp = up, selectOnDown = down };

        private Button AddCard(int index, int x, bool interactive)
        {
            string title = CharterState.Names[index];
            string unavailable = UnavailableReason(index);
            var rect = Box(page, title, Color.white);
            Position(rect, x, CardTop, CardWidth, CardHeight);
            var face = rect.GetComponent<Image>();
            face.sprite = CharterCardArt.For(index);
            face.raycastTarget = interactive;
            CenterLabel(rect, title, 24, 179, 232, 38, 26).fontStyle = FontStyles.Bold;

            if (unavailable != null)
            {
                face.color = new Color32(158, 158, 158, 255);
                CenterLabel(rect, "Unavailable", 22, 233, 236, 31, 22).color = Gold;
                CenterLabel(rect, unavailable, 22, 273, 236, 102, 23);
            }
            else if (index == (int)Charter.Standard)
                CenterLabel(rect, Benefit(index), 22, 232, 236, 74, 22);
            else
            {
                CenterLabel(rect, Benefit(index), 22, 232, 236, 74, 22);
                var divider = Box(rect, "Terms divider", new Color32(192, 175, 139, 125));
                Position(divider, 54, 310, 172, 2);
                divider.GetComponent<Image>().raycastTarget = false;
                CenterLabel(rect, CharterState.Costs[index], 22, 317, 236, 76, 22);
            }
            return interactive ? MakeButton(rect, unavailable == null, () => Inspect(index)) : null;
        }

        private Button Footer(string caption, int x, Action action, out TMP_Text label)
        {
            var rect = Box(page, caption, new Color32(145, 43, 28, 255));
            Position(rect, x, FooterTop, FooterWidth, FooterHeight);
            var face = rect.GetComponent<Image>();
            var button = rect.gameObject.AddComponent<CharterButton>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => action());

            if (footerTemplate?.targetGraphic is Image source)
            {
                face.sprite = source.sprite;
                face.type = source.type;
                face.color = source.color;
                face.pixelsPerUnitMultiplier = EdgePixelsPerUnit(source);
                button.spriteState = footerTemplate.spriteState;
                button.transition = Selectable.Transition.SpriteSwap;
            }
            button.Configure(this, button.spriteState.selectedSprite, face.pixelsPerUnitMultiplier);

            label = CenterLabel(rect, caption, 10, 2, FooterWidth - 20, FooterHeight - 4, 20);
            var nativeLabel = footerTemplate?.transform.Find("Text")?.GetComponent<TMP_Text>();
            if (nativeLabel?.font != null) label.font = nativeLabel.font;
            return button;
        }

        private float EdgePixelsPerUnit(Image source) =>
            source.pixelsPerUnitMultiplier * 3f * (canvas.referencePixelsPerUnit / 100f);

        private Button MakeButton(RectTransform rect, bool available, Action action)
        {
            var button = rect.gameObject.AddComponent<CharterButton>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = new Color(.93f, .93f, .93f);
            colors.selectedColor = colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.9f, .83f, .68f);
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.interactable = available;
            if (!available)
            {
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                return button;
            }
            if (footerTemplate?.targetGraphic is Image nativePlate)
                button.Configure(this, footerTemplate.spriteState.selectedSprite, EdgePixelsPerUnit(nativePlate), card: true);
            button.onClick.AddListener(() => action());
            return button;
        }

        private TMP_Text CenterLabel(Transform parent, string value, int x, int y, int w, int h, int size)
        {
            var label = Label(parent, value, x, y, w, h, size);
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Max(18, size - 4);
            return label;
        }

        private Button AddBack(string caption, int x)
        {
            var button = Footer(caption, x, Back, out backLabel);
            var icon = Box(button.transform, "Input prompt", Color.white);
            Position(icon, 15, (FooterHeight - 24) / 2, 24, 24);
            backIcon = icon.GetComponent<Image>();
            backIcon.enabled = false;
            backIcon.preserveAspect = true;
            backIcon.raycastTarget = false;
            return button;
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

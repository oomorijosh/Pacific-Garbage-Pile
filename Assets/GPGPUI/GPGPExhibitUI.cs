using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GPGPUI
{
    /// <summary>
    /// A world-space museum interface shared by the two Cyber Canoe eye cameras.
    /// Owns only its canvas and preview stage; never changes the existing rig,
    /// exhibit, input actions, EventSystem selection, or source model materials.
    /// </summary>
    public sealed class GPGPExhibitUI : MonoBehaviour
    {
        public Transform sourceScreen;
        public Transform ccHub;
        public CC_CAMERA_SETUP cameraSetup;
        public GPGPLayeredCubeVisualization exhibit;
        public GameObject turtleModel;
        public TMP_FontAsset font;
        [Tooltip("Featured model centre in metres toward the viewer from the display plane.")]
        [Range(.1f, .8f)] public float foregroundDepthMeters = .4f;
        [Tooltip("Seconds per Overview card while automatic cycling is running.")]
        [Min(5f)] public float overviewIntervalSeconds = 25f;
        public CCaux_Waypointer waypointer;

        public int SelectedCategory { get; private set; } = 3;
        public bool HasSelectedCategory { get; private set; }
        public int CurrentView { get; private set; }
        public int CurrentCard { get; private set; }
        public int FocusRow { get; private set; }
        public int FocusIndex { get; private set; }
        public bool AutoCycling { get; private set; } = true;
        public bool IsUIHidden { get; private set; }
        public GameObject FeaturedSource { get; private set; }
        public GameObject FeaturedObject { get; private set; }
        public Transform CanvasTransform => canvasRect;
        bool HasCountryCard => FeaturedSource &&
            GPGPUIContent.EligibleCountryContext(SelectedCategory, FeaturedSource.name);
        InfoCard[] CurrentDebrisCards => GPGPUIContent.DebrisCards(SelectedCategory,
            FeaturedSource ? FeaturedSource.name : null);
        public bool ShowsCountryContext => CurrentView == 1 &&
            CurrentCard == CurrentDebrisCards.Length && HasCountryCard;

        const float Width = 4800f, Height = 1620f;
        const int PreviewLayer = 30;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const float BoundsEpsilon = .000001f;
        const float PanelSlideSeconds = .35f;
        const float SideSlide = 1400f, HeaderSlide = 160f, BottomSlide = 500f;
        static readonly Color Ink = new Color(.022f, .045f, .068f, 1f);
        static readonly Color Surface = new Color(.035f, .075f, .105f, 1f);
        static readonly Color White = new Color(.93f, .96f, .95f);
        static readonly Color Muted = new Color(.62f, .73f, .77f);
        static readonly Color Cyan = new Color(.23f, .85f, .91f);
        static readonly string[] Views = { "Overview", "Debris sizes", "Wildlife", "Take action" };
        static readonly string[] ShortSizes = { "Mega", "Macro", "Meso", "Micro" };
        readonly List<Control> controls = new List<Control>();
        readonly GameObject[] lastSource = new GameObject[4];
        RectTransform canvasRect;
        RectTransform leftPanelGroup, rightPanelGroup, headerGroup, bottomPanelGroup;
        Transform stage, modelPivot;
        TMP_Text title, badge, body, source, page, featureTitle, featureCaption, cycleHint;
        TMP_Text factHeadline, primaryMetricLabel, secondaryMetricLabel;
        RectTransform chart, comparison, primaryMetricShare, secondaryMetricShare;
        CCaux_Waypointer subscribedWaypointer;
        CanvasGroup cardFade;
        System.Random random;
        float elapsed, fadeElapsed = 1f, repeatElapsed;
        Vector2Int previousDirection;
        float modelDiameter;
        float hiddenProgress;
        bool initialized;

        sealed class Control
        {
            public int Row, Index;
            public Image Background;
            public Outline Outline;
            public TMP_Text Label;
            public TMP_Text SelectedMarker;
            public Image SelectedStripe;
        }

        void Awake()
        {
            // A saved preview exists even when the component has never run.
            // Hide it if interaction/UI was disabled before loading the scene.
            if (enabled) return;
            Transform savedCanvas = transform.Find("GPGP UI Canvas");
            Transform savedStage = transform.Find("Featured Debris Stage");
            if (savedCanvas) savedCanvas.gameObject.SetActive(false);
            if (savedStage) savedStage.gameObject.SetActive(false);
        }

        void Start() => BuildPreview();

        void OnEnable()
        {
            BindWaypointer();
            if (!initialized) return;
            if (canvasRect) canvasRect.gameObject.SetActive(true);
            ApplyUIVisibility();
            if (waypointer) SyncToWaypoint(waypointer.CurrentWaypointIndex);
        }

        void OnDisable()
        {
            if (subscribedWaypointer) subscribedWaypointer.OnWaypointChanged -= SyncToWaypoint;
            subscribedWaypointer = null;
            if (!Application.isPlaying) return;
            if (canvasRect) canvasRect.gameObject.SetActive(false);
            if (stage) stage.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!initialized) return;
            if (!ProcessVisibilityInput()) ProcessGamepad(Time.unscaledDeltaTime);
            AdvanceUIVisibility(Time.unscaledDeltaTime);
            AdvanceOverview(Time.unscaledDeltaTime);
            fadeElapsed += Time.unscaledDeltaTime;
            if (cardFade) cardFade.alpha = Mathf.Clamp01(fadeElapsed / .25f);
            if (modelPivot) modelPivot.localRotation = Quaternion.Euler(-12f,
                -25f + Mathf.Sin(Time.unscaledTime * .18f) * 12f, 0f);
        }

        void LateUpdate()
        {
            if (initialized) SyncToScreen();
        }

        /// <summary>Creates a saved editor preview or initializes its runtime state.</summary>
        public void BuildPreview()
        {
            if (!sourceScreen || !cameraSetup || !ccHub || !exhibit)
                throw new InvalidOperationException("Assign the exhibit, Cyber Canoe head, hub and projection screen before building the GPGP UI.");
            if (!font) font = TMP_Settings.defaultFontAsset;
            if (!font) throw new InvalidOperationException("Assign a TextMesh Pro font asset before building the GPGP UI.");
            initialized = false;
            ReleaseOwned("GPGP UI Canvas");
            ReleaseOwned("Featured Debris Stage");
            controls.Clear();
            Array.Clear(lastSource, 0, lastSource.Length);
            random = new System.Random(exhibit.randomSeed + 703);
            SelectedCategory = 3;
            HasSelectedCategory = false;
            CurrentView = CurrentCard = FocusRow = FocusIndex = 0;
            AutoCycling = true;
            IsUIHidden = false;
            hiddenProgress = 0f;
            elapsed = 0f;
            previousDirection = Vector2Int.zero;
            repeatElapsed = 0f;
            CreateCanvas();
            stage = new GameObject("Featured Debris Stage").transform;
            stage.SetParent(transform, false);
            CreateStageLight("Preview key", new Vector3(-.5f, .5f, -.65f), .35f);
            CreateStageLight("Preview fill", new Vector3(.45f, -.15f, -.4f), .18f);
            var pool = SourcePool(3);
            GameObject initial = pool.Find(model => model.name.IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!initial && pool.Count > 0) initial = pool[0];
            lastSource[3] = initial;
            ShowModel(initial);
            initialized = true;
            BindWaypointer();
            ApplyUIVisibility();
            SyncToScreen();
            if (waypointer) SyncToWaypoint(waypointer.CurrentWaypointIndex);
            else Refresh();
        }

        void BindWaypointer()
        {
            if (!waypointer && ccHub)
            {
                CCaux_Waypointer candidate = null;
                foreach (var found in ccHub.GetComponentsInChildren<CCaux_Waypointer>(true))
                {
                    if (!found.isActiveAndEnabled || found.gameObject.scene != gameObject.scene) continue;
                    // Multiple tours require an explicit Inspector assignment.
                    if (candidate) { candidate = null; break; }
                    candidate = found;
                }
                waypointer = candidate;
            }
            if (subscribedWaypointer == waypointer) return;
            if (subscribedWaypointer) subscribedWaypointer.OnWaypointChanged -= SyncToWaypoint;
            subscribedWaypointer = isActiveAndEnabled ? waypointer : null;
            if (subscribedWaypointer) subscribedWaypointer.OnWaypointChanged += SyncToWaypoint;
        }

        public void SyncToWaypoint(int index)
        {
            if (!initialized) return;
            if (index == 0)
            {
                HasSelectedCategory = false;
                if (CurrentView != 0) { SelectView(0); SetAutoCycle(true); }
                else Refresh();
            }
            else if (index >= 1 && index <= 4)
            {
                FocusRow = 0;
                FocusIndex = index - 1;
                if (CurrentView == 1 && HasSelectedCategory && SelectedCategory == 4 - index) Refresh();
                else SelectCategory(4 - index);
            }
        }

        void ReleaseOwned(string ownedName)
        {
            Transform owned = transform.Find(ownedName);
            if (!owned) return;
            owned.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(owned.gameObject);
            else DestroyImmediate(owned.gameObject);
        }

        void CreateCanvas()
        {
            canvasRect = new GameObject("GPGP UI Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
            canvasRect.SetParent(transform, false);
            canvasRect.sizeDelta = new Vector2(Width, Height);
            var canvas = canvasRect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // Do not add a GraphicRaycaster or Selectable: existing UI/gamepad
            // actions retain their own EventSystem and A/B/X button bindings.
            headerGroup = Rect(canvasRect, "Header group", 0, 0, Width, Height);
            leftPanelGroup = Rect(canvasRect, "Left panel group", 0, 0, Width, Height);
            rightPanelGroup = Rect(canvasRect, "Right panel group", 0, 0, Width, Height);
            bottomPanelGroup = Rect(canvasRect, "Bottom panel group", 0, 0, Width, Height);
            Panel(headerGroup, "Header", 0, 0, Width, 135, Ink);
            Text(headerGroup, "Exhibit title", "GREAT PACIFIC GARBAGE PATCH", 80, 20, 1350, 90, 52, White);
            for (int i = 0; i < 4; i++)
                AddControl(2, i, Views[i], 1480 + i * 620, 23, 590, 86, 43);
            Panel(leftPanelGroup, "Information panel", 70, 165, 1260, 1160, Ink);
            Panel(rightPanelGroup, "Object panel", 3470, 165, 1260, 1160, Ink);
            Text(leftPanelGroup, "Info eyebrow", "EXPLORE THE PATCH", 125, 194, 1120, 58, 40, Cyan);
            title = Text(leftPanelGroup, "Information heading", "", 125, 267, 1140, 158, 93, White);
            title.enableAutoSizing = true;
            title.fontSizeMin = 70;
            title.fontSizeMax = 93;
            badge = Text(leftPanelGroup, "Category and origin", "", 125, 430, 1140, 102, 43, Cyan);
            var cardRoot = Rect(leftPanelGroup, "Card content", 125, 547, 1140, 650);
            cardFade = cardRoot.gameObject.AddComponent<CanvasGroup>();
            cardFade.blocksRaycasts = false;
            body = LocalText(cardRoot, "Facts", "", 0, 0, 1140, 395, 63, White);
            factHeadline = LocalText(cardRoot, "Fact headline", "", 0, 0, 1140, 155, 126, Cyan);
            factHeadline.enableAutoSizing = true;
            factHeadline.fontSizeMin = 86;
            factHeadline.fontSizeMax = 126;
            factHeadline.textWrappingMode = TextWrappingModes.NoWrap;
            comparison = Rect(cardRoot, "Piece count and mass comparison", 0, 0, 1140, 280);
            primaryMetricLabel = LocalText(comparison, "Primary metric label", "", 0, 0, 1140, 74, 58, White);
            PanelLocal(comparison, "Primary metric track", 0, 82, 1140, 36, new Color(.09f, .16f, .19f));
            primaryMetricShare = PanelLocal(comparison, "Primary metric share", 0, 82, 1140, 36, Cyan);
            secondaryMetricLabel = LocalText(comparison, "Secondary metric label", "", 0, 145, 1140, 74, 58, White);
            PanelLocal(comparison, "Secondary metric track", 0, 227, 1140, 36, new Color(.09f, .16f, .19f));
            secondaryMetricShare = PanelLocal(comparison, "Secondary metric share", 0, 227, 1140, 36, Cyan);
            chart = Rect(cardRoot, "Country sample chart", 0, 183, 1140, 351);
            BuildCountryChart(chart);
            source = LocalText(cardRoot, "Source and context", "", 0, 525, 1140, 96, 28, Muted);
            page = Text(leftPanelGroup, "Page indicator", "", 125, 1179, 650, 50, 35, Muted);
            AddControl(1, 0, "< Previous", 125, 1240, 347, 65, 38);
            AddControl(1, 1, "Next >", 495, 1240, 300, 65, 38);
            AddControl(1, 2, "Pause", 818, 1240, 452, 65, 38);
            Text(rightPanelGroup, "Preview eyebrow", "IN FRONT OF THE DISPLAY", 3530, 194, 1140, 58, 39, Cyan);
            featureTitle = Text(rightPanelGroup, "Featured object name", "", 3530, 274, 1130, 100, 78, White);
            var frame = Panel(rightPanelGroup, "Object frame", 3530, 401, 1130, 650, new Color(.022f, .055f, .075f));
            var edge = frame.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(.15f, .39f, .44f);
            edge.effectDistance = new Vector2(3, -3);
            featureCaption = Text(rightPanelGroup, "Model caption", "", 3530, 1080, 1130, 132, 39, Muted);
            AddControl(1, 3, "Another item", 3530, 1240, 1130, 65, 42);
            Text(bottomPanelGroup, "Exhibit context", "Layer heights show mass share.\nModels are enlarged for visibility.",
                1420, 1195, 1920, 100, 37, Muted, TextAlignmentOptions.Center);
            Panel(bottomPanelGroup, "Bottom navigation", 0, 1350, Width, 270, Ink);
            for (int i = 0; i < 4; i++)
            {
                int category = 3 - i;
                AddControl(0, i, ShortSizes[i] + "   " + GPGPUIContent.CategoryRange(category),
                    80 + i * 1170, 1375, 1130, 118, 64);
            }
            Text(bottomPanelGroup, "Gamepad instructions", "X  Previous band   B  Next band   D-PAD  Browse   START  Select   SELECT / TAB  Hide UI", 80, 1530, 3300, 65, 38, White);
            cycleHint = Text(bottomPanelGroup, "Cycling status", "", 3400, 1524, 1290, 68, 34, Muted, TextAlignmentOptions.Right);
            Control visibility = AddControl(2, 4, "SELECT  Hide UI", 4060, 23, 660, 86, 43);
            visibility.Background.name = "UI visibility control";
        }

        void BuildCountryChart(RectTransform parent)
        {
            LocalText(parent, "Country heading", "WHERE MARKINGS POINTED", 0, 0, 1140, 48, 37, White);
            for (int i = 0; i < GPGPUIContent.CountryNames.Length; i++)
            {
                float y = 51 + i * 39;
                LocalText(parent, "Country " + i, GPGPUIContent.CountryNames[i], 0, y, 250, 43, 35, Muted);
                PanelLocal(parent, "Bar track " + i, 265, y + 9, 610, 20, new Color(.09f, .16f, .19f));
                PanelLocal(parent, "Share " + i, 265, y + 9,
                    610 * GPGPUIContent.CountryPercentages[i] / 100f, 20, Cyan);
                LocalText(parent, "Sample percentage " + i,
                    GPGPUIContent.CountryPercentages[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%",
                    890, y, 250, 43, 35, White, TextAlignmentOptions.Right);
            }
            LocalText(parent, "Other origins", "Other inferred origins: 17 objects (7.3%)", 0, 292, 1140, 36, 29, Muted);
        }

        Control AddControl(int row, int index, string label, float x, float y, float w, float h, float size)
        {
            Transform parent = row == 0 ? bottomPanelGroup : row == 2 ? headerGroup : index == 3 ? rightPanelGroup : leftPanelGroup;
            if (row == 2 && index == 4) parent = canvasRect;
            RectTransform background = Panel(parent, "Control " + row + ":" + index, x, y, w, h, Surface);
            var outline = background.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(4, -4);
            var text = LocalText(background, "Label", label, 15, 0, w - 30, h, size, White, TextAlignmentOptions.Center);
            var control = new Control { Row = row, Index = index, Background = background.GetComponent<Image>(), Outline = outline, Label = text };
            if (row == 0)
            {
                text.fontSize = 56;
                text.rectTransform.anchoredPosition = new Vector2(15, -9);
                text.rectTransform.sizeDelta = new Vector2(w - 30, 68);
                control.SelectedMarker = LocalText(background, "Selected marker", "SELECTED", 15, 79, w - 30, 34, 27, Cyan, TextAlignmentOptions.Center);
                control.SelectedStripe = PanelLocal(background, "Selected stripe", 12, 0, w - 24, 5, Cyan).GetComponent<Image>();
            }
            controls.Add(control);
            return control;
        }

        RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
            => PanelLocal(parent, name, x, y, w, h, color);

        RectTransform PanelLocal(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = Rect(parent, name, x, y, w, h);
            var graphic = rect.gameObject.AddComponent<Image>();
            graphic.color = color;
            graphic.raycastTarget = false;
            return rect;
        }

        TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
            => LocalText(parent, name, value, x, y, w, h, size, color, alignment);

        TMP_Text LocalText(Transform parent, string name, string value, float x, float y, float w, float h,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var rect = Rect(parent, name, x, y, w, h);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Three focus rows; the header also includes the visibility control.</summary>
        public void Navigate(Vector2 direction)
        {
            if (direction.sqrMagnitude < .01f || IsUIHidden || hiddenProgress > 0f) return;
            AutoCycling = false;
            elapsed = 0f;
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                int count = FocusRow == 2 ? 5 : 4;
                FocusIndex = (FocusIndex + (direction.x > 0 ? 1 : count - 1)) % count;
            }
            else
            {
                FocusRow = Mathf.Clamp(FocusRow + (direction.y > 0 ? 1 : -1), 0, 2);
                FocusIndex = Mathf.Min(FocusIndex, FocusRow == 2 ? 4 : 3);
            }
            Refresh();
        }

        public void ActivateFocused()
        {
            if (IsUIHidden) { SetUIHidden(false); return; }
            if (hiddenProgress > 0f) return;
            if (FocusRow == 0) SelectCategory(3 - FocusIndex);
            else if (FocusRow == 2 && FocusIndex == 4) ToggleUIVisibility();
            else if (FocusRow == 2) SelectView(FocusIndex);
            else if (FocusIndex == 0) NextCard(-1);
            else if (FocusIndex == 1) NextCard(1);
            else if (FocusIndex == 2) SetAutoCycle(!AutoCycling);
            else if (CurrentView == 2) NextCard(1);
            else SelectCategory(SelectedCategory);
        }

        public void SelectCategory(int sourceCategory)
        {
            if (sourceCategory < 0 || sourceCategory > 3) throw new ArgumentOutOfRangeException(nameof(sourceCategory));
            SelectedCategory = sourceCategory;
            HasSelectedCategory = true;
            CurrentView = 1;
            CurrentCard = 0;
            AutoCycling = false;
            elapsed = 0f;
            var choices = SourcePool(sourceCategory);
            if (choices.Count > 1) choices.Remove(lastSource[sourceCategory]);
            GameObject selected = choices.Count == 0 ? null : choices[random.Next(choices.Count)];
            lastSource[sourceCategory] = selected;
            ShowModel(selected);
            Refresh(true);
        }

        public void SelectView(int view)
        {
            if (view < 0 || view > 3) throw new ArgumentOutOfRangeException(nameof(view));
            CurrentView = view;
            if (view == 1) HasSelectedCategory = true;
            CurrentCard = 0;
            AutoCycling = false;
            elapsed = 0f;
            if (view == 2) ShowModel(turtleModel);
            else ShowModel(lastSource[SelectedCategory]);
            Refresh(true);
        }

        public void NextCard(int direction)
        {
            AutoCycling = false;
            elapsed = 0f;
            int count = CardCount();
            CurrentCard = ((CurrentCard + direction) % count + count) % count;
            Refresh(true);
        }

        public void SetAutoCycle(bool running)
        {
            AutoCycling = running && CurrentView == 0;
            elapsed = 0f;
            Refresh();
        }

        public void AdvanceOverview(float delta)
        {
            if (IsUIHidden || hiddenProgress > 0f || !AutoCycling || CurrentView != 0 || delta <= 0f) return;
            elapsed += delta;
            float interval = Mathf.Max(5f, overviewIntervalSeconds);
            if (elapsed < interval) return;
            int steps = Mathf.FloorToInt(elapsed / interval);
            elapsed %= interval;
            CurrentCard = (CurrentCard + steps) % GPGPUIContent.OverviewCards.Length;
            Refresh(true);
        }

        int CardCount() => CurrentView == 0 ? GPGPUIContent.OverviewCards.Length :
            CurrentView == 2 ? GPGPUIContent.WildlifeCards.Length :
            CurrentView == 3 ? GPGPUIContent.ActionCards.Length :
            CurrentDebrisCards.Length + (HasCountryCard ? 1 : 0);

        List<GameObject> SourcePool(int category)
        {
            var result = new List<GameObject>();
            if (!exhibit || exhibit.debrisModelGroups == null || category >= exhibit.debrisModelGroups.Length) return result;
            var group = exhibit.debrisModelGroups[category];
            if (group?.models == null) return result;
            foreach (var model in group.models) if (model && !result.Contains(model)) result.Add(model);
            return result;
        }

        void Refresh(bool fade = false)
        {
            if (!title) return;
            chart.gameObject.SetActive(ShowsCountryContext);
            if (CurrentView == 1)
            {
                InfoCard card = ShowsCountryContext ?
                    new InfoCard("Where markings pointed", "Markings offered clues about some large hard-plastic objects.",
                        GPGPUIContent.CountryScope + "\n" + GPGPUIContent.CountrySource) :
                    CurrentDebrisCards[CurrentCard];
                title.text = CurrentCard == 0 ?
                    (FeaturedSource ? GPGPUIContent.FriendlyModelName(FeaturedSource.name) : "No model available") : card.Heading;
                badge.text = FeaturedSource && GPGPUIContent.IsRubberDebris(FeaturedSource.name) ?
                    "Mega debris >50 cm\nRubber / Origin unknown" :
                    GPGPUIContent.CategoryName(SelectedCategory) + "  " + GPGPUIContent.CategoryRange(SelectedCategory) + "\nOrigin unknown";
                DisplayFact(card);
            }
            else
            {
                InfoCard card = CurrentView == 0 ? GPGPUIContent.OverviewCards[CurrentCard] :
                    CurrentView == 2 ? GPGPUIContent.WildlifeCards[CurrentCard] : GPGPUIContent.ActionCards[CurrentCard];
                title.text = card.Heading;
                badge.text = Views[CurrentView].ToUpperInvariant() +
                    (CurrentView == 0 && !string.IsNullOrEmpty(card.Headline) ? " / 2018 STUDY ESTIMATE" : "");
                DisplayFact(card);
            }
            page.text = "CARD " + (CurrentCard + 1) + " / " + CardCount() +
                (CurrentView == 0 ? (AutoCycling ? "   AUTO" : "   PAUSED") : "   PINNED");
            featureTitle.text = FeaturedSource ? GPGPUIContent.FriendlyModelName(FeaturedSource.name) : "Model unavailable";
            featureCaption.text = CurrentView == 2 ? "Sea turtle - illustrative model.\nNets and lines can entangle wildlife." :
                FeaturedSource && GPGPUIContent.IsRubberDebris(FeaturedSource.name) ?
                "Rubber debris - illustrative model.\nOrigin unknown." :
                "Enlarged illustrative model.\nOrigin unknown. Turn through the size categories below.";
            cycleHint.text = CurrentView == 0 ? (AutoCycling ? "Overview cycles every " + Mathf.Max(5f, overviewIntervalSeconds).ToString("0") + " seconds" : "Overview paused - select Resume") :
                "Item facts stay until you change them";
            foreach (var control in controls)
            {
                bool visibility = control.Row == 2 && control.Index == 4;
                bool focused = visibility && IsUIHidden || control.Row == FocusRow && control.Index == FocusIndex;
                bool selected = control.Row == 2 && control.Index == CurrentView ||
                    control.Row == 0 && HasSelectedCategory && control.Index == 3 - SelectedCategory || visibility && IsUIHidden;
                control.Background.color = selected ? new Color(.04f, .22f, .26f) : Surface;
                control.Label.color = selected ? Cyan : White;
                control.Outline.effectColor = focused ? White : new Color(.1f, .27f, .3f, selected ? 1 : .35f);
                if (control.SelectedMarker) control.SelectedMarker.gameObject.SetActive(selected);
                if (control.SelectedStripe) control.SelectedStripe.gameObject.SetActive(selected);
                if (control.Row == 1 && control.Index == 2)
                {
                    control.Label.text = CurrentView == 0 ? (AutoCycling ? "Pause" : "Resume") : "Facts pinned";
                    control.Label.color = CurrentView == 0 ? White : Muted;
                }
                if (control.Row == 1 && control.Index == 3)
                    control.Label.text = CurrentView == 2 ? "Next wildlife fact" : "Another item";
                if (visibility) control.Label.text = IsUIHidden ? "SELECT  Show UI" : "SELECT  Hide UI";
            }
            if (fade && Application.isPlaying)
            {
                fadeElapsed = 0f;
                cardFade.alpha = 0f;
            }
            Canvas.ForceUpdateCanvases();
        }

        void DisplayFact(InfoCard card)
        {
            bool headline = !string.IsNullOrEmpty(card.Headline);
            factHeadline.gameObject.SetActive(headline);
            factHeadline.text = card.Headline ?? string.Empty;
            comparison.gameObject.SetActive(card.HasComparison);
            primaryMetricLabel.text = card.PrimaryMetricLabel ?? string.Empty;
            secondaryMetricLabel.text = card.SecondaryMetricLabel ?? string.Empty;
            primaryMetricShare.sizeDelta = new Vector2(1140 * Mathf.Clamp01(card.PrimaryMetricFraction), 36);
            secondaryMetricShare.sizeDelta = new Vector2(1140 * Mathf.Clamp01(card.SecondaryMetricFraction), 36);
            float top = card.HasComparison ? 295 : headline ? 170 : 0;
            body.rectTransform.anchoredPosition = new Vector2(0, -top);
            body.rectTransform.sizeDelta = new Vector2(1140, ShowsCountryContext ? 178 : 475 - top);
            body.fontSize = ShowsCountryContext ? 45 : card.HasComparison ? 54 : 63;
            body.text = card.Body;
            source.text = card.Source;
        }

        public void ToggleUIVisibility() => SetUIHidden(!IsUIHidden);

        /// <summary>Temporarily clears the display while preserving the current UI selection.</summary>
        public void SetUIHidden(bool hidden, bool immediate = false)
        {
            IsUIHidden = hidden;
            previousDirection = Vector2Int.zero;
            repeatElapsed = 0f;
            if (immediate) hiddenProgress = hidden ? 1f : 0f;
            ApplyUIVisibility();
            Refresh();
        }

        bool ProcessVisibilityInput()
        {
            var pad = Gamepad.current;
            var keyboard = Keyboard.current;
            if ((pad != null && pad.selectButton.wasPressedThisFrame) ||
                (keyboard != null && keyboard.tabKey.wasPressedThisFrame))
            {
                ToggleUIVisibility();
                return true;
            }
            return false;
        }

        void AdvanceUIVisibility(float delta)
        {
            hiddenProgress = Mathf.MoveTowards(hiddenProgress, IsUIHidden ? 1f : 0f,
                Mathf.Max(0f, delta) / PanelSlideSeconds);
            ApplyUIVisibility();
        }

        void ApplyUIVisibility()
        {
            if (!leftPanelGroup || !rightPanelGroup || !headerGroup || !bottomPanelGroup) return;
            float eased = Mathf.SmoothStep(0f, 1f, hiddenProgress);
            leftPanelGroup.anchoredPosition = new Vector2(-SideSlide * eased, 0f);
            rightPanelGroup.anchoredPosition = new Vector2(SideSlide * eased, 0f);
            headerGroup.anchoredPosition = new Vector2(0f, HeaderSlide * eased);
            bottomPanelGroup.anchoredPosition = new Vector2(0f, -BottomSlide * eased);
            if (stage) stage.gameObject.SetActive(hiddenProgress < 1f);
            SyncToScreen();
        }

        void ProcessGamepad(float delta)
        {
            var pad = Gamepad.current;
            if (pad == null) { previousDirection = Vector2Int.zero; return; }
            if (IsUIHidden)
            {
                previousDirection = Vector2Int.zero;
                repeatElapsed = 0f;
                if (pad.startButton.wasPressedThisFrame) SetUIHidden(false);
                return;
            }
            if (hiddenProgress > 0f) return;
            Vector2 value = pad.dpad.ReadValue();
            Vector2Int direction = value.sqrMagnitude < .1f ? Vector2Int.zero :
                Mathf.Abs(value.x) > Mathf.Abs(value.y) ? new Vector2Int(value.x > 0 ? 1 : -1, 0) : new Vector2Int(0, value.y > 0 ? 1 : -1);
            if (direction == Vector2Int.zero) repeatElapsed = 0f;
            else if (direction != previousDirection)
            {
                Navigate(direction);
                repeatElapsed = .4f;
            }
            else
            {
                repeatElapsed -= delta;
                if (repeatElapsed <= 0f) { Navigate(direction); repeatElapsed = .18f; }
            }
            previousDirection = direction;
            if (pad.startButton.wasPressedThisFrame) ActivateFocused();
        }

        void ShowModel(GameObject prefab)
        {
            if (modelPivot)
            {
                modelPivot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(modelPivot.gameObject); else DestroyImmediate(modelPivot.gameObject);
            }
            FeaturedSource = prefab;
            FeaturedObject = null;
            modelPivot = new GameObject("Model pivot").transform;
            modelPivot.SetParent(stage, false);
            modelPivot.gameObject.SetActive(false);
            if (!prefab) { modelPivot.gameObject.SetActive(true); return; }
            // Normalize while inactive; the wildlife model keeps its authored animation.
            FeaturedObject = Instantiate(prefab, modelPivot, false);
            FeaturedObject.name = "Featured - " + GPGPUIContent.FriendlyModelName(prefab.name);
            FeaturedObject.SetActive(true);
            foreach (var child in FeaturedObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            bool animateWildlife = prefab == turtleModel;
            foreach (var behaviour in FeaturedObject.GetComponentsInChildren<Behaviour>(true))
                if (!animateWildlife || !(behaviour is Animation || behaviour is Animator)) behaviour.enabled = false;
            foreach (var collider in FeaturedObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var rigidbody in FeaturedObject.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }
            foreach (var renderer in FeaturedObject.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (GPGPUIContent.IsRubberDebris(prefab.name))
                {
                    // Improve dark rubber's preview exposure on this clone.
                    // Shared textures/materials and the gallery lights retain
                    // their authored values, including under Forward+ lighting.
                    Material[] slots = renderer.sharedMaterials;
                    for (int index = 0; index < slots.Length; index++)
                    {
                        Material material = slots[index];
                        if (!material || !material.HasProperty(BaseColorId)) continue;
                        Color color = material.GetColor(BaseColorId);
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, index);
                        block.SetVector(BaseColorId, new Vector4(color.r * 8f, color.g * 8f, color.b * 8f, color.a));
                        renderer.SetPropertyBlock(block, index);
                    }
                }
            }
            Bounds bounds = LocalMeshBounds(modelPivot);
            float diameter = Mathf.Max(BoundsEpsilon, bounds.size.magnitude);
            float scale = 1f / diameter;
            FeaturedObject.transform.localPosition -= bounds.center;
            modelPivot.localScale = Vector3.one * scale;
            modelPivot.localRotation = Quaternion.Euler(-12, -25, 0);
            modelDiameter = diameter;
            modelPivot.gameObject.SetActive(true);
            SyncToScreen();
        }

        static Bounds LocalMeshBounds(Transform pivot)
        {
            Bounds combined = new Bounds();
            bool found = false;
            foreach (var filter in pivot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                EncapsulateBounds(filter.sharedMesh.bounds, filter.transform, pivot, ref combined, ref found);
            }
            foreach (var renderer in pivot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                EncapsulateBounds(renderer.localBounds, renderer.transform, pivot, ref combined, ref found);
            return found ? combined : new Bounds(Vector3.zero, Vector3.one);
        }

        static void EncapsulateBounds(Bounds bounds, Transform sourceTransform, Transform pivot, ref Bounds combined, ref bool found)
        {
            // Work in the local hierarchy. A world-position round trip loses
            // precision for sub-millimetre imports far from the scene origin.
            Matrix4x4 toPivot = Matrix4x4.identity;
            for (Transform current = sourceTransform; current && current != pivot; current = current.parent)
                toPivot = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * toPivot;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = toPivot.MultiplyPoint3x4(corner);
                if (!found) { combined = new Bounds(point, Vector3.zero); found = true; }
                else combined.Encapsulate(point);
            }
        }

        void CreateStageLight(string name, Vector3 position, float intensity)
        {
            var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(stage, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = new Color(.85f, .96f, 1f);
            light.intensity = intensity;
            light.range = 2.1f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PreviewLayer;
        }

        void SyncToScreen()
        {
            if (!canvasRect || !stage || !sourceScreen || !cameraSetup) return;
            Vector3 across = sourceScreen.TransformVector(Vector3.right);
            Vector3 up = sourceScreen.TransformVector(Vector3.up);
            float width = across.magnitude, height = up.magnitude;
            Vector3 normal = Vector3.Cross(across, up).normalized;
            Vector3 eye = cameraSetup.transform.position;
            float distance = Vector3.Dot(sourceScreen.position - eye, normal);
            if (distance < 0f) { normal = -normal; distance = -distance; }
            canvasRect.position = sourceScreen.position - normal * .006f;
            canvasRect.rotation = Quaternion.LookRotation(normal, up.normalized);
            canvasRect.localScale = new Vector3(width / Width, height / Height, width / Width);
            float depth = Mathf.Clamp(foregroundDepthMeters, .01f, Mathf.Max(.01f, distance * .25f));
            Vector3 onScreen = sourceScreen.position + across * .353f + up * .052f;
            if (rightPanelGroup) onScreen += across * (rightPanelGroup.anchoredPosition.x / Width);
            float fraction = Mathf.Max(.1f, (distance - depth) / Mathf.Max(.01f, distance));
            stage.position = eye + (onScreen - eye) * fraction;
            stage.rotation = canvasRect.rotation;
            stage.localScale = Vector3.one;
            if (modelPivot && FeaturedObject)
            {
                // A bounding sphere fits every gentle rotation inside the pane.
                float desiredDiameter = Mathf.Min(width * .205f, height * .36f) * fraction;
                modelPivot.localScale = Vector3.one * (desiredDiameter / Mathf.Max(BoundsEpsilon, modelDiameter));
            }
        }
    }
}

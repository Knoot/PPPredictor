using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.FloatingScreen;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using PPPredictor.Data.DisplayInfos;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Zenject;
using HarmonyLib;
using System.Threading.Tasks;
using PPPredictor.Interfaces;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.UI.Component.Component;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.UI.ViewController
{

    [ViewDefinition("PPPredictor.UI.Views.PPPredictorView.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\PPPredictorView.bsml")]
    class PPPredictorViewController : IInitializable, IDisposable, INotifyPropertyChanged
    {
        private static readonly string githubUrl = "https://github.com/no-1-noob/PPPredictor/releases/latest";
        private FloatingScreen floatingScreen;
#pragma warning disable CS0649
        [Inject] private readonly IPPPredictorMgr ppPredictorMgr;
#pragma warning restore CS0649
        public event PropertyChangedEventHandler PropertyChanged;
        private DisplaySessionInfo displaySessionInfo;
        Dictionary<CalculationMode, DisplayPPInfo> dctDisplayPPInfo = new Dictionary<CalculationMode, DisplayPPInfo>();
        private bool _isDataLoading;
        private bool _isScreenMoving = false;
        private List<Action> _lsPropertyChangedActions = new List<Action>();

        public PPPredictorViewController() { }

        [Inject]
        public void Construct()
        {
        }
        public void Initialize()
        {
            BSMLParser.Instance.Initialize();

            Plugin.pppViewController = this;
            displaySessionInfo = new DisplaySessionInfo();
            dctDisplayPPInfo.Add(CalculationMode.PercentToPP, new DisplayPPInfo(CalculationMode.PercentToPP));
            dctDisplayPPInfo.Add(CalculationMode.PPToPercent, new DisplayPPInfo(CalculationMode.PPToPercent));
            dctDisplayPPInfo.Add(CalculationMode.Rank, new DisplayPPInfo(CalculationMode.Rank));
            floatingScreen = FloatingScreen.CreateFloatingScreen(new Vector2(75, 125), true, Plugin.ProfileInfo.Position, new Quaternion(0, 0, 0, 0));
            floatingScreen.gameObject.name = "BSMLFloatingScreen_PPPredictor";
            floatingScreen.gameObject.SetActive(false);
            floatingScreen.transform.eulerAngles = Plugin.ProfileInfo.EulerAngles;
            floatingScreen.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            floatingScreen.Handle.transform.localScale = new Vector2(25, 25);
            floatingScreen.Handle.transform.localPosition = new Vector3(0, 1, -.1f);
            floatingScreen.Handle.transform.localRotation = Quaternion.identity;
            floatingScreen.Handle.hideFlags = HideFlags.HideInHierarchy;
            floatingScreen.ShowHandle = _isScreenMoving;
            floatingScreen.HighlightHandle = false;

            floatingScreen.HandleReleased += OnScreenHandleReleased;
            ppPredictorMgr.ViewActivated += PpPredictorMgr_ViewActivated;
            ppPredictorMgr.OnDataLoading += PpPredictorMgr_OnDataLoading;
            ppPredictorMgr.OnDisplayPPInfo += PpPredictorMgr_OnDisplayPPInfo;
            ppPredictorMgr.OnDisplaySessionInfo += PpPredictorMgr_OnDisplaySessionInfo;
            ppPredictorMgr.OnMapPoolRefreshed += PpPredictorMgr_OnMapPoolRefreshed;
        }

        public void ParseGUI()
        {
            SetupHandleTexture();
            BSMLParser.Instance.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "PPPredictor.UI.Views.PPPredictorView.bsml"), floatingScreen.gameObject, this);
        }

        private async void SetupHandleTexture()
        {
            MeshRenderer floatingScreenMeshRenderer = floatingScreen.Handle.GetComponent<MeshRenderer>();
            Shader unlitShader = Shader.Find("Sprites/Default");
            var material = new Material(unlitShader);
            floatingScreenMeshRenderer.material = material;
            Texture2D tex = await BeatSaberMarkupLanguage.Utilities.LoadTextureFromAssemblyAsync("PPPredictor.Resources.moveIcon.png");
            floatingScreenMeshRenderer.material.SetTexture("_MainTex", tex);
        }

        private void PpPredictorMgr_OnMapPoolRefreshed(object sender, EventArgs e)
        {
            UpdateAllDisplay();
        }

        private void PpPredictorMgr_OnDisplaySessionInfo(object sender, DisplaySessionInfo displaySessionInfo)
        {
            this.displaySessionInfo = displaySessionInfo;
            UpdateSessionDisplay();
        }

        private void PpPredictorMgr_OnDisplayPPInfo(object sender, DisplayPPInfo displayPPInfo)
        {
            this.dctDisplayPPInfo[displayPPInfo.calculationMode] = displayPPInfo;
            UpdatePPDisplay(displayPPInfo.calculationMode);
        }

        private void PpPredictorMgr_OnDataLoading(object sender, bool isDataLoading)
        {
            this._isDataLoading = isDataLoading;
            UpdateLoadingDisplay();
        }

        internal async void ApplySettings()
        {
            this.ppPredictorMgr.RestartOverlayServer();
            await this.ppPredictorMgr.ResetPredictors();
            ResetDisplay(false);
        }

        public void OnScreenHandleReleased(object sender, FloatingScreenHandleEventArgs args)
        {
            Plugin.ProfileInfo.Position = floatingScreen.transform.position;
            Plugin.ProfileInfo.EulerAngles = floatingScreen.transform.eulerAngles;
        }

        public void Dispose()
        {
            floatingScreen.HandleReleased -= OnScreenHandleReleased;
            ppPredictorMgr.ViewActivated -= PpPredictorMgr_ViewActivated;
            if (tabSelector) tabSelector.TextSegmentedControl.didSelectCellEvent -= OnSelectedCellEventChanged;
            Plugin.pppViewController = null;
        }
#pragma warning disable CS0649
        [UIParams]
        private readonly BSMLParserParams bsmlParserParams;
#pragma warning restore CS0649

        [UIAction("#post-parse")]
        protected void PostParse()
        {
            ResetPosition();
            DisplayInitialPercentages();
            this.ppPredictorMgr.ResetDisplay(false);
            CheckVersion();
            if (tabSelector)
            {
                tabSelector.TextSegmentedControl.didSelectCellEvent += OnSelectedCellEventChanged;
            }
            if (tabModeSelector)
            {
                tabModeSelector.transform.localScale = new Vector3(.5f,.5f,.5f);
                tabModeSelector.transform.Translate(new Vector3(-.3f, .5f, 0));
                //TODO:
                //tabModeSelector.TextSegmentedControl.didSelectCellEvent += ;
            }

            SliderFormatting();

            UpdateMinMaxIncements(sliderPercentToPP.Slider.value);
        }

        private void SliderFormatting()
        {
            float scaleFactor = 0.3f;
            incrementMin.Text = string.Empty;
            incrementMin.transform.Rotate(0, 0, 90);
            incrementMin.TextMesh.transform.Rotate(0, 0, -90);
            incrementMin.transform.localScale = new Vector3(scaleFactor, 1, 1);
            incrementMin.TextMesh.transform.localScale = new Vector3(1, 1 / scaleFactor, 1);
            incrementMin.transform.Rotate(0, 0, -7);
            incrementMin.TextMesh.transform.Rotate(0, 0, 7);

            incrementMax.transform.Rotate(0, 0, 90);
            incrementMax.TextMesh.transform.Rotate(0, 0, -95);
            incrementMax.transform.localScale = new Vector3(scaleFactor, 1, 1);
            incrementMax.TextMesh.transform.localScale = new Vector3(1, 1 / scaleFactor, 1);
            incrementMax.transform.Rotate(0, 0, -7);
            incrementMax.TextMesh.transform.Rotate(0, 0, 7);

            sliderPercentToPP.transform.localScale = new Vector3(.85f, 1, 1);
            sliderPercentToPP.transform.Translate(new Vector3(1, 2, 0));
        }

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null
        #region UI Components
        [UIComponent("incrementMin")]
        private readonly IncrementSetting incrementMin;
        [UIComponent("incrementMax")]
        private readonly IncrementSetting incrementMax;
        [UIComponent("sliderPercentToPP")]
        private readonly SliderSetting sliderPercentToPP;
        [UIComponent("sliderPPToPercent")]
        private readonly SliderSetting sliderPPToPercent;
        [UIComponent("sliderRank")]
        private readonly SliderSetting sliderRank;
        [UIComponent("tabSelector")]
        private readonly TabSelector tabSelector;
        [UIComponent("tabModeSelector")]
        private readonly TabSelector tabModeSelector;
        [UIComponent("dropdown-map-pools")]
        private readonly DropDownListSetting dropDownMapPools;
        [UIComponent("ppDisplayComponentPPToPercent")]
        private readonly PPDisplayComponent ppDisplayComponentPPToPercent;
        [UIComponent("ppDisplayComponentPercentToPP")]
        private readonly PPDisplayComponent ppDisplayComponentPercentToPP;
        [UIComponent("ppDisplayComponentRank")]
        private readonly PPDisplayComponent ppDisplayComponentRank;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value null
        #endregion
        [UIAction("sliderFormatPercentToPP")]
        private string sliderFormatPercentToPP(float f)
        {
            return $"{f:F2} %";
        }
        [UIAction("sliderFormatPPToPercent")]
        private string SliderFormatPPToPercent(float f)
        {
            return $"{f:F1}{ppPredictorMgr.CurrentPPPredictor.PPSuffix}";
        }
        [UIAction("sliderFormatRank")]
        private string SliderFormatRank(float f)
        {
            return $"+{f:F2} ranks";
        }

        #region buttons
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("refresh-profile-clicked")]
        private void RefreshProfileClicked()
        {
            this.ppPredictorMgr.RefreshCurrentData(10);
        }
#pragma warning restore IDE0051 // Remove unused private members

#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("reset-session-clicked")]
        private void ResetSessionClicked()
        {
            this.ppPredictorMgr.UpdateCurrentAndCheckResetSession(true);
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("show-update-clicked")]
        private void ShowUpdateClicked()
        {
            bsmlParserParams.EmitEvent("show-update-display-modal");
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("click-close-update-modal")]
        private void CloseUpdateClicked()
        {
            bsmlParserParams.EmitEvent("close-update-display-modal");
            if (HideUpdate)
            {
                Plugin.ProfileInfo.AcknowledgedVersion = NewVersion;
                NewVersion = string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNewVersionAvailable)));
            }
            bsmlParserParams.EmitEvent("close-update-display-modal");
            bsmlParserParams.EmitEvent("open-menu-settings-modal");
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("click-close-github")]
        private void CloseGithubClicked()
        {
            bsmlParserParams.EmitEvent("close-github-notification");
            bsmlParserParams.EmitEvent("show-update-display-modal");
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("click-open-github")]
        private void OpenGithubClicked()
        {
            Process.Start(githubUrl);
            bsmlParserParams.EmitEvent("close-update-display-modal");
            bsmlParserParams.EmitEvent("show-github-notification");
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("arrow-prev-leaderboard-clicked")]
        private void ArrowPrevLeaderboardClicked()
        {
            this.ppPredictorMgr.CyclePredictors(-1);
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning disable IDE0051 // Remove unused private members
        [UIAction("arrow-next-leaderboard-clicked")]
        private void ArrowNextLeaderboardClicked()
        {
            this.ppPredictorMgr.CyclePredictors(1);
        }
#pragma warning restore IDE0051 // Remove unused private members
        #endregion

        #region slider max min input
        [UIValue("sliderValuePercentToPP")]
        private float SliderValuePercentToPP
        {
            get => ppPredictorMgr.CurrentPPPredictor.Percentage;
            set
            {
                ppPredictorMgr.SetPercentage(value);
                Plugin.ProfileInfo.LastPercentageSelected = ppPredictorMgr.CurrentPPPredictor.Percentage;
                ppPredictorMgr.CurrentPPPredictor.CalculatePP(ppPredictorMgr.CurrentPPPredictor.Percentage, CalculationMode.PercentToPP);
                sliderPercentToPP.Slider.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderValuePercentToPP)));
            }
        }

        [UIValue("sliderValuePPtoPercent")]
        private float SliderValuePPtoPercent
        {
            get => ppPredictorMgr.CurrentPPPredictor.TargetPPGain;
            set
            {
                ppPredictorMgr.SetTargetPPGain(value);
                Plugin.ProfileInfo.LastTargetPPGainSelected = ppPredictorMgr.CurrentPPPredictor.TargetPPGain;
                ppPredictorMgr.CurrentPPPredictor.TriggerCalculateTimer(CalculationMode.PPToPercent);
                sliderPPToPercent.Slider.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderValuePPtoPercent)));
            }
        }

        [UIValue("sliderValueRank")]
        private float SliderValueRank
        {
            get => ppPredictorMgr.CurrentPPPredictor.TargetRankGain;
            set
            {
                ppPredictorMgr.SetTargetRankGain(value);
                Plugin.ProfileInfo.LastTargetRankGain = ppPredictorMgr.CurrentPPPredictor.TargetRankGain;
                ppPredictorMgr.CurrentPPPredictor.TriggerCalculateTimer(CalculationMode.Rank);
                sliderRank.Slider.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderValueRank)));
            }
        }

        [UIValue("minValue")]
        private float MinValue
        {
            get => Plugin.ProfileInfo.LastMinPercentageSelected;
            set
            {
                Plugin.ProfileInfo.LastMinPercentageSelected = value;
                incrementMax.MinValue = Plugin.ProfileInfo.LastMinPercentageSelected + 10;
                UpdateMinMaxIncements(sliderPercentToPP.Slider.value);
            }
        }

        [UIValue("maxValue")]
        private float MaxValue
        {
            get => Plugin.ProfileInfo.LastMaxPercentageSelected;
            set
            {
                Plugin.ProfileInfo.LastMaxPercentageSelected = value;
                incrementMin.MaxValue = Plugin.ProfileInfo.LastMaxPercentageSelected - 10;
                UpdateMinMaxIncements(sliderPercentToPP.Slider.value);
            }
        }
        private void UpdateMinMaxIncements(float oldSliderValue)
        {
            sliderPercentToPP.Slider.minValue = Plugin.ProfileInfo.LastMinPercentageSelected;
            sliderPercentToPP.Slider.maxValue = Plugin.ProfileInfo.LastMaxPercentageSelected;
            SliderValuePercentToPP = Math.Max(Math.Min(oldSliderValue, Plugin.ProfileInfo.LastMaxPercentageSelected), Plugin.ProfileInfo.LastMinPercentageSelected);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinValue)));
        }

        #endregion

        #region UI Values session
        [UIValue("sessionRank")]
        private string SessionRank
        {
            get => displaySessionInfo.SessionRank;
        }
        [UIValue("sessionRankDiff")]
        private string SessionRankDiff
        {
            get => displaySessionInfo.SessionRankDiff;
        }
        [UIValue("sessionRankDiffColor")]
        private string SessionRankDiffColor
        {
            get => displaySessionInfo.SessionRankDiffColor;
        }
        [UIValue("sessionCountryRank")]
        private string SessionCountryRank
        {
            get => displaySessionInfo.SessionCountryRank;
        }
        [UIValue("sessionCountryRankDiff")]
        private string SessionCountryRankDiff
        {
            get => displaySessionInfo.SessionCountryRankDiff;
        }
        [UIValue("sessionCountryRankDiffColor")]
        private string SessionCountryRankDiffColor
        {
            get => displaySessionInfo.SessionCountryRankDiffColor;
        }
        [UIValue("countryRankFontColor")]
        private string CountryRankFontColor
        {
            get => displaySessionInfo.CountryRankFontColor;
        }
        [UIValue("sessionPP")]
        private string SessionPP
        {
            get => displaySessionInfo.SessionPP;
        }
        [UIValue("sessionPPDiff")]
        private string SessionPPDiff
        {
            get => displaySessionInfo.SessionPPDiff;
        }
        [UIValue("sessionPPDiffColor")]
        private string SessionPPDiffColor
        {
            get => displaySessionInfo.SessionPPDiffColor;
        }
        #endregion
        #region UI Values Predicted Rank
        #endregion
        [UIValue("isDataLoading")]
        private bool IsDataLoading
        {
            get => _isDataLoading;
        }
        [UIValue("isNoDataLoading")]
        private bool IsNoDataLoading
        {
            get => !(_isDataLoading || _isScreenMoving);
        }
        [UIValue("leaderBoardName")]
        internal string LeaderBoardName
        {
            get
            {
                dropDownMapPools?.UpdateChoices(); //Refresh map pools here...
                return this.ppPredictorMgr.CurrentPPPredictor.LeaderBoardName;
            }
        }
        [UIValue("leaderBoardIcon")]
        internal string LeaderBoardIcon
        {
            get { return this.ppPredictorMgr.CurrentPPPredictor.LeaderBoardIcon; }
        }
        [UIValue("isLeftArrowActive")]
        private bool IsLeftArrowActive
        {
            get => this.ppPredictorMgr.IsLeftArrowActive && !_isScreenMoving;
        }
        [UIValue("isRightArrowActive")]
        private bool IsRightArrowActive
        {
            get => this.ppPredictorMgr.IsRightArrowActive && !_isScreenMoving;
        }
        [UIValue("isMapPoolDropDownActive")]
        private bool IsMapPoolDropDownActive
        {
            get => this.ppPredictorMgr.IsMapPoolDropDownActive && !_isScreenMoving;
        }
        [UIValue("isLeaderboardNavigationActive")]
        private bool IsLeaderboardNavigationActive
        {
            get => this.ppPredictorMgr.IsLeaderboardNavigationActive;
        }
        #region update UI data
        [UIValue("newVersion")]
        private string NewVersion { get; set; }
        [UIValue("currentVersion")]
        private string CurrentVersion { get; set; }
        [UIValue("hide-update")]
        private bool HideUpdate { get; set; }
        [UIValue("isNewVersionAvailable")]
        private bool IsNewVersionAvailable { get => !string.IsNullOrEmpty(NewVersion); }
        #endregion

        #region MapPoolUI Stuff
        [UIValue("map-pool-options")]
        public List<object> MapPoolOptions
        {
            get
            {
                return this.ppPredictorMgr.CurrentPPPredictor.MapPoolOptions;
            }
            set
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MapPoolOptions)));
            }
        }
        [UIValue("current-map-pool")]
        public object CurrentMapPool
        {
            get => this.ppPredictorMgr.CurrentPPPredictor.CurrentMapPool;
            set
            {
                this.ppPredictorMgr.CurrentPPPredictor.CurrentMapPool = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMapPool)));
            }
        }
        #endregion

        [UIValue("displayPPInfoPercentToPP")]
        public object DisplayPPInfoPercentToPP
        {
            get => this.dctDisplayPPInfo[CalculationMode.PercentToPP];
        }
        [UIValue("displayPPInfoPPToPercent")]
        public object DisplayPPInfoPPToPercent
        {
            get => this.dctDisplayPPInfo[CalculationMode.PPToPercent];
        }
        [UIValue("displayPPInfoRank")]
        public object DisplayPPInfoRank
        {
            get => this.dctDisplayPPInfo[CalculationMode.Rank];
        }

        #region moving panel
        [UIAction("move-panel-clicked")]
        private void MovePanelClicked()
        {
            _isScreenMoving = !_isScreenMoving;
            floatingScreen.ShowHandle = _isScreenMoving;
            UpdateLoadingDisplay();
            UpdateLeaderBoardDisplay();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MovePanelIcon)));
        }
        [UIValue("movePanelIcon")]
        private string MovePanelIcon
        {
            get => _isScreenMoving ? "🔓" : "🔒";
        }
        #endregion
        internal void ResetDisplay(bool v)
        {
            ResetPosition();
            DisplayInitialPercentages();
            this.ppPredictorMgr.ResetDisplay(v);
        }

        private void PpPredictorMgr_ViewActivated(object sender, bool active)
        {
            RefreshTabSelection();
            floatingScreen.gameObject.SetActive(active);
            if (active)
            {
                foreach (var action in _lsPropertyChangedActions)
                {
                    //Plugin.DebugPrint($"Executing Action delayed: {action.Method.Name}");
                    action?.Invoke();
                }
                _lsPropertyChangedActions.Clear();
            }
        }

        private async void RefreshTabSelection()
        {
            await Task.Delay(100);
            if (tabSelector != null)
            {
                tabSelector.TextSegmentedControl.SelectCellWithNumber(Plugin.ProfileInfo.SelectedTab);
                AccessTools.Method(typeof(TabSelector), "TabSelected").Invoke(tabSelector, new object[] { tabSelector.TextSegmentedControl, Plugin.ProfileInfo.SelectedTab });
            }
            await Task.Delay(5000);
            Plugin.DebugPrint("try to change tabs");
        }

        private void OnSelectedCellEventChanged(SegmentedControl seg, int index)
        {
            Plugin.ProfileInfo.SelectedTab = index;
        }

        private void DisplayInitialPercentages()
        {
            Plugin.DebugPrint($"DisplayInitialPercentages {Plugin.ProfileInfo.LastPercentageSelected} {Plugin.ProfileInfo.LastTargetPPGainSelected} {Plugin.ProfileInfo.LastTargetRankGain}");
            SliderValuePercentToPP = Plugin.ProfileInfo.LastPercentageSelected;
            SliderValuePPtoPercent = Plugin.ProfileInfo.LastTargetPPGainSelected;
            SliderValueRank = Plugin.ProfileInfo.LastTargetRankGain;
            MinValue = Plugin.ProfileInfo.LastMinPercentageSelected;
            MaxValue = Plugin.ProfileInfo.LastMaxPercentageSelected;
        }

        public void ResetPosition()
        {
            floatingScreen.transform.eulerAngles = Plugin.ProfileInfo.EulerAngles;
            floatingScreen.transform.position = Plugin.ProfileInfo.Position;
        }

        private async void CheckVersion()
        {
            if (Plugin.ProfileInfo.IsVersionCheckEnabled && (DateTime.Now - Plugin.ProfileInfo.DtLastVersionCheck).TotalHours >= 12)
            {
                CurrentVersion = typeof(Plugin).Assembly.GetName().Version.ToString();
                CurrentVersion = $"{CurrentVersion.Substring(0, CurrentVersion.Length - 2)}{Plugin.Beta}";
                string acknowledgedVersion = Plugin.ProfileInfo.AcknowledgedVersion;
                NewVersion = await VersionChecker.VersionChecker.GetCurrentVersionAsync();
                if (string.IsNullOrEmpty(NewVersion) || NewVersion == CurrentVersion || NewVersion == acknowledgedVersion)
                {
                    NewVersion = string.Empty;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNewVersionAvailable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentVersion)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewVersion)));
                Plugin.ProfileInfo.DtLastVersionCheck = DateTime.Now;
            }
        }

        private void UpdateMapPoolChoices()
        {
            //Get the actual object
            object newMappool = null;
            try
            {
                var currentMappoolShort = this.ppPredictorMgr.CurrentPPPredictor.CurrentMapPool as PPPMapPoolShort;
                foreach (var possibleMappool in this.ppPredictorMgr.CurrentPPPredictor.MapPoolOptions)
                {
                    var possibleMappoolShort = possibleMappool as PPPMapPoolShort;
                    if(currentMappoolShort != null && currentMappoolShort != null)
                    {
                        if(possibleMappoolShort.Id == currentMappoolShort.Id)
                        {
                            newMappool = possibleMappool;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"UpdateMapPoolChoices error while finding pool: {ex.Message}");
                newMappool = this.ppPredictorMgr.CurrentPPPredictor.CurrentMapPool;
            }

            dropDownMapPools.Values = this.ppPredictorMgr.CurrentPPPredictor.MapPoolOptions;
            dropDownMapPools?.UpdateChoices();
            dropDownMapPools.Value = newMappool;
            dropDownMapPools.ApplyValue();
        }

        private void UpdateSessionDisplay()
        {
            StartCoroutine(UpdateSessionDisplayInternal);
        }

        private void UpdateSessionDisplayInternal()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionRank)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionRankDiff)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionRankDiffColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionCountryRank)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionCountryRankDiff)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionCountryRankDiffColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountryRankFontColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionPP)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionPPDiff)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionPPDiffColor)));
        }

        private void UpdatePPDisplay(CalculationMode calculationMode)
        {
            StartCoroutine(() => UpdatePPDisplayInternal(calculationMode));
        }

        private void UpdatePPDisplayInternal(CalculationMode calculationMode)
        {
            if(calculationMode == CalculationMode.PPToPercent && ppDisplayComponentPPToPercent != null)
            {
                ppDisplayComponentPPToPercent.UpdateTexts(dctDisplayPPInfo[CalculationMode.PPToPercent]);
            }
            else if (calculationMode == CalculationMode.PercentToPP && ppDisplayComponentPercentToPP != null)
            {
                ppDisplayComponentPercentToPP.UpdateTexts(dctDisplayPPInfo[CalculationMode.PercentToPP]);
            }
            else if (calculationMode == CalculationMode.Rank && ppDisplayComponentRank != null)
            {
                ppDisplayComponentRank.UpdateTexts(dctDisplayPPInfo[CalculationMode.Rank]);
            }
        }

        private void UpdateLeaderBoardDisplay()
        {
            StartCoroutine(UpdateLeaderBoardDisplayInternal);
        }

        private void UpdateLeaderBoardDisplayInternal()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeaderBoardName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeaderBoardIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MapPoolOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMapPool)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLeftArrowActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightArrowActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLeaderboardNavigationActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMapPoolDropDownActive)));
        }

        private void UpdateLoadingDisplay()
        {
            StartCoroutine(UpdateLoadingDisplayInternal);
        }
        private void UpdateLoadingDisplayInternal()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDataLoading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNoDataLoading)));
        }

        private void StartCoroutine(Action methodToExecute)
        {
            if (floatingScreen != null && floatingScreen.isActiveAndEnabled)
            {
                //Plugin.DebugPrint($"Executing Action {methodToExecute.Method.Name}");
                floatingScreen.StartCoroutine(InvokeOnMainThread(methodToExecute));
            }
            else
            {
                //Plugin.DebugPrint($"Floating screen is not active, adding method to property changed actions {methodToExecute.Method.Name}");
                _lsPropertyChangedActions.Add(methodToExecute);
            }
        }


        private System.Collections.IEnumerator InvokeOnMainThread(Action methodToExecute)
        {
            yield return null; // Wait for next frame
            methodToExecute?.Invoke();
        }

        private void UpdateAllDisplay()
        {
            UpdateMapPoolChoices();
            UpdateLeaderBoardDisplay();
            UpdateSessionDisplay();
            foreach (CalculationMode mode in Enum.GetValues(typeof(CalculationMode)))
            {
                UpdatePPDisplay(mode);
            }
        }
    }
}

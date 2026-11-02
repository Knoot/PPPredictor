using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Data;
using PPPredictor.Data.DisplayInfos;
using PPPredictor.Interfaces;
using PPPredictor.Converter;
using SongCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using static PPPredictor.Core.DataType.Enums;
using PPPredictor.Core.DataType.MapPool;
using SongCore;

namespace PPPredictor.Utilities
{
    internal class PPPredictor : IPPPredictor
    {
        internal Leaderboard leaderboardName;
        #region internal values
        private float _percentage;
        private PPPBeatMapInfo _currentBeatMapInfo = new PPPBeatMapInfo();
        private bool _rankGainRunning = false;
        private int _predictionVersion;
        private bool _isActive = false;
        private bool _isBeatMapInfoLoaded = false;
        private bool _hasSelectedBeatMap;
        private DateTime _beatMapInfoLoadingStarted = DateTime.MinValue;
        private int _beatMapInfoRequestVersion = 0;
        private readonly RefreshQueue _refreshQueue = new RefreshQueue();
        private bool _reconnectRefreshRunning, _reconnectRefreshRequested;
        private bool _disposed;
        private readonly System.Threading.SynchronizationContext _uiContext = System.Threading.SynchronizationContext.Current;
        private string _lastUnavailableCalculation;
        private DisplayPPInfo _ppDisplay = new DisplayPPInfo();
        private PPGainResult _ppGainResult = new PPGainResult();
        private Timer _rankTimer;
        private readonly CalculatorInstance calculatorInstance;
        private readonly Func<bool> inMenu;
        private readonly Action<Leaderboard, string, FullScoreSyncReason> requestFullSync;
        private PPPMapPoolShort currentMapPool;
        private List<PPPMapPoolShort> lsMapPools = new List<PPPMapPoolShort>();
        private const double ImproveDataTargetWeightedPP = 1d;
        #endregion

        public string LeaderBoardName
        {
            get
            {
                return _leaderboardInfo.LeaderboardName;
            }
        }
        public string LeaderBoardIcon
        {
            get { return _leaderboardInfo.LeaderboardIcon; }
        }
        public string MapPoolIcon
        {
            get
            {
                string mapPoolIcon = currentMapPool?.IconUrl ?? string.Empty;
                return !string.IsNullOrEmpty(mapPoolIcon) ? mapPoolIcon : _leaderboardInfo.LeaderboardIcon;
            }
        }
        public byte[] MapPoolIconData
        {
            get { return currentMapPool?.IconData; }
            set { currentMapPool.IconData = value; }
        }

        internal PPPLeaderboardInfo _leaderboardInfo;
        private GameplayModifiers _gameplayModifiers;

        public event EventHandler<bool> OnDataLoading;
        public event EventHandler<DisplaySessionInfo> OnDisplaySessionInfo;
        public event EventHandler<DisplayPPInfo> OnDisplayPPInfo;
        public event EventHandler<DisplayImproveInfo> OnDisplayImproveInfo;
        public event EventHandler OnMapPoolRefreshed;

        #region
        public PPPredictor(Leaderboard leaderBoard, CalculatorInstance calculatorInstance, Func<bool> inMenu,
            Action<Leaderboard, string, FullScoreSyncReason> requestFullSync)
        {
            leaderboardName = leaderBoard;
            this.calculatorInstance = calculatorInstance;
            this.inMenu = inMenu;
            this.requestFullSync = requestFullSync;
            _leaderboardInfo = new PPPLeaderboardInfo(leaderBoard);
            lsMapPools = this.calculatorInstance.GetMapPools(leaderboardName);
            //Select current map pool from save data
            int index = 0;
            if(Plugin.ProfileInfo.MapPoolSelection.TryGetValue(leaderboardName.ToString(), out string value))
            {
                index = Math.Max(lsMapPools.FindIndex(x => x.Id == value), index);
            }
            currentMapPool = lsMapPools[index];
            currentMapPool.SelectedByLoading = true;

            //_ppCalculator.OnMapPoolRefreshed += PPCalculator_OnMapPoolRefreshed;

            _rankTimer = new System.Timers.Timer(500);
            _rankTimer.Elapsed += OnRankTimerElapsed;
            _rankTimer.AutoReset = false;
            _rankTimer.Enabled = false;

        }
        #endregion

        #region getter/setter
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hasSelectedBeatMap = false;
            _isActive = false;
            ++_beatMapInfoRequestVersion;
            ++_predictionVersion;
            _refreshQueue.Dispose();
            _rankTimer.Stop();
            _rankTimer.Elapsed -= OnRankTimerElapsed;
            _rankTimer.Dispose();
            OnDataLoading = null;
            OnDisplayPPInfo = null;
            OnDisplaySessionInfo = null;
            OnDisplayImproveInfo = null;
            OnMapPoolRefreshed = null;
        }
        public float Percentage
        {
            get => _percentage;
            set
            {
                _percentage = value;
            }
        }
        #region MapPools
        public List<object> MapPoolOptions
        {
            get
            {
                return lsMapPools.Select(f => (object)f).ToList();
            }
        }
        public object CurrentMapPool
        {
            get => (object)currentMapPool;
            set
            {
                bool isCurrentMapPoolChanging = IsCurrentMapPoolChanging(value);
                currentMapPool = (PPPMapPoolShort)value;
                UpdateMapPoolDetails();
                if (isCurrentMapPoolChanging)
                {
                    ++_beatMapInfoRequestVersion;
                    _isBeatMapInfoLoaded = false;
                    currentMapPool.SelectedByLoading = false;
                    this.RefreshCurrentData(10, true);
                }
                if(currentMapPool != null)
                {
                    Plugin.ProfileInfo.MapPoolSelection[leaderboardName.ToString()] = currentMapPool.Id;
                }
                SetActive(true, isCurrentMapPoolChanging);
            }
        }
        #endregion
        public string PPSuffix
        {
            get => _leaderboardInfo.PpSuffix;
        }
        #endregion

        #region eventHandling

        public void ChangeGameplayModifiers(GameplaySetupViewController gameplaySetupViewController)
        {
            if (currentMapPool != null && gameplaySetupViewController != null && gameplaySetupViewController.gameplayModifiers != null)
            {
                _gameplayModifiers = gameplaySetupViewController.gameplayModifiers;
                _currentBeatMapInfo = calculatorInstance.ApplyModifiersToBeatmapInfo(leaderboardName, currentMapPool.Id, _currentBeatMapInfo, Converter.Converter.ConvertGameplayModifiers(_gameplayModifiers));
                _currentBeatMapInfo.MaxPP = -1;
                CalculatePP();
            }
        }

        public async void DifficultyChanged(BeatmapLevel selectedBeatmapLevel, BeatmapKey beatmapKey)
        {
            try { await UpdateCurrentBeatMapInfos(selectedBeatmapLevel, beatmapKey); }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} map refresh failed: {ex.Message}"); }
        }

        public async Task UpdateCurrentBeatMapInfos(BeatmapLevel selectedBeatmapLevel, BeatmapKey beatmapKey)
        {
            if (_disposed) return;
            int requestVersion = ++_beatMapInfoRequestVersion;
            _isBeatMapInfoLoaded = false;
            _hasSelectedBeatMap = selectedBeatmapLevel != null && !ReferenceEquals(beatmapKey.beatmapCharacteristic, null);
            if (!_hasSelectedBeatMap)
            {
                _currentBeatMapInfo = new PPPBeatMapInfo();
                CalculatePP();
                return;
            }
            _beatMapInfoLoadingStarted = DateTime.Now;
            _currentBeatMapInfo = new PPPBeatMapInfo(selectedBeatmapLevel != null ? Collections.GetCustomLevelHash(selectedBeatmapLevel.levelID) : null, Converter.Converter.ConvertBeatmapKey(beatmapKey));
            SendDisplayImproveInfo("loading...", true);
            await AutoSelectMapPoolForBeatMapInfo();
            if (requestVersion != _beatMapInfoRequestVersion) return;
            await UpdateCurrentBeatMapInfos();
            if (requestVersion != _beatMapInfoRequestVersion) return;
            var needed = calculatorInstance.GetRequiredFullSync(leaderboardName, currentMapPool.Id);
            if (IsRanked() && needed != FullScoreSyncReason.None) requestFullSync(leaderboardName, currentMapPool.Id, needed);
            if (calculatorInstance.RequiresScoreRefresh(leaderboardName, currentMapPool.Id))
                await RefreshPoolData(currentMapPool.Id, 100, false, false, true, requestVersion);
            if (requestVersion != _beatMapInfoRequestVersion) return;
            CalculatePP();
            if (!IsRanked())
            {
                await Task.Delay(2000, _refreshQueue.Token);
                if (requestVersion == _beatMapInfoRequestVersion)
                {
                    CalculatePP();
                }
            }
        }

        private async Task UpdateCurrentBeatMapInfos()
        {
            if (!_hasSelectedBeatMap) return;
            int requestVersion = _beatMapInfoRequestVersion;
            string poolId = currentMapPool.Id;
            var request = new PPPBeatMapInfo(_currentBeatMapInfo, _currentBeatMapInfo.BaseStarRating);
            var beatMapInfo = await calculatorInstance.GetBeatMapInfoAsync(leaderboardName, poolId, request);
            if (!_hasSelectedBeatMap || requestVersion != _beatMapInfoRequestVersion || currentMapPool.Id != poolId) return;
            _currentBeatMapInfo = beatMapInfo;
            _currentBeatMapInfo.SelectedMapSearchString = !string.IsNullOrEmpty(_currentBeatMapInfo.CustomLevelHash) ? PPCalculator.CreateSeachString(_currentBeatMapInfo.CustomLevelHash, "SOLO" + _currentBeatMapInfo.BeatmapKey.serializedName, Core.ParsingUtil.ParseDifficultyNameToInt(_currentBeatMapInfo.BeatmapKey.difficulty.ToString())) : string.Empty;
            _currentBeatMapInfo.OldDotsEnabled = IsOldDotsActive(_currentBeatMapInfo.BeatmapKey);
            _currentBeatMapInfo = GetModifiedBeatMapInfo(_gameplayModifiers);
            _currentBeatMapInfo.MaxPP = -1;
            _isBeatMapInfoLoaded = true;
        }

        private async Task AutoSelectMapPoolForBeatMapInfo()
        {
            if (leaderboardName != Leaderboard.AccSaber || currentMapPool == null)
            {
                return;
            }

            int requestVersion = _beatMapInfoRequestVersion;
            string originalPoolId = currentMapPool.Id;
            PPPMapPoolShort mapPool = await calculatorInstance.FindMapPoolForBeatMapInfoAsync(leaderboardName, _currentBeatMapInfo);
            if (!_hasSelectedBeatMap || requestVersion != _beatMapInfoRequestVersion || currentMapPool.Id != originalPoolId) return;
            if (mapPool == null || mapPool.Id == currentMapPool.Id)
            {
                return;
            }

            currentMapPool = mapPool;
            string poolId = mapPool.Id;
            currentMapPool.SelectedByLoading = true;
            OnMapPoolRefreshed?.Invoke(this, null);
            IsDataLoading(true);
            try
            {
                await calculatorInstance.UpdateMapPoolDetails(leaderboardName, poolId);
                if (!_hasSelectedBeatMap || requestVersion != _beatMapInfoRequestVersion) return;
                await RefreshPoolData(poolId, 10, false, false, true, requestVersion);
            }
            finally
            {
                IsDataLoading(false);
            }
        }
        #endregion

        #region event sending
        private void IsDataLoading(bool isDataLoading)
        {
            if (_disposed) return;
            OnDataLoading?.Invoke(this, isDataLoading);
        }

        private void RunOnMainThread(Action action)
        {
            if (_disposed) return;
            if (_uiContext != null && System.Threading.SynchronizationContext.Current != _uiContext)
                _uiContext.Post(_ => { if (!_disposed) action(); }, null);
            else action();
        }

        private Action<int, int> ProgressForSelection(string poolId, int requestVersion)
        {
            return (loaded, total) => RunOnMainThread(() =>
            {
                if (currentMapPool?.Id == poolId && requestVersion == _beatMapInfoRequestVersion)
                    SendImproveDataLoadingProgress(loaded, total);
            });
        }
        private void SendDisplayPPInfo(DisplayPPInfo displayPPInfo)
        {
            if(_isActive) OnDisplayPPInfo?.Invoke(this, displayPPInfo);
        }
        private void SendImproveDataLoadingProgress(int loadedScores, int totalScores)
        {
            if (!_hasSelectedBeatMap)
            {
                SendDisplayImproveInfo("no map selected", false);
                return;
            }
            if (!HasImproveData())
            {
                return;
            }

            _ppDisplay.ImproveData = $"{LeaderBoardName}: {loadedScores:N0}/{totalScores:N0}";
            SendDisplayImproveInfo(_ppDisplay.ImproveData, true);
            SendDisplayPPInfo(_ppDisplay);
        }
        private void SendDisplayImproveInfo(string text, bool isLoading)
        {
            if (!_hasSelectedBeatMap)
            {
                text = "no map selected";
                isLoading = false;
            }
            if (HasImproveData()) OnDisplayImproveInfo?.Invoke(this, new DisplayImproveInfo(LeaderBoardName, text, isLoading));
        }
        private void SendDisplaySessionInfo(DisplaySessionInfo displaySessionInfo)
        {
            if (_isActive) OnDisplaySessionInfo?.Invoke(this, displaySessionInfo);
        }
        private void PPCalculator_OnMapPoolRefreshed(object sender, EventArgs e)
        {
            OnMapPoolRefreshed?.Invoke(this, null);
        }
        #endregion
        public double CalculatePPatPercentage(double percentage, PPPBeatMapInfo beatMapInfo, bool levelFailed = false, bool levelPaused = false)
        {
            var v = calculatorInstance.CalculatePPatPercentage(leaderboardName, currentMapPool.Id, beatMapInfo, percentage, levelFailed, levelPaused);
            return v;
        }

        public double CalculateMaxPP()
        {
            var v = calculatorInstance.CalculateMaxPP(leaderboardName, currentMapPool.Id, _currentBeatMapInfo);
            return v;
        }

        public PPPBeatMapInfo GetModifiedBeatMapInfo(GameplayModifiers gameplayModifiers, bool levelFailed = false, bool levelPaused = false)
        {
            PPPBeatMapInfo pppBeatMapInfo = calculatorInstance.ApplyModifiersToBeatmapInfo(leaderboardName, currentMapPool.Id, _currentBeatMapInfo, Converter.Converter.ConvertGameplayModifiers(gameplayModifiers), levelFailed, levelPaused);
            return pppBeatMapInfo;
        }

        public bool IsOldDotsActive(Core.DataType.BeatSaberEncapsulation.BeatmapKey beatmapKey)
        {
            return beatmapKey?.serializedName?.Contains(Core.Constants.OldDots) ?? false;
        }

        public double CalculatePPGain(double pp)
        {
            PPGainResult ppGainResult = calculatorInstance.GetPlayerScorePPGain(leaderboardName, currentMapPool.Id, _currentBeatMapInfo.SelectedMapSearchString, pp);
            return ppGainResult.PpDisplayValue;
        }

        public bool IsRanked()
        {
            try
            {
                return _currentBeatMapInfo.BaseStarRating.IsRanked() && (_leaderboardInfo.HasOldDotRanking || !_currentBeatMapInfo.OldDotsEnabled);
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"IsRanked {ex.Message}");
                return false;
            }
        }

        public double? GetPersonalBest()
        {
            return calculatorInstance.GetPersonalBest(leaderboardName, currentMapPool.Id, _currentBeatMapInfo.SelectedMapSearchString);
        }

        internal bool IsCurrentMapPoolChanging(object value)
        {
            
            var currentPool = CurrentMapPool as PPPMapPoolShort;
            var newMapPool = value as PPPMapPoolShort;
            return (currentPool == null || (currentPool != null && ((newMapPool != null && currentPool.Id != newMapPool.Id) || currentMapPool.SelectedByLoading)));
        }

        public void CalculatePP()
        {
            if (_disposed) return;
            ++_predictionVersion;
            if(currentMapPool == null) return;
            if (!_hasSelectedBeatMap)
            {
                _rankTimer.Stop();
                _ppDisplay = new DisplayPPInfo { ImproveData = "no map selected" };
                SendDisplayImproveInfo(_ppDisplay.ImproveData, false);
                DisplayRankGain(null, _ppDisplay);
                return;
            }
            if (!_isBeatMapInfoLoaded)
            {
                _rankTimer.Stop();
                _ppDisplay = new DisplayPPInfo { ImproveData = "loading..." };
                SendDisplayImproveInfo(_ppDisplay.ImproveData, true);
                DisplayRankGain(null, _ppDisplay);
                return;
            }
            if (_currentBeatMapInfo.MaxPP == -1) _currentBeatMapInfo.MaxPP = CalculateMaxPP();
            double pp = CalculatePPatPercentage(_percentage, _currentBeatMapInfo);
            _ppGainResult = calculatorInstance.GetPlayerScorePPGain(leaderboardName, currentMapPool.Id, _currentBeatMapInfo.SelectedMapSearchString, pp);
            double ppGains = PPCalculator.Zeroizer(_ppGainResult.PpDisplayValue);
            _ppDisplay = new DisplayPPInfo();
            if (_currentBeatMapInfo.MaxPP > 0 && pp >= _currentBeatMapInfo.MaxPP)
            {
                _ppDisplay.PPRaw = $"<color=\"yellow\">{pp:F2}{PPSuffix}</color>";
            }
            else
            {
                _ppDisplay.PPRaw = $"{pp:F2}{PPSuffix}";
            }
            _ppDisplay.PPGain = $"{ppGains:+0.##;-0.##;0}{PPSuffix}";
            _ppDisplay.PPGainDiffColor = DisplayHelper.GetDisplayColor(ppGains, false, true);
            _ppDisplay.ImproveData = GetImproveDataDisplay();
            SendDisplayImproveInfo(_ppDisplay.ImproveData, IsImproveDataLoadingDisplay(_ppDisplay.ImproveData));

            DisplayRankGain(null, _ppDisplay);
            //Restart rank calculation timer
            _rankTimer.Stop();
            _rankTimer.Start();
        }

        private string GetImproveDataDisplay()
        {
            if (!HasImproveData())
            {
                return string.Empty;
            }
            if (!_hasSelectedBeatMap) return "no map selected";
            if (!_isBeatMapInfoLoaded)
            {
                return "loading...";
            }
            if (_currentBeatMapInfo.MapInfoRefreshFailed && !IsRanked()) return "load failed";
            if (!IsRanked())
            {
                if ((DateTime.Now - _beatMapInfoLoadingStarted).TotalSeconds < 2)
                {
                    return "loading...";
                }

                return "unranked";
            }

            switch (_currentBeatMapInfo.CalculationBlockReason)
            {
                case PPCalculationBlockReason.RequiresSmallCubesAndProMode:
                    return "SCPM: SC + PM required";
                case PPCalculationBlockReason.ModifiersNotAllowed:
                    return "No modifiers: mods not allowed";
                case PPCalculationBlockReason.NegativeModifiersNotAllowed:
                    return "Golf: negative mods not allowed";
            }

            double maxPP = CalculateMaxPP();
            if (maxPP <= 0 || double.IsNaN(maxPP) || double.IsInfinity(maxPP))
            {
                ReportUnavailableCalculation(maxPP);
                return "unavailable";
            }
            _lastUnavailableCalculation = null;
            if (!calculatorInstance.HasScoreDataForPPGain(leaderboardName, currentMapPool.Id))
            {
                return calculatorInstance.HasDataRefreshFailed(leaderboardName, currentMapPool.Id) ? "load failed" : "loading...";
            }

            string dataStatus = _currentBeatMapInfo.MapInfoRefreshFailed || calculatorInstance.HasDataRefreshFailed(leaderboardName, currentMapPool.Id) ? "cached: " : string.Empty;

            double? rawPP = FindRawPPForWeightedGain(ImproveDataTargetWeightedPP, maxPP);
            if (!rawPP.HasValue)
            {
                return $"{dataStatus}{ImproveDataTargetWeightedPP:F2}{PPSuffix} = >{maxPP:F2}{PPSuffix} (?%)";
            }

            double? percentage = FindPercentageForRawPP(rawPP.Value);
            string percentageText = percentage.HasValue ? $"{percentage.Value:F2}%" : "?%";
            return $"{dataStatus}{ImproveDataTargetWeightedPP:F2}{PPSuffix} = {rawPP.Value:F2}{PPSuffix} ({percentageText})";
        }

        private void ReportUnavailableCalculation(double maxPP)
        {
            var rating = _currentBeatMapInfo.ModifiedStarRating;
            string diagnostic = FormattableString.Invariant(
                $"PP calculation unavailable: leaderboard={LeaderBoardName}; pool={currentMapPool.Id} ({currentMapPool.MapPoolName}); map={_currentBeatMapInfo.SelectedMapSearchString}; maxPP={maxPP}; multiplier={rating.Multiplier}; acc={rating.AccRating}; pass={rating.PassRating}; tech={rating.TechRating}; predictedAcc={rating.PredictedAcc}; noteSpan={rating.NoteSpan}; notes={rating.Notes}; sliders={rating.SliderPercentage}; blockReason={_currentBeatMapInfo.CalculationBlockReason}");
            if (diagnostic == _lastUnavailableCalculation) return;
            _lastUnavailableCalculation = diagnostic;
            Plugin.ErrorPrint(diagnostic);
        }

        private bool HasImproveData()
        {
            return leaderboardName == Leaderboard.BeatLeader || leaderboardName == Leaderboard.ScoreSaber || leaderboardName == Leaderboard.AccSaber;
        }

        private bool IsImproveDataLoadingDisplay(string improveData)
        {
            return _hasSelectedBeatMap && (!_isBeatMapInfoLoaded || improveData == "loading...");
        }

        private double? FindRawPPForWeightedGain(double targetWeightedPP, double maxPP)
        {
            return ImproveTargetSearch.FindRawPP(rawPP =>
                calculatorInstance.GetPlayerScorePPGain(leaderboardName, currentMapPool.Id,
                    _currentBeatMapInfo.SelectedMapSearchString, rawPP).PpGainWeighted, targetWeightedPP, maxPP);
        }

        private double? FindPercentageForRawPP(double targetRawPP)
        {
            return ImproveTargetSearch.FindDisplayedPercentage(
                percentage => CalculatePPatPercentage(percentage, _currentBeatMapInfo), targetRawPP);
        }

        private void OnRankTimerElapsed(object sender, ElapsedEventArgs e)
        {
            RunOnMainThread(UpdateRankPrediction);
        }

        private async void UpdateRankPrediction()
        {
            if (!_hasSelectedBeatMap || !_isBeatMapInfoLoaded || _rankGainRunning) return;
            int version = _predictionVersion;
            string poolId = currentMapPool.Id;
            var display = _ppDisplay;
            _rankGainRunning = true;
            try
            {
                var rankGain = await calculatorInstance.GetPlayerRankGain(leaderboardName, poolId, _ppGainResult.PpTotal);
                if (_hasSelectedBeatMap && version == _predictionVersion && currentMapPool.Id == poolId)
                    DisplayRankGain(rankGain, display);
            }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} rank refresh failed: {ex.Message}"); }
            finally
            {
                _rankGainRunning = false;
                if (version != _predictionVersion && _hasSelectedBeatMap && _isBeatMapInfoLoaded) _rankTimer.Start();
            }
        }

        private void DisplayRankGain(RankGainResult rankGainResult, DisplayPPInfo ppDisplay)
        {
            if (rankGainResult != null)
            {
                ppDisplay.PredictedRankDiffColor = DisplayHelper.GetDisplayColor(rankGainResult.RankGainGlobal, false);
                ppDisplay.PredictedCountryRankDiffColor = _leaderboardInfo.IsCountryRankEnabled ? DisplayHelper.GetDisplayColor(rankGainResult.RankGainCountry, false) : DisplayHelper.ColorCountryRankDisabled;
                if (rankGainResult.IsRankGainCanceledByLimit)
                {
                    ppDisplay.PredictedRank = $"<{rankGainResult.RankGlobal:N0}";
                    ppDisplay.PredictedRankDiff = rankGainResult.RankGainGlobal.ToString(">+#;<-#;0");
                    ppDisplay.PredictedCountryRank = $"<{rankGainResult.RankCountry:N0}";
                    ppDisplay.PredictedCountryRankDiff = rankGainResult.RankGainCountry.ToString(">+#;<-#;0");
                }
                else
                {
                    ppDisplay.PredictedRank = $"{rankGainResult.RankGlobal:N0}";
                    ppDisplay.PredictedRankDiff = rankGainResult.RankGainGlobal.ToString("+#;-#;0");
                    ppDisplay.PredictedCountryRank = $"{rankGainResult.RankCountry:N0}";
                    ppDisplay.PredictedCountryRankDiff = rankGainResult.RankGainCountry.ToString("+#;-#;0");
                }
            }
            else
            {
                ppDisplay.PredictedRank = "...";
                ppDisplay.PredictedRankDiff = "?";
                ppDisplay.PredictedRankDiffColor = DisplayHelper.GetDisplayColor(0, false);
                ppDisplay.PredictedCountryRank = "...";
                ppDisplay.PredictedCountryRankDiff = "?";
                ppDisplay.PredictedCountryRankDiffColor = _leaderboardInfo.IsCountryRankEnabled ? DisplayHelper.GetDisplayColor(0, false) : DisplayHelper.ColorCountryRankDisabled;
            }
            SendDisplayPPInfo(ppDisplay);
        }

        private async Task DisplaySession(bool doResetSession, string poolId = null, bool refresh = true)
        {
            poolId = poolId ?? currentMapPool.Id;
            int version = _beatMapInfoRequestVersion;
            (PPPPlayer sessionPlayer, PPPPlayer currentPlayer) = refresh
                ? await calculatorInstance.UpdatePlayer(leaderboardName, poolId, doResetSession)
                : calculatorInstance.GetCachedPlayers(leaderboardName, poolId);
            if (_disposed) return;
            Plugin.ProfileInfo.LastSessionReset = calculatorInstance.LastSessionReset;
            if (currentMapPool.Id != poolId || version != _beatMapInfoRequestVersion) return;
            DisplaySessionInfo sessionDisplay = new DisplaySessionInfo();
            if (sessionPlayer != null && currentPlayer != null)
            {
                if (Plugin.ProfileInfo.DisplaySessionValues)
                {
                    sessionDisplay.SessionRank = $"{sessionPlayer.Rank:N0}";
                    sessionDisplay.SessionCountryRank = $"{sessionPlayer.CountryRank:N0}";
                    sessionDisplay.SessionPP = $"{sessionPlayer.Pp:F2}{_leaderboardInfo.PpSuffix}";
                }
                else
                {
                    sessionDisplay.SessionRank = $"{currentPlayer.Rank:N0}";
                    sessionDisplay.SessionCountryRank = $"{currentPlayer.CountryRank:N0}";
                    sessionDisplay.SessionPP = $"{currentPlayer.Pp:F2}{_leaderboardInfo.PpSuffix}";
                }
                sessionDisplay.SessionCountryRankDiff = (currentPlayer.CountryRank - sessionPlayer.CountryRank).ToString("+#;-#;0");
                sessionDisplay.SessionCountryRankDiffColor = _leaderboardInfo.IsCountryRankEnabled ? DisplayHelper.GetDisplayColor((currentPlayer.CountryRank - sessionPlayer.CountryRank), true) : DisplayHelper.ColorCountryRankDisabled;
                sessionDisplay.SessionRankDiff = (currentPlayer.Rank - sessionPlayer.Rank).ToString("+#;-#;0");
                sessionDisplay.SessionRankDiffColor = DisplayHelper.GetDisplayColor((currentPlayer.Rank - sessionPlayer.Rank), true);
                sessionDisplay.SessionPPDiff = $"{PPCalculator.Zeroizer(currentPlayer.Pp - sessionPlayer.Pp):+0.##;-0.##;0}{_leaderboardInfo.PpSuffix}";
                sessionDisplay.SessionPPDiffColor = DisplayHelper.GetDisplayColor((currentPlayer.Pp - sessionPlayer.Pp), false);
            }
            sessionDisplay.CountryRankFontColor = _leaderboardInfo.IsCountryRankEnabled ? DisplayHelper.ColorWhite : DisplayHelper.ColorCountryRankDisabled;
            SendDisplaySessionInfo(sessionDisplay);
        }

        public void ScoreSet(PPPScoreSetData data)
        {
            if (string.IsNullOrEmpty(data.hash)) return;
            RunOnMainThread(() =>
            {
                string poolId = currentMapPool.Id;
                if (leaderboardName == Leaderboard.AccSaber)
                {
                    foreach (var pool in calculatorInstance.GetMapPools(leaderboardName))
                    {
                        if (calculatorInstance.IsScoreSetOnCurrentMapPool(leaderboardName, pool.Id, data))
                            RefreshAfterScore(pool.Id, data);
                    }
                    return;
                }
                if (calculatorInstance.IsScoreSetOnCurrentMapPool(leaderboardName, poolId, data))
                    RefreshAfterScore(poolId, data);
            });
        }

        private async void RefreshAfterScore(string poolId, PPPScoreSetData data)
        {
            var expected = new PPPScoreSetData
            {
                hash = data.hash,
                pp = data.leaderboardName == LeaderBoardName ? data.pp : null,
                score = leaderboardName == Leaderboard.AccSaber ? data.score : null
            };
            if (expected.pp.HasValue && expected.pp.Value <= 0) expected = null;
            else calculatorInstance.RegisterExpectedScore(leaderboardName, poolId, expected);
            try { await RefreshPoolData(poolId, 10, false, false, true, _beatMapInfoRequestVersion, expected); }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} score refresh failed: {ex.Message}"); }
        }

        public void RefreshCurrentData(int fetchLength, bool refreshStars = false, bool fetchOnePage = false)
        {
            RefreshCurrentData(fetchLength, refreshStars, fetchOnePage, true);
        }

        public void RefreshAfterReconnect()
        {
            RunOnMainThread(async () =>
            {
                _reconnectRefreshRequested = true;
                if (_reconnectRefreshRunning) return;
                _reconnectRefreshRunning = true;
                try
                {
                    do
                    {
                        _reconnectRefreshRequested = false;
                        int version = _beatMapInfoRequestVersion;
                        string selectedPool = currentMapPool.Id;
                        foreach (var pool in calculatorInstance.GetMapPools(leaderboardName))
                        {
                            if (pool.Id != selectedPool && !calculatorInstance.HasScoreDataForPPGain(leaderboardName, pool.Id)
                                && !calculatorInstance.RequiresScoreRefresh(leaderboardName, pool.Id)) continue;
                            await RefreshPoolData(pool.Id, _leaderboardInfo.LargePageSize, pool.Id == selectedPool,
                                false, true, version);
                        }
                    }
                    while (_reconnectRefreshRequested);
                }
                catch (OperationCanceledException) when (_disposed) { }
                catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} reconnect synchronization failed: {ex.Message}"); }
                finally { _reconnectRefreshRunning = false; }
            });
        }

        private async void RefreshCurrentData(int fetchLength, bool refreshStars, bool fetchOnePage, bool refreshProfile)
        {
            try { await RefreshPoolData(currentMapPool.Id, fetchLength, refreshStars, fetchOnePage, refreshProfile, _beatMapInfoRequestVersion); }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} refresh failed: {ex.Message}"); }
        }

        private Task RefreshPoolData(string poolId, int fetchLength, bool refreshStars, bool fetchOnePage,
            bool refreshProfile, int requestVersion, PPPScoreSetData expectedScore = null, bool resetSession = false)
        {
            return _refreshQueue.Enqueue(async () =>
            {
                IsDataLoading(true);
                try
                {
                    _refreshQueue.Token.ThrowIfCancellationRequested();
                    if (refreshStars && currentMapPool.Id == poolId && requestVersion == _beatMapInfoRequestVersion)
                        await UpdateCurrentBeatMapInfos();
                    var fullReason = calculatorInstance.GetRequiredFullSync(leaderboardName, poolId, expectedScore != null);
                    if (fullReason != FullScoreSyncReason.None)
                    {
                        requestFullSync(leaderboardName, poolId, fullReason);
                        if (resetSession && inMenu()) await DisplaySession(true, poolId);
                        return;
                    }
                    if (!_leaderboardInfo.HasGetRecentScoresFunctionality || leaderboardName == Leaderboard.NoLeaderboard)
                    {
                        if (refreshProfile) await DisplaySession(resetSession, poolId);
                        if (currentMapPool.Id == poolId) CalculatePP();
                        return;
                    }
                    bool confirmed = false;
                    int attempts = expectedScore == null ? 1 : 3;
                    for (int attempt = 0; attempt < attempts; attempt++)
                    {
                        confirmed = await calculatorInstance.GetPlayerScores(leaderboardName, poolId, fetchLength,
                            _leaderboardInfo.LargePageSize, fetchOnePage, ProgressForSelection(poolId, requestVersion), expectedScore,
                            allowFullRefresh: false);
                        _refreshQueue.Token.ThrowIfCancellationRequested();
                        if (refreshProfile) await DisplaySession(resetSession, poolId);
                        calculatorInstance.ObserveScoreRefresh(leaderboardName, poolId, inMenu());
                        if (calculatorInstance.GetPendingFullSyncs().Any(x => x.Board == leaderboardName && x.PoolId == poolId))
                            requestFullSync(leaderboardName, poolId, FullScoreSyncReason.None);
                        var deferred = calculatorInstance.GetRequiredFullSync(leaderboardName, poolId);
                        if (deferred != FullScoreSyncReason.None) requestFullSync(leaderboardName, poolId, deferred);
                        if (currentMapPool.Id == poolId) CalculatePP();
                        if (confirmed) break;
                        if (attempt + 1 < attempts) await Task.Delay(1500 * (attempt + 1), _refreshQueue.Token);
                    }
                    if (!confirmed && expectedScore != null)
                        Plugin.ErrorPrint($"{LeaderBoardName} score was not confirmed by API after {attempts} attempts: pool={poolId}; map={expectedScore.hash}; expectedPP={expectedScore.pp}");
                    if (currentMapPool.Id == poolId) CalculatePP();
                }
                finally { IsDataLoading(false); }
            });
        }

        public async Task UpdateCurrentAndCheckResetSession(bool doResetSession)
        {
            IsDataLoading(true);
            try { await DisplaySession(doResetSession); }
            finally { IsDataLoading(false); }
        }

        public Task RunFullScoreSync(string poolId, System.Threading.CancellationToken cancellation)
        {
            return _refreshQueue.Enqueue(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                IsDataLoading(true);
                try
                {
                    int version = _beatMapInfoRequestVersion;
                    Plugin.DebugPrint($"Full score sync: {LeaderBoardName}/{poolId}");
                    await calculatorInstance.SynchronizeScores(leaderboardName, poolId, cancellation, ProgressForSelection(poolId, version));
                    cancellation.ThrowIfCancellationRequested();
                    await DisplaySession(false, poolId, false);
                    if (currentMapPool.Id == poolId) CalculatePP();
                }
                finally { IsDataLoading(false); }
            });
        }

        public async Task CheckSyncConfirmations()
        {
            if (_disposed || !inMenu() || _refreshQueue.IsBusy) return;
            foreach (var poolId in calculatorInstance.GetDueConfirmationPools(leaderboardName))
            {
                if (_disposed || !inMenu()) return;
                await RefreshPoolData(poolId, 10, false, false, true, _beatMapInfoRequestVersion);
            }
        }

        public async void ResetDisplay(bool resetAll)
        {
            try { await RefreshPoolData(currentMapPool.Id, 100, false, false, true, _beatMapInfoRequestVersion, null, resetAll); }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} reset refresh failed: {ex.Message}"); }
        }

        public void SetActive(bool setActive, bool hasPoolChanged = false)
        {
            if (_isActive && setActive && !hasPoolChanged) return;
            _isActive = setActive;
            if (setActive || hasPoolChanged)
            {
                _ = DisplaySession(false);
                CalculatePP();
                RefreshScoreDataIfMissing();
            }
        }

        private void RefreshScoreDataIfMissing()
        {
            if (!_isActive || currentMapPool == null || _refreshQueue.IsBusy)
            {
                return;
            }
            if (calculatorInstance.HasScoreDataForPPGain(leaderboardName, currentMapPool.Id))
            {
                return;
            }

            RefreshCurrentData(_leaderboardInfo.LargePageSize, false, false, false);
        }

        private async void UpdateMapPoolDetails()
        {
            if (currentMapPool != null)
            {
                string poolId = currentMapPool.Id;
                IsDataLoading(true);
                try { await calculatorInstance.UpdateMapPoolDetails(leaderboardName, poolId); }
                catch (OperationCanceledException) when (_disposed) { }
                catch (Exception ex) { Plugin.ErrorPrint($"{LeaderBoardName} pool refresh failed: {ex.Message}"); }
                finally { IsDataLoading(false); }
            }
        }

        public async Task GetMapPoolIconData()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    using (var response = await client.GetAsync(MapPoolIcon, _refreshQueue.Token))
                    {
                        byte[] rawData = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (_disposed) return;
                        byte[] resizedData = DisplayHelper.ResizeImage(rawData, 128, 128);
                        MapPoolIconData = resizedData;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"GetMapPoolIconData {MapPoolIcon} Error: {ex.Message}");
            }
        }

        public PPPMapPoolShort FindPoolWithSyncURL(string syncUrl)
        {
            return calculatorInstance.FindPoolWithSyncURL(leaderboardName, syncUrl);
        }
    }
}

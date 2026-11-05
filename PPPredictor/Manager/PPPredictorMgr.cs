using BeatSaberPlaylistsLib.Types;
using IPA.Loader;
using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Data.DisplayInfos;
using PPPredictor.Interfaces;
using PPPredictor.WebSocket;
using SongDetailsCache;
using SongDetailsCache.Structs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zenject;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Utilities
{
    internal class PPPredictorMgr : IInitializable, IDisposable, IPPPredictorMgr

    {
        private readonly WebSocketMgr _websocketMgr;
        private const string _beatleaderSyncUrlIdentifier = ".beatleader.";
        private const string _customDataSyncUrl = "syncURL";
        private List<IPPPredictor> _lsPPPredictor = new List<IPPPredictor>();
        private int index = 0;
        private IPPPredictor _currentPPPredictor = new PPPredictorDummy();
        private readonly SemaphoreSlim resetGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SynchronizationContext uiContext = SynchronizationContext.Current;
        private readonly Timer autosave;
        private bool disposed, resetting;
        private int savePending;
        private bool menuAvailable, menuSeen, startupSyncScheduled, fullSyncRunning;
        private CancellationTokenSource fullSyncPause = new CancellationTokenSource();
        private Task fullSyncTask = Task.CompletedTask;
        private bool isLeftArrowActive = false;
        private bool isRightArrowActive = false;
        private bool isMapPoolDropDownActive = true;
        private bool isLeaderboardNavigationActive = false;
        private int _loadingCounter = 0;

        public event EventHandler<bool> ViewActivated;
        public event EventHandler<bool> OnDataLoading;
        public event EventHandler<DisplaySessionInfo> OnDisplaySessionInfo;
        public event EventHandler<DisplayPPInfo> OnDisplayPPInfo;
        public event EventHandler<DisplayImproveInfo> OnDisplayImproveInfo;
        public event EventHandler OnMapPoolRefreshed;

        public IPPPredictor CurrentPPPredictor { get => _currentPPPredictor; }
        public bool IsLeftArrowActive { get => isLeftArrowActive; }
        public bool IsRightArrowActive { get => isRightArrowActive; }
        public bool IsMapPoolDropDownActive { get => isMapPoolDropDownActive; }
        public bool IsLeaderboardNavigationActive { get => isLeaderboardNavigationActive; }

        public WebSocketMgr WebsocketMgr { get => _websocketMgr; }

        internal static CalculatorInstance CalculatorInstance;
        private SongDetails songDetails { get; set; }
        private float _percentage;

        internal PPPredictorMgr()
        {
            this._websocketMgr = new WebSocketMgr(this);
            autosave = new Timer(_ => QueueAutosave(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            _ = ResetPredictors(true);
        }

        public async Task ResetPredictors(bool isConstructor = false)
        {
            if (disposed) return;
            try { await resetGate.WaitAsync(lifetime.Token); }
            catch (OperationCanceledException) when (disposed) { return; }
            resetting = true;
            try
            {
                fullSyncPause.Cancel();
                RefreshLeaderboardVisibilityByIPAPluginManager();
                DisposePredictors();
                _loadingCounter = 0;
                OnDataLoading?.Invoke(this, false);
                var previous = CalculatorInstance;
                previous?.Dispose();
                await _websocketMgr.CloseScoreWebSockets();
                await fullSyncTask;
                if (previous != null)
                {
                    previous.OnMessage -= Logging_OnMessage;
                    await previous.ShutdownAsync();
                    if (!Plugin.ProfileInfo.RefreshAllLeaderboards)
                    {
                        Plugin.ProfileInfo.DctleaderBoardData = previous.GetSaveData();
                        Plugin.ProfileInfo.LastSessionReset = previous.LastSessionReset;
                    }
                    CalculatorInstance = null;
                }
                lifetime.Token.ThrowIfCancellationRequested();
                songDetails = await SongDetails.Init();
                var next = await CalculatorInstance.CreateAsync(
                    await Plugin.ProfileInfo.ParseToSetting(),
                    Plugin.ProfileInfo.DctleaderBoardData,
                    (PPPBeatMapInfo beatMapInfo) => ScoreSaberSongCoreLookUp(beatMapInfo), lifetime.Token
                );
                if (disposed) { await next.ShutdownAsync(); return; }
                CalculatorInstance = next;
                CalculatorInstance.OnMessage += Logging_OnMessage;
                if (Plugin.ProfileInfo.IsScoreSaberEnabled) _lsPPPredictor.Add(CreatePredictor(Leaderboard.ScoreSaber));
                if (Plugin.ProfileInfo.IsBeatLeaderEnabled) _lsPPPredictor.Add(CreatePredictor(Leaderboard.BeatLeader));
                if (Plugin.ProfileInfo.IsHitBloqEnabled) _lsPPPredictor.Add(CreatePredictor(Leaderboard.HitBloq));
                if (Plugin.ProfileInfo.IsAccSaberEnabled) _lsPPPredictor.Add(CreatePredictor(Leaderboard.AccSaber));
                if (!startupSyncScheduled)
                {
                    CalculatorInstance.ScheduleStartupSyncs();
                    startupSyncScheduled = true;
                }
                if (_lsPPPredictor.Count == 0)
                {
                    _lsPPPredictor.Add(CreatePredictor(Leaderboard.NoLeaderboard));
                }


                index = _lsPPPredictor.FindIndex(x => x.LeaderBoardName == Plugin.ProfileInfo.LastLeaderBoardSelected);
                if (index >= 0)
                {
                    _currentPPPredictor = _lsPPPredictor[index];
                }
                else
                {
                    _currentPPPredictor = _lsPPPredictor[0];
                }
                foreach (IPPPredictor pPPredictor in _lsPPPredictor)
                {
                    pPPredictor.Percentage = _percentage;
                    pPPredictor.OnDataLoading += PPPredictor_OnDataLoading;
                    pPPredictor.OnDisplayPPInfo += PPPredictor_OnDisplayPPInfo;
                    pPPredictor.OnDisplayImproveInfo += PPPredictor_OnDisplayImproveInfo;
                    pPPredictor.OnDisplaySessionInfo += PPPredictor_OnDisplaySessionInfo;
                    pPPredictor.OnMapPoolRefreshed += PPPredictor_OnMapPoolRefreshed;
                }
                if(Plugin.ProfileInfo.RefreshAllLeaderboards)
                {
                    //Case when the leaderboard info is cleared while loading from json, start refresh of scores
                    foreach (var item in _lsPPPredictor)
                    {
                        if(item.LeaderBoardName == CurrentPPPredictor.LeaderBoardName)
                        {
                            CurrentPPPredictor.SetActive(true);
                        }
                        else
                        {
                            item.ResetDisplay(false);
                        }
                    }
                }
                else
                {
                    CurrentPPPredictor.SetActive(true);
                }
                SetNavigationArrowInteractivity();
                Plugin.ProfileInfo.RefreshAllLeaderboards = false;
                _websocketMgr.CreateScoreWebSockets();
            }
            catch (OperationCanceledException) when (disposed) { }
            catch (Exception ex)
            {
                DisposePredictors();
                if (CalculatorInstance != null) await CalculatorInstance.ShutdownAsync();
                CalculatorInstance = null;
                Plugin.ErrorPrint($"Unable to initialize predictors: {ex.Message}");
            }
            finally
            {
                resetting = false;
                resetGate.Release();
                if (!disposed)
                {
                    RenewFullSyncToken();
                    PumpFullSyncs();
                }
            }
        }

        private IPPPredictor CreatePredictor(Leaderboard board) => new PPPredictor(board, CalculatorInstance,
            () => menuAvailable && !disposed && !resetting, QueueFullScoreSync);

        private void QueueFullScoreSync(Leaderboard board, string poolId, FullScoreSyncReason reason)
        {
            if (disposed || CalculatorInstance == null) return;
            CalculatorInstance.RequestFullScoreSync(board, poolId, reason);
            PumpFullSyncs();
        }

        public void SetMenuAvailable(bool available)
        {
            if (disposed) return;
            if (!available)
            {
                menuAvailable = false;
                fullSyncPause.Cancel();
                CalculatorInstance?.SuppressMismatchAfterGameplay();
                return;
            }
            if (!menuAvailable && menuSeen) CalculatorInstance?.SuppressMismatchAfterGameplay();
            menuAvailable = true;
            menuSeen = true;
            RenewFullSyncToken();
            PumpFullSyncs();
        }

        private void RenewFullSyncToken()
        {
            if (!fullSyncPause.IsCancellationRequested) return;
            var previous = fullSyncPause;
            fullSyncPause = new CancellationTokenSource();
            _ = fullSyncTask.ContinueWith(_ => previous.Dispose(), TaskScheduler.Default);
        }

        public void RequestFullScoreSync()
        {
            if (disposed || resetting || CalculatorInstance == null) return;
            foreach (var predictor in _lsPPPredictor)
            {
                if (!Enum.TryParse(predictor.LeaderBoardName, out Leaderboard board)) continue;
                foreach (var pool in CalculatorInstance.GetMapPools(board))
                    if (Equals(pool.Id, ((PPPMapPoolShort)predictor.CurrentMapPool).Id)
                        || CalculatorInstance.HasScoreDataForPPGain(board, pool.Id) || CalculatorInstance.RequiresScoreRefresh(board, pool.Id))
                        CalculatorInstance.RequestFullScoreSync(board, pool.Id, FullScoreSyncReason.Manual);
            }
            PumpFullSyncs();
        }

        private void PumpFullSyncs()
        {
            if (disposed || resetting || !menuAvailable || fullSyncRunning || CalculatorInstance == null) return;
            fullSyncRunning = true;
            fullSyncTask = RunFullSyncs(fullSyncPause.Token);
        }

        private async Task RunFullSyncs(CancellationToken token)
        {
            try
            {
                while (!disposed && !resetting && menuAvailable)
                {
                    token.ThrowIfCancellationRequested();
                    var requests = CalculatorInstance.GetPendingFullSyncs();
                    if (requests.Count == 0) break;
                    var next = requests[0];
                    var predictor = _lsPPPredictor.FirstOrDefault(x => x.LeaderBoardName == next.Board.ToString());
                    if (predictor == null) break;
                    Plugin.DebugPrint($"Full score sync reason: {next.Board}/{next.PoolId}: {next.Reason}");
                    await predictor.RunFullScoreSync(next.PoolId, token);
                    if (!disposed && !resetting && !Plugin.ProfileInfo.RefreshAllLeaderboards)
                        await ProfileInfoMgr.SaveCurrentProfileAsync(Plugin.ProfileInfo, CalculatorInstance);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || disposed || resetting) { }
            catch (Exception ex) { Plugin.ErrorPrint($"Full sync queue failed: {ex.Message}"); }
            finally
            {
                fullSyncRunning = false;
                if (token.IsCancellationRequested && menuAvailable && !disposed && !resetting)
                    uiContext?.Post(_ => PumpFullSyncs(), null);
            }
        }

        private void QueueAutosave()
        {
            if (disposed || Interlocked.Exchange(ref savePending, 1) != 0) return;
            if (uiContext == null) { Interlocked.Exchange(ref savePending, 0); return; }
            uiContext.Post(async _ =>
            {
                try
                {
                    var calculator = CalculatorInstance;
                    if (!disposed && !resetting && !Plugin.ProfileInfo.RefreshAllLeaderboards && calculator != null && !calculator.IsDisposed)
                        await ProfileInfoMgr.SaveCurrentProfileAsync(Plugin.ProfileInfo, calculator);
                    if (!disposed && !resetting && menuAvailable)
                    {
                        foreach (var predictor in _lsPPPredictor.ToArray()) await predictor.CheckSyncConfirmations();
                        PumpFullSyncs();
                    }
                }
                catch (Exception ex) { Plugin.ErrorPrint($"Profile autosave failed: {ex.Message}"); }
                finally { Interlocked.Exchange(ref savePending, 0); }
            }, null);
        }

        private void DisposePredictors()
        {
            foreach (var predictor in _lsPPPredictor)
            {
                predictor.OnDataLoading -= PPPredictor_OnDataLoading;
                predictor.OnDisplayPPInfo -= PPPredictor_OnDisplayPPInfo;
                predictor.OnDisplayImproveInfo -= PPPredictor_OnDisplayImproveInfo;
                predictor.OnDisplaySessionInfo -= PPPredictor_OnDisplaySessionInfo;
                predictor.OnMapPoolRefreshed -= PPPredictor_OnMapPoolRefreshed;
                predictor.Dispose();
            }
            _lsPPPredictor = new List<IPPPredictor>();
            _currentPPPredictor = new PPPredictorDummy();
        }

        private void Logging_OnMessage(object sender, LoggingMessage e)
        {
            switch (e.loggingType)
            {
                case LoggingMessage.LoggingType.Error:
                    Plugin.ErrorPrint(e.message);
                    break;
                case LoggingMessage.LoggingType.Info:
                    Plugin.DebugPrint(e.message);
                    break;
                case LoggingMessage.LoggingType.DebugNetworkPrint:
                    Plugin.DebugNetworkPrint(e.message, e.leaderboard);
                    break;
                default:
                    return;
            }
        }

        private PPPBeatMapInfo ScoreSaberSongCoreLookUp(PPPBeatMapInfo beatMapInfo)
        {
            if (songDetails.songs.FindByHash(beatMapInfo.CustomLevelHash, out Song song))
            {
                if(Enum.TryParse(beatMapInfo.BeatmapKey.difficulty.ToString(), out MapDifficulty mapDifficulty))
                {
                    if (song.GetDifficulty(out SongDifficulty songDiff, mapDifficulty))
                    {
                        return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(songDiff.stars));
                    }
                }
            }
            return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0));
        }

        private void RefreshLeaderboardVisibilityByIPAPluginManager()
        {
            List<PluginMetadata> lsEnabledPlugin = PluginManager.EnabledPlugins.ToList();
            Plugin.ProfileInfo.IsScoreSaberEnabled = lsEnabledPlugin.FirstOrDefault(x => x.Name == Leaderboard.ScoreSaber.ToString()) != null;
            Plugin.ProfileInfo.IsBeatLeaderEnabled = lsEnabledPlugin.FirstOrDefault(x => x.Name == Leaderboard.BeatLeader.ToString()) != null;
            Plugin.ProfileInfo.IsHitBloqEnabled = lsEnabledPlugin.FirstOrDefault(x => x.Name == CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Leaderboard.HitBloq.ToString())) != null;
            Plugin.ProfileInfo.IsAccSaberEnabled = Plugin.ProfileInfo.IsScoreSaberEnabled && Plugin.ProfileInfo.IsAccSaberEnabledManual;
        }

        public void RestartOverlayServer()
        {
            _websocketMgr.RestartOverlayServer();
        }

        private void PPPredictor_OnMapPoolRefreshed(object sender, EventArgs e)
        {
            OnMapPoolRefreshed?.Invoke(this, null);
        }

        #region event handler
        private void PPPredictor_OnDisplaySessionInfo(object sender, DisplaySessionInfo displaySessionInfo)
        {
            OnDisplaySessionInfo?.Invoke(this, displaySessionInfo);
        }

        private void PPPredictor_OnDisplayPPInfo(object sender, DisplayPPInfo displayPPInfo)
        {
            OnDisplayPPInfo?.Invoke(this, displayPPInfo);
        }

        private void PPPredictor_OnDisplayImproveInfo(object sender, DisplayImproveInfo displayImproveInfo)
        {
            OnDisplayImproveInfo?.Invoke(this, displayImproveInfo);
        }

        private void PPPredictor_OnDataLoading(object sender, bool isDataLoading)
        {
            _loadingCounter = Math.Max(_loadingCounter + (isDataLoading ? +1 : -1), 0);
            if ((isDataLoading && _loadingCounter == 1) || (!isDataLoading && !IsDataLoading()))
            {
                OnDataLoading?.Invoke(this, isDataLoading);
            }
        }

        public bool IsDataLoading()
        {
            return _loadingCounter > 0;
        }
        #endregion

        public void CyclePredictors(int offset, bool triggerMapPoolRefresh = true)
        {
            _lsPPPredictor.ForEach(item => item.SetActive(false));
            index = Math.Min(Math.Max((index + offset), 0), _lsPPPredictor.Count() - 1);
            _currentPPPredictor = _lsPPPredictor[index];
            //CurrentPPPredictor.SetActive(true);
            Plugin.ProfileInfo.LastLeaderBoardSelected = CurrentPPPredictor.LeaderBoardName;
            SetNavigationArrowInteractivity(triggerMapPoolRefresh);
        }

        private void SetNavigationArrowInteractivity(bool triggerMapPoolRefresh = true)
        {
            if (_lsPPPredictor.Count() == 1) isLeaderboardNavigationActive = false;
            else isLeaderboardNavigationActive = true;
            isLeftArrowActive = index > 0;
            isRightArrowActive = index < _lsPPPredictor.Count() - 1;
            isMapPoolDropDownActive = CurrentPPPredictor.MapPoolOptions.Count() > 1;
            CurrentPPPredictor.CalculatePP();
            if(triggerMapPoolRefresh) OnMapPoolRefreshed?.Invoke(this, null);
        }

        public void ChangeGameplayModifiers(GameplaySetupViewController gameplaySetupViewController)
        {
            foreach (var item in _lsPPPredictor)
            {
                item.ChangeGameplayModifiers(gameplaySetupViewController);
            }
        }

        public void DifficultyChanged(BeatmapLevel selectedBeatmapLevel, BeatmapKey beatmapKey)
        {
            foreach (var item in _lsPPPredictor)
            {
                item.DifficultyChanged(selectedBeatmapLevel, beatmapKey);
            }
        }

        public void UpdateCurrentAndCheckResetSession(bool v)
        {
            foreach (var item in _lsPPPredictor)
            {
                item.UpdateCurrentAndCheckResetSession(v);
            }
        }

        public void RefreshCurrentData(int v, bool refreshStars = false)
        {
            foreach (var item in _lsPPPredictor)
            {
                item.RefreshCurrentData(v, refreshStars);
            }
        }

        public void RefreshAfterReconnect(string leaderboardName)
        {
            foreach (var item in _lsPPPredictor)
                if (item.LeaderBoardName == leaderboardName || item.LeaderBoardName == Leaderboard.HitBloq.ToString()
                    || item.LeaderBoardName == Leaderboard.AccSaber.ToString())
                    item.RefreshAfterReconnect();
        }

        public void SetPercentage(float percentage)
        {
            _percentage = percentage;
            foreach (var item in _lsPPPredictor)
            {
                item.Percentage = percentage;
            }
        }

        public double GetPercentage()
        {
            foreach (var item in _lsPPPredictor)
            {
                return item.Percentage;
            }
            return 0;
        }

        public void ResetDisplay(bool resetAll)
        {
            foreach (var item in _lsPPPredictor)
            {
                item.ResetDisplay(resetAll);
            }
        }

        public double GetPPAtPercentageForCalculator(Leaderboard leaderBoardName, double percentage, bool levelFailed, bool levelPaused, PPPBeatMapInfo beatMapInfo)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.CalculatePPatPercentage(percentage, beatMapInfo, levelFailed, levelPaused); ;
            }
            return 0;
        }
        public string GetPPSuffixForLeaderboard(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.PPSuffix;
            }
            return string.Empty;
        }

        public double GetMaxPPForCalculator(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.CalculateMaxPP(); ;
            }
            return 0;
        }

        public PPPBeatMapInfo GetModifiedBeatMapInfo(Leaderboard leaderBoardName, GameplayModifiers gameplayModifiers, bool levelFailed = false, bool levelPaused = false)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.GetModifiedBeatMapInfo(gameplayModifiers, levelFailed, levelPaused);

            }
            return new PPPBeatMapInfo();
        }

        public bool IsRanked(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.IsRanked();
            }
            return false;
        }

        public double GetPPGainForCalculator(Leaderboard leaderBoardName, double pp)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.CalculatePPGain(pp); ;
            }
            return 0;
        }

        public double? GetPersonalBest(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.GetPersonalBest(); ;
            }
            return null;
        }

        public string GetLeaderboardIcon(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.LeaderBoardIcon;
            }
            return string.Empty;
        }
        public string GetMapPoolIcon(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.MapPoolIcon;
            }
            return string.Empty;
        }

        public async Task<byte[]> GetLeaderboardIconData(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                if(predictor.MapPoolIconData == null)
                {
                    await predictor.GetMapPoolIconData();
                }
                return predictor.MapPoolIconData;
            }
            return null;
        }

        public void ActivateView(bool activate)
        {
            ViewActivated?.Invoke(this, activate);
        }

        public List<object> GetMapPoolsFromLeaderboard(Leaderboard leaderBoardName)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderBoardName.ToString());
            if (predictor != null)
            {
                return predictor.MapPoolOptions;
            }
            return new List<object>();
        }

        public async Task UpdateCurrentBeatMapInfos(BeatmapLevel selectedBeatmapLevel, BeatmapKey beatmapKey)
        {
            await Task.WhenAll(_lsPPPredictor.Select(predictor => predictor.UpdateCurrentBeatMapInfos(selectedBeatmapLevel, beatmapKey)));
        }

        public void Initialize()
        {
            BS_Utils.Utilities.BSEvents.levelCleared += OnLevelCleared;
        }

        private void OnLevelCleared(StandardLevelScenesTransitionSetupDataSO transition, LevelCompletionResults results)
        {
            if (!Plugin.ProfileInfo.IsAccSaberEnabled || transition.practiceSettings != null || transition.beatmapLevel == null) return;
            string hash = SongCore.Collections.GetCustomLevelHash(transition.beatmapLevel.levelID);
            if (string.IsNullOrEmpty(hash)) return;
            var key = Converter.Converter.ConvertBeatmapKey(transition.beatmapKey);
            _websocketMgr.ScheduleAccSaberRefresh(new PPPScoreSetData
            {
                leaderboardName = Leaderboard.AccSaber.ToString(),
                hash = PPCalculator.CreateSeachString(hash, "SOLO" + key.serializedName, (int)key.difficulty),
                score = results.multipliedScore
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            fullSyncPause.Cancel();
            autosave.Dispose();
            lifetime.Cancel();
            BS_Utils.Utilities.BSEvents.levelCleared -= OnLevelCleared;
            DisposePredictors();
            if (CalculatorInstance != null)
            {
                CalculatorInstance.OnMessage -= Logging_OnMessage;
                _ = CalculatorInstance.ShutdownAsync();
            }
            _websocketMgr.Dispose();
        }

        public void FindPoolWithSyncURL(IPlaylist playlist)
        {
            if (playlist != null && Plugin.ProfileInfo.IsPredictorSwitchBySyncUrlEnabled)
            {
                if (playlist.TryGetCustomData(_customDataSyncUrl, out object outSyncURL) && !string.IsNullOrEmpty(outSyncURL as string))
                {
                    string syncUrl = outSyncURL.ToString();
                    foreach (IPPPredictor predictor in _lsPPPredictor)
                    {
                        if (syncUrl.Contains(_beatleaderSyncUrlIdentifier) && predictor.LeaderBoardName == Leaderboard.BeatLeader.ToString())
                        {
                            index = _lsPPPredictor.FindIndex(x => x.LeaderBoardName == predictor.LeaderBoardName);
                            CyclePredictors(0);
                            break;
                        }
                        PPPMapPoolShort mapPool = predictor.FindPoolWithSyncURL(outSyncURL as string);
                        if(mapPool != null)
                        {
                            predictor.CurrentMapPool = mapPool;
                            index = _lsPPPredictor.FindIndex(x => x.LeaderBoardName == predictor.LeaderBoardName);
                            CyclePredictors(0, false);
                            _ = RefreshPoolDisplay(500);
                            break;
                        }
                    }
                }
            }
        }

        public async Task RefreshPoolDisplay(int msDelay)
        {
            await Task.Delay(msDelay);
            OnMapPoolRefreshed?.Invoke(this, null);
        }

        public void ScoreSet(string leaderboardName, PPPScoreSetData data)
        {
            IPPPredictor predictor = _lsPPPredictor.Find(x => x.LeaderBoardName == leaderboardName);
            if (predictor != null)
            {
                predictor.ScoreSet(data);
            }
        }
    }
}

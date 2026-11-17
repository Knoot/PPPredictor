using System;
using Zenject;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberPlaylistsLib.Types;
using PPPredictor.Interfaces;
using System.Threading.Tasks;

namespace PPPredictor.Utilities
{
    class MainMenuMgr : IInitializable, IDisposable
    {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value null
        [Inject] private readonly LevelSelectionNavigationController levelSelectionNavigationController;
        [Inject] private readonly GameplaySetupViewController gameplaySetupViewController;
        [Inject] private readonly IPPPredictorMgr ppPredictorMgr;
        [Inject] private readonly AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController;
        [Inject] private readonly LobbyGameStateController lobbyGameStateController;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value null

        private readonly MenuButton menuButton;
        private StandardLevelDetailViewController levelDetails;
        private bool disposed;

        public MainMenuMgr()
        { 
            menuButton = new MenuButton("PPPredictor", "Predict PP gains", ProfileInfoMgr.ShowSettingsFlow, true);
        }

        public void Dispose()
        {
            disposed = true;
            ppPredictorMgr.SetMenuAvailable(false);
            MenuButtons.Instance.UnregisterButton(menuButton);
            if (levelDetails != null)
            {
                levelDetails.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
                levelDetails.didChangeContentEvent -= OnDetailContentChanged;
                levelDetails.didActivateEvent -= OnLevelDetailsActivated;
            }
            levelSelectionNavigationController.didActivateEvent -= OnLevelSelectionActivated;
            levelSelectionNavigationController.didDeactivateEvent -= OnLevelSelectionDeactivated;
            gameplaySetupViewController._gameplayModifiersPanelController.didChangeGameplayModifiersEvent -= DidChangeGameplayModifiersEvent;
            annotatedBeatmapLevelCollectionsViewController.didSelectAnnotatedBeatmapLevelCollectionEvent -= AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            ppPredictorMgr.OnDataLoading -= PpPredictorMgr_OnDataLoading;
        }

        public void Initialize()
        {
            ppPredictorMgr.SetMenuAvailable(true);
            MenuButtons.Instance.RegisterButton(menuButton);
            levelDetails = levelSelectionNavigationController._levelCollectionNavigationController._levelDetailViewController;
            levelDetails.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            levelDetails.didChangeContentEvent += OnDetailContentChanged;
            levelDetails.didActivateEvent += OnLevelDetailsActivated;
            levelSelectionNavigationController.didActivateEvent += OnLevelSelectionActivated;
            levelSelectionNavigationController.didDeactivateEvent += OnLevelSelectionDeactivated;
            gameplaySetupViewController._gameplayModifiersPanelController.didChangeGameplayModifiersEvent += DidChangeGameplayModifiersEvent;
            annotatedBeatmapLevelCollectionsViewController.didSelectAnnotatedBeatmapLevelCollectionEvent += AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            ppPredictorMgr.OnDataLoading += PpPredictorMgr_OnDataLoading;
        }

        private void PpPredictorMgr_OnDataLoading(object sender, bool isDataLoading)
        {
            RefreshButtonInteraction();
        }

        private void DidChangeGameplayModifiersEvent()
        {
            if (IsNormalMainMenu()) this.ppPredictorMgr.ChangeGameplayModifiers(this.gameplaySetupViewController);
        }

        private void OnDifficultyChanged(StandardLevelDetailViewController details)
        {
            RestoreSelection("difficulty changed");
        }

        private void OnDetailContentChanged(StandardLevelDetailViewController details, StandardLevelDetailViewController.ContentType contentType)
        {
            // The game sends Inactive (with a null sender) when hiding this view for gameplay/results.
            if (contentType == StandardLevelDetailViewController.ContentType.Inactive) return;
            if (disposed || !IsNormalMainMenu() || !levelDetails.isActiveAndEnabled) return;
            if (contentType == StandardLevelDetailViewController.ContentType.OwnedAndReady)
            {
                RestoreSelection("details ready");
            }
            else
            {
                Plugin.Log?.Debug($"Clearing predictor selection: detail content={contentType}");
                this.ppPredictorMgr.DifficultyChanged(null, default);
            }
        }

        private void OnLevelDetailsActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            RestoreSelection("details activated");
        }

        private void RestoreSelection(string reason)
        {
            if (disposed || !IsNormalMainMenu() || levelDetails == null || !levelDetails.isActiveAndEnabled
                || !levelDetails._contentIsOwnedAndReady) return;
            try
            {
                var level = levelDetails.beatmapLevel;
                var key = levelDetails.beatmapKey;
                if (level == null || ReferenceEquals(key.beatmapCharacteristic, null)) return;
                Plugin.Log?.Debug($"Restoring predictor selection: source={reason}; level={level.levelID}; difficulty={key.difficulty}");
                ppPredictorMgr.SetMenuAvailable(true);
                ppPredictorMgr.DifficultyChanged(level, key);
                ppPredictorMgr.ChangeGameplayModifiers(gameplaySetupViewController);
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"Failed to restore predictor selection after menu activation: {ex.Message}");
            }
        }

        private void OnLevelSelectionActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (disposed) return;
            bool normalMenu = IsNormalMainMenu();
            ppPredictorMgr.SetMenuAvailable(normalMenu);
            ppPredictorMgr.ActivateView(normalMenu);
            // Child activation/OwnedAndReady will retry if its content is not ready yet.
            if (normalMenu) RestoreSelection("menu activated");
        }
        private void OnLevelSelectionDeactivated(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            RefreshButtonInteraction(true);
            this.ppPredictorMgr.ActivateView(false);
        }

        private void AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent(BeatmapLevelPack lvlPack)
        {
            this.ppPredictorMgr.FindPoolWithSyncURL((lvlPack as PlaylistLevelPack)?.playlist);
        }

        private void RefreshButtonInteraction(bool forceRefresh = false)
        {
            //Deactivate Settings while data is loading
            bool isInteractable = !this.ppPredictorMgr.IsDataLoading();
            if (menuButton.Interactable != isInteractable || forceRefresh)
            {
                menuButton.Interactable = isInteractable;
                _ = RefreshDelay();
            }
        }

        private async Task RefreshDelay()
        {
            await Task.Delay(250);
            MenuButtons.Instance.Refresh();
        }

        /// <summary>
        /// Check if we are NOT in a lobby
        /// </summary>
        /// <returns></returns>
        private bool IsNormalMainMenu()
        {
            return lobbyGameStateController.state == MultiplayerLobbyState.None;
        }
    }
}

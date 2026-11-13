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
        private bool levelSelectionActive;
        private bool disposed;
        private int selectionActivationVersion;

        public MainMenuMgr()
        { 
            menuButton = new MenuButton("PPPredictor", "Predict PP gains", ProfileInfoMgr.ShowSettingsFlow, true);
        }

        public void Dispose()
        {
            disposed = true;
            levelSelectionActive = false;
            ++selectionActivationVersion;
            ppPredictorMgr.SetMenuAvailable(false);
            MenuButtons.Instance.UnregisterButton(menuButton);
            levelSelectionNavigationController.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
            levelSelectionNavigationController.didChangeLevelDetailContentEvent -= OnDetailContentChanged;
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
            levelSelectionNavigationController.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            levelSelectionNavigationController.didChangeLevelDetailContentEvent += OnDetailContentChanged;
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

        private void OnDifficultyChanged(LevelSelectionNavigationController lvlSelectionNavigationCtrl)
        {
            if (!disposed && levelSelectionActive && IsNormalMainMenu()) DiffultyChangedDecideCustomMap(lvlSelectionNavigationCtrl);
        }

        private void OnDetailContentChanged(LevelSelectionNavigationController lvlSelectionNavigationCtrl, StandardLevelDetailViewController.ContentType contentType)
        {
            if (disposed || !levelSelectionActive || !IsNormalMainMenu()) return;
            if (contentType == StandardLevelDetailViewController.ContentType.OwnedAndReady)
            {
                DiffultyChangedDecideCustomMap(lvlSelectionNavigationCtrl);
            }
            else
            {
                this.ppPredictorMgr.DifficultyChanged(null, default);
            }
        }

        private void DiffultyChangedDecideCustomMap(LevelSelectionNavigationController lvlSelectionNavigationCtrl)
        {
            this.ppPredictorMgr.DifficultyChanged(lvlSelectionNavigationCtrl.beatmapLevel, lvlSelectionNavigationCtrl.beatmapKey);

        }

        private async void OnLevelSelectionActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (disposed) return;
            levelSelectionActive = true;
            int version = ++selectionActivationVersion;
            bool normalMenu = IsNormalMainMenu();
            ppPredictorMgr.SetMenuAvailable(normalMenu);
            ppPredictorMgr.ActivateView(normalMenu);
            if (!normalMenu) return;

            // Child controllers restore their selection after the parent's activation event.
            await Task.Yield();
            if (disposed || !levelSelectionActive || version != selectionActivationVersion || !IsNormalMainMenu()) return;
            try
            {
                var details = levelSelectionNavigationController._levelCollectionNavigationController?._levelDetailViewController;
                if (details != null && details.isActiveAndEnabled && details._contentIsOwnedAndReady)
                {
                    DiffultyChangedDecideCustomMap(levelSelectionNavigationController);
                    ppPredictorMgr.ChangeGameplayModifiers(gameplaySetupViewController);
                }
                else
                {
                    ppPredictorMgr.DifficultyChanged(null, default);
                }
            }
            catch (Exception ex)
            {
                Plugin.ErrorPrint($"Failed to restore predictor selection after menu activation: {ex.Message}");
            }
        }
        private void OnLevelSelectionDeactivated(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            levelSelectionActive = false;
            ++selectionActivationVersion;
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

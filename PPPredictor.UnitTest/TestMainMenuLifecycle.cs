#nullable enable
using System.Reflection;
using PPPredictor.Interfaces;
using PPPredictor.Utilities;

namespace UnitTests.Application
{
    [TestClass]
    public class TestMainMenuLifecycle
    {
        private sealed class Predictor : IPPPredictorMgr
        {
            public event EventHandler<bool>? OnDataLoading;
            internal BeatmapLevel? Selected;
            internal MenuTestBeatmapKey Key;
            internal int Clears, Changes;
            internal bool MenuAvailable;
            public void SetMenuAvailable(bool available) => MenuAvailable = available;
            public void ActivateView(bool active) { }
            public bool IsDataLoading() => false;
            public void DifficultyChanged(BeatmapLevel? level, MenuTestBeatmapKey key)
            {
                Selected = level; Key = key; Changes++;
                if (level == null) Clears++;
            }
            public void ChangeGameplayModifiers(GameplaySetupViewController controller) { }
            public void FindPoolWithSyncURL(object? playlist) { }
            internal void RaiseLoading() => OnDataLoading?.Invoke(this, false);
        }

        private sealed class Context : IDisposable
        {
            internal readonly MainMenuMgr Menu = new();
            internal readonly LevelSelectionNavigationController Navigation = new();
            internal readonly Predictor Predictor = new();
            internal readonly LobbyGameStateController Lobby = new();
            internal StandardLevelDetailViewController Details => Navigation._levelCollectionNavigationController._levelDetailViewController;

            internal Context()
            {
                Inject("levelSelectionNavigationController", Navigation);
                Inject("gameplaySetupViewController", new GameplaySetupViewController());
                Inject("ppPredictorMgr", Predictor);
                Inject("annotatedBeatmapLevelCollectionsViewController", new AnnotatedBeatmapLevelCollectionsViewController());
                Inject("lobbyGameStateController", Lobby);
                Menu.Initialize();
            }
            private void Inject(string field, object value) => typeof(MainMenuMgr)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Menu, value);
            internal void Ready()
            {
                Details.beatmapLevel = new BeatmapLevel();
                Details.beatmapKey = new MenuTestBeatmapKey(new object(), 9);
                Details._contentIsOwnedAndReady = true;
            }
            public void Dispose() => Menu.Dispose();
        }

        [TestMethod]
        public void InactiveBeforeAndAfterReturnDoesNotClearSelection()
        {
            using var c = new Context();
            c.Ready();
            c.Details.ContentChanged(StandardLevelDetailViewController.ContentType.OwnedAndReady);
            c.Details.isActiveAndEnabled = false;
            c.Details.ContentChanged(StandardLevelDetailViewController.ContentType.Inactive);
            c.Navigation.Deactivate();
            c.Navigation.Activate();
            c.Details.isActiveAndEnabled = true;
            c.Details.Activate();
            c.Details.ContentChanged(StandardLevelDetailViewController.ContentType.Inactive);
            Assert.AreSame(c.Details.beatmapLevel, c.Predictor.Selected);
            Assert.AreEqual(0, c.Predictor.Clears);
        }

        [TestMethod]
        public void ChildActivationRestoresWithoutParentOrDifficultyEvent()
        {
            using var c = new Context();
            c.Ready();
            c.Navigation.Deactivate();
            c.Predictor.MenuAvailable = false;
            c.Details.Activate();
            Assert.AreSame(c.Details.beatmapLevel, c.Predictor.Selected);
            Assert.AreEqual(c.Details.beatmapKey, c.Predictor.Key);
            Assert.IsTrue(c.Predictor.MenuAvailable);
        }

        [TestMethod]
        public void DelayedReadyEventRestoresTheActualDifficulty()
        {
            using var c = new Context();
            c.Navigation.Activate();
            c.Details.Activate();
            Assert.AreEqual(0, c.Predictor.Changes);
            c.Ready();
            c.Details.ContentChanged(StandardLevelDetailViewController.ContentType.OwnedAndReady);
            Assert.AreSame(c.Details.beatmapLevel, c.Predictor.Selected);
            c.Details.beatmapKey = new MenuTestBeatmapKey(new object(), 7);
            c.Details.DifficultyChanged();
            Assert.AreEqual(7, c.Predictor.Key.difficulty);
            Assert.AreEqual(0, c.Predictor.Clears);
        }

        [DataTestMethod]
        [DataRow((int)StandardLevelDetailViewController.ContentType.Loading)]
        [DataRow((int)StandardLevelDetailViewController.ContentType.OwnedAndDownloading)]
        [DataRow((int)StandardLevelDetailViewController.ContentType.NoAllowedDifficultyBeatmap)]
        [DataRow((int)StandardLevelDetailViewController.ContentType.Buy)]
        [DataRow((int)StandardLevelDetailViewController.ContentType.Error)]
        public void ExplicitUnavailableContentStillClearsSelection(int content)
        {
            using var c = new Context();
            c.Ready();
            c.Details.Activate();
            c.Details._contentIsOwnedAndReady = false;
            c.Details.ContentChanged((StandardLevelDetailViewController.ContentType)content);
            Assert.IsNull(c.Predictor.Selected);
            Assert.AreEqual(1, c.Predictor.Clears);
        }

        [TestMethod]
        public void DisposeRemovesAllDetailSubscriptions()
        {
            var c = new Context();
            c.Ready();
            c.Dispose();
            c.Details.Activate();
            c.Details.DifficultyChanged();
            c.Details.ContentChanged(StandardLevelDetailViewController.ContentType.OwnedAndReady);
            c.Navigation.Activate();
            Assert.AreEqual(0, c.Predictor.Changes);
        }

        [TestMethod]
        public void MultiplayerAndHiddenDetailsDoNotRestoreSoloSelection()
        {
            using var c = new Context();
            c.Ready();
            c.Lobby.state = MultiplayerLobbyState.Lobby;
            c.Details.Activate();
            Assert.AreEqual(0, c.Predictor.Changes);
            c.Lobby.state = MultiplayerLobbyState.None;
            c.Details.isActiveAndEnabled = false;
            c.Navigation.Activate();
            Assert.AreEqual(0, c.Predictor.Changes);
        }
    }
}

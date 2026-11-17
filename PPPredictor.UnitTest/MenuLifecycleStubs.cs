#nullable enable
// Only the game/UI boundary is stubbed; lifecycle tests compile the production MainMenuMgr.
namespace Zenject
{
    internal sealed class InjectAttribute : Attribute { }
    internal interface IInitializable { void Initialize(); }
}

internal sealed class BeatmapLevel { public string levelID = "MAP"; }
internal readonly record struct MenuTestBeatmapKey(object? beatmapCharacteristic, int difficulty);
internal class BeatmapLevelPack { }
internal sealed class GameplaySetupViewController
{
    public event Action? didChangeGameplayModifiersEvent;
    internal GameplaySetupViewController _gameplayModifiersPanelController => this;
    internal void RaiseModifiers() => didChangeGameplayModifiersEvent?.Invoke();
}
internal sealed class LobbyGameStateController { internal MultiplayerLobbyState state; }
internal enum MultiplayerLobbyState { None, Lobby }
internal sealed class AnnotatedBeatmapLevelCollectionsViewController
{
    public event Action<BeatmapLevelPack>? didSelectAnnotatedBeatmapLevelCollectionEvent;
    internal void RaisePack(BeatmapLevelPack pack) => didSelectAnnotatedBeatmapLevelCollectionEvent?.Invoke(pack);
}
internal sealed class LevelCollectionNavigationController
{
    internal StandardLevelDetailViewController _levelDetailViewController = new();
}
internal sealed class LevelSelectionNavigationController
{
    internal LevelCollectionNavigationController _levelCollectionNavigationController = new();
    public event Action<bool, bool, bool>? didActivateEvent;
    public event Action<bool, bool>? didDeactivateEvent;
    internal void Activate() => didActivateEvent?.Invoke(false, false, false);
    internal void Deactivate() => didDeactivateEvent?.Invoke(false, false);
}
internal sealed class StandardLevelDetailViewController
{
    public enum ContentType { Loading, OwnedAndReady, NoAllowedDifficultyBeatmap, OwnedAndDownloading, Buy, BuyInfo, Error, Inactive }
    internal bool isActiveAndEnabled = true;
    internal bool _contentIsOwnedAndReady;
    internal BeatmapLevel? beatmapLevel;
    internal MenuTestBeatmapKey beatmapKey;
    public event Action<StandardLevelDetailViewController>? didChangeDifficultyBeatmapEvent;
    public event Action<StandardLevelDetailViewController?, ContentType>? didChangeContentEvent;
    public event Action<bool, bool, bool>? didActivateEvent;
    internal void Activate() => didActivateEvent?.Invoke(false, false, false);
    internal void DifficultyChanged() => didChangeDifficultyBeatmapEvent?.Invoke(this);
    internal void ContentChanged(ContentType content) => didChangeContentEvent?.Invoke(content == ContentType.Inactive ? null : this, content);
}

namespace BeatSaberPlaylistsLib.Types
{
    internal sealed class PlaylistLevelPack : BeatmapLevelPack { internal object? playlist = null; }
}

namespace BeatSaberMarkupLanguage.MenuButtons
{
    internal sealed class MenuButton
    {
        internal bool Interactable;
        internal MenuButton(string name, string hint, Action action, bool interactable) { Interactable = interactable; }
    }
    internal sealed class MenuButtons
    {
        internal static readonly MenuButtons Instance = new();
        internal void RegisterButton(MenuButton button) { }
        internal void UnregisterButton(MenuButton button) { }
        internal void Refresh() { }
    }
}

namespace PPPredictor
{
    internal static class Plugin
    {
        internal sealed class TestLogger { internal void Debug(string message) { } }
        internal static readonly TestLogger Log = new();
        internal static void ErrorPrint(string message) => throw new InvalidOperationException(message);
    }
}

namespace PPPredictor.Utilities
{
    internal static class ProfileInfoMgr { internal static void ShowSettingsFlow() { } }
}

namespace PPPredictor.Interfaces
{
    internal interface IPPPredictorMgr
    {
        event EventHandler<bool> OnDataLoading;
        void SetMenuAvailable(bool available);
        void ActivateView(bool active);
        bool IsDataLoading();
        void DifficultyChanged(BeatmapLevel? level, MenuTestBeatmapKey key);
        void ChangeGameplayModifiers(GameplaySetupViewController controller);
        void FindPoolWithSyncURL(object? playlist);
    }
}

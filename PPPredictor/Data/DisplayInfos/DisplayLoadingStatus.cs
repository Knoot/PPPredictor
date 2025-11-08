using BeatSaberMarkupLanguage.Attributes;

namespace PPPredictor.Data.DisplayInfos
{
    internal class DisplayLoadingStatus
    {
        private string _loadingStateIcon;
        private string _loadingStateText;

        public DisplayLoadingStatus(string loadingStateIcon, string loadingStateText)
        {
            _loadingStateIcon = loadingStateIcon;
            _loadingStateText = loadingStateText;
        }

        [UIValue("loadingStateIcon")]
        private string LoadingStateIcon
        {
            get => _loadingStateIcon;
        }
        [UIValue("loadingStateText")]
        private string LoadingStateText
        {
            get => _loadingStateText;
        }
    }
}

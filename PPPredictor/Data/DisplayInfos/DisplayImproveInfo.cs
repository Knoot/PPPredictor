namespace PPPredictor.Data.DisplayInfos
{
    internal class DisplayImproveInfo
    {
        internal string LeaderboardName { get; }
        internal string Text { get; }
        internal bool IsLoading { get; }

        internal DisplayImproveInfo(string leaderboardName, string text, bool isLoading)
        {
            LeaderboardName = leaderboardName;
            Text = text;
            IsLoading = isLoading;
        }
    }
}

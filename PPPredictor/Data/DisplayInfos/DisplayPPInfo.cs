using BeatSaberMarkupLanguage.Attributes;
using PPPredictor.Utilities;
using UnityEngine;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Data.DisplayInfos
{
    public class DisplayPPInfo
    {
        public CalculationMode calculationMode { get; set; } = CalculationMode.PercentToPP;
        public string PPRaw { get; set; } = string.Empty;
        public string PPGain { get; set; } = string.Empty;
        public string PPGainDiffColor { get; set; } = DisplayHelper.ColorWhite;
        public string PredictedRank { get; set; } = string.Empty;
        public string PredictedRankDiff { get; set; } = string.Empty;
        public string PredictedRankDiffColor { get; set; } = DisplayHelper.ColorWhite;
        public string PredictedCountryRank { get; set; } = string.Empty;
        public string PredictedCountryRankDiff { get; set; } = string.Empty;
        public string PredictedCountryRankDiffColor { get; set; } = DisplayHelper.ColorWhite;
        public string TargetPPPercentage { get; set; } = "string.Empty% :)";

        public DisplayPPInfo(CalculationMode calculationMode)
        {
            this.calculationMode = calculationMode;
        }
        public DisplayPPInfo(DisplayPPInfo displayPPInfo)
        {
            calculationMode = displayPPInfo.calculationMode;
            TargetPPPercentage = displayPPInfo.TargetPPPercentage;
        }
    }
}

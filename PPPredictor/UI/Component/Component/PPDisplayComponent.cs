using PPPredictor.Data.DisplayInfos;
using TMPro;
using UnityEngine;

namespace PPPredictor.UI.Component.Component
{
    public class PPDisplayComponent : MonoBehaviour
    {
        private bool firstUpdate = true;
        public TextMeshPro TargetPPPercentage { get; protected set; }
        public TextMeshPro PredictedRank { get; protected set; }
        public TextMeshPro PredictedCountryRank { get; protected set; }
        public TextMeshPro PPGain { get; protected set; }
        public TextMeshPro HeaderPredictedRank { get; protected set; }
        public TextMeshPro HeaderPredictedCountryRank { get; protected set; }
        public TextMeshPro HeaderPPGain { get; protected set; }

        public void Init(TextMeshPro TargetPPPercentage,
            TextMeshPro PredictedRank,
            TextMeshPro PredictedCountryRank,
            TextMeshPro PPGain,
            TextMeshPro HeaderPredictedRank,
            TextMeshPro HeaderPredictedCountryRank,
            TextMeshPro HeaderPPGain)
        {
            this.TargetPPPercentage = TargetPPPercentage;
            this.PredictedRank = PredictedRank;
            this.PredictedCountryRank = PredictedCountryRank;
            this.PPGain = PPGain;
            this.HeaderPredictedRank = HeaderPredictedRank;
            this.HeaderPredictedCountryRank = HeaderPredictedCountryRank;
            this.HeaderPPGain = HeaderPPGain;
        }

        public void UpdateTexts(DisplayPPInfo displayPPInfo)
        {
            if (firstUpdate)
            {
                Plugin.DebugPrint($"UpdateTexts firstcall {displayPPInfo.calculationMode}");
                firstUpdate = false;
                if(displayPPInfo.calculationMode == Core.DataType.Enums.CalculationMode.PercentToPP)
                {
                    this.TargetPPPercentage.enabled = false;
                }
                else
                {
                    float offset = 0.075f;
                    this.PredictedCountryRank.transform.Translate(new Vector3(0,offset,0));
                    this.HeaderPredictedCountryRank.transform.Translate(new Vector3(0, offset, 0));

                    this.PPGain.transform.Translate(new Vector3(0, 2 * offset, 0));
                    this.HeaderPPGain.transform.Translate(new Vector3(0, 2 * offset, 0));

                    this.TargetPPPercentage.transform.Translate(new Vector3(0, 3 * offset, 0));
                    //this.PredictedRank.transform.Translate(new Vector3(0, 0, 0));
                }
            }
            this.PredictedRank.text = $"{displayPPInfo.PredictedRank} [<color=\"{displayPPInfo.PredictedRankDiffColor}\">{displayPPInfo.PredictedRankDiff}</color>]";
            this.PredictedCountryRank.text = $"{displayPPInfo.PredictedCountryRank} [<color=\"{displayPPInfo.PredictedCountryRankDiffColor}\">{displayPPInfo.PredictedCountryRankDiff}</color>]";
            this.PPGain.text = $"{displayPPInfo.PPRaw} [<color=\"{displayPPInfo.PPGainDiffColor}\">{displayPPInfo.PPGain}</color>]";
            this.TargetPPPercentage.text = displayPPInfo.TargetPPPercentage;

            this.HeaderPredictedRank.text = "Global";
            //this.HeaderPredictedCountryRank.text = $"<color=\"{displayPPInfo.countryRankFontColor}\">>Country";
            this.HeaderPredictedCountryRank.text = "Country";
            this.HeaderPPGain.text = "PP";
        }
    }
}

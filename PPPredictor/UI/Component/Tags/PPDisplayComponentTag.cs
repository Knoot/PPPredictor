using BeatSaberMarkupLanguage.Tags;
using PPPredictor.UI.Component.Component;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PPPredictor.UI.Component.Tags
{
    public class PPDisplayComponentTag : BSMLTag
    {
        public override string[] Aliases => new[] { "pp-display" };

        public override GameObject CreateObject(Transform parent)
        {
            Plugin.DebugPrint("PPDisplayComponentTag CreateObject");
            GameObject container = new GameObject("PPDisplayComponent");

            container.AddComponent<LayoutElement>();
            container.transform.SetParent(parent, false);

            TextMeshPro TargetPPPercentage = GenerateTextMesh(container, TextAlignmentOptions.Center);
            TextMeshPro PredictedRank = GenerateTextMesh(container, TextAlignmentOptions.Center);
            TextMeshPro PredictedCountryRank = GenerateTextMesh(container, TextAlignmentOptions.Center);
            TextMeshPro PPGain = GenerateTextMesh(container, TextAlignmentOptions.Center);
            TextMeshPro HeaderPredictedRank = GenerateTextMesh(container);
            TextMeshPro HeaderPredictedCountryRank = GenerateTextMesh(container);
            TextMeshPro HeaderPPGain = GenerateTextMesh(container);

            float col1 = .2f;
            float col2 = .1f;

            float row1 = .1f;
            float row2 = -.1f;
            float row3 = -.3f;
            float row4 = -.5f;

            PredictedRank.transform.Translate(new Vector3(col2, row1, 0f));
            PredictedCountryRank.transform.Translate(new Vector3(col2, row2, 0f));
            PPGain.transform.Translate(new Vector3(col2, row3, 0f));

            HeaderPredictedRank.transform.Translate(new Vector3(col1, row1, 0f));
            HeaderPredictedCountryRank.transform.Translate(new Vector3(col1, row2, 0f));
            HeaderPPGain.transform.Translate(new Vector3(col1, row3, 0f));

            TargetPPPercentage.transform.Translate(new Vector3(col2, row4, 0)); //Moved away for now

            container.AddComponent<PPDisplayComponent>().Init(TargetPPPercentage,
                PredictedRank,
                PredictedCountryRank,
                PPGain,
                HeaderPredictedRank,
                HeaderPredictedCountryRank,
                HeaderPPGain);


            return container;
        }

        public TextMeshPro GenerateTextMesh(GameObject gameObject, TextAlignmentOptions textAlignment = TextAlignmentOptions.Left)
        {
            GameObject gameObject2 = new GameObject("PPDisplayComponentTextHolder");
            gameObject2.transform.SetParent(gameObject.transform, false);
            var text = gameObject2.AddComponent<TextMeshPro>();
            text.alignment = textAlignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            RectTransform rect = text.rectTransform;
            rect.sizeDelta = new Vector2(999f, rect.sizeDelta.y);
            if(textAlignment == TextAlignmentOptions.Left)
            {
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }
            return text;
        }
    }
}

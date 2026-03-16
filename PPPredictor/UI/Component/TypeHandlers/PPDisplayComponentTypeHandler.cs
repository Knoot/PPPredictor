using BeatSaberMarkupLanguage.TypeHandlers;
using PPPredictor.UI.Component.Component;
using System;
using System.Collections.Generic;

namespace PPPredictor.UI.Component.TypeHandlers
{
    [ComponentHandler(typeof(PPDisplayComponent))]
    public class PPDisplayComponentTypeHandler : TypeHandler<PPDisplayComponent>
    {
        public override Dictionary<string, string[]> Props => new Dictionary<string, string[]>()
        {
            { "data", new[] { "data" } },
        };

        public override Dictionary<string, Action<PPDisplayComponent, string>> Setters => new Dictionary<string, Action<PPDisplayComponent, string>>()
        {
        };
    }
}

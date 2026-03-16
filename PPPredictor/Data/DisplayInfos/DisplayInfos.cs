using PPPredictor.Core.DataType;
using System;
using System.Timers;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Data.DisplayInfos
{
    internal class DisplayInfos
    {
        public bool RankGainRunning { get; set; } = false;
        public double LastPPGainCall { get; set; } = 0;
        public DisplayPPInfo DisplayPPInfo { get; set; }
        public PPGainResult PPGainResult { get; set; }
        public Timer RankTimer { get; set; }
        public Timer PPCaclulationTimer { get; set; }
        public bool IsValid { get; set; } = true;

        public DisplayInfos(CalculationMode calculationMode, ElapsedEventHandler onRankTimerElapsed, ElapsedEventHandler onPPCalculationTimerElapsed)
        {
            DisplayPPInfo = new DisplayPPInfo(calculationMode);
            PPGainResult = new PPGainResult();
            var _rankTimer = new System.Timers.Timer(500);
            _rankTimer.Elapsed += onRankTimerElapsed;
            _rankTimer.AutoReset = false;
            _rankTimer.Enabled = false;
            RankTimer = _rankTimer;

            var _ppCalculationRankTimer = new System.Timers.Timer(250);
            _ppCalculationRankTimer.Elapsed += onPPCalculationTimerElapsed;
            _ppCalculationRankTimer.AutoReset = false;
            _ppCalculationRankTimer.Enabled = false;
            PPCaclulationTimer = _ppCalculationRankTimer;
        }
    }
}

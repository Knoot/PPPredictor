using PPPredictor.Core.DataType;
using PPPredictor.Data;
using System;
using System.Threading.Tasks;

namespace PPPredictor.Interfaces
{
    internal interface IPPPWebSocket
    {
        event EventHandler<PPPScoreSetData> OnScoreSet;
        event EventHandler OnReconnected;
        string LeaderboardName { get; }
        void Start();
        Task StopWebSocket();
    }
}

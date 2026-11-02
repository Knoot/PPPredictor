using PPPredictor.Core.DataType;
using PPPredictor.Data;
using PPPredictor.Interfaces;
using System;
using PPPredictor.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Zenject;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.WebSocket
{
    internal class WebSocketMgr : IInitializable, IDisposable
    {
        private readonly IPPPredictorMgr _ppPredictorMgr;
        private List<IPPPWebSocket> _lsWebSockets = new List<IPPPWebSocket>();
        private readonly DelayedScoreRefreshQueue delayedRefresh;
        private readonly SynchronizationContext uiContext = SynchronizationContext.Current;
        private bool disposed;

        internal WebSocketOverlayServer OverlayServer;

        public WebSocketMgr(IPPPredictorMgr ppPredictorMgr)
        {
            this._ppPredictorMgr = ppPredictorMgr;
            delayedRefresh = new DelayedScoreRefreshQueue((leaderboard, data) =>
                _ppPredictorMgr.ScoreSet(leaderboard.ToString(), data), TimeSpan.FromSeconds(5));
            RestartOverlayServer();
        }

        public void CreateScoreWebSockets()
        {
            if (disposed || _lsWebSockets.Count != 0) return;
            if (Plugin.ProfileInfo.IsScoreSaberEnabled)
            {
                PPPWebSocket<PPPWsScoreSaberCommand> socket = new PPPWebSocket<PPPWsScoreSaberCommand>("wss://scoresaber.com/ws", Leaderboard.ScoreSaber.ToString());
                StartScoreSocket(socket);
            }
            if (Plugin.ProfileInfo.IsBeatLeaderEnabled)
            {
                PPPWebSocket<PPPWsBeatLeaderData> socket = new PPPWebSocket<PPPWsBeatLeaderData>("wss://sockets.api.beatleader.com/scores", Leaderboard.BeatLeader.ToString());
                StartScoreSocket(socket);
            }
        }

        private void StartScoreSocket(IPPPWebSocket socket)
        {
            socket.OnScoreSet += PPPWebsocket_OnScoreSet;
            socket.OnReconnected += Socket_OnReconnected;
            _lsWebSockets.Add(socket);
            socket.Start();
        }

        private void DispatchForSocket(object sender, Action action)
        {
            void Apply()
            {
                if (!disposed && sender is IPPPWebSocket socket && _lsWebSockets.Contains(socket)) action();
            }
            if (uiContext != null && SynchronizationContext.Current != uiContext) uiContext.Post(_ => Apply(), null);
            else Apply();
        }

        private void Socket_OnReconnected(object sender, EventArgs args)
        {
            DispatchForSocket(sender, () => _ppPredictorMgr.RefreshAfterReconnect(((IPPPWebSocket)sender).LeaderboardName));
        }

        private void PPPWebsocket_OnScoreSet(object sender, PPPScoreSetData data)
        {
            DispatchForSocket(sender, () => HandleScore(data));
        }

        private void HandleScore(PPPScoreSetData data)
        {
            _ppPredictorMgr.ScoreSet(data.leaderboardName, data);
            if (Plugin.ProfileInfo.IsHitBloqEnabled)
            {
                AddDelayedRefresh(Leaderboard.HitBloq ,data);
            }
            ScheduleAccSaberRefresh(data);
        }

        internal void ScheduleAccSaberRefresh(PPPScoreSetData data)
        {
            if (Plugin.ProfileInfo.IsAccSaberEnabled) AddDelayedRefresh(Leaderboard.AccSaber, data);
        }

        private async void AddDelayedRefresh(Leaderboard leaderboard, PPPScoreSetData data)
        {
            try { await delayedRefresh.Schedule(leaderboard, data); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Plugin.ErrorPrint($"{leaderboard} delayed refresh failed: {ex.Message}"); }
        }

        internal void RestartOverlayServer()
        {
            OverlayServer?.CloseSocket();
            OverlayServer = new WebSocketOverlayServer();
            OverlayServer.StartSocket();
        }

        #region init dispose
        public void Dispose()
        {
            disposed = true;
            delayedRefresh.Dispose();
            OverlayServer.CloseSocket();
            _ = CloseScoreWebSockets();
        }

        public Task CloseScoreWebSockets()
        {
            delayedRefresh.CancelPending();
            var stopping = new List<Task>();
            foreach (var socket in _lsWebSockets)
            {
                socket.OnScoreSet -= PPPWebsocket_OnScoreSet;
                socket.OnReconnected -= Socket_OnReconnected;
                stopping.Add(socket.StopWebSocket());
            }
            _lsWebSockets.Clear();
            return Task.WhenAll(stopping);
        }

        public void Initialize()
        {
            //throw new NotImplementedException();
        }
        #endregion
    }
}

using Newtonsoft.Json;
using PPPredictor.Core;
using PPPredictor.Core.DataType;
using PPPredictor.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace PPPredictor.WebSocket
{
    internal class PPPWebSocket<T> : IPPPWebSocket where T : IPPPRawWebsocketData
    {
        private readonly string leaderboardName;
        private readonly string url;
        private readonly ReconnectLoop reconnect;
        private Task<UserInfo> userInfo;
        private int started, stopped;
        public event EventHandler<PPPScoreSetData> OnScoreSet;
        public event EventHandler OnReconnected;
        public string LeaderboardName => leaderboardName;

        public PPPWebSocket(string url, string leaderboardName)
        {
            this.leaderboardName = leaderboardName;
            this.url = url;
            reconnect = new ReconnectLoop(RunConnection,
                ex => Plugin.ErrorPrint($"WebSocket {leaderboardName}: {ex.Message}; reconnecting."));
            reconnect.Connected += isReconnect =>
            {
                if (isReconnect && Volatile.Read(ref stopped) == 0) OnReconnected?.Invoke(this, EventArgs.Empty);
            };
        }

        public void Start()
        {
            if (Volatile.Read(ref stopped) != 0 || Interlocked.Exchange(ref started, 1) != 0) return;
            userInfo = Plugin.GetUserInfoBS();
            reconnect.Start();
        }

        private async Task RunConnection(CancellationToken token, Action connected)
        {
            if (userInfo == null || userInfo.IsFaulted || userInfo.IsCanceled) userInfo = Plugin.GetUserInfoBS();
            var canceled = new TaskCompletionSource<UserInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            UserInfo user;
            using (token.Register(() => canceled.TrySetCanceled()))
                user = await await Task.WhenAny(userInfo, canceled.Task).ConfigureAwait(false);
            string userId = user.platformUserId;
            token.ThrowIfCancellationRequested();
            using (var socket = new WebSocketSharp.WebSocket(url))
            {
                socket.WaitTime = TimeSpan.FromSeconds(5);
                socket.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler opened = (sender, args) => connected();
                EventHandler<CloseEventArgs> closed = (sender, args) => disconnected.TrySetResult(true);
                EventHandler<ErrorEventArgs> failed = (sender, args) => disconnected.TrySetException(
                    args.Exception ?? new InvalidOperationException(args.Message));
                EventHandler<MessageEventArgs> message = (sender, args) =>
                {
                    if (token.IsCancellationRequested || disconnected.Task.IsCompleted || Volatile.Read(ref stopped) != 0 || !args.IsText
                        || args.Data == "Connected to the ScoreSaber WSS") return;
                    try
                    {
                        var raw = JsonConvert.DeserializeObject<T>(args.Data);
                        var data = raw.ConvertToPPPWebSocketData(leaderboardName);
                        if (data != null && data.userId == userId && !token.IsCancellationRequested && !disconnected.Task.IsCompleted)
                            OnScoreSet?.Invoke(this, data);
                    }
                    catch (Exception ex) { Plugin.ErrorPrint($"WebSocket {leaderboardName} message: {ex.Message}"); }
                };
                socket.OnOpen += opened;
                socket.OnClose += closed;
                socket.OnError += failed;
                socket.OnMessage += message;
                try
                {
                    using (token.Register(() => disconnected.TrySetCanceled()))
                    {
                        token.ThrowIfCancellationRequested();
                        socket.ConnectAsync();
                        await disconnected.Task.ConfigureAwait(false);
                    }
                }
                finally
                {
                    socket.OnOpen -= opened;
                    socket.OnClose -= closed;
                    socket.OnError -= failed;
                    socket.OnMessage -= message;
                    // Close BeatLeader as well; detached callbacks cannot schedule another attempt.
                    socket.Close();
                }
            }
        }

        public Task StopWebSocket()
        {
            if (Interlocked.Exchange(ref stopped, 1) == 0) reconnect.Dispose();
            return reconnect.Completion;
        }
    }
}

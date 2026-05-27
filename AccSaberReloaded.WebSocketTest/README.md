# ScoreSaber WebSocket Test

Standalone console app for watching the ScoreSaber score websocket without starting Beat Saber or the mod.
It uses `WebSocketSharp.WebSocket`, matching the mod's websocket library path.

```bash
dotnet run --project AccSaberReloaded.WebSocketTest -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\Beat Saber"
```

Useful options:

```bash
dotnet run --project AccSaberReloaded.WebSocketTest -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\Beat Saber" -- --user 76561198826845259
dotnet run --project AccSaberReloaded.WebSocketTest -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\Beat Saber" -- --raw
dotnet run --project AccSaberReloaded.WebSocketTest -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\Beat Saber" -- --no-reconnect
dotnet run --project AccSaberReloaded.WebSocketTest -p:GameDirectory="C:\Program Files (x86)\Steam\steamapps\common\Beat Saber" -- --log-keepalive
```

The app sends a websocket ping every 45 seconds by default. Use `--keepalive-interval <seconds>` to test another interval or `--no-keepalive` to reproduce idle disconnect behavior.
The default URL is `wss://scoresaber.com/ws`; use `--url <url>` to test another endpoint.

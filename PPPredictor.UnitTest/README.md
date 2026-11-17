# Application Lifecycle Tests

Run with `dotnet test PPPredictor.UnitTest/PPPredictor.UnitTest.csproj`.

This project compiles the production `MainMenuMgr` against small game/UI stubs.
It verifies the order of selection, activation and disposal events without loading
Unity. It deliberately lives outside the independent `PPPredictor.Core` submodule.
These tests do not replace verification of scene transitions in the game.

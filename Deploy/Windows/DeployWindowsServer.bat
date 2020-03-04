@ECHO OFF

cd ../../Barotrauma/BarotraumaServer
dotnet publish WindowsServer.csproj -c Release --self-contained -r win-x64 /p:Platform=x64

PAUSE

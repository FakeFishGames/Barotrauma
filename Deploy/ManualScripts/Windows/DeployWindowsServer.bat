@ECHO OFF

cd ../../../Barotrauma/BarotraumaServer
dotnet publish WindowsServer.csproj -c Release --self-contained -r win-x64 /p:Platform=x64  /p:RollForward=Disable /p:RuntimeFrameworkVersion=3.1.16

PAUSE

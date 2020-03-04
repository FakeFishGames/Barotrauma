@ECHO OFF

cd ../../Barotrauma

cd BarotraumaClient
dotnet publish LinuxClient.csproj -c Release --self-contained -r linux-x64 /p:Platform=x64

cd ..
cd BarotraumaServer
dotnet publish LinuxServer.csproj -c Release --self-contained -r linux-x64 /p:Platform=x64

PAUSE

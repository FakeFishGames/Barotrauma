@ECHO OFF

cd ../../../Barotrauma

cd BarotraumaClient
dotnet publish LinuxClient.csproj -c Unstable -clp:ErrorsOnly;Summary --self-contained -r linux-x64 /p:Platform=x64  /p:RollForward=Disable /p:RuntimeFrameworkVersion=3.1.16

cd ..
cd BarotraumaServer
dotnet publish LinuxServer.csproj -c Unstable -clp:ErrorsOnly;Summary --self-contained -r linux-x64 /p:Platform=x64  /p:RollForward=Disable /p:RuntimeFrameworkVersion=3.1.16

PAUSE

#!/bin/sh

cd ../../../Barotrauma

cd BarotraumaClient
dotnet publish MacClient.csproj -c Unstable -clp:"ErrorsOnly;Summary" --self-contained -r osx-x64 \/p:Platform="x64" \/p:RollForward=Disable \/p:RuntimeFrameworkVersion=3.1.16

cd ..
cd BarotraumaServer
dotnet publish MacServer.csproj -c Unstable -clp:"ErrorsOnly;Summary" --self-contained -r osx-x64 \/p:Platform="x64" \/p:RollForward=Disable \/p:RuntimeFrameworkVersion=3.1.16

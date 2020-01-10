#!/bin/sh

cd ../../Barotrauma

cd BarotraumaClient
dotnet publish MacClient.csproj -c Release --self-contained -r osx-x64 \/p:Platform="x64"

cd ..
cd BarotraumaServer
dotnet publish MacServer.csproj -c Release --self-contained -r osx-x64 \/p:Platform="x64"

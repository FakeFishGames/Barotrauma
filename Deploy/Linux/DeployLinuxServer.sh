#!/bin/sh

cd ../../Barotrauma/BarotraumaServer
dotnet publish LinuxServer.csproj -c Release --self-contained -r linux-x64 \/p:Platform="x64"

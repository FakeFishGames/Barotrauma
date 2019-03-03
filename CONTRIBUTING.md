# Contributing

Welcome to Barotrauma's GitHub repository! If you're here to report an issue or to contribute to the development, please read the instructions below before you do.

## I have a question!
Please check [our FAQ](https://barotraumagame.com/faq/) in case the question has already been answered. If not, you can post the question on the [Barotrauma discussion forum](https://undertowgames.com/forum/viewforum.php?f=17) or stop by at our [Discord Server](https://discordapp.com/invite/xjeSzWU).

## Reporting a bug
If you've encountered a bug, you can report it in the [issue tracker](https://github.com/Regalis11/Barotrauma/issues). Please follow the instructions in the issue template to make it easier for us to diagnose and fix the issue.

## Code contributions
Before you start doing modifications to the code or submitting pull requests to the repository, it is important that you've read and understood (at least the human-readable summary part) of [our EULA](https://github.com/Regalis11/Barotrauma/blob/master/EULA.txt). To sum it up, Barotrauma is not an open source project in the sense of free, open source software that you can freely distribute or reuse. Even though the early versions of the game have been available for free, the game will eventually have a price tag. If you're not comfortable with your contributions potentially being used in a commercial product, do not submit pull requests to the repository.

### Getting started
Installing Visual Studio
You need a version of Visual Studio that supports C# 6.0 to compile game. If you don't have a compatible version of Visual Studio installed, you can get Visual Studio Community 2017 from the following link: https://visualstudio.microsoft.com/downloads/

Make sure you select ".NET desktop development" during the install process to make sure you have the required features to work with Barotrauma.

### Installing dependencies
We use NuGet to make the solution download most of the required libraries and dependencies automatically, but you also need to make sure you have the OpenAL audio library installed.
You can get OpenAL from the following link: https://www.openal.org/downloads/oalinst.zip

### Building the game
The Barotrauma_Solution.sln solution includes 3 important projects: BarotraumaClient, BarotraumaServer and BarotraumaShared. The client project includes the code only required by the client executable: graphics-related code, audio, particle effects and such. The server project includes logic that's only needed by the dedicated server executable. The shared project contains most of the gameplay, physics and networking logic and everything else that's needed by both the client executable and the dedicated server.

Unless you want to work on the dedicated server, you should make sure BarotraumaClient is selected as the startup project.

Next you should choose the build configuration. When developing on Windows, choose either DebugWindows or ReleaseWindows. The debug build configuration includes some features that make debugging and testing a little easier: things such as additional console commands, being able to move the submarine with the IJKL keys and allowing clients to use any console command in multiplayer. The debug builds don't create crash reports when an unhandled exception occurs - the intention behind this is to allow exceptions to be caught by the debugger instead of having the game close and write a report.

### Programming guidelines
We (loosely) follow the C# naming and coding conventions recommended by Microsoft.

https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines

https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions

### Commit messages
We don't have a strict format for commit messages, but please try to make them informative - what did you change and why. It's also recommended that you reference any relevant commits and issue reports in the commit message. If you're fixing a bug caused by a specific commit, mention that commit ("the method added in a1b9b7z didn't take into account things A and B..."), or if you're fixing something that's listed in the issue tracker, mention that issue ("Fixes #123")



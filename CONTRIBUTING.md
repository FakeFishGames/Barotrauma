# Contributing

Welcome to Barotrauma's GitHub repository! If you're here to report an issue or to contribute to the development, please read the instructions below before you do.

## I have a question!
Please check [our FAQ](https://barotraumagame.com/faq/) in case the question has already been answered. If not, you can post the question on the [Barotrauma discussion forum](https://undertowgames.com/forum/viewforum.php?f=17) or stop by at our [Discord Server](https://discord.gg/undertow).

## Reporting a bug
If you've encountered a bug, you can report it in the [issue tracker](https://github.com/Regalis11/Barotrauma/issues). Please follow the instructions in the issue template to make it easier for us to diagnose and fix the issue.

## Code contributions
Before you start doing modifications to the code or submitting pull requests to the repository, it is important that you've read and understood (at least the human-readable summary part) of [our EULA](https://github.com/Regalis11/Barotrauma/blob/master/EULA.txt). To sum it up, Barotrauma is not an open source project in the sense of free, open source software that you can freely distribute or reuse. Even though the early versions of the game have been available for free, current versions of the game have a price tag. If you're not comfortable with your contributions potentially being used in a commercial product, do not submit pull requests to the repository.

### Getting started
#### Windows and macOS
You need a version of Visual Studio that supports C# 8.0 to compile game. If you don't have a compatible version of Visual Studio installed, you can get the latest version of Visual Studio from the following link: https://visualstudio.microsoft.com/

When installing on Windows, make sure you select ".NET desktop development" during the install process to make sure you have the required features to work with Barotrauma.

#### Linux
You will need to install the .NET Core 3.0 SDK according to the instructions laid out on Microsoft's docs: https://docs.microsoft.com/en-us/dotnet/core/install/linux-package-manager-ubuntu-1904

To edit the source code, we recommend using [Visual Studio Code](https://code.visualstudio.com/) with [Microsoft's C# extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp).

Additionally, you can debug the game by running it through `lldb` with the SOS extension, as described here: https://github.com/dotnet/coreclr/blob/master/Documentation/building/debugging-instructions.md

### Installing dependencies
We use NuGet to make the solution download most of the required libraries and dependencies automatically.

### Modifying the source code
Barotrauma's source code is split up into three folders: `BarotraumaClient`, `BarotraumaServer` and `BarotraumaShared`. The non-code assets (i.e. textures, sound files, miscellaneous XML) are mainly found in the `BarotraumaShared` folder.

### Building the game
To develop from Visual Studio, open the solution that corresponds to the platform you're currently working with, then select the desired configuration.

You can also use your favorite source editor and build through the command line by navigating to the projects you wish to build and running the following command: `dotnet build [project].csproj -c [Debug/Release] /p:Platform=x64`

To deploy for release, run the scripts in the `Deploy` directory; the resulting binaries you'll want to redistribute should be found at `Barotrauma/bin/Release[Windows/Mac/Linux]/netcoreapp3.0/[win-x64/osx-x64/linux-x64]/publish`

The `BarotraumaShared/Content` folder, which contains Barotrauma's art, item XMLs, sounds, and other assets, is not included in the GitHub repository. If you have a legal copy of the game, you can copy the `Content` folder from the game's files to `BarotraumaShared/Content`.

The debug build configurations include some features that make debugging and testing a little easier: things such as additional console commands, being able to move the submarine with the IJKL keys and allowing clients to use any console command in multiplayer. The debug builds don't create crash reports when an unhandled exception occurs - the intention behind this is to allow exceptions to be caught by the debugger instead of having the game close and write a report.

If you see "GIT_UNAVAILABLE" in the game version info at the bottom-left corner, you will need to edit your `PATH` environment variable so git can be found.

### Programming guidelines
We (loosely) follow the C# naming and coding conventions recommended by Microsoft.

https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines

https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions

### Commit messages
We don't have a strict format for commit messages, but please try to make them informative - what did you change and why. It's also recommended that you reference any relevant commits and issue reports in the commit message. If you're fixing a bug caused by a specific commit, mention that commit ("the method added in a1b9b7z didn't take into account things A and B..."), or if you're fixing something that's listed in the issue tracker, mention that issue ("Fixes #123")



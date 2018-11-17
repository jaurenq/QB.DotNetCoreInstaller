# DotNetCoreInstaller

A library and [.NET Core Tool](https://github.com/natemcmaster/dotnet-tools) for automating
standalone installations of .NET Core.

## What Is This?

This tool does essentially the same thing as the .NET shared-runtime install scripts
from the .NET Core team ([Install Scripts](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)).

#### Benefits

* Unlike the standard scripts, this tool can perform cross-platform installation.  E.g., if you're on Windows, and you need to get a MacOS shared runtime, the standard scripts won't help you.
* Being built on .NET, the tool can be made a dependency of a .NET project and thus be used easily cross-platform as part of a project's build process.

#### Downsides

* Being built on .NET, you must already have .NET installed in order to run it.
* If the .NET Core team changes their distribution mechanism, the official scripts will likely be updated immediately; this package will need time to catch up.
* This tool is currently is missing a few of the features that the official scripts support, like ability to install the SDK.  Pull requests welcome.

This tool is meant to be used in build and packaging scenarios, when building standalone
deployments for which `dotnet publish` doesn't fit the need.  Example: writing a .NET Core
plugin for another app that itself isn't written in .NET Core.  I don't suggest using it
on end-user's machines; you don't want deployed software breaking in the field because
the .NET Core team changed their distribution mechanism.

Seriously, unless you know why you need this, you're probably looking for
[dotnet publish](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).

### Installing Globally

You'll need at least .NET Core v2.1 or higher.

dotnet tool install -g QB.DotNetCoreInstaller


### Usage - Command Line

Example: Installing shared .NET Core runtime v2.1.4 for MacOS x64
```
dotnet install -i ./dotnet-osx-x64 -r dotnet -p osx -a x64 -v 2.1.4
```


Example: Installing shared .NET Core runtime v2.1.2 for Windows x86
```
dotnet install -i ./dotnet-win-x86 -r dotnet -p win -a x86 -v 2.1.2
```

Example: Installing shared .NET Core runtime v2.1.2 for Windows x86
```
dotnet install -i ./dotnet-win-x86 -r dotnet -p win -a x86 -v 2.1.2
```

Example: Get some help
```
dotnet install -h
```


### Usage - Library

Here's a minimal exmaple of using the installer as a library.

```C#
	using DotNetCore.Tools;

	...

    var parms = new DotNetDistributionParameters(".\dotnet-shared", DotNetPlatform.Windows, DotNetArchitecture.x64, "2.1.4")
    {
        Force = forceOption.HasValue(),
        Runtime = DotNetRuntime.NETCore, // Or DotNetRuntime.AspNetCore to get ASP.Net
        Log = (s) => Log(s)
    };

    var installer = new DotNetCoreInstaller();
    await installer.InstallStandalone(parms);
```


### Installing as a Package Dependency

If you want to use dotnet-publish as part of your build process, you may want to
make it a package dependency instead of requiring people who build your code to
install the tool globally.

TODO: Let's add a section describing how to do this once we've got it working :-)


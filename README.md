# DotNetCoreInstaller

A library and [.NET Core Tool](https://github.com/natemcmaster/dotnet-tools) for automating
standalone installations of .NET Core.

## What Is This?

This tool does essentially the same thing as the .NET shared-runtime install scripts
from the .NET Core team ([Install Scripts](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)).

Benefits

* Unlike the MS scripts, this tool can perform cross-platform installation.  E.g., if you're on Windows, and you need to get a MacOS shared runtime, the MS scripts won't help you.
* Being built on .NET, the tool can be made a dependency of a .NET project and thus be used easily cross-platform as part of a project's build process.

Negatives

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

TODO: Sections for installing into a project, installing as a global tool, and usage
examples.  Show that it's alao a reusable class that you can call from your own code, with usage example.


### Starting a New Project with `dotnet new`

If you don't know which option you want, you probably want this option.  You'll need the
.NET SDK v2.0 or higher installed.  These instructions work for Windows and macOS.

1. Create a directory for your new project.

2. Install or update the 'dotnet new' template for XPNet.

```
dotnet new -i XPNet.CLR.Template
```

3. Create a new plugin project.
```
dotnet new xpnetplugin -n YourPluginName
```

That will leave you with a new project (a .csproj file) and a single C# code file with an
empty plugin class.

Happy coding!

### Building

When you're ready to build and run your plugin, run the following command from the
directory that contains your .csproj file.

```
dotnet publish -c Debug 
```

If you want to make a local build without immediately copying+deploying into
a local X-Plane install, just leave off the -o parameter and its argument.
That will build your plugin and place it in a directory on disk like so:

> YourProjectRoot/bin/Debug/netcoreapp2.0/publish

The exact location will vary depending on which version of .NET Core you are
targetting, your release configuration, etc.  To deploy to X-Plane, copy the
contents of that publish directory into a new plugin folder in X-Plane,
download and place a compatible .NET Core hosting runtime in the same folder,
and start X-Plane.  See the section `Installing into X-Plane` below for
details.

To build in Release mode for distribution, just specify Release configuration
and look in the corresponding Release directory on disk for the output.

```
dotnet publish -c Release
```

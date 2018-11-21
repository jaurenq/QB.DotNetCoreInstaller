

using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DotNetCore.Tools
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            ConsoleColor.Enable();

            var app = new CommandLineApplication();
            app.Name = "dotnet install";
            app.Description = "Performs standalone .NET Core installation.";
            app.HelpOption("-h|--help");
            app.ExtendedHelpText = @"
              The -v option is required.  Passing just a major and minor
              version will cause the app to download the latest published
              version in that series.

              Example: ""-v 2.1.4"" will install exactly version 2.1.4
                       ""-v 2.1"" may install 2.1.0, 2.1.6, etc. - whatever
                       the latest in the 2.1 series is.
            ";

            var installDirOption = app.Option("-i|--install-dir <INSTALL>", "Where to install", CommandOptionType.SingleValue);
            var runtimeOption = app.Option("-r|--runtime <RUNTIME>", "The runtime to install (dotnet or aspnet)", CommandOptionType.SingleValue);
            var platformOption = app.Option("-p|--platform <PLATFORM>", "The platform to install for (win, osx, linux or android)", CommandOptionType.SingleValue);
            var archOption = app.Option("-a|--arch <ARCH>", "The OS architecture to install for (x64 or x86)", CommandOptionType.SingleValue);
            var versionOption = app.Option("-v|--version <PLATFORM>", "What version to install (e.g., 2.1 or 2.1.4)", CommandOptionType.SingleValue);
            var forceOption = app.Option("-f|--force ", "Force reinstallation", CommandOptionType.NoValue);

            app.OnExecute(async () =>
            {
                try
                {
                    var installDir = installDirOption.Value() ?? Directory.GetCurrentDirectory();
                    var runtime = GetRuntime(runtimeOption.Value());
                    var platform = platformOption.Value() ?? GetCurrentPlatform();
                    var arch = archOption.Value() ?? GetCurrentArchitecture();
                    var version = versionOption.Value() ?? throw new UsageException("The -v|--version parameter is required.");

                    installDir = Path.GetFullPath(installDir);

                    var parms = new DotNetDistributionParameters(installDir, platform, arch, version)
                    {
                        Force = forceOption.HasValue(),
                        Runtime = runtime,
                        Log = (s) => Log(s)
                    };

                    var installer = new DotNetCoreInstaller();
                    await installer.InstallStandalone(parms);
                }
                catch (UsageException exc)
                {
                    Console.Error.WriteLine($"{Red("Error: ")} {exc.Message}");
                    Console.Error.WriteLine($"Try: {White("dotnet install -h")}");
                }
                catch (Exception exc)
                {
                    var e = exc;
                    while (e != null)
                    {
                        Console.Error.WriteLine($"{Red("Exception:")} {e.Message}");
                        Console.Error.WriteLine(Gray(e.StackTrace));
                        Console.Error.WriteLine();

                        e = e.InnerException;
                    }
                }

                return 0;
            });

            return app.Execute(args);
        }

        private static string Red(string str) => Wrap("31;1m", str);
        private static string Gray(string str) => Wrap("38;5;243m", str);
        private static string White(string str) => Wrap("37;1m", str);
        private static string Wrap(string ansiCode, string str) => $"\x1b[{ansiCode}{str}\x1b[0m";

        private static void Log(string str) => Console.Out.WriteLine(str);

        private static DotNetRuntime GetRuntime(string sRuntime)
        {
            switch (sRuntime?.ToLower())
            {
                case null:
                case "":
                case "dotnet":
                    return DotNetRuntime.NETCore;

                case "aspnet":
                    return DotNetRuntime.AspNetCore;

                default:
                    throw new UsageException($"Unrecognized runtime option: {sRuntime}");
            }
        }

        private static string GetCurrentArchitecture()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.Arm64:
                case Architecture.X64:
                    return DotNetArchitecture.x64;

                case Architecture.X86:
                    return DotNetArchitecture.x86;

                default:
                    throw new UsageException($"Current architecture {RuntimeInformation.OSArchitecture} is unsupported.");
            }
        }

        private static string GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return DotNetPlatform.Windows;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return DotNetPlatform.MacOS;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return DotNetPlatform.Linux;
            else
                throw new UsageException($"Current architecture {RuntimeInformation.OSArchitecture} is unsupported.");
        }
    }

    internal class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        { }
    }

    internal static class ConsoleColor
    {
        const int STD_OUTPUT_HANDLE = -11;
        const int STD_ERROR_HANDLE = -12;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 4;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        public static void Enable()
        {
            // Starting in newer versions of Windows 10, the console supports ANSI
            // by default, but you have to enable it with platform-specific calls.
            // This code doesn't even try to support older versions of Windows.

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnableWin32Console(STD_OUTPUT_HANDLE);
                EnableWin32Console(STD_ERROR_HANDLE);
            }
        }

        private static void EnableWin32Console(int iHandle)
        {
            var handle = GetStdHandle(iHandle);
            GetConsoleMode(handle, out var mode);
            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(handle, mode);
        }
    }
}

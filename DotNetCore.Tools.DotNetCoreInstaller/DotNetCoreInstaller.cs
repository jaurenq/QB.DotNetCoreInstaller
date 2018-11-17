
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly:InternalsVisibleTo("DotNetCore.Tools.DotNetCoreInstallerTests")]

namespace DotNetCore.Tools
{
    using Downloader = Func<Uri, string, Task>;
    using Extractor = Func<string, string, bool, Task>;

    /// <summary>
    /// Thrown by <see cref="DotNetCoreInstaller"/> when an installation error occurs.
    /// </summary>
    public class DotNetCoreInstallerException : Exception
    {
        public DotNetCoreInstallerException(string message, Exception innerException = null)
            : base(message, innerException)
        { }
    }

    /// <summary>
    /// Provides capabilities for installing a .NET Core shared runtime.
    /// </summary>
    public class DotNetCoreInstaller
    {
        private readonly Downloader DownloadFile;
        private readonly Extractor ExtractFile;
        private readonly IFileSystem m_filesystem;

        public DotNetCoreInstaller()
            : this(Util.DownloadFile, Util.ExtractFile, new FileSystem())
        { }

        internal DotNetCoreInstaller(Downloader downloader, Extractor extractor, IFileSystem filesystem)
        {
            DownloadFile = downloader ?? throw new ArgumentNullException(nameof(downloader));
            ExtractFile = extractor ?? throw new ArgumentNullException(nameof(extractor));
            m_filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
        }

        public async Task InstallStandalone(DotNetDistributionParameters parms)
        {
            try
            {
                var assetName = GetAssetName(parms);
                var dotnetPackageRelativePath = GetDotnetPackageRelativePath(parms);

                var arch = parms.Architecture;
                var specificVersion = parms.Version;
                var uri = BuildDownloadLink(parms, parms.Feed, specificVersion, arch);
                var pkgPath = m_filesystem.Path.Combine(parms.InstallDir, dotnetPackageRelativePath, specificVersion);

                if (!parms.Force && m_filesystem.Directory.Exists(pkgPath))
                {
                    parms.Log?.Invoke($"Skipping installation: {assetName} version {specificVersion} is already installed.");
                    return;
                }

                parms.Log?.Invoke($"Installing {assetName} {parms.Platform}-{arch} v{specificVersion} to {parms.InstallDir}...");

                m_filesystem.Directory.CreateDirectory(parms.InstallDir);

                var zipPath = m_filesystem.Path.GetTempFileName() + GetArchiveExtension(parms);

                await DownloadFile(uri, zipPath);
                await ExtractFile(zipPath, parms.InstallDir, parms.Force);

                if (!m_filesystem.Directory.Exists(pkgPath))
                    throw new DotNetCoreInstallerException($"{assetName} version {specificVersion} failed to install with an unknown error.");

                m_filesystem.File.Delete(zipPath);

                parms.Log?.Invoke($"Installation Complete.");
            }
            catch (Exception exc) when (!(exc is DotNetCoreInstallerException))
            {
                throw new DotNetCoreInstallerException("Unexpected error during installation.  See the inner exception.", exc);
            }
        }

        private string GetAssetName(DotNetDistributionParameters parms)
        {
            switch (parms.Runtime)
            {
                case DotNetRuntime.NETCore:
                    return ".NET Core Runtime";

                case DotNetRuntime.AspNetCore:
                    return "ASP.NET Core Runtime";

                default:
                    throw new DotNetCoreInstallerException("Unhandled value for DotNetDistributionParameters.Runtime: " + parms.Runtime);
            }
        }

        private string GetDotnetPackageRelativePath(DotNetDistributionParameters parms)
        {
            switch (parms.Runtime)
            {
                case DotNetRuntime.NETCore:
                    return m_filesystem.Path.Combine("shared", "Microsoft.NETCore.App");

                case DotNetRuntime.AspNetCore:
                    return m_filesystem.Path.Combine("shared", "Microsoft.AspNetCore.App");

                default:
                    throw new DotNetCoreInstallerException("Unhandled value for DotNetDistributionParameters.Runtime: " + parms.Runtime);
            }
        }

        private string GetArchiveExtension(DotNetDistributionParameters parms)
        {
            var platform = parms.Platform?.ToLower();

            if (platform == DotNetPlatform.Windows)
                return "zip";
            else if (platform == DotNetPlatform.MacOS || platform == DotNetPlatform.Linux)
                return "tar.gz";
            else
                throw new DotNetCoreInstallerException("Unhandled value for DotNetDistributionParameters.Platform: " + platform);
        }

        private Uri BuildDownloadLink(DotNetDistributionParameters parms, string feed, string specificVersion, string arch)
        {
            var ext = GetArchiveExtension(parms);

            switch (parms.Runtime)
            {
                case DotNetRuntime.NETCore:
                    return new Uri($"{feed}/Runtime/{specificVersion}/dotnet-runtime-{specificVersion}-{parms.Platform}-{arch}.{ext}");

                case DotNetRuntime.AspNetCore:
                    return new Uri($"{feed}/aspnetcore/Runtime/{specificVersion}/aspnetcore-runtime-{specificVersion}-{parms.Platform}-{arch}.{ext}");

                default:
                    throw new DotNetCoreInstallerException("Unhandled value for DotNetDistributionParameters.Runtime: " + parms.Runtime);
            }
        }
    }

    internal static class Util
    {
        internal static async Task<HttpWebResponse> Get(Uri uri)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(uri);
                var response = await request.GetResponseAsync();
                return (HttpWebResponse)response;
            }
            catch (Exception exc)
            {
                throw new DotNetCoreInstallerException($"Failed to download from {uri}.", exc);
            }
        }

        internal static Task ExtractFile(string zipPath, string outPath, bool overwrite)
        {
            return Task.Run(() =>
            {
                var extractOptions = new ExtractionOptions() { ExtractFullPath = true, Overwrite = true };

                using (var stream = File.OpenRead(zipPath))
                {
                    var reader = ReaderFactory.Open(stream);
                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.IsDirectory)
                            continue;

                        var path = Path.GetFullPath(Path.Combine(outPath, reader.Entry.Key));
                        var dir = Path.GetDirectoryName(path);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        reader.WriteEntryToDirectory(outPath, extractOptions);
                    }
                }
            });
        }

        internal static async Task DownloadFile(Uri uri, string outPath)
        {
            var response = await Get(uri);

            using (var stream = response.GetResponseStream())
            using (var fileStream = System.IO.File.Create(outPath))
                await stream.CopyToAsync(fileStream);
        }
    }

    public enum DotNetRuntime
    {
        NETCore,
        AspNetCore,
    }

    //public static class DotNetChannels
    //{
    //    public static string v1 = "1.0";
    //    public static string v2 = "2.0";
    //    public static string LTS = "LTS";
    //}

    public static class DotNetArchitecture
    {
        public static string x64 = "x64";
        public static string x86 = "x86";
    }

    public static class DotNetPlatform
    {
        public static string Windows = "win";
        public static string Linux = "linux";
        public static string MacOS = "osx";
        public static string Android = "android";
    }

    public class DotNetDistributionParameters
    {
        public static string DefaultFeed = "https://dotnetcli.azureedge.net/dotnet";

        public DotNetDistributionParameters(string installDir, string platform, string architecture, string version)
        {
            InstallDir = installDir;
            Platform = platform;
            Architecture = architecture;
            Version = version;
        }

        public string InstallDir { get; }
        public string Platform { get; }
        public string Architecture { get; }
        public string Version { get; set; }

        // public string Channel { get; set; } = DotNetChannels.LTS;

        public DotNetRuntime Runtime { get; set; } = DotNetRuntime.NETCore;

        public string Feed { get; set; } = DefaultFeed;

        public bool Force { get; set; } = false;

        public Action<string> Log { get; set; }
    }
}

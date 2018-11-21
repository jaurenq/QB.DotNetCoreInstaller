
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

                parms.Log?.Invoke($"Install of {assetName} {parms.Platform}-{arch} v{parms.Version} requested.");

                var pkgRoot = m_filesystem.Path.Combine(parms.InstallDir, dotnetPackageRelativePath);
                if (m_filesystem.Directory.Exists(pkgRoot))
                {
                    var matchingVersions = m_filesystem.Directory.GetDirectories(pkgRoot, parms.Version + "*");
                    if (!parms.Force && matchingVersions.Length > 0)
                    {
                        // There may be more than one matching version, but there is also
                        // at least the first one; this message may be incomplete but it
                        // is true and sufficient for the purpose.
                        parms.Log?.Invoke($"Skipping installation: {assetName} version {Path.GetFileName(matchingVersions[0])} is already installed at {parms.InstallDir}.");
                        return;
                    }
                }

                var specificVersion = await ResolveVersion(parms, parms.Version);
                var uri = BuildDownloadLink(parms, parms.CachedFeed, specificVersion, arch);
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

        private async Task<string> ResolveVersion(DotNetDistributionParameters parms, string requestedVersion)
        {
            string resolvedVersion;

            var s = requestedVersion.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (s.Length == 2)
            {
                var channel = requestedVersion;

                parms.Log?.Invoke($"Looking for latest version in series {channel}...");

                var uri = BuildLatestVersionLink(parms, parms.UncachedFeed, channel);
                var tmpPath = m_filesystem.Path.GetTempFileName();

                await DownloadFile(uri, tmpPath);

                var versionText = m_filesystem.File.ReadAllText(tmpPath);

                var entries = versionText.Split(new string[0], StringSplitOptions.RemoveEmptyEntries);

                var commitHash = entries[0];
                resolvedVersion = entries[1];

                parms.Log?.Invoke($"Found {resolvedVersion} ({commitHash})");
            }
            else if (s.Length == 3)
            {
                resolvedVersion = requestedVersion;
            }
            else
            {
                throw new DotNetCoreInstallerException($"Unhandled or unrecognized requested version syntax: {requestedVersion}");
            }

            return resolvedVersion;
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
                    throw BadRuntime(parms.Runtime);
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
                    throw BadRuntime(parms.Runtime);
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
                throw BadPlatform(platform);
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
                    throw BadRuntime(parms.Runtime);
            }
        }

        private Uri BuildLatestVersionLink(DotNetDistributionParameters parms, string feed, string channel)
        {
            var ext = GetArchiveExtension(parms);
    
            switch (parms.Runtime)
            {
                case DotNetRuntime.NETCore:
                    return new Uri($"{feed}/Runtime/{channel}/latest.version");

                case DotNetRuntime.AspNetCore:
                    return new Uri($"{feed}/aspnetcore/Runtime/{channel}/latest.version");

                default:
                    throw BadRuntime(parms.Runtime);
            }
        }

        private static Exception BadRuntime(DotNetRuntime runtime) =>
            new DotNetCoreInstallerException($"Unhandled value for DotNetDistributionParameters.Runtime: {runtime}");

        private static Exception BadPlatform(string platform) =>
            throw new DotNetCoreInstallerException($"Unhandled value for DotNetDistributionParameters.Platform: {platform}");
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
        public static string DefaultCachedFeed = "https://dotnetcli.azureedge.net/dotnet";
        public static string DefaultUncachedFeed = "https://dotnetcli.blob.core.windows.net/dotnet";

        /// <summary>
        /// Constructs parameters for distribution with required parameters.  Additional parameters
        /// may be specified
        /// </summary>
        /// <param name="installDir">
        /// The location to install the shared runtime.
        /// </param>
        /// <param name="platform">
        /// The platform to install.
        /// </param>
        /// <param name="architecture"></param>
        /// <param name="version"></param>
        public DotNetDistributionParameters(string installDir, string platform, string architecture, string version)
        {
            InstallDir = installDir ?? throw new ArgumentNullException(nameof(installDir));
            Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            Architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        /// <summary>
        /// The location to install the shared runtime.  Set via the constructor.
        /// </summary>
        public string InstallDir { get; }

        /// <summary>
        /// The platform to install a runtime for.  See <see cref="DotNetPlatform"/>.
        /// </summary>
        public string Platform { get; }

        /// <summary>
        /// The architecture to install a runtime for.  See <see cref="DotNetArchitecture"/>.
        /// </summary>
        public string Architecture { get; }

        /// <summary>
        /// The version to install.  Set via the constructor.  Must either
        /// be a specific 3-component version (e.g., 2.1.3) or a 2-component
        /// version (e.g. 2.1), meaning to install the latest runtime in that
        /// family.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// The runtime to install.  By default, the platform is .NET Core;
        /// change this to install ASP.Net or, in the future, other supported
        /// platform runtimes.  See <see cref="DotNetRuntime"/>.
        /// </summary>
        public DotNetRuntime Runtime { get; set; } = DotNetRuntime.NETCore;

        /// <summary>
        /// The cached (CDN) feed to download runtimes from.
        /// </summary>
        public string CachedFeed { get; set; } = DefaultCachedFeed;

        /// <summary>
        /// The uncached feed to download runtimes from; used to determine
        /// what the latest version of a runtime is.
        /// </summary>
        public string UncachedFeed { get; set; } = DefaultUncachedFeed;

        /// <summary>
        /// Set to true to force the runtime to be (re)downloaded and (re)installed
        /// even if it already appears to be present.
        /// </summary>
        /// <remarks>
        /// If the runtime wasn't present,
        /// has no effect.  Normally, when this isn't set to true, if the runtime
        /// appears to have been previously installed the install will exit early
        /// without any network activity.
        /// </remarks>
        public bool Force { get; set; } = false;

        /// <summary>
        /// Set this to a delegate that will receive log output during the install.
        /// If not set, no log output will be provided.
        /// </summary>
        public Action<string> Log { get; set; }
    }
}

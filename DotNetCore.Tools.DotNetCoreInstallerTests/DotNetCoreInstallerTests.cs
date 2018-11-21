
using DotNetCore.Tools;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Xunit;

namespace DotNetCoreInstallerTests
{
    /// <summary>
    /// Unit tests for the <see cref="DotNetCoreInstaller"/> class.
    /// </summary>
    /// <remarks>
    /// ENHANCE: This isn't a full-coverage test suite at present.
    /// </remarks>
    public class DotNetCoreInstallerTests : IDisposable
    {
        private readonly DirectoryInfo TestRoot;
        private readonly string InstallDir;

        private const string DefaultTestRelease = "2.1.0";

        public DotNetCoreInstallerTests()
        {
            TestRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            InstallDir = Path.Combine(TestRoot.FullName, "NetCoreInstallation");
        }

        public void Dispose()
        {
            TestRoot.Delete(recursive: true);
        }

        #region Mock Factories

        private Mock<Func<Uri, string, Task>> CreateMockDownloader() => new Mock<Func<Uri, string, Task>>(MockBehavior.Strict);
        private Mock<Func<string, string, bool, Task>> CreateMockExtractor() => new Mock<Func<string, string, bool, Task>>(MockBehavior.Strict);

        private Mock<IFileSystem> CreateMockFilesystem()
        {
            var mockfs = new Mock<IFileSystem>(MockBehavior.Strict);

            var mockPath = new Mock<PathBase>();

            // Just make Path.Combine() work as you'd expect.
            mockPath.Setup(p => p.Combine(It.IsAny<string>(), It.IsAny<string>())).Returns((string s1, string s2) => Path.Combine(s1, s2));
            mockPath.Setup(p => p.Combine(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns((string s1, string s2, string s3) => Path.Combine(s1, s2, s3));

            mockfs.Setup(f => f.Directory).Returns(new Mock<DirectoryBase>(MockBehavior.Strict).Object);
            mockfs.Setup(f => f.Path).Returns(mockPath.Object);
            mockfs.Setup(f => f.File).Returns(new Mock<FileBase>(MockBehavior.Strict).Object);

            return mockfs;
        }

        #endregion // Mock Factories

        [Fact]
        public async void InstallsNetCoreByDefault()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x64, DefaultTestRelease);
            await InstallsMocked(parms);
        }

        [Fact]
        public async void InstallsNetCoreExplicitly()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x64, DefaultTestRelease)
            {
                Runtime = DotNetRuntime.NETCore
            };
            await InstallsMocked(parms);
        }

        [Fact]
        public async void InstallsAspNetCoreExplicitly()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x64, DefaultTestRelease)
            {
                Runtime = DotNetRuntime.AspNetCore
            };
            await InstallsMocked(parms);
        }

        [Fact]
        public async void HonorsPlatform()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.MacOS, DotNetArchitecture.x64, DefaultTestRelease);
            await InstallsMocked(parms);
        }

        [Fact]
        public async void HonorsArchitecture()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, DefaultTestRelease);
            await InstallsMocked(parms);
        }

        [Fact]
        public async void HonorsVersion()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, "2.0.4");
            await InstallsMocked(parms);
        }

        [Fact]
        public async void LooksUpLatestVersionInSeriesWhenRequested()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, "2.0");
            await InstallsMocked(parms, expectsLatestVersionLookup: true);
        }

        [Theory]
        [InlineData("")]
        [InlineData("2")]
        [InlineData("2.")]
        [InlineData("2.3.2.1")]
        [InlineData("bob")]
        public async void ThrowsWithBadVersionNumber(string badVersion)
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, badVersion);
            var exc = await Assert.ThrowsAsync<DotNetCoreInstallerException>(async () => await InstallsMocked(parms, failExtract: true));

            Assert.Contains("version", exc.Message);
        }

        [Fact]
        public async void ExitsEarlyIfDirectoryExistsWithExactVersion()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, DefaultTestRelease);

            var logMock = new Mock<Action<string>>();
            parms.Log = logMock.Object;

            await InstallsMocked(parms, earlyExit: true);

            logMock.Verify(l => l(It.Is<string>(s => s.Contains("is already installed"))));
        }

        [Fact]
        public async void ExitsEarlyIfDirectoryExistsWithAnyCompatibleVersion()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, "2.0");

            var logMock = new Mock<Action<string>>();
            parms.Log = logMock.Object;

            await InstallsMocked(parms, earlyExit: true);

            logMock.Verify(l => l(It.Is<string>(s => s.Contains("is already installed"))));
        }

        [Fact]
        public async void RunsEvenIfCompatibleVersionDirectoryExistsIfForced()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, "2.0");
            parms.Force = true;
            await InstallsMocked(parms, forcedOverwrite: true, expectsLatestVersionLookup: true);
        }

        [Fact]
        public async void RunsEvenIfExactVersionDirectoryExistsIfForced()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, DefaultTestRelease);
            parms.Force = true;
            await InstallsMocked(parms, forcedOverwrite: true);
        }

        [Fact]
        public async void ThrowsIfExtractDoesNotComplete()
        {
            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x86, DefaultTestRelease);
            var exc = await Assert.ThrowsAsync<DotNetCoreInstallerException>(async () => await InstallsMocked(parms, failExtract: true));

            Assert.Contains("failed to install", exc.Message);
        }

        private async Task InstallsMocked(
            DotNetDistributionParameters parms,
            bool earlyExit = false, bool forcedOverwrite = false, bool failExtract = false, bool expectsLatestVersionLookup = false)
        {
            var platform = parms.Platform;
            var arch = parms.Architecture;
            var version = parms.Version;

            var mockDownloader = CreateMockDownloader();
            var mockExtractor = CreateMockExtractor();

            var mockFilesystem = CreateMockFilesystem();

            var mockFile = Mock.Get(mockFilesystem.Object.File);
            var mockDirectory = Mock.Get(mockFilesystem.Object.Directory);

            var installer = new DotNetCoreInstaller(mockDownloader.Object, mockExtractor.Object, mockFilesystem.Object);

            var archiveExt = (parms.Platform == DotNetPlatform.Windows) ? "zip" : "tar.gz";

            var assetDir = (parms.Runtime == DotNetRuntime.NETCore) ? "Microsoft.NETCore.App" : "Microsoft.AspNetCore.App";
            var expectedPackageRoot = Path.Combine(InstallDir, "shared", assetDir);

            mockDirectory.Setup(d => d.Exists(expectedPackageRoot)).Returns(true); // Just test the true case; the false case is uninteresting and the code is covered by other tests anyway.
            mockDirectory.Setup(d => d.GetDirectories(expectedPackageRoot, parms.Version + "*")).Returns(
                earlyExit ? new [] { version + ".3" } : new string[0]
            );

            if (expectsLatestVersionLookup)
            {
                var expectedLatestUri = (parms.Runtime == DotNetRuntime.NETCore)
                    ? new Uri($"{DotNetDistributionParameters.DefaultUncachedFeed}/Runtime/{version}/latest.version")
                    : new Uri($"{DotNetDistributionParameters.DefaultUncachedFeed}/aspnetcore/Runtime/{version}/latest.version");

                mockDownloader.Setup(d => d(expectedLatestUri, It.IsAny<string>())).Returns(Task.CompletedTask);            // Download latest.version File

                version = version + ".3"; // Pretend that the latest of, e.g. 2.2, is 2.2.3.

                mockFile.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns($"some-hash\n{version}");
            }

            var expectedUri = (parms.Runtime == DotNetRuntime.NETCore)
                ? new Uri($"{DotNetDistributionParameters.DefaultCachedFeed}/Runtime/{version}/dotnet-runtime-{version}-{platform}-{arch}.{archiveExt}")
                : new Uri($"{DotNetDistributionParameters.DefaultCachedFeed}/aspnetcore/Runtime/{version}/aspnetcore-runtime-{version}-{platform}-{arch}.{archiveExt}");
            var expectedInstallPath = Path.Combine(InstallDir, "shared", assetDir, version);

            if (earlyExit)
            {
                mockDirectory.Setup(d => d.Exists(expectedInstallPath)).ReturnsInOrder(true);                               // Checking for version directory.
            }
            else
            {
                bool initiallyPresent = forcedOverwrite ? true : false;
                bool finallyPresent = failExtract ? false : true;

                mockDirectory.Setup(d => d.Exists(expectedInstallPath)).ReturnsInOrder(initiallyPresent, finallyPresent);   // Checking for version directory.
                mockDirectory.Setup(d => d.CreateDirectory(parms.InstallDir)).Returns(value: null);                         // Creating the root install dir.
                mockDownloader.Setup(d => d(expectedUri, It.IsAny<string>())).Returns(Task.CompletedTask);                  // Download
                mockExtractor.Setup(e => e(It.IsAny<string>(), parms.InstallDir, forcedOverwrite))                          // Extract
                    .Returns(Task.CompletedTask);
                mockFile.Setup(f => f.Delete(It.IsAny<string>()));                                                          // Deleting the temporary zip file.
            }

            await installer.InstallStandalone(parms);

            mockDownloader.VerifyAll();
            mockExtractor.VerifyAll();
        }

        /// <summary>
        /// This test runs a real download from the actual .NET distribution servers.
        /// As such, it is set to not run by default with the rest of the test suite.
        /// You can run it manually by running the suite with an attached debugger.
        /// </summary>
        [FactRunnableInDebugOnly]
        public async void InstallsForRealWithSpecificVersion()
        {
            var platform = DotNetPlatform.Windows;
            var arch = DotNetArchitecture.x64;
            var version = "2.1.4";

            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x64, version);
            var installer = new DotNetCoreInstaller();
            await installer.InstallStandalone(parms);

            var expectedInstallPath = Path.Combine(InstallDir, "shared", "Microsoft.NETCore.App", version);
            Assert.True(Directory.Exists(expectedInstallPath));
        }

        [FactRunnableInDebugOnly]
        public async void InstallsForRealWithLatestVersion()
        {
            var platform = DotNetPlatform.Windows;
            var arch = DotNetArchitecture.x64;
            var version = "2.1";

            var parms = new DotNetDistributionParameters(InstallDir, DotNetPlatform.Windows, DotNetArchitecture.x64, version);
            var installer = new DotNetCoreInstaller();
            await installer.InstallStandalone(parms);

            var matchingDirectories = Directory.GetDirectories(Path.Combine(InstallDir, "shared", "Microsoft.NETCore.App"), version + "*");
            Assert.True(matchingDirectories.Length == 1 && Directory.Exists(matchingDirectories[0]));
        }
    }

    public class FactRunnableInDebugOnlyAttribute : FactAttribute
    {
        public FactRunnableInDebugOnlyAttribute()
        {
            if (!Debugger.IsAttached)
                Skip = "Only running in interactive mode.";
        }
    }

    public static class MoqExtensions
    {
        public static void ReturnsInOrder<T, TResult>(
            this Moq.Language.Flow.ISetup<T, TResult> setup,
            params TResult[] results) where T : class
        {
            setup.Returns(new Queue<TResult>(results).Dequeue);
        }
    }
}


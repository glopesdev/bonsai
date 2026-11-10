using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bonsai.NuGet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Bonsai.Configuration.Tests;

[TestClass]
public sealed class PackageManagerTests
{
    static readonly NuGetFramework ProjectFramework = NuGetFramework.Parse("net472");
    static readonly PackageIdentity DependencyV1 = new("Dependency", NuGetVersion.Parse("1.0.0"));
    static readonly PackageIdentity DependencyV2 = new("Dependency", NuGetVersion.Parse("2.0.0"));
    static readonly PackageIdentity DependentV1 = new("Dependent", NuGetVersion.Parse("1.0.0"));
    static readonly PackageIdentity DependentV2 = new("Dependent", NuGetVersion.Parse("2.0.0"));

    string rootPath;

    string FeedPath => Path.Combine(rootPath, "feed");

    string PackagesPath => Path.Combine(rootPath, "packages");

    [TestInitialize]
    public void Initialize()
    {
        rootPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(FeedPath);
        Directory.CreateDirectory(PackagesPath);
        File.WriteAllText(
            Path.Combine(rootPath, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{Path.Combine(rootPath, "global")}" />
              </config>
              <packageSources>
                <clear />
                <add key="test" value="{FeedPath}" />
              </packageSources>
            </configuration>
            """);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(rootPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void AddFeedPackage(PackageIdentity package, string dependencyRange = null)
    {
        var builder = new PackageBuilder
        {
            Id = package.Id,
            Version = package.Version,
            Description = package.Id
        };
        builder.Authors.Add(nameof(PackageManagerTests));
        builder.Files.Add(new PhysicalPackageFile(new MemoryStream(new byte[] { 0 })) { TargetPath = "content/placeholder.txt" });
        if (dependencyRange != null)
        {
            var dependency = new PackageDependency(DependencyV1.Id, VersionRange.Parse(dependencyRange));
            builder.DependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.AnyFramework, new[] { dependency }));
        }

        using var stream = File.Create(Path.Combine(FeedPath, $"{package.Id}.{package.Version}.nupkg"));
        builder.Save(stream);
    }

    PackageManager CreatePackageManager()
    {
        var settings = Settings.LoadSpecificSettings(rootPath, "NuGet.config");
        return new PackageManager(settings, new PackageSourceProvider(settings), PackagesPath);
    }

    static bool IsInstalled(PackageManager packageManager, PackageIdentity package)
    {
        return packageManager.PathResolver.GetInstalledPath(package) != null;
    }

    [TestMethod]
    // Regression test for https://github.com/bonsai-rx/bonsai/issues/1042
    public async Task InstallPackage_VersionOutsideInstalledDependentRange_ThrowsAndKeepsInstalledVersion()
    {
        AddFeedPackage(DependencyV1);
        AddFeedPackage(DependencyV2);
        AddFeedPackage(DependentV1, "[1.0.0, 2.0.0)");
        var packageManager = CreatePackageManager();
        await packageManager.InstallPackageAsync(DependentV1, ProjectFramework, ignoreDependencies: false, CancellationToken.None);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => packageManager.InstallPackageAsync(DependencyV2, ProjectFramework, ignoreDependencies: false, CancellationToken.None));
        StringAssert.Contains(exception.Message, "'Dependent 1.0.0' requires 'Dependency (>= 1.0.0 && < 2.0.0)'");
        Assert.IsTrue(IsInstalled(packageManager, DependencyV1), "the installed dependency version should be kept");
        Assert.IsFalse(IsInstalled(packageManager, DependencyV2), "the incompatible dependency version should not be installed");
    }

    [TestMethod]
    public async Task InstallPackage_VersionInsideInstalledDependentRange_ReplacesInstalledVersion()
    {
        AddFeedPackage(DependencyV1);
        AddFeedPackage(DependencyV2);
        AddFeedPackage(DependentV1, "1.0.0");
        var packageManager = CreatePackageManager();
        await packageManager.InstallPackageAsync(DependentV1, ProjectFramework, ignoreDependencies: false, CancellationToken.None);

        await packageManager.InstallPackageAsync(DependencyV2, ProjectFramework, ignoreDependencies: false, CancellationToken.None);
        Assert.IsTrue(IsInstalled(packageManager, DependencyV2), "the compatible dependency version should be installed");
        Assert.IsFalse(IsInstalled(packageManager, DependencyV1), "the previous dependency version should be removed");
    }

    [TestMethod]
    public async Task ValidatePackageDependencies_IncompatiblePackageSet_Throws()
    {
        AddFeedPackage(DependencyV1);
        AddFeedPackage(DependencyV2);
        AddFeedPackage(DependentV1, "[1.0.0, 2.0.0)");
        var packageManager = CreatePackageManager();
        await packageManager.InstallPackageAsync(DependentV1, ProjectFramework, ignoreDependencies: false, CancellationToken.None);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => packageManager.ValidatePackageDependenciesAsync(new[] { DependentV1, DependencyV2 }, ProjectFramework, CancellationToken.None));
        StringAssert.Contains(exception.Message, "'Dependent 1.0.0' requires 'Dependency (>= 1.0.0 && < 2.0.0)'");
    }

    [TestMethod]
    public async Task ValidatePackageDependencies_CompatiblePackageSet_InstallsNothing()
    {
        AddFeedPackage(DependencyV1);
        AddFeedPackage(DependentV1, "[1.0.0, 2.0.0)");
        var packageManager = CreatePackageManager();

        await packageManager.ValidatePackageDependenciesAsync(new[] { DependentV1, DependencyV1 }, ProjectFramework, CancellationToken.None);
        Assert.IsFalse(IsInstalled(packageManager, DependentV1), "validation should not install packages");
        Assert.IsFalse(IsInstalled(packageManager, DependencyV1), "validation should not install packages");
    }

    [TestMethod]
    public async Task InstallPackages_DependentUpdatedWithDependency_ReplacesBothInstalledVersions()
    {
        AddFeedPackage(DependencyV1);
        AddFeedPackage(DependencyV2);
        AddFeedPackage(DependentV1, "[1.0.0, 2.0.0)");
        AddFeedPackage(DependentV2, "[2.0.0, 3.0.0)");
        var packageManager = CreatePackageManager();
        await packageManager.InstallPackageAsync(DependentV1, ProjectFramework, ignoreDependencies: false, CancellationToken.None);

        await packageManager.InstallPackagesAsync(new[] { DependencyV2, DependentV2 }, ProjectFramework, CancellationToken.None);
        Assert.IsTrue(IsInstalled(packageManager, DependencyV2), "the dependency should be updated");
        Assert.IsTrue(IsInstalled(packageManager, DependentV2), "the dependent should be updated");
        Assert.IsFalse(IsInstalled(packageManager, DependencyV1), "the previous dependency version should be removed");
        Assert.IsFalse(IsInstalled(packageManager, DependentV1), "the previous dependent version should be removed");
    }
}

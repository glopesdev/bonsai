﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bonsai.NuGet.Packaging;
using Bonsai.NuGet.Properties;
using Newtonsoft.Json;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;

namespace Bonsai.NuGet
{
    public static class PackageExtensions
    {
        public static readonly string ContentFolder = PathUtility.EnsureTrailingSlash(PackagingConstants.Folders.Content);

        public static bool IsPackageType(this LocalPackageInfo packageInfo, string typeName)
        {
            return packageInfo.Nuspec.IsPackageType(typeName);
        }

        public static bool IsPackageType(this PackageReaderBase packageReader, string typeName)
        {
            return packageReader.NuspecReader.IsPackageType(typeName);
        }

        public static bool IsPackageType(this NuspecReader reader, string typeName)
        {
            return reader.GetPackageTypes().IsPackageType(typeName);
        }

        public static bool IsPackageType(this IReadOnlyList<PackageType> packageTypes, string typeName)
        {
            if (packageTypes.Count == 0
                && PackageType.PackageTypeNameComparer.Equals(typeName, PackageType.Dependency.Name))
            {
                return true;
            }

            return packageTypes.Any(type => PackageType.PackageTypeNameComparer.Equals(type.Name, typeName));
        }

        public static bool IsLibraryPackage(this PackageReaderBase packageReader)
        {
            return packageReader.IsPackageType(Constants.LibraryPackageType)
                || packageReader.NuspecReader.GetTags()?.Contains(Constants.BonsaiTag) is true;
        }

        public static bool IsGalleryPackage(this PackageReaderBase packageReader)
        {
            return packageReader.IsPackageType(Constants.GalleryPackageType)
                || packageReader.NuspecReader.GetTags() is string tagText
                && tagText.Contains(Constants.BonsaiTag)
                && tagText.Contains(Constants.GalleryTag);
        }

        public static bool IsExecutablePackage(this PackageReaderBase packageReader, PackageIdentity identity, NuGetFramework projectFramework)
        {
            var entryPoint = identity.Id + Constants.BonsaiExtension;
            var nearestFrameworkGroup = packageReader.GetContentItems().GetNearest(projectFramework);
            var executablePackage = nearestFrameworkGroup?.Items.Any(file => PathUtility.GetRelativePath(ContentFolder, file) == entryPoint);
            return IsGalleryPackage(packageReader) && executablePackage.GetValueOrDefault();
        }

        static BonsaiMetadata GetBonsaiMetadata(this PackageReaderBase packageReader)
        {
            var bonsaiMetadataFile = packageReader.GetFiles().SingleOrDefault(path => path == Constants.BonsaiMetadataFile);
            if (bonsaiMetadataFile is null)
                return null;

            using var stream = packageReader.GetStream(bonsaiMetadataFile);
            using var reader = new StreamReader(stream);
            return JsonConvert.DeserializeObject<BonsaiMetadata>(reader.ReadToEnd());
        }

        public static string InstallExecutablePackage(this PackageReaderBase packageReader, PackageIdentity package, NuGetFramework projectFramework, string targetPath)
        {
            var targetId = Path.GetFileName(targetPath);
            var targetEntryPoint = targetId + Constants.BonsaiExtension;
            var targetEntryPointLayout = targetEntryPoint + Constants.LayoutExtension;
            var packageEntryPoint = package.Id + Constants.BonsaiExtension;
            var packageEntryPointLayout = packageEntryPoint + Constants.LayoutExtension;

            var bonsaiMetadata = GetBonsaiMetadata(packageReader);
            if (bonsaiMetadata is not null &&
                bonsaiMetadata.Gallery.TryGetValue(BonsaiMetadata.DefaultWorkflow, out var workflowMetadata))
            {
                packageEntryPoint = workflowMetadata.Path;
                packageEntryPointLayout = null;
            }

            var nearestFrameworkGroup = packageReader.GetContentItems().GetNearest(projectFramework);
            if (nearestFrameworkGroup != null)
            {
                foreach (var file in nearestFrameworkGroup.Items)
                {
                    var effectivePath = PathUtility.GetRelativePath(ContentFolder, file);
                    if (effectivePath == packageEntryPoint) effectivePath = targetEntryPoint;
                    else if (effectivePath == packageEntryPointLayout) effectivePath = targetEntryPointLayout;
                    effectivePath = Path.Combine(targetPath, effectivePath);
                    PathUtility.EnsureParentDirectory(effectivePath);

                    using var stream = packageReader.GetStream(file);
                    using var targetStream = File.Create(effectivePath);
                    stream.CopyTo(targetStream);
                }
            }

            var effectiveEntryPoint = Path.Combine(targetPath, targetEntryPoint);
            if (!File.Exists(effectiveEntryPoint))
            {
                var message = string.Format(Resources.MissingWorkflowEntryPoint, targetEntryPoint);
                throw new InvalidOperationException(message);
            }

            return effectiveEntryPoint;
        }

        public static async Task<PackageReaderBase> InstallPackageAsync(
            this IPackageManager packageManager,
            string packageId,
            NuGetVersion version,
            NuGetFramework projectFramework,
            CancellationToken cancellationToken = default)
        {
            var package = new PackageIdentity(packageId, version);
            var logMessage = package.Version == null ? Resources.InstallPackageLatestVersion : Resources.InstallPackageVersion;
            packageManager.Logger.LogInformation(string.Format(logMessage, package.Id, package.Version));
            return await packageManager.InstallPackageAsync(package, projectFramework, ignoreDependencies: false, cancellationToken);
        }

        public static async Task<PackageReaderBase> RestorePackageAsync(
            this IPackageManager packageManager,
            string packageId,
            NuGetVersion version,
            NuGetFramework projectFramework,
            CancellationToken cancellationToken = default)
        {
            var package = new PackageIdentity(packageId, version);
            packageManager.Logger.LogInformation(string.Format(Resources.RestorePackageVersion, packageId, version));
            return await packageManager.InstallPackageAsync(package, projectFramework, ignoreDependencies: true, cancellationToken);
        }
    }
}

﻿using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using Net.Chdk.Detectors.Software;
using Net.Chdk.Meta.Providers.Zip;
using Net.Chdk.Model.Software;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Net.Chdk.Meta.Providers.Software.Zip
{
    sealed class ZipSoftwareMetaProvider : ZipMetaProvider<SoftwareInfo>, ISoftwareMetaProvider
    {
        private static readonly Version Version = new Version("1.0");

        private IBinarySoftwareDetector SoftwareDetector { get; }
        private ICategoryMetaProvider CategoryProvider { get; }
        private IProductMetaProvider ProductProvider { get; }
        private ICameraMetaProvider CameraProvider { get; }
        private ISourceMetaProvider SourceProvider { get; }
        private IBuildMetaProvider BuildProvider { get; }
        private ICompilerMetaProvider CompilerProvider { get; }
        private IEncodingMetaProvider EncodingProvider { get; }

        public ZipSoftwareMetaProvider(IBinarySoftwareDetector softwareDetector, ICategoryMetaProvider categoryProvider, IBootMetaProvider bootProvider,
            IProductMetaProvider productProvider, ICameraMetaProvider cameraProvider, ISourceMetaProvider sourceProvider,
            IBuildMetaProvider buildProvider, ICompilerMetaProvider compilerProvider, IEncodingMetaProvider encodingProvider,
            ILogger<ZipSoftwareMetaProvider> logger)
            : base(bootProvider, logger)
        {
            SoftwareDetector = softwareDetector;
            CategoryProvider = categoryProvider;
            ProductProvider = productProvider;
            CameraProvider = cameraProvider;
            SourceProvider = sourceProvider;
            BuildProvider = buildProvider;
            CompilerProvider = compilerProvider;
            EncodingProvider = encodingProvider;
        }

        public IEnumerable<SoftwareInfo> GetSoftware(string path)
        {
            if (!path.Contains('?') && !path.Contains('*'))
                return GetItems(path);
            var dir = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path);
            return Directory.EnumerateFiles(dir, pattern)
                .SelectMany(file => GetItems(file));
        }

        protected override SoftwareInfo DoGetItem(ZipFile zip, string name, ZipEntry entry)
        {
            using (var stream = zip.GetInputStream(entry))
            using (var memoryStream = new MemoryStream((int)entry.Size))
            {
                stream.CopyTo(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var buffer = memoryStream.ToArray();
                return GetSoftware(buffer, name, entry);
            }
        }

        private SoftwareInfo GetSoftware(byte[] buffer, string name, ZipEntry entry)
        {
            var software = SoftwareDetector.GetSoftware(buffer, null, default(CancellationToken));
            if (software == null)
                Logger.LogError("Cannot detect software");

            if (software.Camera != null)
            {
                ValidateSoftware(software, entry, name);
                UpdateSoftware(software);
            }
            else
            {
                var created = entry.DateTime.ToUniversalTime();
                software.Product = ProductProvider.GetProduct(name, created);
                UpdateSoftware(software);
                software.Camera = GetCamera(name);
            }

            return software;
        }

        private void ValidateSoftware(SoftwareInfo software, ZipEntry entry, string name)
        {
            var created = entry.DateTime.ToUniversalTime();
            var product = ProductProvider.GetProduct(name, created);
            var camera = CameraProvider.GetCamera(name);

            if (!product.Name.Equals(software.Product.Name))
                Logger.LogWarning("Mismatching product name: {0}", software.Product.Name);

            if (!product.Version.Equals(software.Product.Version))
                Logger.LogWarning("Mismatching product version: {0}", software.Product.Version);

            if (!product.Language.Equals(software.Product.Language))
                Logger.LogWarning("Mismatching product language: {0}", software.Product.Language);

            if (!camera.Platform.Equals(software.Camera.Platform))
                Logger.LogWarning("Mismatching platform: {0}", software.Camera.Platform);

            if (!camera.Revision.Equals(software.Camera.Revision))
                Logger.LogWarning("Mismatching revision: {0}", software.Camera.Revision);
        }

        private void UpdateSoftware(SoftwareInfo software)
        {
            software.Category = CategoryProvider.GetCategory(software);
            software.Source = SourceProvider.GetSource(software);
            software.Build = BuildProvider.GetBuild(software);
            software.Compiler = CompilerProvider.GetCompiler(software);
            software.Encoding = EncodingProvider.GetEncoding(software.Encoding);
        }

        private SoftwareCameraInfo GetCamera(string name)
        {
            var camera = CameraProvider.GetCamera(name);
            return new SoftwareCameraInfo
            {
                Platform = camera.Platform,
                Revision = camera.Revision,
            };
        }
    }
}

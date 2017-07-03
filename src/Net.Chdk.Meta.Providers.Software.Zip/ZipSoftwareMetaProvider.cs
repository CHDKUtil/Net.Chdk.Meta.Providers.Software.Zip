using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using Net.Chdk.Detectors.Software;
using Net.Chdk.Model.Category;
using Net.Chdk.Model.Software;
using Net.Chdk.Providers.Boot;
using Net.Chdk.Providers.Category;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Net.Chdk.Meta.Providers.Software.Zip
{
    sealed class ZipSoftwareMetaProvider : ISoftwareMetaProvider
    {
        private static readonly Version Version = new Version("1.0");

        private ILogger Logger { get; }

        private IBinarySoftwareDetector SoftwareDetector { get; }
        private IProductMetaProvider ProductProvider { get; }
        private ICameraMetaProvider CameraProvider { get; }
        private ISourceMetaProvider SourceProvider { get; }
        private IBuildMetaProvider BuildProvider { get; }
        private ICompilerMetaProvider CompilerProvider { get; }

        private string FileName { get; }
        private CategoryInfo Category { get; }

        public ZipSoftwareMetaProvider(IBinarySoftwareDetector softwareDetector, ICategoryProvider categoryProvider, IBootProviderResolver bootProviderResolver,
            IProductMetaProvider productProvider, ICameraMetaProvider cameraProvider, ISourceMetaProvider sourceProvider, IBuildMetaProvider buildProvider, ICompilerMetaProvider compilerProvider,
            ILogger<ZipSoftwareMetaProvider> logger)
        {
            Logger = logger;

            SoftwareDetector = softwareDetector;
            ProductProvider = productProvider;
            CameraProvider = cameraProvider;
            SourceProvider = sourceProvider;
            BuildProvider = buildProvider;
            CompilerProvider = compilerProvider;

            Category = categoryProvider.GetCategories().Single();
            FileName = bootProviderResolver.GetBootProvider(Category.Name).FileName;
        }

        public IEnumerable<SoftwareInfo> GetSoftware(string path)
        {
            if (!path.Contains('?') && !path.Contains('*'))
                return DoGetSoftware(path);
            var dir = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path);
            return Directory.EnumerateFiles(dir, pattern)
                .SelectMany(file => DoGetSoftware(file));
        }

        private IEnumerable<SoftwareInfo> DoGetSoftware(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open))
            {
                var name = Path.GetFileName(path);
                return GetSoftware(stream, name);
            }
        }

        private IEnumerable<SoftwareInfo> GetSoftware(Stream stream, string name)
        {
            using (var zip = new ZipFile(stream))
            {
                return GetSoftware(zip, name).ToArray();
            }
        }

        private IEnumerable<SoftwareInfo> GetSoftware(ZipFile zip, string name)
        {
            Logger.LogInformation("Enter {0}", name);
            foreach (ZipEntry entry in zip)
            {
                var items = GetSoftware(zip, entry);
                foreach (var item in items)
                    yield return item;
                yield return GetSoftware(zip, name, entry);
            }
            Logger.LogInformation("Exit {0}", name);
        }

        private IEnumerable<SoftwareInfo> GetSoftware(ZipFile zip, ZipEntry entry)
        {
            if (!entry.IsFile)
                return Enumerable.Empty<SoftwareInfo>();

            var ext = Path.GetExtension(entry.Name);
            if (!".zip".Equals(ext, StringComparison.OrdinalIgnoreCase))
                return Enumerable.Empty<SoftwareInfo>();
            var name = Path.GetFileName(entry.Name);

            using (var stream = zip.GetInputStream(entry))
            {
                return GetSoftware(stream, name);
            }
        }

        private SoftwareInfo GetSoftware(ZipFile zip, string name, ZipEntry entry)
        {
            if (!entry.IsFile)
                return null;

            if (!FileName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                return null;

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
                software.Source = SourceProvider.GetSource(software);
                software.Build = BuildProvider.GetBuild(software);
                software.Compiler = CompilerProvider.GetCompiler(software);
            }
            else
            {
                var created = entry.DateTime.ToUniversalTime();
                software.Product = ProductProvider.GetProduct(name, created);
                software.Source = SourceProvider.GetSource(software);
                software.Build = BuildProvider.GetBuild(software);
                software.Compiler = CompilerProvider.GetCompiler(software);
                software.Camera = CameraProvider.GetCamera(name);
            }

            return software;
        }

        private void ValidateSoftware(SoftwareInfo software, ZipEntry entry, string name)
        {
            var created = entry.DateTime.ToUniversalTime();
            var product = ProductProvider.GetProduct(name, created);
            var camera = CameraProvider.GetCamera(name);

            if (!Category.Equals(software.Category))
                Logger.LogWarning("Mismatching category name: {0}", software.Category.Name);

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
    }
}

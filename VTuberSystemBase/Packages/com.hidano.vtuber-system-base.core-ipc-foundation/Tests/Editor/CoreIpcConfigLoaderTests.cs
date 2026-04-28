#nullable enable
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Configuration;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcConfigLoaderTests
    {
        private CoreIpcConfigAsset? _createdAsset;

        [TearDown]
        public void TearDown()
        {
            if (_createdAsset != null)
            {
                UnityEngine.Object.DestroyImmediate(_createdAsset);
                _createdAsset = null;
            }
        }

        [Test]
        public void Load_WithEmptyContext_ReturnsEmbeddedDefaults()
        {
            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext());

            var defaults = new CoreIpcOptions();
            Assert.AreEqual(defaults.Host, options.Host);
            Assert.AreEqual(defaults.Port, options.Port);
            Assert.AreEqual(defaults.DefaultRequestTimeout, options.DefaultRequestTimeout);
            Assert.AreEqual(defaults.MaxMessageSizeBytes, options.MaxMessageSizeBytes);
            Assert.AreEqual(defaults.LogLevel, options.LogLevel);
        }

        [Test]
        public void Load_WithResourcesAssetOnly_AppliesAssetValues()
        {
            var assetOptions = new CoreIpcOptions
            {
                Host = "10.0.0.1",
                Port = 9000,
                LogLevel = LogLevel.Debug,
            };
            _createdAsset = CoreIpcConfigAsset.Create(assetOptions);

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                ResourceLoader = () => _createdAsset,
            });

            Assert.AreEqual("10.0.0.1", options.Host);
            Assert.AreEqual(9000, options.Port);
            Assert.AreEqual(LogLevel.Debug, options.LogLevel);
        }

        [Test]
        public void Load_WithStreamingAssetsJsonOnly_OverridesDefaultsForSpecifiedFields()
        {
            const string json = @"{ ""port"": 50001, ""logLevel"": ""Warning"" }";

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                StreamingAssetsJsonProvider = () => json,
            });

            Assert.AreEqual(50001, options.Port);
            Assert.AreEqual(LogLevel.Warning, options.LogLevel);
            Assert.AreEqual("127.0.0.1", options.Host, "Unspecified field must fall through to embedded default.");
            Assert.AreEqual(1_048_576L, options.MaxMessageSizeBytes);
        }

        [Test]
        public void Load_WithAppDataJsonOnly_AppliesPortChange()
        {
            const string json = @"{ ""port"": 51234 }";

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                AppDataJsonProvider = () => json,
            });

            Assert.AreEqual(51234, options.Port,
                "%AppData% layer is the topmost overlay; port change must take effect.");
            Assert.AreEqual("127.0.0.1", options.Host);
        }

        [Test]
        public void Load_AppliesThreeLayerPrecedence_AppDataWinsThenStreamingThenResource()
        {
            _createdAsset = CoreIpcConfigAsset.Create(new CoreIpcOptions
            {
                Host = "resource.host",
                Port = 1000,
                ReconnectMaxAttempts = 11,
                LogLevel = LogLevel.Trace,
            });

            const string streamingJson = @"{ ""host"": ""streaming.host"", ""port"": 2000 }";
            const string appDataJson = @"{ ""port"": 3000 }";

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                ResourceLoader = () => _createdAsset,
                StreamingAssetsJsonProvider = () => streamingJson,
                AppDataJsonProvider = () => appDataJson,
            });

            Assert.AreEqual(3000, options.Port, "AppData layer wins on port.");
            Assert.AreEqual("streaming.host", options.Host, "StreamingAssets wins over Resource for host (AppData omitted host).");
            Assert.AreEqual(11, options.ReconnectMaxAttempts, "Resource value preserved when no upper layer specifies it.");
            Assert.AreEqual(LogLevel.Trace, options.LogLevel, "Resource log level preserved when upper layers omit it.");
        }

        [Test]
        public void Load_PartialOverride_PreservesUnspecifiedFieldsFromLowerLayer()
        {
            _createdAsset = CoreIpcConfigAsset.Create(new CoreIpcOptions
            {
                Host = "base.host",
                Port = 4000,
                ReconnectMaxAttempts = 7,
                MaxMessageSizeBytes = 2_000_000,
            });

            const string appDataJson = @"{ ""host"": ""only-host-overridden"" }";

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                ResourceLoader = () => _createdAsset,
                AppDataJsonProvider = () => appDataJson,
            });

            Assert.AreEqual("only-host-overridden", options.Host);
            Assert.AreEqual(4000, options.Port);
            Assert.AreEqual(7, options.ReconnectMaxAttempts);
            Assert.AreEqual(2_000_000L, options.MaxMessageSizeBytes);
        }

        [Test]
        public void Load_WithInvalidJson_FallsBackToPriorLayerWithoutThrowing()
        {
            _createdAsset = CoreIpcConfigAsset.Create(new CoreIpcOptions { Port = 4242 });

            var options = CoreIpcConfigLoader.Load(new CoreIpcConfigLoader.LoadContext
            {
                ResourceLoader = () => _createdAsset,
                AppDataJsonProvider = () => "{ not valid json",
            });

            Assert.AreEqual(4242, options.Port,
                "Invalid AppData JSON must be ignored, leaving Resource layer values intact.");
        }

        [Test]
        public void GetDefaultAppDataPath_IncludesVTuberSystemBaseSubdirectory()
        {
            string path = CoreIpcConfigLoader.GetDefaultAppDataPath();

            StringAssert.Contains("VTuberSystemBase", path);
            StringAssert.EndsWith("core-ipc-config.json", path);
        }

        [Test]
        public void GetDefaultStreamingAssetsPath_EndsWithExpectedFileName()
        {
            string path = CoreIpcConfigLoader.GetDefaultStreamingAssetsPath();

            StringAssert.EndsWith("core-ipc-config.json", path);
            Assert.IsTrue(
                path.Replace('\\', '/').Contains(Application.streamingAssetsPath.Replace('\\', '/')),
                $"Path '{path}' should be rooted under streamingAssetsPath '{Application.streamingAssetsPath}'.");
        }

        [Test]
        public void Load_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CoreIpcConfigLoader.Load(null!));
        }
    }
}

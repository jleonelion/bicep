// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Containers.ContainerRegistry;
using Azure.Containers.ContainerRegistry.Specialized;
using Azure.Core;
using Azure.Identity;
using Bicep.Core.Modules;
using Bicep.Core.Registry.Oci;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bicep.Core.Registry
{
    public class AcrClient : IOciArtifactClient
    {
        private readonly string artifactCachePath;
        private readonly TokenCredential tokenCredential;

        public AcrClient(string artifactCachePath, TokenCredential tokenCredential)
        {
            this.artifactCachePath = artifactCachePath;
            this.tokenCredential = tokenCredential;
        }

        public async Task<OciClientResult> PullAsync(OciArtifactModuleReference reference)
        {
            string modulePath = GetLocalPackageDirectory(reference);

            // ensure that the directory exists
            try
            {
                Directory.CreateDirectory(modulePath);
            }
            catch(Exception exception)
            {
                return new(false, exception.Message);
            }

            try
            {
                var registryUri = new Uri($"https://{reference.Registry}");

                var client = new ContainerRegistryClient(registryUri, tokenCredential);
                string digest = await ResolveDigest(client, reference);
                
                var blobClient = new ContainerRegistryArtifactBlobClient(registryUri, tokenCredential, reference.Repository);
                await PullDigest(blobClient, digest, modulePath);

                return new(true, null);
            }
            catch(OciException exception)
            {
                // we can trust the message in our own exception
                return new(false, exception.Message);
            }
            catch(Exception exception)
            {
                return new(false, $"Unhandled exception: {exception}");
            }
        }

        private static async Task<string> ResolveDigest(ContainerRegistryClient client, OciArtifactModuleReference reference)
        {
            var artifact = client.GetArtifact(reference.Repository, reference.Tag);

            var manifestProperties = await artifact.GetManifestPropertiesAsync();

            return manifestProperties.Value.Digest;
        }

        private static async Task PullDigest(ContainerRegistryArtifactBlobClient client, string digest, string modulePath)
        {
            var manifestResult = await client.DownloadManifestAsync(digest);

            // the SDK doesn't expose all the manifest properties we need
            var manifest = DeserializeManifest(manifestResult.Value.Content);

            foreach(var layer in manifest.Layers)
            {
                var fileName = layer.Annotations.TryGetValue("org.opencontainers.image.title", out var title) ? title : TrimSha(layer.Digest);

                var layerPath = Path.Combine(modulePath, fileName) ?? throw new InvalidOperationException("Combined artifact path is null.");

                var blobResult = await client.DownloadBlobAsync(layer.Digest);

                using var fileStream = new FileStream(layerPath, FileMode.Create);
                await blobResult.Value.Content.CopyToAsync(fileStream);
            }
        }

        private static OciManifest DeserializeManifest(Stream stream)
        {
            using var streamReader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
            using var reader = new JsonTextReader(streamReader);

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            });

            var manifest = serializer.Deserialize<OciManifest>(reader);
            if (manifest is not null)
            {
                return manifest;
            }

            throw new InvalidOperationException("Unable to deserialize artifact manifest content.");
        }

        public string GetLocalPackageDirectory(OciArtifactModuleReference reference)
        {
            var baseDirectories = new[]
            {
                this.artifactCachePath,
                reference.Registry
            };

            // TODO: Directory convention problematic. /foo/bar:baz and /foo:bar will share directories
            var directories = baseDirectories
                .Concat(reference.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries))
                .Append(reference.Tag)
                .ToArray();

            return Path.Combine(directories);
        }

        public string GetLocalPackageEntryPointPath(OciArtifactModuleReference reference) => Path.Combine(this.GetLocalPackageDirectory(reference), "main.bicep");

        private static string TrimSha(string digest)
        {
            int index = digest.IndexOf(':');
            if (index > -1)
            {
                return digest.Substring(index + 1);
            }

            return digest;
        }

        private class OciException : Exception
        {
            public OciException(string message) : base(message)
            {
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Containers.ContainerRegistry;
using Azure.Containers.ContainerRegistry.Specialized;
using Azure.Core;
using Bicep.Core.Modules;
using Bicep.Core.Registry.Oci;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public async Task<OciClientResult> PullAsync(OciArtifactModuleReference moduleReference)
        {
            try
            {
                var client = this.CreateClient(moduleReference);
                string digest = await ResolveDigest(client, moduleReference);

                string modulePath = GetLocalPackageDirectory(moduleReference);
                CreateModuleDirectory(modulePath);

                var blobClient = this.CreateBlobClient(moduleReference);
                await PullDigest(blobClient, digest, modulePath);

                return new(true, null);
            }
            catch(RequestFailedException exception) when (exception.Status == 404)
            {
                return new(false, "Module not found.");
            }
            catch(AcrClientException exception)
            {
                // we can trust the message in our own exception
                return new(false, exception.Message);
            }
            catch(Exception exception)
            {
                return new(false, $"Unhandled exception: {exception}");
            }
        }

        public async Task PushArtifactAsync(OciArtifactModuleReference moduleReference, StreamDescriptor config, params StreamDescriptor[] layers)
        {
            // TODO: How do we choose this? Does it ever change?
            var algorithmIdentifier = DescriptorFactory.AlgorithmIdentifierSha256;

            var blobClient = this.CreateBlobClient(moduleReference);

            config.ResetStream();
            var configDescriptor = DescriptorFactory.CreateDescriptor(algorithmIdentifier, config);

            config.ResetStream();
            var configUploadResult = await blobClient.UploadBlobAsync(config.Stream);

            var layerDescriptors = new List<OciDescriptor>(layers.Length);
            foreach (var layer in layers)
            {
                layer.ResetStream();
                var layerDescriptor = DescriptorFactory.CreateDescriptor(algorithmIdentifier, layer);
                layerDescriptors.Add(layerDescriptor);

                layer.ResetStream();
                var layerUploadResult = await blobClient.UploadBlobAsync(layer.Stream);
            }

            var manifest = new OciManifest(2, configDescriptor, layerDescriptors);
            using var manifestStream = new MemoryStream();
            OciManifestSerialization.SerializeManifest(manifestStream, manifest);

            manifestStream.Position = 0;
            var manifestDigest = DescriptorFactory.ComputeDigest(algorithmIdentifier, manifestStream);

            manifestStream.Position = 0;
            // BUG: the client closes the stream :(
            var manifestUploadResult = await blobClient.UploadManifestAsync(manifestStream, new UploadManifestOptions(ManifestMediaType.OciManifestV1));
            
            //var client = this.CreateClient(moduleReference);
            //var manifestArtifact = client.GetArtifact(moduleReference.Repository, manifestDigest);

            //var tagUpdateResult = await manifestArtifact.UpdateTagPropertiesAsync(moduleReference.Tag, new ArtifactTagProperties { Digest = manifestDigest });
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

        private static Uri GetRegistryUri(OciArtifactModuleReference moduleReference) => new Uri($"https://{moduleReference.Registry}");

        private ContainerRegistryClient CreateClient(OciArtifactModuleReference moduleReference) => new(GetRegistryUri(moduleReference), this.tokenCredential);

        private ContainerRegistryArtifactBlobClient CreateBlobClient(OciArtifactModuleReference moduleReference) => new(GetRegistryUri(moduleReference), this.tokenCredential, moduleReference.Repository);

        private static string TrimSha(string digest)
        {
            int index = digest.IndexOf(':');
            if (index > -1)
            {
                return digest.Substring(index + 1);
            }

            return digest;
        }

        private static void CreateModuleDirectory(string modulePath)
        {
            try
            {
                // ensure that the directory exists
                Directory.CreateDirectory(modulePath);
            }
            catch (Exception exception)
            {
                throw new AcrClientException("Unable to create the local module directory.", exception);
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
            var manifest = OciManifestSerialization.DeserializeManifest(manifestResult.Value.Content);

            foreach (var layer in manifest.Layers)
            {
                var fileName = layer.Annotations.TryGetValue("org.opencontainers.image.title", out var title) ? title : TrimSha(layer.Digest);

                var layerPath = Path.Combine(modulePath, fileName) ?? throw new InvalidOperationException("Combined artifact path is null.");

                var blobResult = await client.DownloadBlobAsync(layer.Digest);

                using var fileStream = new FileStream(layerPath, FileMode.Create);
                await blobResult.Value.Content.CopyToAsync(fileStream);
            }
        }

        private class AcrClientException : Exception
        {
            public AcrClientException(string message) : base(message)
            {
            }

            public AcrClientException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}

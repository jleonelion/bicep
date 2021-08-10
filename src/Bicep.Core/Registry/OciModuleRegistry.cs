// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Identity;
using Bicep.Core.Diagnostics;
using Bicep.Core.FileSystem;
using Bicep.Core.Modules;
using Bicep.Core.Registry.Oci;
using Bicep.Core.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Bicep.Core.Registry
{
    public class OciModuleRegistry : IModuleRegistry
    {
        private readonly IFileResolver fileResolver;

        private readonly IOciArtifactClient client;

        public OciModuleRegistry(IFileResolver fileResolver)
        {
            this.fileResolver = fileResolver;
            //this.client = new OrasClient(GetArtifactCachePath());
            this.client = new AcrClient(GetArtifactCachePath(), new DefaultAzureCredential());
        }

        public string Scheme => ModuleReferenceSchemes.Oci;

        public RegistryCapabilities Capabilities => RegistryCapabilities.Publish;

        public ModuleReference? TryParseModuleReference(string reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder) => OciArtifactModuleReference.TryParse(reference, out failureBuilder);

        public bool IsModuleRestoreRequired(ModuleReference reference)
        {
            // TODO: This may need to be updated to account for concurrent processes updating the local cache
            var typed = ConvertReference(reference);

            // if module is missing, it requires init
            return !this.fileResolver.FileExists(GetEntryPointUri(typed));
        }

        public Uri? TryGetLocalModuleEntryPointPath(Uri parentModuleUri, ModuleReference reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var typed = ConvertReference(reference);
            failureBuilder = null;
            return GetEntryPointUri(typed);
        }

        public async Task<IDictionary<ModuleReference, DiagnosticBuilder.ErrorBuilderDelegate>> RestoreModules(IEnumerable<ModuleReference> references)
        {
            var statuses = new Dictionary<ModuleReference, DiagnosticBuilder.ErrorBuilderDelegate>();
            foreach(var reference in references.OfType<OciArtifactModuleReference>())
            {
                using (var timer = new ExecutionTimer($"Restore module {reference.FullyQualifiedReference}"))
                {
                    var result = await this.client.PullAsync(reference);

                    if (!result.Success)
                    {
                        if (result.ErrorMessage is not null)
                        {
                            statuses.Add(reference, x => x.ModuleRestoreFailedWithMessage(reference.FullyQualifiedReference, result.ErrorMessage));
                            timer.OnFail(result.ErrorMessage);
                        }
                        else
                        {
                            statuses.Add(reference, x => x.ModuleRestoreFailed(reference.FullyQualifiedReference));
                            timer.OnFail();
                        }
                    }
                }
            }

            return statuses;
        }

        public Task PublishModule(ModuleReference moduleReference, string moduleDirectory)
        {
            //var typed = ConvertReference(moduleReference);

            //var config = new OciBlob()

            //var manifest = new OciManifest(2, )

            return Task.CompletedTask;
        }

        private static string GetArtifactCachePath()
        {
            // TODO: Will NOT work if user profile is not loaded on Windows! (Az functions load exes like that)
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(basePath, ".bicep", "artifacts");
        }

        private Uri GetEntryPointUri(OciArtifactModuleReference reference)
        {
            string localArtifactPath = this.client.GetLocalPackageEntryPointPath(reference);
            if (Uri.TryCreate(localArtifactPath, UriKind.Absolute, out var uri))
            {
                return uri;
            }

            throw new NotImplementedException($"Local OCI artifact path is malformed: \"{localArtifactPath}\"");
        }

        private static OciArtifactModuleReference ConvertReference(ModuleReference reference)
        {
            if(reference is OciArtifactModuleReference typed)
            {
                return typed;
            }

            throw new ArgumentException($"Reference type '{reference.GetType().Name}' is not supported.");
        }
    }
}
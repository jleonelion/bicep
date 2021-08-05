// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Modules;
using System.Threading.Tasks;

namespace Bicep.Core.Registry
{
    public interface IOciArtifactClient
    {
        Task<OciClientResult> PullAsync(OciArtifactModuleReference reference);

        string GetLocalPackageDirectory(OciArtifactModuleReference reference);

        string GetLocalPackageEntryPointPath(OciArtifactModuleReference reference);
    }
}

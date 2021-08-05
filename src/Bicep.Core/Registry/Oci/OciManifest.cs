// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Bicep.Core.Registry.Oci
{
    public class OciManifest
    {
        public OciManifest(int schemaVersion, OciBlob config, IEnumerable<OciBlob> layers)
        {
            this.SchemaVersion = schemaVersion;
            this.Config = config;
            this.Layers = layers.ToImmutableArray();
        }

        public int SchemaVersion { get; }

        public OciBlob Config { get; }

        public ImmutableArray<OciBlob> Layers { get; }
    }
}

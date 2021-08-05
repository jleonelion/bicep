// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Bicep.Core.Registry.Oci
{
    public class OciBlob
    {
        public OciBlob(string mediaType, string digest, long size, IDictionary<string, string>? annotations)
        {
            this.MediaType = mediaType;
            this.Digest = digest;
            this.Size = size;
            this.Annotations = annotations?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty;
        }

        public string MediaType { get; }

        public string Digest { get; }

        public long Size { get; }

        public ImmutableDictionary<string, string> Annotations { get; }
    }
}

using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

#pragma warning disable SKEXP0001

namespace SemanticKernel.ConsoleApp.Models
{
    class VectorModel
    {
        [VectorStoreRecordKey]
        [TextSearchResultName]
        public Guid Key { get; init; }

        [VectorStoreRecordData]
        [TextSearchResultValue]
        public required string Text { get; init; }

        [VectorStoreRecordVector(3072)]
        public ReadOnlyMemory<float> Embedding { get; init; }
    }
}
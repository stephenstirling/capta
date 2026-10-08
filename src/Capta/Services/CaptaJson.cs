using System.Text.Json.Serialization;

namespace Capta.Services;

/// <summary>Source-generated JSON, so serialisation survives trimming in Release packages.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CaptureMetadata))]
[JsonSerializable(typeof(List<CaptureHistory.Entry>))]
internal sealed partial class CaptaJson : JsonSerializerContext;

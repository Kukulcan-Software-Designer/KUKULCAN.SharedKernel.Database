using KUKULCAN.SharedKernel.Identifiers;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demo identifier used by the console client.</summary>
public sealed class ClientEntityId(Guid value) : GuidEntityId(value);

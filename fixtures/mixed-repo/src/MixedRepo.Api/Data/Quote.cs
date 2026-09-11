namespace MixedRepo.Api.Data;

public sealed record Quote(int Id, int TenantId, string Customer, decimal Total, string Status);

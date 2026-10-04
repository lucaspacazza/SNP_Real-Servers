namespace DataCenterAutomatorServers;

// ServerID identifies racked equipment. coopLooseId identifies physical items,
// including many chassis overlapping on the trolley.
internal sealed record ServerIdentityRecord(string ServerId, string LooseId, int RackPositionUid, string VariantId);

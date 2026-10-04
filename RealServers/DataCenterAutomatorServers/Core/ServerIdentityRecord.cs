namespace DataCenterAutomatorServers;

// ServerID identifies racked equipment. coopLooseId identifies physical items,
// including many chassis overlapping on the trolley.
internal sealed record ServerIdentityRecord(string ServerId, string LooseId, int RackPositionUid, string VariantId)
{
    // Schema 1 used zero for both unknown and loose state. It is never evidence
    // for clearing a native rack reservation. Schema 2 uses only -1 for an
    // explicitly captured loose/held/trolley object; positive values are UIDs.
    internal const int UnknownRackPositionUid = 0;
    internal const int LooseRackPositionUid = -1;
}

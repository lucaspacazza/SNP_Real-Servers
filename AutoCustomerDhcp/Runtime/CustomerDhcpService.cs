using Il2Cpp;

namespace AutoCustomerDhcp;

internal static class CustomerDhcpService
{
    internal static string Assign(Server server)
    {
        if (server == null)
            return "DHCP: servidor indisponível.";

        var map = NetworkMap.instance;
        if (map == null)
            return "DHCP: rede ainda não está disponível.";

        var customerId = server.GetCustomerID();
        var customer = map.GetCustomerBase(customerId);
        if (customer == null)
            return "DHCP: selecione um cliente com contrato ativo.";

        var usableAddresses = customer.usableIpsPerApp;
        var appTypes = customer.appIdToServerType;
        if (usableAddresses == null || appTypes == null)
            return "DHCP: as faixas do cliente ainda não estão disponíveis.";

        var pools = new List<AddressPool>();
        foreach (var entry in usableAddresses)
        {
            if (!appTypes.TryGetValue(entry.Key, out var serverType) || entry.Value == null)
                continue;

            var addresses = new List<string>();
            foreach (var address in entry.Value)
                addresses.Add(address);
            pools.Add(new AddressPool(entry.Key, serverType, addresses));
        }

        // Avoid running the native validator (which also displays notifications)
        // for addresses that are already occupied by another registered endpoint.
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = map.servers;
        if (endpoints != null)
        {
            foreach (var entry in endpoints)
            {
                var endpoint = entry.Value;
                if (endpoint != null && endpoint.ServerID != server.ServerID && !string.IsNullOrEmpty(endpoint.IP))
                    occupied.Add(endpoint.IP);
            }
        }

        var choice = AddressAllocator.Choose(pools, server.serverType, server.appID, server.IP,
            address => occupied.Contains(address) || map.IsIpAddressDuplicate(address, server));
        if (choice == null)
            return "DHCP: nenhum IP livre compatível com este servidor no cliente selecionado. IP anterior mantido.";

        if (!string.Equals(server.IP, choice.Address, StringComparison.Ordinal))
            server.SetIP(choice.Address);
        // Repaint the application logo even if two customers use the same app ID.
        server.UpdateAppID(choice.AppId);

        return $"DHCP: cliente {customerId} — IP {choice.Address}.";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoCustomerDhcp;

public sealed record AddressPool(int AppId, int ServerType, IReadOnlyList<string> Addresses);

public sealed record AddressChoice(string Address, int AppId);

/// <summary>Chooses from usable addresses supplied by the game's own subnet calculation.</summary>
public static class AddressAllocator
{
    public static AddressChoice? Choose(
        IReadOnlyList<AddressPool> pools,
        int serverType,
        int preferredAppId,
        string? currentIp,
        Func<string, bool> isOccupied)
    {
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(isOccupied);

        var compatible = pools
            .Where(pool => pool is not null && pool.ServerType == serverType && pool.Addresses is not null)
            .OrderBy(pool => pool.AppId == preferredAppId ? 0 : 1)
            .ThenBy(pool => pool.AppId)
            .ToArray();

        // Preserve a valid assignment when reopening or reselecting a customer.
        if (IsValidAddress(currentIp))
        {
            foreach (var pool in compatible)
            {
                if (pool.Addresses.Contains(currentIp!, StringComparer.Ordinal))
                {
                    if (!isOccupied(currentIp!))
                        return new AddressChoice(currentIp!, pool.AppId);
                    break;
                }
            }
        }

        foreach (var pool in compatible)
        {
            foreach (var address in pool.Addresses)
            {
                if (IsValidAddress(address) && !isOccupied(address))
                    return new AddressChoice(address, pool.AppId);
            }
        }

        return null;
    }

    private static bool IsValidAddress(string? address)
    {
        if (string.IsNullOrEmpty(address) || address == "0.0.0.0")
            return false;

        var octets = address.Split('.');
        if (octets.Length != 4)
            return false;

        foreach (var octet in octets)
        {
            if (octet.Length is < 1 or > 3 || (octet.Length > 1 && octet[0] == '0'))
                return false;

            var value = 0;
            foreach (var digit in octet)
            {
                if (digit is < '0' or > '9')
                    return false;
                value = (value * 10) + digit - '0';
            }

            if (value > 255)
                return false;
        }

        return true;
    }
}

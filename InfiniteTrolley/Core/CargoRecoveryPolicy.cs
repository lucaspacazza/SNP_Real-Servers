namespace InfiniteTrolley;

/// <summary>Eligibility for one native save-loading recovery window.</summary>
public static class CargoRecoveryPolicy
{
    /// <summary>
    /// Excludes objects already held or racked, objects containing the cart,
    /// and boxed or nested cargo that must remain attached to its container.
    /// The adapter supplies membership of the cart's actual trigger volume.
    /// </summary>
    public static bool ShouldRecover(bool inHands, bool inRack, bool trolleyIsInsideItem,
        bool associatedWithTrolley, bool insideLoadVolume, bool nestedCargo = false)
    {
        if (inHands || inRack || trolleyIsInsideItem || nestedCargo) return false;
        return associatedWithTrolley || insideLoadVolume;
    }
}

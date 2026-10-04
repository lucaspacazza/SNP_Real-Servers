using System;
using MelonLoader;
using UnityEngine;
using NativeSaveSystem = Il2Cpp.SaveSystem;
using System.IO;

namespace DataCenterAutomatorServers;

public sealed class DataCenterAutomatorServersMod : MelonMod
{
	private static class ModHolder
	{
		internal static DataCenterAutomatorServersMod? Instance;
	}

	private CatalogInjector? _injector;

	private SaveLifecycleCoordinator<ServerIdentityRecord[]>? _saveLifecycle;
	private NativeSaveSystem.OnSavingData? _savingHandler;
	private NativeSaveSystem.OnLoadingDataLater? _loadedHandler;
	private bool _pendingPersistenceRecovery;
	private int _recoverAfterFrame;

	public override void OnInitializeMelon()
	{
		ModHolder.Instance = this;
		Log.Info("Initializing.");
		_injector = new CatalogInjector();
		_saveLifecycle = new SaveLifecycleCoordinator<ServerIdentityRecord[]>(
			NativeSaveDirectory,
			() => _injector.CapturePersistenceSnapshot(),
			(raw, file, records) => _injector.CommitPersistenceSnapshot(raw, file, records),
			(raw, file) => _injector.BeginPersistenceLoad(raw, file),
			() => _injector.CompletePersistenceLoad(),
			name => NativeSaveSystem.GetRawSaveEntry(name));
		try
		{
			PatchRegistry.Apply(((MelonBase)this).HarmonyInstance);
			SubscribeSaveEvents();
		}
		catch
		{
			((MelonBase)this).HarmonyInstance.UnpatchSelf();
			throw;
		}
	}

	private static string NativeSaveDirectory()
	{
		string directory = NativeSaveSystem.saveDirPath;
		if (!string.IsNullOrWhiteSpace(directory))
			return directory;
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine(Path.GetDirectoryName(localAppData) ?? localAppData, "LocalLow", "Waseku", "Data Center", "saves");
	}

	private void SubscribeSaveEvents()
	{
		_savingHandler = (Action)(() =>
		{
			try { _saveLifecycle?.CaptureOutsideTransaction(); }
			catch (Exception error) { Log.Error("Could not capture exact server identities before native saving.", error); }
		});
		_loadedHandler = (Action)(() =>
		{
			_pendingPersistenceRecovery = true;
			_recoverAfterFrame = Time.frameCount + 2;
		});
		NativeSaveSystem.onSavingData += _savingHandler;
		NativeSaveSystem.onLoadingDataLater += _loadedHandler;
	}

	public override void OnDeinitializeMelon()
	{
		if (_savingHandler != null)
			NativeSaveSystem.onSavingData -= _savingHandler;
		if (_loadedHandler != null)
			NativeSaveSystem.onLoadingDataLater -= _loadedHandler;
		_pendingPersistenceRecovery = false;
		if (ModHolder.Instance == this)
			ModHolder.Instance = null;
	}

	internal static SaveTransaction<ServerIdentityRecord[]>? PrepareNativeSave(string? name)
	{
		try { return ModHolder.Instance?._saveLifecycle?.PrepareSave(name); }
		catch (Exception error)
		{
			Log.Error("Could not prepare server identity snapshot; native saving will continue.", error);
			return null;
		}
	}

	internal static void CompleteNativeSave(SaveTransaction<ServerIdentityRecord[]>? state)
	{
		try
		{
			if (state != null && ModHolder.Instance?._saveLifecycle?.CompleteSave(state) != true)
				Log.Warning("The native save did not expose an unambiguous changed file; no server identity snapshot was attached.");
		}
		catch (Exception error) { Log.Error("Could not persist RealServers identity metadata after native saving.", error); }
	}

	internal static void CancelNativeSave(SaveTransaction<ServerIdentityRecord[]>? state) =>
		ModHolder.Instance?._saveLifecycle?.CancelSave(state);

	internal static void BeginNativeLoad(string? name)
	{
		var mod = ModHolder.Instance;
		if (mod == null)
			return;
		mod._pendingPersistenceRecovery = false;
		try { mod._saveLifecycle?.BeginLoad(name); }
		catch (Exception error)
		{
			// A failed lookup must not leak the previous world's identities.
			mod._injector?.BeginPersistenceLoad(string.Empty, string.Empty);
			Log.Error("Could not select the saved server identities for this world.", error);
		}
	}

	internal static void RestoreLoadedObject(object? instance, object? data)
	{
		if (instance == null || data == null)
			return;
		try { ModHolder.Instance?._injector?.RestoreLoadedObjectIdentity(instance, data); }
		catch (Exception error) { Log.Error("Could not restore an exact saved server identity.", error); }
	}

	internal static void AbortNativeLoad()
	{
		var mod = ModHolder.Instance;
		if (mod == null)
			return;
		mod._pendingPersistenceRecovery = false;
		mod._injector?.AbortPersistenceLoad();
	}

	internal static void ResetNativeWorld()
	{
		var mod = ModHolder.Instance;
		if (mod == null)
			return;
		mod._pendingPersistenceRecovery = false;
		mod._injector?.ResetPersistenceForNewWorld();
	}

	public override void OnSceneWasLoaded(int buildIndex, string sceneName)
	{
		Log.Info($"Scene loaded: {sceneName} ({buildIndex}).");
		RuntimeGltfVisualLoader.BeginScene();
		_injector?.ResetRuntimeAttemptState();
		_injector?.BeginKnownServerRepairWindow();
	}

	public override void OnUpdate()
	{
		if (_pendingPersistenceRecovery && Time.frameCount >= _recoverAfterFrame
			&& (_injector?.CanCompletePersistenceLoad() ?? false))
		{
			try
			{
				_saveLifecycle?.CompleteLoad();
				_pendingPersistenceRecovery = false;
			}
			catch (Exception error)
			{
				_pendingPersistenceRecovery = false;
				_injector?.AbortPersistenceLoad();
				Log.Error("Could not complete exact server identity recovery.", error);
			}
		}
		if (_injector != null)
		{
			_injector.MaintainSpawnedVariants("scheduled-update");
			_injector.MaintainLenovoConfigurationPanelInput();
		}
	}

	internal static void TryRegisterFromPatch(string source, object? instance)
	{
		try
		{
			ModHolder.Instance?._injector?.TryRegisterAll(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered registration failed from " + source + ".", exception);
		}
	}

	internal static void TryRefreshShopItemFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryRefreshShopItemText(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered shop text refresh failed from " + source + ".", exception);
		}
	}

	internal static void TryClearPendingPurchasesFromPatch(string source)
	{
		try
		{
			ModHolder.Instance?._injector?.ClearPendingPurchases(source);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered pending purchase clear failed from " + source + ".", exception);
		}
	}

	internal static void TryConfigureSpawnedCartItemFromPatch(string source, object? instance, int uid)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryConfigureSpawnedCartItem(source, instance, uid);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered spawned item configuration failed from " + source + ".", exception);
		}
	}

	internal static void TryTrackShopPurchaseFromPatch(string source, object? instance, int itemId, int price, object? itemType, string? displayName)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryTrackShopPurchase(source, instance, itemId, price, itemType, displayName);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered shop purchase tracking failed from " + source + ".", exception);
		}
	}

	internal static void TryRewriteShopPurchaseDisplayNameFromPatch(string source, int itemId, int price, object? itemType, ref string displayName)
	{
		try
		{
			ModHolder.Instance?._injector?.TryRewriteShopPurchaseDisplayName(source, itemId, price, itemType, ref displayName);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered shop purchase display name rewrite failed from " + source + ".", exception);
		}
	}

	internal static void PrepareShopPurchaseFromPatch(string source, ref int itemId, int price, object? itemType, ref string displayName)
	{
		int originalId = itemId;
		string originalName = displayName;
		try { ModHolder.Instance?._injector?.PrepareShopPurchase(source, ref itemId, price, itemType, ref displayName); }
		catch (Exception error)
		{
			itemId = originalId;
			displayName = originalName;
			Log.Error("Could not prepare the exact cart item for " + source + ".", error);
		}
	}

	internal static ServerVariantSpec? PreparePurchasePrefabFromPatch(string source, object? shop, ref int itemId, object? itemType)
	{
		ServerVariantSpec? expected = ServerVariantSpec.FindByPurchaseItemId(itemId);
		try { return ModHolder.Instance?._injector?.PreparePurchasePrefab(source, shop, ref itemId, itemType); }
		catch (Exception error)
		{
			// A custom shop ID is not an index in the native carrier catalog.
			if (expected != null) itemId = expected.BaseItemId;
			Log.Error("Could not prepare the exact checkout carrier for " + source + ".", error);
			return expected;
		}
	}

	internal static GameObject? FinalizePurchasePrefabFromPatch(string source, object? shop, GameObject? prefab, ServerVariantSpec? spec)
	{
		try { return ModHolder.Instance?._injector?.FinalizePurchasePrefab(source, shop, prefab, spec); }
		catch (Exception error) { Log.Error("Could not finalize the checkout carrier for " + source + ".", error); return null; }
	}

	internal static PurchaseSpawnState? BeginPhysicalSpawnFromPatch(string source, object? shop, GameObject? prefab, int price, object? itemType)
	{
		try { return ModHolder.Instance?._injector?.BeginPhysicalSpawn(source, shop, prefab, price, itemType); }
		catch (Exception error) { Log.Error("Could not capture exact physical spawn state for " + source + ".", error); return null; }
	}

	internal static void CompletePhysicalSpawnFromPatch(string source, object? shop, object? result, PurchaseSpawnState? state)
	{
		try { ModHolder.Instance?._injector?.CompletePhysicalSpawn(source, shop, result, state); }
		catch (Exception error) { Log.Error("Could not configure the newly purchased server from " + source + ".", error); }
	}

	internal static void EndPurchaseCheckoutFromPatch(object? shop)
	{
		try { ModHolder.Instance?._injector?.EndPurchaseCheckout(shop); }
		catch (Exception error) { Log.Error("Could not clear checkout identity context.", error); }
	}

	internal static PurchaseCheckoutState? BeginPurchaseCheckoutFromPatch(string source, object? shop)
	{
		try { return ModHolder.Instance?._injector?.BeginPurchaseCheckout(source, shop); }
		catch (Exception error) { Log.Error("Could not capture the purchase checkout inventory for " + source + ".", error); return null; }
	}

	internal static void CompletePurchaseCheckoutFromPatch(string source, object? shop, PurchaseCheckoutState? state)
	{
		try { ModHolder.Instance?._injector?.CompletePurchaseCheckout(source, shop, state); }
		catch (Exception error) { Log.Error("Could not configure the new purchase checkout inventory for " + source + ".", error); }
	}

	internal static void TryConfigureSpawnedPhysicalItemFromPatch(string source, object? instance, int price, object? itemType, object? result)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryConfigureSpawnedPhysicalItem(source, instance, price, itemType, result);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered physical spawn configuration failed from " + source + ".", exception);
		}
	}

	internal static GameObject? TryConfigurePurchasePrefabFromPatch(string source, object? computerShop, GameObject? prefab, int itemId, object? itemType)
	{
		try
		{
			return ModHolder.Instance?._injector?.TryConfigurePurchasePrefab(source, computerShop, prefab, itemId, itemType);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered purchase prefab visual configuration failed from " + source + ".", exception);
			return null;
		}
	}

	internal static void TryConfigureInitializedCartItemFromPatch(string source, object? instance, int firstSpawnUid)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryConfigureInitializedCartItem(source, instance, firstSpawnUid);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered initialized cart item configuration failed from " + source + ".", exception);
		}
	}

	internal static void TryFinalizeInsertedServerFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryFinalizeInsertedServer(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered inserted server finalization failed from " + source + ".", exception);
		}
	}

	internal static void TryRepairKnownPersistedServerFromPatch(string source, object? instance, bool forceDisplayRefresh)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryRepairKnownPersistedServer(source, instance, forceDisplayRefresh);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered persisted server repair failed from " + source + ".", exception);
		}
	}

	internal static void TryPersistKnownServerIdentityFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.PersistKnownServerIdentity(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered modded server identity persistence failed from " + source + ".", exception);
		}
	}

	internal static void TryPrepareLoadedCableFromPatch(string source, object? cableData)
	{
		if (cableData == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.PrepareLoadedCable(source, cableData);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered loaded cable preparation failed from " + source + ".", exception);
		}
	}

	internal static void TryFinalizeLoadedCableFromPatch(string source, object? cableData)
	{
		if (cableData == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.FinalizeLoadedCable(source, cableData);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered loaded cable finalization failed from " + source + ".", exception);
		}
	}

	internal static void TryRestoreLoadedServerRackFromPatch(string source, object? instance, object? serverSaveData)
	{
		if (instance == null || serverSaveData == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TrackLoadedServerRackState(source, instance, serverSaveData);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered loaded server rack restoration failed from " + source + ".", exception);
		}
	}

	internal static void RepairLoadedRackStateBeforeAssetManagement(string source)
	{
		try
		{
			ModHolder.Instance?._injector?.RepairLoadedRackState(source, force: true);
		}
		catch (Exception exception)
		{
			Log.Error("Rack restoration before Asset Management failed from " + source + ".", exception);
		}
	}

	internal static void TryConfigurePendingLifecycleServerFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryConfigurePendingLifecycleServer(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered pending lifecycle server configuration failed from " + source + ".", exception);
		}
	}

	internal static bool ShouldAllowCableLinkInteractionFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return true;
		}
		try
		{
			return (ModHolder.Instance?._injector?.ShouldAllowCableLinkInteraction(source, instance)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered cable-link validation failed from " + source + ".", exception);
			return true;
		}
	}

	internal static void TrySyncPowerStateFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TrySyncPowerState(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered power-state sync failed from " + source + ".", exception);
		}
	}

	internal static bool ShouldAllowPowerButtonInteractionFromPatch(string source, object? instance, bool forceState)
	{
		if (instance == null)
		{
			return true;
		}
		try
		{
			return (ModHolder.Instance?._injector?.ShouldAllowPowerButtonInteraction(source, instance, forceState)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered power-button validation failed from " + source + ".", exception);
			return true;
		}
	}

	internal static bool TryHandleLenovoConfigInteractionFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return true;
		}
		try
		{
			return (ModHolder.Instance?._injector?.TryHandleLenovoConfigInteraction(source, instance)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo IP configuration interaction failed from " + source + ".", exception);
			return true;
		}
	}

	internal static bool TryHandleLenovoConfigButtonInteractionFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return true;
		}
		try
		{
			return (ModHolder.Instance?._injector?.TryHandleLenovoConfigButtonInteraction(source, instance)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo configuration button interaction failed from " + source + ".", exception);
			return true;
		}
	}

	internal static void TryConfigurePowerControlsFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TryConfigurePowerControls(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered power-control configuration failed from " + source + ".", exception);
		}
	}

	internal static void TryPrepareLenovoMoveToHandFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.PrepareLenovoMoveToHand(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo MoveToHand preparation failed from " + source + ".", exception);
		}
	}

	internal static void TryPrepareLenovoRackInsertionFromPatch(string source, object? instance)
	{
		try
		{
			ModHolder.Instance?._injector?.TryPrepareLenovoRackInsertion(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo rack insertion preparation failed from " + source + ".", exception);
		}
	}

	internal static bool TryHandleLenovoRackPositionClickFromPatch(string source, object? instance)
	{
		try
		{
			CatalogInjector injector = ModHolder.Instance?._injector;
			return injector == null || injector.TryHandleLenovoRackPositionClick(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo rack-position insertion failed from " + source + ".", exception);
			return true;
		}
	}

	internal static void TryOverrideLenovoRackSlotSizeFromPatch(ref int sizeInU)
	{
		try
		{
			ModHolder.Instance?._injector?.TryOverrideLenovoRackSlotSize(ref sizeInU);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo rack slot-size override failed.", exception);
		}
	}

	internal static bool TryOverrideLenovoRackPositionAllowedFromPatch(object? instance, bool checkAvailability, ref bool result)
	{
		try
		{
			return (ModHolder.Instance?._injector?.TryOverrideLenovoRackPositionAllowed(instance, checkAvailability, ref result)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered Lenovo rack-position validation override failed.", exception);
			return true;
		}
	}

	internal static bool TryOverrideManualRackValidationFromPatch(object? instance, ref bool result)
	{
		try
		{
			return (ModHolder.Instance?._injector?.TryOverrideManualRackValidation(instance, ref result)).GetValueOrDefault(true);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered manual rack validation override failed.", exception);
			return true;
		}
	}

	internal static void TryTrackTechnicianRepairTargetFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.TrackTechnicianRepairTarget(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered technician repair target tracking failed from " + source + ".", exception);
		}
	}

	internal static void TryFinalizeTechnicianRepairFromPatch(string source, object? instance)
	{
		if (instance == null)
		{
			return;
		}
		try
		{
			ModHolder.Instance?._injector?.FinalizeTechnicianRepair(source, instance);
		}
		catch (Exception exception)
		{
			Log.Error("Patch-triggered technician repair finalization failed from " + source + ".", exception);
		}
	}
}

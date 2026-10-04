using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DataCenterAutomatorServers;

internal static class PatchRegistry
{
	private static bool _applied;

	public static void Apply(HarmonyLib.Harmony? harmony)
	{
		if (!_applied)
		{
			if (harmony == null)
			{
				Log.Warning("Harmony instance is missing; scheduled scans will still run.");
				return;
			}
			int num = 0;
			num += PatchPersistenceMethods(harmony);
			num += PatchTypeMethods(harmony, "ComputerShop", "InteractOnClick", "ButtonShopScreen", "CreateShopTemplate", "CreateShopButton", "CleanUpShop", "UnlockFromSave", "ButtonClear", "ButtonCancel");
			num += PatchShopItemVisualMethods(harmony, "Awake", "Start", "UpdateVisualState");
			num += PatchComputerShopPurchaseMethods(harmony, "ButtonBuyShopItem", "SpawnAllPurchasedItems");
			num += PatchComputerShopPrefabMethods(harmony, "GetPrefabForItem");
			num += PatchShopCartAddSpawnedMethods(harmony, "AddSpawnedItem");
			num += PatchServerInsertedMethods(harmony, "ServerInsertedInRack");
			num += PatchServerLifecycleRepairMethods(harmony, "Awake", "Start", "OnEnable", "OnLoadingComplete", "MoveToHand", "DropObject");
			num += PatchServerRackValidationMethods(harmony, "ValidateRackPosition");
			num += PatchAssetManagementMethods(harmony, "OnEnable");
			num += PatchRackPositionMethods(harmony, "InteractOnClick");
			num += PatchRackPositionAllowedMethods(harmony, "IsAllowedItem");
			// Modded 1U devices use the dedicated manual insertion path. Patching the
			// Rack size methods globally lets a pending modded device leak its 1U size
			// into stock Green/Yellow servers during load and normal rack operations.
			num += PatchServerPowerMethods(harmony, "PowerButton");
			num += PatchServerInteractionMethods(harmony, "InteractOnClick");
			num += PatchWorldObjectButtonMethods(harmony, "InteractOnClick");
			num += PatchCableLinkInteractionMethods(harmony, "InteractOnClick");
			num += PatchCableLoadMethods(harmony, "LoadCable");
			num += PatchTechnicianRepairMethods(harmony, "AssignJob", "RepairDevice");
			_applied = true;
			Log.Info($"Harmony registration hooks installed: {num} method(s).");
		}
	}

	private static int PatchPersistenceMethods(HarmonyLib.Harmony harmony)
	{
		Type save = ReflectionUtil.FindType("SaveSystem") ?? throw new TypeLoadException("The native SaveSystem is unavailable.");
		int count = 0;
		foreach (string name in new[] { "SaveGame", "Load", "LoadFromBytes" })
		{
			MethodInfo target = save.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.SingleOrDefault(method => method.Name == name)
				?? throw new MissingMethodException(save.FullName, name);
			if (name == "SaveGame")
				harmony.Patch(target, new HarmonyMethod(typeof(PatchRegistry), nameof(NativeSavePrefix)),
					new HarmonyMethod(typeof(PatchRegistry), nameof(NativeSavePostfix)), null,
					new HarmonyMethod(typeof(PatchRegistry), nameof(NativeSaveFinalizer)), null);
			else
				harmony.Patch(target, new HarmonyMethod(typeof(PatchRegistry),
					name == "LoadFromBytes" ? nameof(NativeBytesLoadPrefix) : nameof(NativeLoadPrefix)),
					null, null, new HarmonyMethod(typeof(PatchRegistry), nameof(NativeLoadFinalizer)), null);
			count++;
		}
		var patched = new System.Collections.Generic.HashSet<MethodInfo>();
		foreach (string typeName in new[] { "Interact", "UsableObject", "Server" })
		{
			Type type = ReflectionUtil.FindType(typeName);
			if (type == null)
				continue;
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
				.Where(method => method.Name == "OnLoad" && method.GetParameters().Length == 1))
			{
				if (patched.Add(method))
				{
					harmony.Patch(method, postfix: new HarmonyMethod(typeof(PatchRegistry), nameof(LoadedObjectPostfix)));
					count++;
				}
			}
		}
		if (patched.Count == 0)
			throw new MissingMethodException("No native object OnLoad hook was found; server identity recovery is unavailable.");
		Type menu = ReflectionUtil.FindType("MainMenu") ?? throw new TypeLoadException("The native MainMenu is unavailable.");
		MethodInfo newGame = menu.GetMethod("NewGame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			?? throw new MissingMethodException(menu.FullName, "NewGame");
		harmony.Patch(newGame, prefix: new HarmonyMethod(typeof(PatchRegistry), nameof(NewWorldPrefix)));
		count++;
		Log.Info("Exact server identity persistence hooks installed: " + count + ".");
		return count;
	}

	public static void NativeSavePrefix(string? __0, out SaveTransaction<ServerIdentityRecord[]>? __state) =>
		__state = DataCenterAutomatorServersMod.PrepareNativeSave(__0);

	public static void NativeSavePostfix(SaveTransaction<ServerIdentityRecord[]>? __state) =>
		DataCenterAutomatorServersMod.CompleteNativeSave(__state);

	public static Exception? NativeSaveFinalizer(SaveTransaction<ServerIdentityRecord[]>? __state, Exception? __exception)
	{
		if (__exception != null)
			DataCenterAutomatorServersMod.CancelNativeSave(__state);
		return __exception;
	}

	public static void NativeLoadPrefix(string? __0) => DataCenterAutomatorServersMod.BeginNativeLoad(__0);
	public static void NativeBytesLoadPrefix() => DataCenterAutomatorServersMod.BeginNativeLoad(null);
	public static Exception? NativeLoadFinalizer(Exception? __exception)
	{
		if (__exception != null)
			DataCenterAutomatorServersMod.AbortNativeLoad();
		return __exception;
	}
	public static void NewWorldPrefix() => DataCenterAutomatorServersMod.ResetNativeWorld();
	public static void LoadedObjectPostfix(object? __instance, object? __0) =>
		DataCenterAutomatorServersMod.RestoreLoadedObject(__instance, __0);

	private static int PatchTypeMethods(HarmonyLib.Harmony harmony, string typeName, params string[] methodNames)
	{
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType(typeName);
		if (type == null)
		{
			Log.Warning("Type '" + typeName + "' not available yet; scheduled scans will cover it.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "RegistrationPostfix", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("Internal postfix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, (HarmonyMethod)null, new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name}.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void RegistrationPostfix(object? __instance, MethodBase __originalMethod)
	{
		string source = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
		if (string.Equals(__originalMethod.Name, "ButtonClear", StringComparison.OrdinalIgnoreCase) || string.Equals(__originalMethod.Name, "ButtonCancel", StringComparison.OrdinalIgnoreCase))
		{
			DataCenterAutomatorServersMod.TryClearPendingPurchasesFromPatch(source);
		}
		DataCenterAutomatorServersMod.TryRegisterFromPatch(source, __instance);
	}

	private static int PatchShopItemVisualMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("ShopItem");
		if (type == null)
		{
			Log.Warning("Type 'ShopItem' not available yet; visible shop text refresh will use registration-time updates only.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "ShopItemVisualPostfix", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("Internal ShopItem visual postfix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, (HarmonyMethod)null, new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for visible text refresh.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void ShopItemVisualPostfix(object? __instance, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TryRefreshShopItemFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance);
	}

	private static int PatchComputerShopPurchaseMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("ComputerShop") ?? throw new TypeLoadException("ComputerShop is unavailable.");
		int count = 0;
		foreach (string name in methodNames)
		{
			MethodInfo target = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
				.SingleOrDefault(method => method.Name == name) ?? throw new MissingMethodException(type.FullName, name);
			HarmonyMethod prefix = null;
			HarmonyMethod postfix;
			HarmonyMethod finalizer = null;
			if (name == "ButtonBuyShopItem")
			{
				prefix = new HarmonyMethod(typeof(PatchRegistry), nameof(ComputerShopBuyPrefix));
				postfix = new HarmonyMethod(typeof(PatchRegistry), nameof(ComputerShopBuyPostfix));
			}
			else if (name == "SpawnAllPurchasedItems")
			{
				if (target.ReturnType != typeof(void)) throw new NotSupportedException("The safe checkout target must return void.");
				prefix = new HarmonyMethod(typeof(PatchRegistry), nameof(ComputerShopCheckoutPrefix));
				postfix = new HarmonyMethod(typeof(PatchRegistry), nameof(ComputerShopCheckoutPostfix));
				finalizer = new HarmonyMethod(typeof(PatchRegistry), nameof(ComputerShopCheckoutFinalizer));
			}
			else throw new MissingMethodException("Unsupported purchase lifecycle target: " + name);
			harmony.Patch(target, prefix, postfix, null, finalizer, null);
			count++;
			Log.Info($"Patched {target.DeclaringType.FullName}.{target.Name} for exact cart/checkout identity.");
		}
		return count;
	}

	public static void ComputerShopBuyPrefix(MethodBase __originalMethod, ref int itemID, int price, object itemType, ref string displayName, bool isCustomColor)
	{
		if (!isCustomColor)
		{
			DataCenterAutomatorServersMod.PrepareShopPurchaseFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, ref itemID, price, itemType, ref displayName);
		}
	}

	public static void ComputerShopBuyPostfix(object? __instance, MethodBase __originalMethod, int itemID, int price, object itemType, string displayName, bool isCustomColor)
	{
		if (!isCustomColor)
		{
			DataCenterAutomatorServersMod.TryTrackShopPurchaseFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance, itemID, price, itemType, displayName);
		}
	}

	public static void ComputerShopCheckoutPrefix(object? __instance, MethodBase __originalMethod, out PurchaseCheckoutState? __state)
	{
		__state = DataCenterAutomatorServersMod.BeginPurchaseCheckoutFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance);
	}

	public static void ComputerShopCheckoutPostfix(object? __instance, MethodBase __originalMethod, PurchaseCheckoutState? __state)
	{
		try
		{
			DataCenterAutomatorServersMod.CompletePurchaseCheckoutFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance, __state);
		}
		finally { DataCenterAutomatorServersMod.EndPurchaseCheckoutFromPatch(__instance); }
	}
	public static Exception? ComputerShopCheckoutFinalizer(object? __instance, Exception? __exception)
	{
		if (__exception != null) DataCenterAutomatorServersMod.EndPurchaseCheckoutFromPatch(__instance);
		return __exception;
	}

	private static int PatchRackPositionMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("RackPosition");
		if (type == null)
		{
			Log.Warning("Type 'RackPosition' not available yet; Lenovo rack insertion will use the normal server path.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "RackPositionInsertionPrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			Log.Error("Internal RackPosition insertion prefix method was not found.");
			return 0;
		}
		int count = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					harmony.Patch(method, new HarmonyMethod(prefixMethod), null, null, null, null);
					count++;
					Log.Info($"Patched {type.FullName}.{method.Name} for Lenovo 1U rack insertion.");
				}
				catch (Exception exception)
				{
					Log.Warning($"Could not patch {type.FullName}.{method.Name} for Lenovo 1U rack insertion: {exception.Message}");
				}
			}
		}
		return count;
	}

	private static int PatchRackSlotSizeMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("Rack");
		if (type == null)
		{
			Log.Warning("Type 'Rack' not available yet; Lenovo slot-size override will not be available in this session.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "RackSlotSizePrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			Log.Error("Internal Rack slot-size prefix method was not found.");
			return 0;
		}
		int count = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					harmony.Patch(method, new HarmonyMethod(prefixMethod), null, null, null, null);
					count++;
					Log.Info($"Patched {type.FullName}.{method.Name} for Lenovo 1U slot sizing.");
				}
				catch (Exception exception)
				{
					Log.Warning($"Could not patch {type.FullName}.{method.Name} for Lenovo 1U slot sizing: {exception.Message}");
				}
			}
		}
		return count;
	}

	private static int PatchRackPositionAllowedMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("RackPosition");
		if (type == null)
		{
			Log.Warning("Type 'RackPosition' not available yet; Lenovo 1U insertion validation will not be available in this session.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "RackPositionAllowedPrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			Log.Error("Internal RackPosition allowed-item prefix method was not found.");
			return 0;
		}
		int count = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					harmony.Patch(method, new HarmonyMethod(prefixMethod), null, null, null, null);
					count++;
					Log.Info($"Patched {type.FullName}.{method.Name} for Lenovo 1U insertion validation.");
				}
				catch (Exception exception)
				{
					Log.Warning($"Could not patch {type.FullName}.{method.Name} for Lenovo 1U insertion validation: {exception.Message}");
				}
			}
		}
		return count;
	}

	public static bool RackPositionAllowedPrefix(object? __instance, bool checkAvailability, ref bool __result)
	{
		return DataCenterAutomatorServersMod.TryOverrideLenovoRackPositionAllowedFromPatch(__instance, checkAvailability, ref __result);
	}

	public static void RackSlotSizePrefix(ref int sizeInU)
	{
		DataCenterAutomatorServersMod.TryOverrideLenovoRackSlotSizeFromPatch(ref sizeInU);
	}

	public static bool RackPositionInsertionPrefix(object? __instance, MethodBase __originalMethod)
	{
		return DataCenterAutomatorServersMod.TryHandleLenovoRackPositionClickFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	private static int PatchComputerShopPrefabMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("ComputerShop") ?? throw new TypeLoadException("ComputerShop is unavailable.");
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "ComputerShopGetPrefabPostfix", (Type[])null, (Type[])null);
		MethodInfo prefix = AccessTools.Method(typeof(PatchRegistry), "ComputerShopGetPrefabPrefix");
		if (methodInfo == null || prefix == null)
		{
			throw new MissingMethodException("Exact SKU prefab hooks are unavailable.");
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, new HarmonyMethod(prefix), new HarmonyMethod(methodInfo), null, null, null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for purchase prefab visual swap.");
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException("Exact SKU carrier mapping failed for " + item.Name, ex);
				}
			}
		}
		if (num == 0) throw new MissingMethodException(type.FullName, "GetPrefabForItem");
		return num;
	}

	public static void ComputerShopGetPrefabPrefix(object __instance, MethodBase __originalMethod, ref int itemID, object itemType, out ServerVariantSpec? __state) =>
		__state = DataCenterAutomatorServersMod.PreparePurchasePrefabFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance, ref itemID, itemType);

	public static void ComputerShopGetPrefabPostfix(object __instance, MethodBase __originalMethod, ref GameObject __result, ServerVariantSpec? __state)
	{
		GameObject? replacement = DataCenterAutomatorServersMod.FinalizePurchasePrefabFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance, __result, __state);
		if (replacement != null)
		{
			__result = replacement;
		}
	}

	private static int PatchShopCartAddSpawnedMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("ShopCartItem");
		if (type == null)
		{
			Log.Warning("Type 'ShopCartItem' not available yet; spawned server port upgrades will wait for scheduled/context hooks.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "ShopCartPostfix", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("Internal ShopCart postfix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, (HarmonyMethod)null, new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for spawned server configuration.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void ShopCartPostfix(object? __instance, MethodBase __originalMethod, int uid)
	{
		DataCenterAutomatorServersMod.TryConfigureSpawnedCartItemFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __instance, uid);
	}

	private static int PatchServerInsertedMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			Log.Warning("Type 'Server' not available yet; inserted server finalization will wait for scheduled persisted repair.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "ServerInsertedPostfix", (Type[])null, (Type[])null);
		MethodInfo prefix = AccessTools.Method(typeof(PatchRegistry), nameof(ServerInsertedPrefix));
		if (methodInfo == null)
		{
			Log.Error("Internal ServerInserted postfix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, new HarmonyMethod(prefix), new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for inserted server finalization.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void ServerInsertedPrefix(object? __instance, object? serverSaveData) =>
		DataCenterAutomatorServersMod.TryPrepareLoadedServerIdentityFromPatch(__instance, serverSaveData);

	public static void ServerInsertedPostfix(object? __instance, MethodBase __originalMethod, object? serverSaveData)
	{
		string source = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
		if (serverSaveData != null)
		{
			source += ".saved";
			DataCenterAutomatorServersMod.TryRestoreLoadedServerRackFromPatch(source, __instance, serverSaveData);
		}
		DataCenterAutomatorServersMod.TryFinalizeInsertedServerFromPatch(source, __instance);
	}

	private static int PatchAssetManagementMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("AssetManagement");
		MethodInfo prefix = AccessTools.Method(typeof(PatchRegistry), nameof(AssetManagementPrefix));
		if (type == null || prefix == null)
		{
			return 0;
		}
		int count = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				harmony.Patch(method, new HarmonyMethod(prefix), null, null, null, null);
				count++;
			}
		}
		return count;
	}

	public static void AssetManagementPrefix(MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.RepairLoadedRackStateBeforeAssetManagement(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix");
	}

	private static int PatchServerLifecycleRepairMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			Log.Warning("Type 'Server' not available yet; persisted server repair will use scheduled scans only.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "ServerLifecycleRepairPostfix", (Type[])null, (Type[])null);
		MethodInfo methodInfo2 = AccessTools.Method(typeof(PatchRegistry), "ServerMoveToHandPrefix", (Type[])null, (Type[])null);
		if (methodInfo == null || methodInfo2 == null)
		{
			Log.Error("Internal Server lifecycle repair postfix method was not found.");
			return 0;
		}
		int num = 0;
		var patchedTargets = new System.Collections.Generic.HashSet<MethodInfo>();
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo reflected in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				MethodInfo item = NativeHookTargets.ResolveDeclared(reflected);
				if (!patchedTargets.Add(item)) continue;
				try
				{
					HarmonyMethod prefix = string.Equals(item.Name, "MoveToHand", StringComparison.OrdinalIgnoreCase) ? new HarmonyMethod(methodInfo2) : null;
					harmony.Patch((MethodBase)item, prefix, new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {item.DeclaringType.FullName}.{item.Name} for persisted server repair.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void ServerLifecycleRepairPostfix(object? __instance, MethodBase __originalMethod)
	{
		__instance = ResolveServerHookInstance(__instance);
		if (__instance == null) return;
		string source = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".postfix";
		if (string.Equals(__originalMethod.Name, "MoveToHand", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(__originalMethod.Name, "DropObject", StringComparison.OrdinalIgnoreCase))
		{
			DataCenterAutomatorServersMod.TryConfigurePowerControlsFromPatch(source, __instance);
		}
		else
		{
			DataCenterAutomatorServersMod.TryConfigurePendingLifecycleServerFromPatch(source, __instance);
		}
		DataCenterAutomatorServersMod.TryPersistKnownServerIdentityFromPatch(source, __instance);
	}

	public static void ServerMoveToHandPrefix(object? __instance, MethodBase __originalMethod)
	{
		__instance = ResolveServerHookInstance(__instance);
		if (__instance == null) return;
		DataCenterAutomatorServersMod.TryPrepareLenovoMoveToHandFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	private static object? ResolveServerHookInstance(object? instance)
	{
		if (instance == null) return null;
		Type server = ReflectionUtil.FindType("Server");
		if (server == null) return null;
		return server.IsInstanceOfType(instance) ? instance : ReflectionUtil.TryCastIl2CppObject(instance, server);
	}

	private static int PatchServerPowerMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			Log.Warning("Type 'Server' not available yet; Lenovo power-light sync will wait for scheduled repair.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "ServerPowerPrefix", (Type[])null, (Type[])null);
		MethodInfo postfixMethod = AccessTools.Method(typeof(PatchRegistry), "ServerPowerPostfix", (Type[])null, (Type[])null);
		if (prefixMethod == null || postfixMethod == null)
		{
			Log.Error("Internal Server power patch method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, new HarmonyMethod(prefixMethod), new HarmonyMethod(postfixMethod), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for Lenovo power validation and light sync.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static bool ServerPowerPrefix(object? __instance, MethodBase __originalMethod, object[] __args)
	{
		bool forceState = __args != null && __args.Length > 0 && __args[0] is bool value && value;
		return DataCenterAutomatorServersMod.ShouldAllowPowerButtonInteractionFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance, forceState);
	}

	private static int PatchServerRackValidationMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "ServerRackValidationPrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			return 0;
		}
		int count = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				harmony.Patch(method, new HarmonyMethod(prefixMethod), null, null, null, null);
				count++;
			}
		}
		return count;
	}

	public static bool ServerRackValidationPrefix(object? __instance, ref bool __result)
	{
		return DataCenterAutomatorServersMod.TryOverrideManualRackValidationFromPatch(__instance, ref __result);
	}

	public static void ServerPowerPostfix(object? __instance, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TrySyncPowerStateFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".postfix", __instance);
	}

	private static int PatchServerInteractionMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			Log.Warning("Type 'Server' not available yet; Lenovo IP configuration button will not be available in this session.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "ServerInteractionPrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			Log.Error("Internal Server interaction prefix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, new HarmonyMethod(prefixMethod), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for Lenovo IP configuration interaction.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static bool ServerInteractionPrefix(object? __instance, MethodBase __originalMethod)
	{
		return DataCenterAutomatorServersMod.TryHandleLenovoConfigInteractionFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	private static int PatchWorldObjectButtonMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("WorldObjectButton");
		if (type == null)
		{
			Log.Warning("Type 'WorldObjectButton' not available yet; Lenovo configuration button will use the server interaction fallback.");
			return 0;
		}
		MethodInfo prefixMethod = AccessTools.Method(typeof(PatchRegistry), "WorldObjectButtonInteractionPrefix", (Type[])null, (Type[])null);
		if (prefixMethod == null)
		{
			Log.Error("Internal WorldObjectButton interaction prefix method was not found.");
			return 0;
		}
		int patched = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					harmony.Patch(method, new HarmonyMethod(prefixMethod));
					patched++;
					Log.Info("Patched " + type.FullName + "." + method.Name + " for Lenovo configuration button interaction.");
				}
				catch (Exception exception)
				{
					Log.Warning("Could not patch " + type.FullName + "." + method.Name + ": " + exception.Message);
				}
			}
		}
		return patched;
	}

	public static bool WorldObjectButtonInteractionPrefix(object? __instance, MethodBase __originalMethod)
	{
		return DataCenterAutomatorServersMod.TryHandleLenovoConfigButtonInteractionFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	private static int PatchCableLinkInteractionMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("CableLink");
		if (type == null)
		{
			Log.Warning("Type 'CableLink' not available yet; modded server cable-family guard will not be available in this session.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "CableLinkInteractPrefix", (Type[])null, (Type[])null);
		if (methodInfo == null)
		{
			Log.Error("Internal CableLink interaction prefix method was not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					harmony.Patch((MethodBase)item, new HarmonyMethod(methodInfo), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for modded server cable-family validation.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static bool CableLinkInteractPrefix(object? __instance, MethodBase __originalMethod)
	{
		return DataCenterAutomatorServersMod.ShouldAllowCableLinkInteractionFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	private static int PatchCableLoadMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		Type type = ReflectionUtil.FindType("CablePositions");
		MethodInfo prefix = AccessTools.Method(typeof(PatchRegistry), nameof(CableLoadPrefix));
		MethodInfo postfix = AccessTools.Method(typeof(PatchRegistry), nameof(CableLoadPostfix));
		if (type == null || prefix == null || postfix == null)
		{
			Log.Warning("Type 'CablePositions' or its Real Servers load hooks are unavailable; saved modded cables cannot be rebuilt in this session.");
			return 0;
		}
		int patched = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					harmony.Patch(method, new HarmonyMethod(prefix), new HarmonyMethod(postfix), null, null, null);
					patched++;
					Log.Info("Patched " + type.FullName + "." + method.Name + " for saved modded cable endpoint restoration.");
				}
				catch (Exception exception)
				{
					Log.Warning("Could not patch " + type.FullName + "." + method.Name + ": " + exception.Message);
				}
			}
		}
		return patched;
	}

	public static void CableLoadPrefix(object? cableData, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TryPrepareLoadedCableFromPatch(
			__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix",
			cableData);
	}

	public static void CableLoadPostfix(object? cableData, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TryFinalizeLoadedCableFromPatch(
			__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".postfix",
			cableData);
	}

	private static int PatchTechnicianRepairMethods(HarmonyLib.Harmony harmony, params string[] methodNames)
	{
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Expected O, but got Unknown
		Type type = ReflectionUtil.FindType("Technician");
		if (type == null)
		{
			Log.Warning("Type 'Technician' not available yet; modded server EOL replacement normalization will not be available in this session.");
			return 0;
		}
		MethodInfo methodInfo = AccessTools.Method(typeof(PatchRegistry), "TechnicianRepairPrefix", (Type[])null, (Type[])null);
		MethodInfo methodInfo2 = AccessTools.Method(typeof(PatchRegistry), "TechnicianRepairPostfix", (Type[])null, (Type[])null);
		if (methodInfo == null || methodInfo2 == null)
		{
			Log.Error("Internal technician repair patch methods were not found.");
			return 0;
		}
		int num = 0;
		foreach (string methodName in methodNames)
		{
			foreach (MethodInfo item in from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
				select m)
			{
				try
				{
					HarmonyMethod val = ((!string.Equals(methodName, "RepairDevice", StringComparison.OrdinalIgnoreCase)) ? ((HarmonyMethod)null) : new HarmonyMethod(methodInfo));
					harmony.Patch((MethodBase)item, val, new HarmonyMethod(methodInfo2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
					Log.Info($"Patched {type.FullName}.{item.Name} for modded server technician replacement support.");
				}
				catch (Exception ex)
				{
					Log.Warning($"Could not patch {type.FullName}.{item.Name}: {ex.Message}");
				}
			}
		}
		return num;
	}

	public static void TechnicianRepairPrefix(object? __instance, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TryTrackTechnicianRepairTargetFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".prefix", __instance);
	}

	public static void TechnicianRepairPostfix(object? __instance, MethodBase __originalMethod)
	{
		DataCenterAutomatorServersMod.TryFinalizeTechnicianRepairFromPatch(__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name + ".postfix", __instance);
	}
}

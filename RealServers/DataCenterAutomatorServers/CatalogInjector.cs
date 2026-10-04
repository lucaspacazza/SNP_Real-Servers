using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace DataCenterAutomatorServers;

internal sealed class CatalogInjector
{
	private static readonly bool VerboseDiagnostics = false;

	private const int LenovoCablePortCount = 2;

	private static readonly Vector3 LenovoCableInteractionWorldCenter = new Vector3(0f, 0f, 0.014f);

	private static readonly Vector3 LenovoCableInteractionWorldSize = new Vector3(0.06f, 0.045f, 0.06f);

	private static readonly Vector3 DellR440CableInteractionWorldSize = new Vector3(0.032f, 0.024f, 0.018f);

	// The Dell guide model has recessed RJ45 openings. This is expressed in the
	// marker's local space, before the imported model's runtime scale is applied.
	private const float DellR440PortVisualInset = 0.05f;

	private static readonly Vector3 LenovoPowerInteractionWorldSize = new Vector3(0.025f, 0.025f, 0.018f);

	private const float LenovoPowerInteractionFrontOffset = 0.012f;

	private const string LenovoIpConfigButtonName = "ButtonEdit";

	// Matches the original ButtonEdit.001 collider measured from the game's
	// Server.Green1 prefab. It is the red vertical configuration button.
	private static readonly Vector3 LenovoIpConfigButtonWorldSize = new Vector3(0.016f, 0.038f, 0.012f);

	private const float LenovoIpConfigButtonFrontOffset = 0.012f;

	// Small visual correction requested for the red ButtonEdit control. This is
	// expressed in the GLB marker parent's axes: right (+X) and up (+Y).
	private static readonly Vector3 LenovoIpConfigButtonMarkerOffset = new Vector3(0.012f, 0.012f, 0f);

	// The Blender PowerButton marker is the authoritative center of the visible
	// power control, including the small offset outside the marked panel surface.
	private static readonly Vector3 LenovoPowerButtonLocalOffset = Vector3.zero;

	private static readonly Vector3 LenovoPowerLedLocalOffset = new Vector3(0f, 0f, -0.06f);

	private static readonly Color LenovoPowerLedColor = new Color(0.1f, 1f, 0.25f, 1f);

	private static Material _lenovoPowerLedMaterial;

	private readonly HashSet<string> _registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, object> _baseItemsBySpecKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<object, object> _ownerCollectionByBaseItem = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

	private readonly Dictionary<int, ServerVariantSpec> _spawnedSpecsByUid = new Dictionary<int, ServerVariantSpec>();

	private readonly Dictionary<object, ServerVariantSpec> _pendingSpecsByServer = new Dictionary<object, ServerVariantSpec>(ReferenceEqualityComparer.Instance);

	private readonly Dictionary<object, ServerVariantSpec> _technicianRepairSpecs = new Dictionary<object, ServerVariantSpec>(ReferenceEqualityComparer.Instance);

	private readonly Queue<(ServerVariantSpec Spec, DateTime CreatedAt)> _pendingInsertionSpecs = new Queue<(ServerVariantSpec, DateTime)>();

	private readonly HashSet<object> _serversBeingRepaired = new HashSet<object>(ReferenceEqualityComparer.Instance);

	private readonly HashSet<object> _knownModdedServers = new HashSet<object>(ReferenceEqualityComparer.Instance);
	private readonly HashSet<object> _preparedPhysicalServers = new HashSet<object>(ReferenceEqualityComparer.Instance);

	private readonly HashSet<object> _pendingSceneConfiguredServers = new HashSet<object>(ReferenceEqualityComparer.Instance);

	private readonly HashSet<string> _repairedPersistedServerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, object> _loadedServersById = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<int> _loadedCablesWithModdedEndpoints = new HashSet<int>();

	private readonly HashSet<int> _pendingLoadedCableRedrawIds = new HashSet<int>();

	private readonly HashSet<string> _loggedUnknownCableFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<object, ServerVariantSpec> _lenovoPowerControlSpecsByServer = new Dictionary<object, ServerVariantSpec>(ReferenceEqualityComparer.Instance);

	private readonly Dictionary<object, GameObject> _lenovoPowerButtonObjectsByServer = new Dictionary<object, GameObject>(ReferenceEqualityComparer.Instance);

	private readonly Dictionary<object, object> _lenovoMountedRackPositionsByServer = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

	private readonly Dictionary<object, ManualRackMount> _manualRackMountsByServer = new Dictionary<object, ManualRackMount>(ReferenceEqualityComparer.Instance);

	// ServerSaveData is the authoritative source for a loaded server's rack slot.
	// Keep this only until the corresponding RackPosition exists in the scene.
	private readonly Dictionary<object, int> _pendingLoadedServerRackUids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

	// ServerInsertedInRack(saved) is the only reliable rack signal after loading
	// existing saves. The game's currentRackPosition reference is null for these
	// manually-sized 1U variants even though the chassis is visibly in the rack.
	private readonly HashSet<int> _computerAppMountedServerInstanceIds = new HashSet<int>();

	private LenovoConfigurationPanel _lenovoConfigurationPanel;

	private Texture2D _lenovoConfigurationLogo;

	private readonly Dictionary<string, Texture2D> _shopIconTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Sprite> _shopIconSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

	private Font _lenovoConfigurationFont;

	private readonly HashSet<string> _loggedBlockedRackInteractions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly RuntimeVariantRegistry _variantRegistry = new RuntimeVariantRegistry();

	private readonly HashSet<object> _visitedContainers = new HashSet<object>(ReferenceEqualityComparer.Instance);

	private readonly HashSet<object> _visitedCandidates = new HashSet<object>(ReferenceEqualityComparer.Instance);

	private readonly PurchaseRoutingContext _purchaseRouting = new PurchaseRoutingContext();
	private static readonly Dictionary<int, GameObject> PurchasePrefabCache = new Dictionary<int, GameObject>();
	private static readonly HashSet<int> PurchasePrototypeInstanceIds = new HashSet<int>();
	private static GameObject _purchasePrototypeContainer;

	private DateTime _lastVerboseDiscoveryAt = DateTime.MinValue;

	private DateTime _lastSpawnMaintenanceAt = DateTime.MinValue;

	private DateTime _lastRackStateRecoveryAt = DateTime.MinValue;
	private ExactRackIndex<object> _rackPositionRepairIndex;

	private object _pendingLenovoRackInsertionServer;

	private DateTime _pendingLenovoRackInsertionUntil = DateTime.MinValue;

	private bool _reconcilingRackOccupancy;

	private bool _loggedComputerShopDiagnostics;

	private bool _loadedCableRedrawScheduled;

	private bool _persistenceLoading;
	private DateTime _pendingRackRepairUntil = DateTime.MinValue;
	private int _nextConfigurationPanelRefreshFrame;
	private readonly Dictionary<object, string> _loadedLooseIdsByServer = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<object, string> _loadedSavedServerIds = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<object, float[]> _pendingLegacyRackPoses = new Dictionary<object, float[]>(ReferenceEqualityComparer.Instance);

	public bool HasRegisteredVariants => _registeredIds.Count == ServerCatalog.ShopVariants.Length;

	public void ResetRuntimeAttemptState()
	{
		_registeredIds.Clear();
		_baseItemsBySpecKey.Clear();
		_ownerCollectionByBaseItem.Clear();
		_spawnedSpecsByUid.Clear();
		_pendingSpecsByServer.Clear();
		_technicianRepairSpecs.Clear();
		_pendingInsertionSpecs.Clear();
		_serversBeingRepaired.Clear();
		_knownModdedServers.Clear();
		_preparedPhysicalServers.Clear();
		_pendingSceneConfiguredServers.Clear();
		_repairedPersistedServerIds.Clear();
		_loadedServersById.Clear();
		_loadedLooseIdsByServer.Clear();
		_loadedSavedServerIds.Clear();
		_pendingLegacyRackPoses.Clear();
		_loadedCablesWithModdedEndpoints.Clear();
		_pendingLoadedCableRedrawIds.Clear();
		_manualRackMountsByServer.Clear();
		_pendingLoadedServerRackUids.Clear();
		_pendingRackRepairUntil = DateTime.MinValue;
		_computerAppMountedServerInstanceIds.Clear();
		_visitedContainers.Clear();
		_visitedCandidates.Clear();
		_purchaseRouting.Clear();
		_pendingLenovoRackInsertionServer = null;
		_pendingLenovoRackInsertionUntil = DateTime.MinValue;
		_lastRackStateRecoveryAt = DateTime.MinValue;
		_rackPositionRepairIndex = null;
		_loggedComputerShopDiagnostics = false;
		_loadedCableRedrawScheduled = false;
		_lenovoPowerControlSpecsByServer.Clear();
		_lenovoPowerButtonObjectsByServer.Clear();
		_lenovoMountedRackPositionsByServer.Clear();
		_loggedBlockedRackInteractions.Clear();
	}

	public void BeginKnownServerRepairWindow()
	{
		_variantRegistry.Load();
		if (_variantRegistry.Count > 0)
		{
			Log.Info("Loaded " + _variantRegistry.Count.ToString(CultureInfo.InvariantCulture) + " persisted modded server marker(s).");
		}
	}

	public void BeginPersistenceLoad(string rawSaveName, string saveFilePath)
	{
		_persistenceLoading = true;
		ResetRuntimeAttemptState();
		_variantRegistry.SelectSave(rawSaveName, saveFilePath);
		Log.Info("Selected RealServers save identity scope: '" + rawSaveName + "', knownModels=" + _variantRegistry.Count.ToString(CultureInfo.InvariantCulture) + ".");
	}

	public ServerIdentityRecord[] CapturePersistenceSnapshot()
	{
		if (_persistenceLoading)
		{
			throw new InvalidOperationException("Cannot capture Real Servers identity while a save is still loading.");
		}
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			return Array.Empty<ServerIdentityRecord>();
		}
		var records = new List<ServerIdentityRecord>();
		var seen = new HashSet<int>();
		foreach (object server in FindUnityObjectsOfTypeAllDirect(serverType))
		{
			GameObject serverObject = ResolveGameObject(server);
			if (!IsLiveInventory(serverObject) || !seen.Add(serverObject.GetInstanceID()))
			{
				continue;
			}
			ServerVariantSpec spec = ResolveExactOwnedSpec(server);
			if (spec == null)
			{
				continue;
			}
			EnsureUniqueServerIdentity(server, spec, "save-snapshot");
			string looseId = EnsureLooseIdentity(server);
			string serverId = ReadServerId(server) ?? string.Empty;
			int rackUid = PrepareNativeSnapshotRackUid(server);
			_variantRegistry.RecordIdentity(serverId, looseId, rackUid, spec);
			records.Add(new ServerIdentityRecord(serverId, looseId, rackUid, spec.VariantId));
		}
		Log.Info("Captured RealServers save identities: models=" + records.Count.ToString(CultureInfo.InvariantCulture) + ", mounted=" + records.Count(record => record.RackPositionUid > 0).ToString(CultureInfo.InvariantCulture) + ".");
		return records.ToArray();
	}

	public void CommitPersistenceSnapshot(string rawSaveName, string saveFilePath, ServerIdentityRecord[] records)
	{
		_variantRegistry.CommitSnapshot(rawSaveName, saveFilePath, records);
	}

	public void CompletePersistenceLoad()
	{
		// Called once after the game's late load phase; never run a global scan
		// from the per-frame maintenance loop.
		try
		{
			_rackPositionRepairIndex = CreateRackPositionIndex();
			RepairPersistedSceneVariants("save-load-complete");
			RepairLoadedRackState("save-load-complete", force: false);
			RepairLegacyExactRackMounts();
			_pendingRackRepairUntil = DateTime.UtcNow.AddSeconds(5);
		}
		finally
		{
			_rackPositionRepairIndex = null;
			_persistenceLoading = false;
		}
	}

	public void AbortPersistenceLoad()
	{
		_persistenceLoading = false;
		_pendingRackRepairUntil = DateTime.MinValue;
		_rackPositionRepairIndex = null;
	}

	public void ResetPersistenceForNewWorld()
	{
		ResetRuntimeAttemptState();
		_variantRegistry.SelectSave(string.Empty, string.Empty);
		_persistenceLoading = false;
	}

	public bool CanCompletePersistenceLoad()
	{
		Type systemType = ReflectionUtil.FindType("WaypointInitializationSystem");
		object system = systemType == null ? null : ReflectionUtil.ReadStaticMember(systemType, "Instance", "instance");
		return !(ReflectionUtil.ReadMember(system, "isLoading", "IsLoading") is bool loading && loading);
	}

	public void RestoreLoadedObjectIdentity(object instance, object data)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		object server = instance == null || serverType == null ? null : ReflectionUtil.TryCastIl2CppObject(instance, serverType);
		if (server == null || data == null)
		{
			return;
		}
		string looseId = ReflectionUtil.ReadMember(data, "coopLooseId", "CoopLooseId")?.ToString();
		if (!string.IsNullOrWhiteSpace(looseId))
		{
			_loadedLooseIdsByServer[server] = looseId;
			ReflectionUtil.WriteMember(server, looseId, "coopLooseId", "CoopLooseId");
		}
		ServerVariantSpec spec = _variantRegistry.GetByLooseId(looseId) ?? _variantRegistry.Get(ReadServerId(server));
		if (spec == null)
		{
			return;
		}
		_pendingSpecsByServer[server] = spec;
		_knownModdedServers.Add(server);
		ClearStaleLoadedRackState(server);
		ConfigureServerAndPorts(server, spec, "Interact.OnLoad.exact-identity", logWhenUnchanged: false);
		RecordServerIdentity(server, spec);
	}

	private ServerVariantSpec ResolveExactOwnedSpec(object server)
	{
		if (server == null) return null;
		if (_pendingSpecsByServer.TryGetValue(server, out ServerVariantSpec pendingSpec)) return pendingSpec;
		ServerVariantSpec visualSpec = ResolveSpecFromCustomVisual(ResolveGameObject(server)) ?? ResolveSpecFromRuntimeObject(server);
		if (visualSpec != null) return visualSpec;
		return _variantRegistry.GetByLooseId(ReadLooseIdentity(server)) ?? _variantRegistry.Get(ReadServerId(server));
	}

	private string ReadLooseIdentity(object server)
	{
		string liveId = ReflectionUtil.ReadMember(server, "coopLooseId", "CoopLooseId")?.ToString();
		if (!string.IsNullOrWhiteSpace(liveId)) return liveId;
		return _loadedLooseIdsByServer.TryGetValue(server, out string loadedId) ? loadedId : null;
	}

	private string EnsureLooseIdentity(object server)
	{
		string looseId = ReadLooseIdentity(server);
		if (string.IsNullOrWhiteSpace(looseId))
		{
			looseId = Guid.NewGuid().ToString("N");
			if (!ReflectionUtil.WriteMember(server, looseId, "coopLooseId", "CoopLooseId")) return null;
		}
		return looseId;
	}

	private static int ReadRestorableRackUid(object server)
	{
		int rackUid = ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID"));
		return CanRestoreServerRack(server, rackUid) ? rackUid : -1;
	}

	private int PrepareNativeSnapshotRackUid(object server)
	{
		// A manual mount is an explicit player-selected slot, not a proximity
		// guess. Publish its registered native reservation before serialization.
		if (_manualRackMountsByServer.TryGetValue(server, out ManualRackMount mount)
			&& IsTrackedManualRackMountStillValid(server, mount))
		{
			if (!TryEnsureNativeRackPositionUid(mount.RackPosition, out int uid))
				throw new InvalidOperationException("Cannot save an unregistered manual rack reservation.");
			ReflectionUtil.WriteMember(server, uid, "rackPositionUID", "RackPositionUID");
			ReflectionUtil.WriteMember(server, false, "isOnTrolley", "IsOnTrolley");
			EnsureRackPositionOccupied(mount.Rack, mount.RackPosition, Math.Max(1, mount.Spec.RackUnits));
			return uid;
		}
		return ReadRestorableRackUid(server);
	}

	private static bool CanRestoreServerRack(object server, int rackUid)
	{
		bool inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands") is bool held && held;
		bool onTrolley = ReflectionUtil.ReadMember(server, "isOnTrolley", "IsOnTrolley") is bool trolley && trolley;
		return RuntimeOwnershipPolicy.CanRestoreRack(rackUid, inHands, onTrolley);
	}

	private int RackUidDuringLoad(object server, int nativeRackUid)
	{
		return _persistenceLoading
			? _variantRegistry.GetSavedRackPositionUid(ReadSavedSourceServerId(server), ReadLooseIdentity(server)) ?? nativeRackUid
			: nativeRackUid;
	}

	private void ClearStaleLoadedRackState(object server)
	{
		if (!_persistenceLoading) return;
		int? savedRackUid = _variantRegistry.GetSavedRackPositionUid(ReadSavedSourceServerId(server), ReadLooseIdentity(server));
		if (savedRackUid.HasValue && savedRackUid.Value <= 0)
		{
			// Our saved snapshot says this individual object was loose, in hands,
			// or on the trolley. A leftover native rack UID is not a reservation.
			ReflectionUtil.WriteMember(server, null, "currentRackPosition", "CurrentRackPosition");
			ReflectionUtil.WriteMember(server, -1, "rackPositionUID", "RackPositionUID");
			UnregisterComputerAppMountedServer(server);
			_manualRackMountsByServer.Remove(server);
			_lenovoMountedRackPositionsByServer.Remove(server);
		}
	}

	private bool RecordServerIdentity(object server, ServerVariantSpec spec)
	{
		if (server == null || spec == null || !IsLiveInventory(ResolveGameObject(server))) return false;
		_knownModdedServers.Add(server);
		return _variantRegistry.RecordIdentity(ReadServerId(server) ?? string.Empty, EnsureLooseIdentity(server), ReadRestorableRackUid(server), spec);
	}

	private static bool IsLiveInventory(GameObject serverObject) => serverObject != null &&
		RuntimeOwnershipPolicy.CanCaptureIdentity(serverObject.scene.IsValid(),
			IsPurchasePrototype(serverObject) || RuntimeGltfVisualLoader.IsCachedPurchasePrefab(serverObject));

	private static bool IsPurchasePrototype(GameObject gameObject)
	{
		for (Transform current = gameObject != null ? gameObject.transform : null; current != null; current = current.parent)
			if (PurchasePrototypeInstanceIds.Contains(current.gameObject.GetInstanceID())) return true;
		return false;
	}

	public bool TryRegisterAll(string source, object? triggerInstance = null)
	{
		try
		{
			if (triggerInstance != null && string.Equals(triggerInstance.GetType().Name, "ComputerShop", StringComparison.OrdinalIgnoreCase))
			{
				RealServersComputerApp.Ensure(triggerInstance, this);
			}
			TryEnsureComputerApps();
			if (triggerInstance != null)
			{
				TryRegisterShopUiVariants(source, triggerInstance);
			}
			if (HasRegisteredVariants)
			{
				return true;
			}
			Discover(source, triggerInstance);
			int count = _registeredIds.Count;
			ServerVariantSpec[] all = ServerCatalog.ShopVariants;
			foreach (ServerVariantSpec serverVariantSpec in all)
			{
				if (_registeredIds.Contains(serverVariantSpec.VariantId) || !_baseItemsBySpecKey.TryGetValue(SpecKey(serverVariantSpec), out object value))
				{
					continue;
				}
				if (!_ownerCollectionByBaseItem.TryGetValue(value, out object value2))
				{
					Log.Warning("Found base item for " + serverVariantSpec.VariantDisplayName + ", but not its owning collection. Base: " + ReflectionUtil.DescribeObject(value));
					continue;
				}
				object obj = CloneAndConfigure(value, serverVariantSpec);
				if (obj != null)
				{
					if (CollectionAlreadyContains(value2, serverVariantSpec.VariantId, serverVariantSpec.VariantDisplayName))
					{
						_registeredIds.Add(serverVariantSpec.VariantId);
					}
					else if (ReflectionUtil.AddToCollection(value2, obj, serverVariantSpec.VariantId))
					{
						_registeredIds.Add(serverVariantSpec.VariantId);
						Log.Info($"Registered {serverVariantSpec.VariantDisplayName} from {ReflectionUtil.DescribeObject(value)} into {ReflectionUtil.SafeTypeName(value2)}.");
					}
					else
					{
						Log.Warning($"Could not add {serverVariantSpec.VariantDisplayName} to collection {ReflectionUtil.SafeTypeName(value2)}.");
					}
				}
			}
			if (_registeredIds.Count - count > 0 || HasRegisteredVariants)
			{
				Log.Info($"Registration status: {_registeredIds.Count}/{ServerCatalog.ShopVariants.Length} variants ready.");
			}
			return HasRegisteredVariants;
		}
		catch (Exception exception)
		{
			Log.Error("Catalog registration failed from " + source + ".", exception);
			return false;
		}
	}

	public void TryRefreshShopItemText(string source, object shopItem)
	{
		object shopItem2 = shopItem;
		if (shopItem2.GetType().Name.Equals("ShopItem", StringComparison.OrdinalIgnoreCase))
		{
			if (HideUnavailableShopItem(shopItem2)) return;
			ServerVariantSpec serverVariantSpec = ServerCatalog.ShopVariants.FirstOrDefault((ServerVariantSpec s) => ShopItemMatchesVariant(shopItem2, s));
			if (!(serverVariantSpec == null))
			{
				RestoreShopPurchaseIdentity(shopItem2, serverVariantSpec);
				object obj = ReflectionUtil.ReadMember(shopItem2, "gameObject", "GameObject");
				UpdateShopItemText(shopItem2, obj ?? shopItem2, serverVariantSpec);
			}
		}
	}

	private void TryEnsureComputerApps()
	{
		foreach (object computerShop in EnumerateComputerShops())
		{
			RealServersComputerApp.Ensure(computerShop, this);
		}
	}

	internal IReadOnlyList<ComputerAppServer> GetComputerAppServers()
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			return Array.Empty<ComputerAppServer>();
		}
		List<ComputerAppServer> result = new List<ComputerAppServer>();
		// Legacy saves can contain several modded physical servers that still have
		// the same carrier id (Server.Green1). Deduplicate by the Unity object, not
		// that carrier id, so each mounted chassis is exposed independently.
		HashSet<int> seenServerObjects = new HashSet<int>();
		int discovered = 0;
		int recognized = 0;
		int mounted = 0;
		int loggedUnmatched = 0;
		object[] rackPositions = FindUnityObjectsOfTypeAllDirect(ReflectionUtil.FindType("RackPosition")).ToArray();
		foreach (object server in FindUnityObjectsOfTypeAllDirect(serverType))
		{
			discovered++;
			if (!TryGetComputerAppModSpec(server, out ServerVariantSpec spec))
			{
				continue;
			}
			recognized++;
			if (!IsComputerAppMountedServer(server, rackPositions))
			{
				if (loggedUnmatched++ < 3)
				{
					GameObject candidateObject = ResolveGameObject(server);
					Log.Info("Real Servers app unmatched server: id='" + (ReadServerId(server) ?? string.Empty) + "', position=" + (candidateObject != null ? candidateObject.transform.position.ToString() : "<none>") + ", rackUid=" + ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID")).ToString(CultureInfo.InvariantCulture) + ", rackPositions=" + rackPositions.Length.ToString(CultureInfo.InvariantCulture) + ".");
				}
				continue;
			}
			mounted++;
			GameObject serverObject = ResolveGameObject(server);
			int instanceId = serverObject != null ? serverObject.GetInstanceID() : 0;
			if (instanceId == 0 || !seenServerObjects.Add(instanceId))
			{
				continue;
			}
			string serverId = ReadServerId(server) ?? ReflectionUtil.ReadUnityObjectName(server);
			result.Add(BuildComputerAppServer(server, spec, serverId));
		}
		Log.Info("Real Servers computer app inventory: discovered=" + discovered.ToString(CultureInfo.InvariantCulture) + ", recognized=" + recognized.ToString(CultureInfo.InvariantCulture) + ", mounted=" + mounted.ToString(CultureInfo.InvariantCulture) + ", listed=" + result.Count.ToString(CultureInfo.InvariantCulture) + ".");
		return result.OrderBy(item => item.Brand, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	internal ComputerAppServer? RefreshComputerAppServer(object server)
	{
		if (!TryGetComputerAppModSpec(server, out ServerVariantSpec spec) || !IsComputerAppMountedServer(server, null))
		{
			return null;
		}
		return BuildComputerAppServer(server, spec, ReadServerId(server) ?? ReflectionUtil.ReadUnityObjectName(server));
	}

	private bool TryGetComputerAppModSpec(object server, out ServerVariantSpec spec)
	{
		spec = ResolveExactOwnedSpec(server);
		return spec != null;
	}

	private static ServerVariantSpec ResolveSpecFromCustomVisual(GameObject serverObject)
	{
		if (serverObject == null)
		{
			return null;
		}
		foreach (ServerVariantSpec candidate in ServerVariantSpec.All)
		{
			if (serverObject.transform.Find("RealServers_VisualRoot_" + candidate.FamilyKey) != null)
			{
				return candidate;
			}
		}
		return null;
	}

	private bool IsComputerAppMountedServer(object server, IReadOnlyList<object> knownRackPositions)
	{
		object held = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
		object trolley = ReflectionUtil.ReadMember(server, "isOnTrolley", "IsOnTrolley");
		if ((held is bool heldValue && heldValue) || (trolley is bool trolleyValue && trolleyValue))
		{
			return false;
		}
		if (IsServerInsertedInRack(server) || HasTrackedManualRackMount(server) || HasTrackedLenovoRackMount(server) || IsComputerAppRegisteredMounted(server))
		{
			return true;
		}
		// A chassis sitting beside a rack is still loose inventory. A mounted
		// state must come from the native reservation or an exact saved rack UID.
		return false;
	}

	private bool HasTrackedManualRackMount(object server)
	{
		return _manualRackMountsByServer.Keys.Any(candidate => SameUnityObject(candidate, server));
	}

	private bool HasTrackedLenovoRackMount(object server)
	{
		return _lenovoMountedRackPositionsByServer.Keys.Any(candidate => SameUnityObject(candidate, server));
	}

	private bool IsComputerAppRegisteredMounted(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		return serverObject != null && _computerAppMountedServerInstanceIds.Contains(serverObject.GetInstanceID());
	}

	private void RegisterComputerAppMountedServer(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject != null)
		{
			_computerAppMountedServerInstanceIds.Add(serverObject.GetInstanceID());
		}
	}

	private void UnregisterComputerAppMountedServer(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject != null)
		{
			_computerAppMountedServerInstanceIds.Remove(serverObject.GetInstanceID());
		}
	}

	internal void ToggleComputerAppPower(object server)
	{
		if (!IsComputerAppMountedServer(server, null))
		{
			return;
		}
		bool isPoweredOn = IsServerPoweredOn(server);
		if (!ReflectionUtil.InvokeMethod(server, "PowerButton", !isPoweredOn))
		{
			Log.Warning("Real Servers computer app could not change server power state.");
			return;
		}
		TrySyncPowerState("computer-app", server);
	}

	internal void ChangeComputerAppCustomer(object server, bool forward)
	{
		if (IsComputerAppMountedServer(server, null))
		{
			ReflectionUtil.InvokeMethod(server, "ButtonClickChangeCustomer", forward);
		}
	}

	internal void OpenComputerAppIpEditor(object server)
	{
		if (!IsComputerAppMountedServer(server, null))
		{
			return;
		}
		GameObject physicalCanvas = ResolveGameObject(ReflectionUtil.ReadMember(server, "canvas", "Canvas"));
		if (physicalCanvas != null)
		{
			physicalCanvas.SetActive(true);
		}
		if (!ReflectionUtil.InvokeMethod(server, "ButtonClickChangeIP"))
		{
			Log.Warning("Real Servers computer app could not open the native IP editor.");
		}
	}

	internal void OpenComputerAppConfigurationPanel(object server)
	{
		if (!IsComputerAppMountedServer(server, null))
		{
			return;
		}
		OpenLenovoConfigurationPanel("computer-app", server);
	}

	private static ComputerAppServer BuildComputerAppServer(object server, ServerVariantSpec spec, string serverId)
	{
		string brand = spec.FamilyKey.StartsWith("dell", StringComparison.OrdinalIgnoreCase)
			? "Dell"
			: spec.FamilyKey.StartsWith("lenovo", StringComparison.OrdinalIgnoreCase) ? "Lenovo" : "Outros";
		string ipAddress = ReflectionUtil.ReadMember(server, "IP", "ip")?.ToString() ?? "0.0.0.0";
		int customerId = ConvertLenovoCustomerId(ReflectionUtil.InvokeMethodReturn(server, "GetCustomerID"));
		return new ComputerAppServer(server, serverId, brand, spec.VariantDisplayName, string.IsNullOrWhiteSpace(ipAddress) ? "0.0.0.0" : ipAddress, customerId, IsServerPoweredOn(server));
	}

	internal sealed class ComputerAppServer
	{
		internal ComputerAppServer(object server, string serverId, string brand, string displayName, string ipAddress, int customerId, bool isPoweredOn)
		{
			Server = server;
			ServerId = serverId;
			Brand = brand;
			DisplayName = displayName;
			IpAddress = ipAddress;
			CustomerId = customerId;
			IsPoweredOn = isPoweredOn;
		}

		internal readonly object Server;
		internal readonly string ServerId;
		internal readonly string Brand;
		internal readonly string DisplayName;
		internal readonly string IpAddress;
		internal readonly int CustomerId;
		internal readonly bool IsPoweredOn;
	}

	public void ClearPendingPurchases(string source)
	{
		int count = _pendingInsertionSpecs.Count;
		int count2 = _spawnedSpecsByUid.Count;
		int count3 = _pendingSpecsByServer.Count;
		_pendingInsertionSpecs.Clear();
		_spawnedSpecsByUid.Clear();
		_pendingSpecsByServer.Clear();
		if (count > 0 || count2 > 0 || count3 > 0)
		{
			Log.Info($"Cleared pending modded server purchase tracking from {source}: insertions={count.ToString(CultureInfo.InvariantCulture)}, spawned={count2.ToString(CultureInfo.InvariantCulture)}, servers={count3.ToString(CultureInfo.InvariantCulture)}.");
		}
	}

	public void TryConfigureSpawnedCartItem(string source, object cartItem, int uid)
	{
		// Cart entries preserve a SKU, but only the actual spawn transaction
		// proves which physical object is new. Never claim an old UID here.
		if (uid < 0 || !_spawnedSpecsByUid.ContainsKey(uid)) return;
		if (!cartItem.GetType().Name.Equals("ShopCartItem", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		ServerVariantSpec serverVariantSpec = ResolveSpecFromCartItem(cartItem);
		if (serverVariantSpec == null)
		{
			LogUnresolvedPossibleVariantCartItem(source, cartItem, uid);
			return;
		}
		_spawnedSpecsByUid[uid] = serverVariantSpec;
		EnqueuePendingInsertion(serverVariantSpec, source, uid);
		object obj = ReflectionUtil.ReadMember(cartItem, "shop");
		object obj2 = ((obj == null) ? null : ReflectionUtil.ReadMember(obj, "spawnedItems"));
		object obj3 = ((obj2 == null) ? null : ReflectionUtil.GetDictionaryValue(obj2, uid));
		if (obj3 == null)
		{
			Log.Warning($"Could not find spawned GameObject uid {uid} for {serverVariantSpec.VariantDisplayName} from {source}.");
		}
		else
		{
			ConfigureSpawnedServerVariant(obj3, serverVariantSpec, uid, source);
			TrackPendingSpawnedServers(obj3, serverVariantSpec, uid);
		}
	}

	public void TryConfigureInitializedCartItem(string source, object cartItem, int firstSpawnUid)
	{
		if (cartItem.GetType().Name.Equals("ShopCartItem", StringComparison.OrdinalIgnoreCase))
		{
			TryConfigureSpawnedCartItem(source, cartItem, firstSpawnUid);
		}
	}

	public void TryTrackShopPurchase(string source, object? computerShop, int itemId, int price, object? itemType, string? displayName)
	{
		ServerVariantSpec serverVariantSpec = ResolveSpecFromPurchase(itemId, price, itemType, displayName);
		if (!(serverVariantSpec == null))
		{
			Log.Info($"Tracked shop purchase click for {serverVariantSpec.VariantDisplayName} from {source}: itemID={itemId.ToString(CultureInfo.InvariantCulture)}, price={price.ToString(CultureInfo.InvariantCulture)}, itemType={itemType ?? ""}, displayName='{displayName ?? ""}'.");
			TryAssociatePendingSpawnedItems(source + ":post-buy", computerShop);
		}
		// Current native buying fills the cart. Physical spawning happens later
		// in SpawnAllPurchasedItems, with the stable SKU still on that entry.
	}

	public void TryRewriteShopPurchaseDisplayName(string source, int itemId, int price, object? itemType, ref string displayName)
	{
		PrepareShopPurchase(source, ref itemId, price, itemType, ref displayName);
	}

	public bool PrepareShopPurchase(string source, ref int itemId, int price, object? itemType, ref string displayName)
	{
		ServerVariantSpec spec = ResolveSpecFromPurchase(itemId, price, itemType, displayName);
		if (spec == null) return false;
		itemId = spec.PurchaseItemId;
		displayName = spec.VariantDisplayName;
		Log.Info($"Prepared cart SKU {itemId} for {spec.VariantDisplayName} from {source}.");
		return true;
	}

	public ServerVariantSpec? PreparePurchasePrefab(string source, object? shop, ref int itemId, object? itemType)
	{
		ServerVariantSpec spec = PurchaseRoutingPolicy.RoutePrefab(ref itemId, itemType);
		if (spec != null) Log.Info($"Resolved checkout SKU {spec.PurchaseItemId} to native carrier {itemId} for {spec.VariantDisplayName} from {source}.");
		return spec;
	}

	public GameObject? FinalizePurchasePrefab(string source, object? shop, GameObject? prefab, ServerVariantSpec? spec)
	{
		GameObject replacement = prefab != null && spec != null ? CreateDistinctPurchasePrefab(prefab, spec) : null;
		GameObject selected = replacement != null ? replacement : prefab;
		// A custom route requires its own prototype; falling back to the shared
		// stock carrier would make separately preloaded SKUs indistinguishable.
		_purchaseRouting.SetPrefab(PurchaseShopId(shop), selected != null ? selected.GetInstanceID() : 0, replacement != null ? spec : null);
		return replacement;
	}

	private static GameObject CreateDistinctPurchasePrefab(GameObject sourcePrefab, ServerVariantSpec spec)
	{
		if (PurchasePrefabCache.TryGetValue(spec.PurchaseItemId, out GameObject cached) && cached != null) return cached;
		if (_purchasePrototypeContainer == null)
		{
			_purchasePrototypeContainer = new GameObject("RealServers_PurchasePrototypes");
			_purchasePrototypeContainer.SetActive(false);
			PurchasePrototypeInstanceIds.Add(_purchasePrototypeContainer.GetInstanceID());
			UnityEngine.Object.DontDestroyOnLoad(_purchasePrototypeContainer);
		}
		// Inactive parent suppresses native Awake/OnEnable on the cached copy.
		// Preserve activeSelf: native Instantiate(position, rotation) makes an
		// independent, active physical clone just as for the original carrier.
		GameObject prototype = UnityEngine.Object.Instantiate(sourcePrefab, _purchasePrototypeContainer.transform, false);
		PurchasePrototypeInstanceIds.Add(prototype.GetInstanceID());
		// Native GenerateUniqueServerId uses GameObject.name as its family
		// prefix. SKU ownership lives in the route, not in the carrier name.
		prototype.name = sourcePrefab.name;
		prototype.transform.position = new Vector3(0f, -5000f, 0f);
		// An empty copied child carries the exact SKU independently from the
		// stock carrier name required by native server ID generation.
		GameObject marker = new GameObject(PurchaseRoutingPolicy.CreateMarkerName(spec));
		marker.transform.SetParent(prototype.transform, worldPositionStays: false);
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType != null)
			foreach (object server in ReflectionUtil.GetComponentsInChildren(prototype, serverType)) ClearPurchasePrototypeIdentity(server);
		PurchasePrefabCache[spec.PurchaseItemId] = prototype;
		Log.Info($"Created isolated purchase prototype for SKU {spec.PurchaseItemId}: activeSelf={prototype.activeSelf}, activeInHierarchy={prototype.activeInHierarchy}.");
		return prototype;
	}

	private static void ClearPurchasePrototypeIdentity(object server)
	{
		ReflectionUtil.WriteMember(server, string.Empty, "ServerID", "serverID", "ServerId", "serverId");
		ReflectionUtil.WriteMember(server, string.Empty, "coopLooseId", "CoopLooseId");
	}

	public PurchaseCheckoutState? BeginPurchaseCheckout(string source, object? shop)
	{
		int shopId = PurchaseShopId(shop);
		if (shopId == 0) return null;
		var before = CaptureShopSpawnedItems(shop);
		return new PurchaseCheckoutState(shopId, before.ToDictionary(pair => pair.Key, pair => pair.Value.GetInstanceID()));
	}

	public void CompletePurchaseCheckout(string source, object? shop, PurchaseCheckoutState? state)
	{
		if (state == null || state.ShopInstanceId != PurchaseShopId(shop)) return;
		var after = CaptureShopSpawnedItems(shop);
		int[] newUids = PurchaseRoutingPolicy.SelectNewSpawnUids(state.Before,
			after.ToDictionary(pair => pair.Key, pair => pair.Value.GetInstanceID()));
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null) return;
		foreach (int uid in newUids)
		{
			GameObject spawned = after[uid];
			if (!IsLiveInventory(spawned)) continue;
			var names = new List<string>();
			foreach (Transform child in spawned.GetComponentsInChildren<Transform>(includeInactive: true)) names.Add(child.gameObject.name);
			ServerVariantSpec spec = PurchaseRoutingPolicy.ResolveMarkerVariant(names);
			if (spec == null) continue;
			object[] servers = ReflectionUtil.GetComponentsInChildren(spawned, serverType).ToArray();
			if (servers.Length == 0) continue;
			foreach (object server in servers)
			{
				EnsureLooseIdentity(server);
				EnsureUniqueServerIdentity(server, spec, source + ".checkout-identity");
			}
			Log.Info($"Confirmed new checkout spawn uid={uid}, SKU={spec.PurchaseItemId}, proof=checkout-delta-and-marker from {source}.");
			ConfigureSpawnedServerVariant(spawned, spec, uid, source);
			TrackPendingSpawnedServers(spawned, spec, uid);
		}
	}

	public PurchaseSpawnState? BeginPhysicalSpawn(string source, object? shop, GameObject? prefab, int price, object? itemType)
	{
		return null;
	}

	public void CompletePhysicalSpawn(string source, object? shop, object? nativeResult, PurchaseSpawnState? state)
	{
	}

	public void EndPurchaseCheckout(object? shop) => _purchaseRouting.EndCheckout(PurchaseShopId(shop));

	private static int PurchaseShopId(object? shop)
	{
		GameObject gameObject = ResolveGameObject(shop);
		return gameObject != null ? gameObject.GetInstanceID() : 0;
	}

	private static Dictionary<int, GameObject> CaptureShopSpawnedItems(object? shop)
	{
		var result = new Dictionary<int, GameObject>();
		Il2Cpp.ComputerShop native = shop as Il2Cpp.ComputerShop
			?? ReflectionUtil.TryCastIl2CppObject(shop, typeof(Il2Cpp.ComputerShop)) as Il2Cpp.ComputerShop;
		if (native == null || native.spawnedItems == null) return result;
		foreach (var pair in native.spawnedItems)
		{
			GameObject gameObject = pair.Value;
			if (pair.Key >= 0 && gameObject != null) result[pair.Key] = gameObject;
		}
		return result;
	}

	// Compatibility entry points for older bridges: without the spawn prefix
	// there is no proof of a newly created object and no scene association.
	public GameObject? TryConfigurePurchasePrefab(string source, object? computerShop, GameObject? prefab, int itemId, object? itemType) => null;

	public void TryConfigureSpawnedPhysicalItem(string source, object computerShop, int price, object? itemType, object? result)
	{
	}

	public void MaintainSpawnedVariants(string source)
	{
		if (DateTime.UtcNow - _lastSpawnMaintenanceAt < TimeSpan.FromSeconds(1.0))
		{
			return;
		}
		_lastSpawnMaintenanceAt = DateTime.UtcNow;
		if (_spawnedSpecsByUid.Count != 0)
		{
			foreach (object item in EnumerateComputerShops())
			{
				TryAssociatePendingSpawnedItems(source + ":pending-scan", item);
			}
		}
		MaintainLenovoPowerControls(source + ":power-controls");
		if (RuntimeOwnershipPolicy.ShouldRetryLoadedRacks(_pendingLoadedServerRackUids.Count, DateTime.UtcNow, _pendingRackRepairUntil))
		{
			RepairLoadedRackState(source + ":pending-loaded-racks");
		}
		if (_pendingLegacyRackPoses.Count > 0 && DateTime.UtcNow <= _pendingRackRepairUntil)
			RepairLegacyExactRackMounts();
	}

	private void RepairPersistedSceneVariants(string source)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			return;
		}
		foreach (object server in FindUnityObjectsOfTypeAllDirect(serverType))
		{
			GameObject liveObject = ResolveGameObject(server);
			if (!IsLiveInventory(liveObject)) continue;
			TryRepairKnownPersistedServer(source, server, forceDisplayRefresh: true);
			// A save only records the game's base Server.Green/Yellow identity. Once
			// its scene object exists, our visual root is the authoritative mod marker
			// and lets us re-hide the base renderer even if the transient registry was
			// not populated during an earlier scene-load callback.
			GameObject serverObject = ResolveGameObject(server);
			ServerVariantSpec visualSpec = ResolveSpecFromCustomVisual(serverObject);
			if (visualSpec == null)
			{
				continue;
			}
			_knownModdedServers.Add(server);
			RuntimeGltfVisualLoader.EnsureVisualStillApplied(server, visualSpec);
			if (visualSpec.UsesLenovoCarrierProfile)
			{
				RestorePersistedRackMountState(server, visualSpec, source + ":visual-marker");
			}
		}
	}

	public void TryRepairKnownPersistedServer(string source, object server, bool forceDisplayRefresh)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null || !type.IsInstanceOfType(server) || _serversBeingRepaired.Contains(server))
		{
			return;
		}
		string text = ReadServerId(server);
		if (IsSpecificServerId(text)) _loadedServersById[text] = server;
		ServerVariantSpec serverVariantSpec = ResolveExactOwnedSpec(server);
		if (serverVariantSpec == null)
		{
			return;
		}
		_serversBeingRepaired.Add(server);
		try
		{
			ClearStaleLoadedRackState(server);
			(int ServerCount, int PortCount, int ChangedServerCount, int ChangedPortCount) tuple = ConfigureServerAndPorts(server, serverVariantSpec, source, logWhenUnchanged: false);
			// Unity restores the server transform from the save, but does not always
			// restore currentRackPosition for our 1U carrier variants. Rebuild that
			// native reference before gameplay/UI queries the server's rack state.
			bool restoredRackMount = RestorePersistedRackMountState(server, serverVariantSpec, source);
			if (!restoredRackMount)
			{
				TrackExistingManualRackMount(server, serverVariantSpec);
			}
			else
			{
				RuntimeGltfVisualLoader.ApplyVisualIfAvailable(server, serverVariantSpec, hideOriginalRenderers: true);
			}
			int item = tuple.PortCount;
			int item2 = tuple.ChangedServerCount;
			int item3 = tuple.ChangedPortCount;
			if (forceDisplayRefresh && (item2 > 0 || item3 > 0))
			{
				ForceServerDisplayRefresh(server);
			}
			if (IsSpecificServerId(text)) _repairedPersistedServerIds.Add(text);
			_knownModdedServers.Add(server);
			if (item2 > 0 || item3 > 0)
			{
				Log.Info($"Repaired persisted {serverVariantSpec.VariantDisplayName} from {source}: serverId='{text}', ports={item}, serverChanged={item2 > 0}, changedPorts={item3}.");
			}
		}
		finally
		{
			_serversBeingRepaired.Remove(server);
		}
	}

	public bool PersistKnownServerIdentity(string source, object server)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (server == null || serverType == null || !serverType.IsInstanceOfType(server))
		{
			return false;
		}
		ServerVariantSpec spec = ResolveExactOwnedSpec(server);
		string serverId = ReadServerId(server);
		if (spec == null)
		{
			return false;
		}
		bool assignedUniqueId = EnsureUniqueServerIdentity(server, spec, source);
		serverId = ReadServerId(server);
		if (IsSpecificServerId(serverId)) _loadedServersById[serverId] = server;
		bool changed = RecordServerIdentity(server, spec);
		if (changed)
		{
			Log.Info("Persisted loose modded server identity for " + spec.VariantDisplayName + " from " + source + ": serverId='" + serverId + "'.");
		}
		return assignedUniqueId || changed;
	}

	public void PrepareLoadedCable(string source, object cableData)
	{
		int cableId = ToInt(ReflectionUtil.ReadMember(cableData, "cableID", "CableID", "cableId", "CableId"));
		bool hasModdedEndpoint = false;
		HashSet<string> endpointServerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string endpointName in new[] { "startPoint", "StartPoint", "endPoint", "EndPoint" })
		{
			object endpoint = ReflectionUtil.ReadMember(cableData, endpointName);
			string serverId = ReflectionUtil.ReadMember(endpoint, "serverID", "ServerID", "serverId", "ServerId")?.ToString();
			if (!IsSpecificServerId(serverId) || !endpointServerIds.Add(serverId))
			{
				continue;
			}
			ServerVariantSpec spec = _variantRegistry.Get(serverId);
			if (spec == null)
			{
				continue;
			}
			hasModdedEndpoint = true;
			object server = FindLoadedServerById(serverId);
			if (server != null)
			{
				TryRepairKnownPersistedServer(source + ":endpoint", server, forceDisplayRefresh: false);
			}
		}
		if (hasModdedEndpoint && cableId >= 0)
		{
			_loadedCablesWithModdedEndpoints.Add(cableId);
		}
	}

	public void FinalizeLoadedCable(string source, object cableData)
	{
		int cableId = ToInt(ReflectionUtil.ReadMember(cableData, "cableID", "CableID", "cableId", "CableId"));
		if (cableId < 0 || !_loadedCablesWithModdedEndpoints.Remove(cableId))
		{
			return;
		}
		_pendingLoadedCableRedrawIds.Add(cableId);
		if (_loadedCableRedrawScheduled)
		{
			return;
		}
		_loadedCableRedrawScheduled = true;
		MelonLoader.MelonCoroutines.Start(RedrawLoadedCablesAfterRestore(source));
	}

	private IEnumerator RedrawLoadedCablesAfterRestore(string source)
	{
		// CablePositions.LoadCable and every Server.OnLoadingComplete callback run
		// during the same load wave. Two frames let both endpoint servers finish
		// their native restoration before the final cable mesh is rebuilt.
		yield return null;
		yield return null;
		int[] cableIds = _pendingLoadedCableRedrawIds.OrderBy(id => id).ToArray();
		_pendingLoadedCableRedrawIds.Clear();
		_loadedCableRedrawScheduled = false;
		Type cablePositionsType = ReflectionUtil.FindType("CablePositions");
		object cablePositions = cablePositionsType == null
			? null
			: ReflectionUtil.ReadStaticMember(cablePositionsType, "instance", "Instance");
		if (cablePositions == null && cablePositionsType != null)
		{
			cablePositions = FindUnityObjectsOfTypeAllDirect(cablePositionsType).FirstOrDefault();
		}
		if (cablePositions == null)
		{
			Log.Warning("Could not redraw " + cableIds.Length.ToString(CultureInfo.InvariantCulture) + " loaded modded cable(s): CablePositions.instance is unavailable.");
			yield break;
		}
		int redrawn = 0;
		foreach (int cableId in cableIds)
		{
			if (RedrawLoadedCable(cablePositions, cableId))
			{
				redrawn++;
			}
		}
		if (redrawn > 0)
		{
			Log.Info("Redrew " + redrawn.ToString(CultureInfo.InvariantCulture) + " loaded modded cable(s) after server port restoration from " + source + ".");
		}
	}

	private static bool RedrawLoadedCable(object cablePositions, int cableId)
	{
		return ReflectionUtil.InvokeMethod(cablePositions, "RedrawCable", cableId);
	}

	private object FindLoadedServerById(string serverId)
	{
		if (_loadedServersById.TryGetValue(serverId, out object cached)
			&& string.Equals(ReadServerId(cached), serverId, StringComparison.OrdinalIgnoreCase))
		{
			return cached;
		}
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			return null;
		}
		foreach (object server in FindUnityObjectsOfTypeAllDirect(serverType))
		{
			string candidateId = ReadServerId(server);
			if (!string.IsNullOrWhiteSpace(candidateId))
			{
				_loadedServersById[candidateId] = server;
			}
		}
		return _loadedServersById.TryGetValue(serverId, out object found) ? found : null;
	}

	public void TryConfigurePendingLifecycleServer(string source, object server)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null || !type.IsInstanceOfType(server) || _serversBeingRepaired.Contains(server))
		{
			return;
		}
		ServerVariantSpec spec = ResolveExactOwnedSpec(server);
		if (spec == null)
		{
			return;
		}
		if (IsPhysicalServerPrepared(server))
		{
			// Native Start/OnEnable may re-enable carrier geometry after our initial
			// configuration. Preserve that repair without reimporting the model or
			// rebuilding every port, collider and rack reference a second time.
			bool changed = ConfigureServer(server, spec);
			RuntimeGltfVisualLoader.EnsureVisualStillApplied(server, spec);
			if (changed) ForceServerDisplayRefresh(server);
			return;
		}
		_serversBeingRepaired.Add(server);
		try
		{
			(int ServerCount, int PortCount, int ChangedServerCount, int ChangedPortCount) tuple = ConfigureServerAndPorts(server, spec, source, logWhenUnchanged: true);
			ForceServerDisplayRefresh(server);
			_pendingSpecsByServer[server] = spec;
			_pendingSceneConfiguredServers.Add(server);
			_knownModdedServers.Add(server);
			RemoveOnePendingSpec(spec);
			Log.Info($"Configured pending lifecycle server as {spec.VariantDisplayName} from {source}: serverId='{ReadServerId(server) ?? ""}', serverChanged={tuple.ChangedServerCount > 0}, ports={tuple.PortCount}, changedPorts={tuple.ChangedPortCount}, object='{ReflectionUtil.ReadUnityObjectName(server)}'.");
		}
		finally
		{
			_serversBeingRepaired.Remove(server);
		}
	}

	public void TryFinalizeInsertedServer(string source, object server)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null || !type.IsInstanceOfType(server))
		{
			return;
		}
		ServerVariantSpec serverVariantSpec = null;
		if (_pendingSpecsByServer.TryGetValue(server, out ServerVariantSpec value))
		{
			serverVariantSpec = value;
		}
		if ((object)serverVariantSpec == null)
		{
			serverVariantSpec = ResolveSpecFromRuntimeObject(server);
		}
		if ((object)serverVariantSpec == null)
		{
			serverVariantSpec = _variantRegistry.Get(ReadServerId(server));
		}
		serverVariantSpec ??= ResolveExactOwnedSpec(server);
		if (serverVariantSpec == null)
		{
			if (_pendingInsertionSpecs.Count > 0)
			{
				Log.Info($"Server insertion from {source} had {_pendingInsertionSpecs.Count.ToString(CultureInfo.InvariantCulture)} pending modded purchase(s), but no match. Server id='{ReadServerId(server) ?? ""}', serverType={ReflectionUtil.ReadMember(server, "serverType") ?? ""}, max={ReflectionUtil.ReadMember(server, "maxProcessingSpeed") ?? ""}, object='{ReflectionUtil.ReadUnityObjectName(server)}'.");
			}
			return;
		}
		bool alreadyPrepared = IsPhysicalServerPrepared(server);
		int item;
		int item2;
		int item3;
		if (alreadyPrepared)
		{
			(int PortCount, int ChangedPortCount) liveControls = ConfigureServerLiveControls(server, serverVariantSpec, source, logWhenUnchanged: true);
			item = liveControls.PortCount;
			item2 = 0;
			item3 = liveControls.ChangedPortCount;
		}
		else
		{
			(int ServerCount, int PortCount, int ChangedServerCount, int ChangedPortCount) tuple = ConfigureServerAndPorts(server, serverVariantSpec, source, logWhenUnchanged: true);
			item = tuple.PortCount;
			item2 = tuple.ChangedServerCount;
			item3 = tuple.ChangedPortCount;
			ForceServerDisplayRefresh(server);
		}
		if (!RestorePersistedRackMountState(server, serverVariantSpec, source) && _lenovoMountedRackPositionsByServer.TryGetValue(server, out object trackedRackPosition))
		{
			RestoreLenovoRackMountState(server, trackedRackPosition, source + ".finalize");
		}
		_knownModdedServers.Add(server);
		string text = ReadServerId(server);
		bool value2 = RecordServerIdentity(server, serverVariantSpec);
		_pendingSpecsByServer.Remove(server);
		RemoveOnePendingSpec(serverVariantSpec);
		Log.Info($"Finalized inserted {serverVariantSpec.VariantDisplayName} from {source}: serverId='{text ?? ""}', prepared={alreadyPrepared}, serverChanged={item2 > 0}, ports={item}, changedPorts={item3}, persisted={value2}.");
	}

	public void TrackLoadedServerRackState(string source, object server, object serverSaveData)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (server == null || serverSaveData == null || serverType == null || !serverType.IsInstanceOfType(server))
		{
			return;
		}
		int rackPositionUid = ToInt(ReflectionUtil.ReadMember(serverSaveData, "rackPositionUID", "RackPositionUID", "rackPositionUid", "RackPositionUid"));
		RecoverLoadedServerVariantIdentity(server, serverSaveData, rackPositionUid, source);
		string savedId = ReadSavedSourceServerId(server, serverSaveData);
		if (rackPositionUid <= 0 && _variantRegistry.HasUnknownSavedRackLocation(savedId, ReadLooseIdentity(server))
			&& ReflectionUtil.ReadMember(serverSaveData, "position", "Position") is Vector3 savedPosition
			&& ReflectionUtil.ReadMember(serverSaveData, "rotation", "Rotation") is Quaternion savedRotation)
			_pendingLegacyRackPoses[server] = Pose(savedPosition, savedRotation);
		rackPositionUid = RackUidDuringLoad(server, rackPositionUid);
		ClearStaleLoadedRackState(server);
		if (ResolveExactOwnedSpec(server) == null || !CanRestoreServerRack(server, rackPositionUid)) return;
		_pendingLoadedServerRackUids[server] = rackPositionUid;
		if (TryRestoreExactLoadedServerRackState(server, rackPositionUid, source))
		{
			_pendingLoadedServerRackUids.Remove(server);
		}
	}

	private void RecoverLoadedServerVariantIdentity(object server, object serverSaveData, int rackPositionUid, string source)
	{
		string serverId = ReadSavedSourceServerId(server, serverSaveData);
		ServerVariantSpec spec = _variantRegistry.GetByLooseId(ReadLooseIdentity(server)) ?? _variantRegistry.Get(serverId);
		if (spec == null)
		{
			return;
		}
		_pendingSpecsByServer[server] = spec;
		RecordServerIdentity(server, spec);
	}

	public void PrepareLoadedServerIdentity(object instance, object data)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		object server = serverType == null ? null : ReflectionUtil.TryCastIl2CppObject(instance, serverType);
		if (server == null || data == null) return;
		string savedId = ReflectionUtil.ReadMember(data, "serverID", "ServerID", "serverId")?.ToString();
		if (IsSpecificServerId(savedId)) _loadedSavedServerIds[server] = savedId;
		ServerVariantSpec spec = _variantRegistry.Get(savedId);
		if (spec == null) return;
		_pendingSpecsByServer[server] = spec;
		ConfigureServer(server, spec);
	}

	private string ReadSavedSourceServerId(object server, object data = null)
	{
		if (_loadedSavedServerIds.TryGetValue(server, out string cached)) return cached;
		string saved = data == null ? null : ReflectionUtil.ReadMember(data, "serverID", "ServerID", "serverId")?.ToString();
		if (IsSpecificServerId(saved)) { _loadedSavedServerIds[server] = saved; return saved; }
		return ReadServerId(server);
	}

	private static float[] Pose(Vector3 position, Quaternion rotation) =>
		new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };

	private void RepairLegacyExactRackMounts()
	{
		if (_pendingLegacyRackPoses.Count == 0) return;
		Type slotType = ReflectionUtil.FindType("RackPosition");
		if (slotType == null) return;
		var slots = new List<object>();
		var poses = new List<float[]>();
		foreach (object slot in FindUnityObjectsOfTypeAllDirect(slotType))
		{
			GameObject gameObject = ResolveGameObject(slot);
			if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.activeInHierarchy) continue;
			slots.Add(slot);
			poses.Add(Pose(gameObject.transform.position, gameObject.transform.rotation));
		}
		var index = new ExactRackPoseIndex(poses);
		int restored = 0;
		foreach (var pending in _pendingLegacyRackPoses.ToArray())
		{
			object server = pending.Key;
			ServerVariantSpec spec = ResolveExactOwnedSpec(server);
			if (spec == null || ResolveGameObject(server) == null
				|| ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands") is bool holding && holding
				|| ReflectionUtil.ReadMember(server, "isOnTrolley", "IsOnTrolley") is bool trolley && trolley)
			{ _pendingLegacyRackPoses.Remove(server); continue; }
			int match = index.FindUnique(pending.Value, candidate =>
			{
				object slot = slots[candidate];
				object rack = ReflectionUtil.ReadMember(slot, "rack", "Rack");
				int position = ToInt(ReflectionUtil.ReadMember(slot, "positionIndex", "PositionIndex"));
				return rack != null && IsManualRackRangeAvailable(rack, position, spec.RackUnits)
					&& IsRackPositionAvailableForManualMount(rack, position, spec.RackUnits);
			});
			if (match < 0 || !TryEnsureNativeRackPositionUid(slots[match], out int uid)) continue;
			ReflectionUtil.WriteMember(server, slots[match], "currentRackPosition", "CurrentRackPosition");
			ReflectionUtil.WriteMember(server, uid, "rackPositionUID", "RackPositionUID");
			ReflectionUtil.InvokeMethod(server, "RemoveRigidbody");
			RestoreLenovoRackMountState(server, slots[match], "legacy-exact-saved-pose");
			RuntimeGltfVisualLoader.ApplyVisualIfAvailable(server, spec, hideOriginalRenderers: true);
			SetRootPickupColliderEnabled(server, enabled: true);
			RecordServerIdentity(server, spec);
			_pendingLegacyRackPoses.Remove(server);
			restored++;
		}
		Log.Info("Exact legacy rack recovery: restored=" + restored.ToString(CultureInfo.InvariantCulture)
			+ ", pending=" + _pendingLegacyRackPoses.Count.ToString(CultureInfo.InvariantCulture) + ".");
	}

	private static bool PersistedServerMatchesDellCarrierSpec(ServerVariantSpec spec, string? serverId, int savedServerType)
	{
		if (spec == null || !spec.UsesDellStaticCarrierProfile || string.IsNullOrWhiteSpace(serverId))
		{
			return false;
		}
		string nativeServerPrefix = spec.BaseRuntimeToken.Replace('_', '.') + "_";
		if (!serverId.StartsWith(nativeServerPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		// This checks native carrier compatibility only. Color/workload type
		// cannot identify the manufacturer of a saved physical server.
		int expectedSavedType = spec.BaseRuntimeToken.EndsWith("2", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
		return savedServerType == expectedSavedType;
	}

	public void TrackTechnicianRepairTarget(string source, object technician)
	{
		ServerVariantSpec serverVariantSpec = ResolveSpecFromTechnician(technician);
		if (!(serverVariantSpec == null))
		{
			_technicianRepairSpecs[technician] = serverVariantSpec;
			object obj = ReflectionUtil.ReadMember(technician, "server");
			Log.Info($"Tracked technician replacement target for {serverVariantSpec.VariantDisplayName} from {source}: technician='{ReflectionUtil.ReadUnityObjectName(technician)}', serverId='{((obj == null) ? "" : (ReadServerId(obj) ?? ""))}'.");
		}
	}

	public void FinalizeTechnicianRepair(string source, object technician)
	{
		ServerVariantSpec serverVariantSpec = ResolveSpecFromTechnician(technician);
		if (serverVariantSpec == null && _technicianRepairSpecs.TryGetValue(technician, out ServerVariantSpec value))
		{
			serverVariantSpec = value;
		}
		if (serverVariantSpec == null)
		{
			return;
		}
		int num = 0;
		foreach (object item in EnumerateTechnicianServerCandidates(technician))
		{
			if (ServerObjectNameLooksLikeSpec(item, serverVariantSpec) || ServerLooksLikeVariantForSpec(item, serverVariantSpec))
			{
				ConfigureServerAndPorts(item, serverVariantSpec, source, logWhenUnchanged: true);
				ForceServerDisplayRefresh(item);
				_knownModdedServers.Add(item);
				_variantRegistry.Set(ReadServerId(item), serverVariantSpec);
				num++;
			}
		}
		if (num > 0)
		{
			Log.Info($"Finalized technician replacement for {serverVariantSpec.VariantDisplayName} from {source}: configuredServers={num.ToString(CultureInfo.InvariantCulture)}.");
		}
	}

	private void ConfigureSpawnedServerVariant(object spawnedGameObject, ServerVariantSpec spec, int uid, string source, bool logWhenUnchanged = true)
	{
		bool visualChanged = RuntimeGltfVisualLoader.ApplyVisualIfAvailable(spawnedGameObject, spec, hideOriginalRenderers: true);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		Type type = ReflectionUtil.FindType("Server");
		if (type != null)
		{
			foreach (object item5 in (IEnumerable<object>)ReflectionUtil.GetComponentsInChildren(spawnedGameObject, type).ToList())
			{
				(int ServerCount, int PortCount, int ChangedServerCount, int ChangedPortCount) tuple = ConfigureServerAndPorts(item5, spec, source, logWhenUnchanged: false);
				int item = tuple.ServerCount;
				int item2 = tuple.PortCount;
				int item3 = tuple.ChangedServerCount;
				int item4 = tuple.ChangedPortCount;
				num += item;
				num3 += item2;
				num2 += item3;
				num4 += item4;
			}
		}
		if (num2 > 0 || num4 > 0 || visualChanged || logWhenUnchanged)
		{
			Log.Info($"Configured spawned {spec.VariantDisplayName} uid {uid} from {source}: visualChanged={visualChanged}, servers={num} changed={num2}, ports={num3} changed={num4}, link={spec.ConnectorHint}/{spec.NetworkSpeedGbps.ToString(CultureInfo.InvariantCulture)}G, sfpType={spec.SfpType}, directFiber={spec.DirectFiberPort}.");
		}
		if (num > 0)
		{
			// A successful UID match is final. Known ownership and the durable
			// native/loose identifiers now handle later lifecycle callbacks.
			_spawnedSpecsByUid.Remove(uid);
			RemoveOnePendingSpec(spec);
		}
	}

	private void TryAssociatePendingSpawnedItems(string source, object? computerShop)
	{
		if (computerShop == null || _spawnedSpecsByUid.Count == 0)
		{
			return;
		}
		object spawnedItems = ReflectionUtil.ReadMember(computerShop, "spawnedItems");
		if (spawnedItems == null)
		{
			return;
		}
		foreach (KeyValuePair<int, ServerVariantSpec> pending in _spawnedSpecsByUid.ToArray())
		{
			int uid = pending.Key;
			object spawnedObject = ReflectionUtil.GetDictionaryValue(spawnedItems, uid);
			if (spawnedObject == null)
			{
				continue;
			}
			ServerVariantSpec spec = pending.Value;
			ConfigureSpawnedServerVariant(spawnedObject, spec, uid, source, logWhenUnchanged: true);
			TrackPendingSpawnedServers(spawnedObject, spec, uid);
			Log.Info($"Associated pending purchase {spec.VariantDisplayName} with spawned uid {uid.ToString(CultureInfo.InvariantCulture)} from {source}: object='{ReflectionUtil.ReadUnityObjectName(spawnedObject)}'.");
		}
	}



	private ServerVariantSpec[] ActivePendingSpecs()
	{
		DateTime utcNow = DateTime.UtcNow;
		return _pendingInsertionSpecs.Where(((ServerVariantSpec Spec, DateTime CreatedAt) pending) => utcNow - pending.CreatedAt <= TimeSpan.FromMinutes(10.0)).Select(((ServerVariantSpec Spec, DateTime CreatedAt) pending) => pending.Spec).GroupBy((ServerVariantSpec spec) => spec.VariantId, StringComparer.OrdinalIgnoreCase).Select((IGrouping<string, ServerVariantSpec> group) => group.First()).ToArray();
	}

	private void TrackPendingSpawnedServers(object spawnedGameObject, ServerVariantSpec spec, int uid)
	{
		Type type = ReflectionUtil.FindType("Server");
		if (type == null)
		{
			return;
		}
		int num = 0;
		foreach (object componentsInChild in ReflectionUtil.GetComponentsInChildren(spawnedGameObject, type))
		{
			_pendingSpecsByServer[componentsInChild] = spec;
			num++;
		}
		if (num > 0)
		{
			Log.Info($"Tracking {num.ToString(CultureInfo.InvariantCulture)} pending server component(s) for {spec.VariantDisplayName} uid {uid.ToString(CultureInfo.InvariantCulture)}.");
		}
	}

	private void EnqueuePendingInsertion(ServerVariantSpec spec, string source, int uid)
	{
		_pendingInsertionSpecs.Enqueue((spec, DateTime.UtcNow));
		while (_pendingInsertionSpecs.Count > RuntimeOwnershipPolicy.MaximumPendingPurchases)
		{
			_pendingInsertionSpecs.Dequeue();
		}
		string value = ((uid < 0) ? "<pending>" : uid.ToString(CultureInfo.InvariantCulture));
		Log.Info($"Queued pending rack insertion for {spec.VariantDisplayName} uid {value} from {source}.");
	}



	private void RemoveOnePendingSpec(ServerVariantSpec spec)
	{
		int count = _pendingInsertionSpecs.Count;
		bool flag = false;
		DateTime utcNow = DateTime.UtcNow;
		for (int i = 0; i < count; i++)
		{
			var (serverVariantSpec, dateTime) = _pendingInsertionSpecs.Dequeue();
			if (!(utcNow - dateTime > TimeSpan.FromMinutes(10.0)))
			{
				if (!flag && string.Equals(serverVariantSpec.VariantId, spec.VariantId, StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
				}
				else
				{
					_pendingInsertionSpecs.Enqueue((serverVariantSpec, dateTime));
				}
			}
		}
	}

	private static bool ServerLooksLikeVariantForSpec(object server, ServerVariantSpec spec)
	{
		if (!ServerObjectNameLooksLikeSpec(server, spec))
		{
			return false;
		}
		double runtimeSpeed = ToDouble(ReflectionUtil.ReadMember(server, "maxProcessingSpeed"));
		if (Math.Abs(runtimeSpeed - (double)spec.RuntimeProcessingSpeed) < 0.001)
		{
			return true;
		}
		// Existing R440/Lenovo saves were previously written as 125000 IOPS
		// (runtime 1.25). Accept that value once so the registered instance can be
		// migrated to the current 4000-IOPS profile instead of reverting to stock.
		return spec.UsesLenovoCarrierProfile && Math.Abs(runtimeSpeed - 1.25) < 0.001;
	}

	private static bool ServerObjectNameLooksLikeSpec(object server, ServerVariantSpec spec)
	{
		string baseRuntimeToken = spec.BaseRuntimeToken;
		if (TextContainsBaseToken(ReflectionUtil.ReadUnityObjectName(server), baseRuntimeToken))
		{
			return true;
		}
		return TextContainsBaseToken(BuildSearchText(server), baseRuntimeToken);
	}

	private static bool TextContainsBaseToken(string text, string token)
	{
		string text2 = text.Replace('_', '.');
		string value = token.Replace('_', '.');
		return text2.Contains(value, StringComparison.OrdinalIgnoreCase);
	}

	private (int ServerCount, int PortCount, int ChangedServerCount, int ChangedPortCount) ConfigureServerAndPorts(object server, ServerVariantSpec spec, string source, bool logWhenUnchanged)
	{
		if (source.IndexOf("ServerInsertedInRack", StringComparison.OrdinalIgnoreCase) >= 0 &&
			CanRestoreServerRack(server, RackUidDuringLoad(server, ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID")))))
		{
			RegisterComputerAppMountedServer(server);
		}
		int num3 = (ConfigureServer(server, spec) ? 1 : 0);
		EnsureComputerAppIdentity(server, spec);
		bool visualChanged = RuntimeGltfVisualLoader.ApplyVisualIfAvailable(server, spec, hideOriginalRenderers: true);
		(int PortCount, int ChangedPortCount) liveControls = ConfigureServerLiveControls(server, spec, source, logWhenUnchanged: false);
		int num = liveControls.PortCount;
		int num2 = liveControls.ChangedPortCount;
		EnsureUniqueServerIdentity(server, spec, source);
		RecordServerIdentity(server, spec);
		// Identity restoration can recognize a stock carrier before any custom
		// geometry, physical dimensions or port layout has been applied. Publish
		// readiness only after the complete physical configuration finishes. A
		// missing optional GLB legitimately retains the configured native chassis.
		MarkPhysicalServerPrepared(server);
		if (num3 > 0 || num2 > 0 || visualChanged || logWhenUnchanged)
		{
			Log.Info($"Configured server {spec.VariantDisplayName} from {source}: serverChanged={num3 > 0}, visualChanged={visualChanged}, ports={num}, changedPorts={num2}, link={spec.ConnectorHint}/{spec.NetworkSpeedGbps.ToString(CultureInfo.InvariantCulture)}G, fiberLanes={spec.FiberLaneCount}.");
		}
		return (ServerCount: 1, PortCount: num, ChangedServerCount: num3, ChangedPortCount: num2);
	}

	private bool IsPhysicalServerPrepared(object server) => IsPhysicalServerPreparedIn(_preparedPhysicalServers, server);
	private void MarkPhysicalServerPrepared(object server) => MarkPhysicalServerPreparedIn(_preparedPhysicalServers, server);
	private static bool IsPhysicalServerPreparedIn(HashSet<object> prepared, object server) => server != null && prepared.Contains(server);
	private static void MarkPhysicalServerPreparedIn(HashSet<object> prepared, object server)
	{
		if (server != null) prepared.Add(server);
	}

	private static void EnsureComputerAppIdentity(object server, ServerVariantSpec spec)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null || spec == null)
		{
			return;
		}
		const string markerName = "RealServers_ModIdentity";
		Transform marker = serverObject.transform.Find(markerName);
		if (marker == null)
		{
			GameObject markerObject = new GameObject(markerName);
			markerObject.transform.SetParent(serverObject.transform, false);
			marker = markerObject.transform;
		}
		// The persisted server-ID registry carries the variant. This child only
		// marks the live Unity object as belonging to Real Servers.
		marker.name = markerName;
	}

	private (int PortCount, int ChangedPortCount) ConfigureServerLiveControls(object server, ServerVariantSpec spec, string source, bool logWhenUnchanged)
	{
		int portCount = 0;
		int changedPortCount = 0;
		if (!spec.UsesDellStaticCarrierProfile)
		{
			ConfigureLenovoPowerControls(server, spec, logWhenConfigured: logWhenUnchanged);
			ConfigureLenovoIpConfigControls(server, spec, logWhenConfigured: logWhenUnchanged);
			RefreshLenovoScreenPresentation(server, spec);
		}
		Type type = ReflectionUtil.FindType("CableLink");
		if (type != null)
		{
			foreach (object item in EnumerateServerCableLinks(server, type))
			{
				int portIndex = portCount;
				portCount++;
				if (ConfigureCableLink(item, spec, server, portIndex))
				{
					changedPortCount++;
				}
				ConfigureCableLinkTransform(item, spec, server, portIndex);
			}
		}
		if (logWhenUnchanged)
		{
			Log.Info($"Configured live controls for {spec.VariantDisplayName} from {source}: ports={portCount}, changedPorts={changedPortCount}, inRack={IsServerInsertedInRack(server).ToString(CultureInfo.InvariantCulture)}.");
		}
		return (PortCount: portCount, ChangedPortCount: changedPortCount);
	}

	private static void RefreshLenovoScreenPresentation(object server, ServerVariantSpec spec)
	{
		if (!spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		// Base Server.Green1 instances must remain untouched. The Lenovo GLB is
		// the only object carrying this marker, so it is a reliable ownership
		// check before changing any on-screen text.
		if (FindLenovoMarker(server, "PowerButton") == null)
		{
			return;
		}
		HideLenovoStockScreenElement(server, "txtServerScreen", "TxtServerScreen", "txtServerIP", "TxtServerIP", "txtIP", "TxtIP", "screenText", "ScreenText");
		HideLenovoStockScreenElement(server, "customerLogo", "CustomerLogo", "serverScreenIcon", "ServerScreenIcon", "screenIcon", "ScreenIcon");
		HideLenovoStockScreenGraphics(server);
	}

	private static void HideLenovoStockScreenElement(object server, params string[] memberNames)
	{
		object element = ReflectionUtil.ReadMember(server, memberNames);
		if (element == null)
		{
			return;
		}
		// Disable both the component and its object. Native display refreshes can
		// reactivate GameObjects, but leave the component disabled.
		ReflectionUtil.WriteMember(element, false, "enabled", "Enabled");
		GameObject elementObject = ResolveGameObject(element);
		if (elementObject != null)
		{
			elementObject.SetActive(false);
		}
	}

	private static void HideLenovoStockScreenGraphics(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return;
		}
		foreach (Graphic graphic in serverObject.GetComponentsInChildren<Graphic>(includeInactive: true))
		{
			if (graphic != null && !IsLenovoInteractionControl(graphic.transform))
			{
				graphic.enabled = false;
			}
		}
		foreach (Component component in serverObject.GetComponentsInChildren<Component>(includeInactive: true))
		{
			if (component == null || IsLenovoInteractionControl(component.transform))
			{
				continue;
			}
			string typeName = component.GetType().Name;
			if (!typeName.Contains("TextMesh", StringComparison.OrdinalIgnoreCase) && !typeName.Contains("TMP_Text", StringComparison.OrdinalIgnoreCase) && !typeName.Contains("SpriteRenderer", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			ReflectionUtil.WriteMember(component, false, "enabled", "Enabled");
			Renderer renderer = component.GetComponent<Renderer>();
			if (renderer != null)
			{
				renderer.enabled = false;
			}
		}
	}

	private static bool IsLenovoInteractionControl(Transform transform)
	{
		for (Transform current = transform; current != null; current = current.parent)
		{
			string name = current.name ?? string.Empty;
			if (name.Contains("ButtonPower", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("PowerButton", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("ButtonEdit", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("RJ45", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsServerPoweredOn(object server)
	{
		object obj = ReflectionUtil.ReadMember(server, "isOn", "IsOn");
		bool flag = default(bool);
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
	}

	public void TrySyncPowerState(string source, object server)
	{
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null || !spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		if (SyncLenovoPowerLed(server))
		{
			Log.Info("Synced Lenovo power LED from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "', isOn=" + IsServerPoweredOn(server).ToString(CultureInfo.InvariantCulture) + ".");
		}
	}

	public void TryConfigurePowerControls(string source, object server)
	{
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null)
		{
			return;
		}
		// MoveToHand disables the exact chassis collider while the object is held.
		// Every modded model must restore that same root collider on DropObject;
		// otherwise the visual exists but falls through world and rack geometry.
		if (source.IndexOf("DropObject", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			SetRootPickupColliderEnabled(server, enabled: true);
		}
		if (!spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		ConfigureServer(server, spec);
		if (source.IndexOf("MoveToHand", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			SetPlayerHandProfile(server, spec, source);
		}
		if (source.IndexOf("DropObject", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			_lenovoMountedRackPositionsByServer.Remove(server);
		}
		ConfigureServerLiveControls(server, spec, source, logWhenUnchanged: false);
		Log.Info("Reconfigured Lenovo power controls from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "'.");
	}

	public void PrepareLenovoMoveToHand(string source, object server)
	{
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null)
		{
			return;
		}
		ConfigureServer(server, spec);
		SetPlayerHandProfile(server, spec, source);
		UnregisterComputerAppMountedServer(server);
		UntrackManualRackMount(server, reconcileRack: true);
		SetRootPickupColliderEnabled(server, enabled: false);
	}

	public void TryPrepareLenovoRackInsertion(string source, object rackPosition)
	{
		object server = FindHeldModdedServer(out ServerVariantSpec spec);
		if (server == null)
		{
			return;
		}
		ConfigureServer(server, spec);
		SetPlayerHandProfile(server, spec, source);
		_pendingLenovoRackInsertionServer = server;
		_pendingLenovoRackInsertionUntil = DateTime.UtcNow.AddSeconds(3.0);
		Log.Info("Prepared modded " + spec.RackUnits.ToString(CultureInfo.InvariantCulture) + "U rack insertion from " + source + ": rackPosition='" + ReflectionUtil.ReadUnityObjectName(rackPosition) + "'.");
	}

	public bool TryHandleLenovoRackPositionClick(string source, object rackPosition)
	{
		object server = FindHeldModdedServer(out ServerVariantSpec spec);
		if (rackPosition == null)
		{
			return true;
		}
		if (server == null)
		{
			// Never intercept an ordinary rack click. The stock handler owns all
			// existing devices and their rack-state bookkeeping.
			return true;
		}
		object rack = ReflectionUtil.ReadMember(rackPosition, "rack", "Rack");
		GameObject serverObject = ResolveGameObject(server);
		GameObject slotObject = ResolveGameObject(rackPosition);
		if (rack == null || serverObject == null || slotObject == null)
		{
			Log.Warning("Could not resolve the Lenovo rack insertion target from " + source + ".");
			return true;
		}
		ReconcileRackOccupancy(rack);
		int positionIndex = ToInt(ReflectionUtil.ReadMember(rackPosition, "positionIndex", "PositionIndex"));
		if (!IsManualRackRangeAvailable(rack, positionIndex, spec.RackUnits))
		{
			Log.Info("Rejected overlapping manual rack mount for " + spec.VariantDisplayName + ": slot='" + ReflectionUtil.ReadUnityObjectName(rackPosition) + "', index=" + positionIndex.ToString(CultureInfo.InvariantCulture) + ".");
			return false;
		}
		if (!IsRackPositionAvailableForManualMount(rack, positionIndex, spec.RackUnits))
		{
			Log.Info("Rejected occupied rack slot for " + spec.VariantDisplayName + ": slot='" + ReflectionUtil.ReadUnityObjectName(rackPosition) + "', index=" + positionIndex.ToString(CultureInfo.InvariantCulture) + ".");
			return false;
		}

		// The stock RackPosition coroutine uses the prefab's 3U shape before it
		// reaches ServerInsertedInRack. Lenovo is a logical Yellow server for
		// workload compatibility, but its physical rack contract is 1U.
		// The coroutine also assigns/registers the slot's native UID. Our custom
		// animation must complete that same step before dropping the held item.
		if (!TryEnsureNativeRackPositionUid(rackPosition, out int nativeRackUid))
		{
			Log.Warning("Could not register the selected native rack slot; the server remains in the player's hands.");
			return false;
		}
		ConfigureServer(server, spec);
		SetPlayerHandProfile(server, spec, source);
		if (!ReflectionUtil.InvokeMethod(server, "DropObject"))
		{
			Log.Warning("Could not release the held Lenovo before rack insertion; keeping the stock rack path.");
			return true;
		}

		Vector3 targetPosition = slotObject.transform.position;
		Quaternion targetRotation = slotObject.transform.rotation;
		// The physical model's front faces local -Z. Start one chassis depth in
		// front of the exact rack slot, then slide straight inward like a drawer.
		Vector3 drawerStartPosition = targetPosition - targetRotation * Vector3.forward * 0.48f;
		ReflectionUtil.WriteMember(server, rackPosition, "currentRackPosition", "CurrentRackPosition");
		ReflectionUtil.WriteMember(server, nativeRackUid, "rackPositionUID", "RackPositionUID");
		ReflectionUtil.WriteMember(server, false, "objectInHands", "ObjectInHands");
		ReflectionUtil.WriteMember(server, false, "isOnTrolley", "IsOnTrolley");
		ManualRackFormFactor.ApplyToServer(server, spec);
		EnsureUniqueServerIdentity(server, spec, source);
		// The Yellow 3U carrier is vertically offset while outside a rack. Refresh
		// after assigning currentRackPosition so the 1U GLB is re-anchored to the
		// exact RackPosition clicked by the player.
		RuntimeGltfVisualLoader.ApplyVisualIfAvailable(server, spec, hideOriginalRenderers: true);
		serverObject.transform.position = drawerStartPosition;
		serverObject.transform.rotation = targetRotation;
		ReflectionUtil.InvokeMethod(rack, "MarkPositionAsUsed", positionIndex, spec.RackUnits);
		ReflectionUtil.InvokeMethod(rackPosition, "SetUsed", true);
		ReflectionUtil.InvokeMethod(server, "RemoveRigidbody");
		RestoreLenovoRackMountState(server, rackPosition, source + ".post-insert");
		// Manual insertion bypasses the stock RackPosition coroutine. Notify the
		// native Server only after its physical contract is already 1U, then
		// restore the exact manual state in case the carrier rewrites it.
		bool nativeRackRegistration = ReflectionUtil.InvokeMethod(server, "ServerInsertedInRack", new object?[] { null });
		RestoreLenovoRackMountState(server, rackPosition, source + ".native-rack-registration");
		RuntimeGltfVisualLoader.ApplyVisualIfAvailable(server, spec, hideOriginalRenderers: true);
		serverObject.transform.position = drawerStartPosition;
		serverObject.transform.rotation = targetRotation;
		TrackManualRackMount(server, rack, rackPosition, spec);
		SetRootPickupColliderEnabled(server, enabled: true);
		_pendingLenovoRackInsertionServer = null;
		_pendingLenovoRackInsertionUntil = DateTime.MinValue;
		ConfigureServerLiveControls(server, spec, source + ".manual-rack-mount", logWhenUnchanged: false);
		MelonLoader.MelonCoroutines.Start(AnimateManualRackInsertion(serverObject.transform, drawerStartPosition, targetPosition, targetRotation));
		Log.Info("Mounted " + spec.VariantDisplayName + " through manual rack form factor from " + source + ": slot='" + ReflectionUtil.ReadUnityObjectName(rackPosition) + "', index=" + positionIndex.ToString(CultureInfo.InvariantCulture) + ", rackUnits=" + spec.RackUnits.ToString(CultureInfo.InvariantCulture) + ", nativeRackRegistration=" + nativeRackRegistration.ToString(CultureInfo.InvariantCulture) + ".");
		return false;
	}

	private bool IsManualRackPosition(object rackPosition)
	{
		return _manualRackMountsByServer.Values.Any(mount => SameUnityObject(mount.RackPosition, rackPosition));
	}

	private static IEnumerator AnimateManualRackInsertion(Transform serverTransform, Vector3 startPosition, Vector3 targetPosition, Quaternion targetRotation)
	{
		const float duration = 0.35f;
		float elapsed = 0f;
		while (serverTransform != null && elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float progress = Mathf.Clamp01(elapsed / duration);
			float easedProgress = progress * progress * (3f - 2f * progress);
			serverTransform.position = Vector3.Lerp(startPosition, targetPosition, easedProgress);
			serverTransform.rotation = targetRotation;
			yield return null;
		}
		if (serverTransform != null)
		{
			serverTransform.position = targetPosition;
			serverTransform.rotation = targetRotation;
		}
	}

	private bool EnsureUniqueServerIdentity(object server, ServerVariantSpec spec, string source)
	{
		if (!IsLiveInventory(ResolveGameObject(server))) return false;
		string serverId = ReadServerId(server);
		if (IsSpecificServerId(serverId))
		{
			if (!string.IsNullOrWhiteSpace(serverId))
			{
				_variantRegistry.Set(serverId, spec);
			}
			return false;
		}
		object generatedId = ReflectionUtil.InvokeMethodReturn(server, "GenerateUniqueServerId");
		string uniqueId = generatedId?.ToString();
		if (!IsSpecificServerId(uniqueId))
		{
			Log.Warning("Could not generate a specific unique server ID for " + spec.VariantDisplayName + " from " + source + ": generated='" + (uniqueId ?? "") + "'.");
			return false;
		}
		ReflectionUtil.WriteMember(server, uniqueId, "ServerID", "serverID", "ServerId", "serverId");
		_variantRegistry.Set(uniqueId, spec);
		ConfigureServerLiveControls(server, spec, source + ".unique-server-id", logWhenUnchanged: false);
		Log.Info("Assigned native server ID to modded server: variant='" + spec.VariantDisplayName + "', serverId='" + uniqueId + "', source=" + source + ".");
		return true;
	}

	private void RestoreLenovoRackMountState(object server, object rackPosition, string source)
	{
		if (server == null || rackPosition == null ||
			!CanRestoreServerRack(server, ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID"))))
		{
			return;
		}
		ReflectionUtil.WriteMember(server, rackPosition, "currentRackPosition", "CurrentRackPosition");
		ReflectionUtil.WriteMember(server, ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID")), "rackPositionUID", "RackPositionUID");
		ManualRackFormFactor.ApplyToServer(server, ResolveSpecForLiveServer(server));
		_lenovoMountedRackPositionsByServer[server] = rackPosition;
		object rack = ReflectionUtil.ReadMember(rackPosition, "rack", "Rack");
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (rack != null && spec != null)
		{
			TrackManualRackMount(server, rack, rackPosition, spec);
			EnsureRackPositionOccupied(rack, rackPosition, Math.Max(1, spec.RackUnits));
			RecordServerIdentity(server, spec);
		}
		Log.Info("Restored modded " + (spec?.RackUnits ?? 1).ToString(CultureInfo.InvariantCulture) + "U rack mount state from " + source + ": rackPositionUid=" + ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID")).ToString(CultureInfo.InvariantCulture) + ".");
	}

	private static bool TryEnsureNativeRackPositionUid(object rackPosition, out int uid)
	{
		uid = ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID"));
		if (uid > 0) return true;
		Type managerType = ReflectionUtil.FindType("MainGameManager");
		object manager = managerType == null ? null : ReflectionUtil.ReadStaticMember(managerType, "instance", "Instance");
		Type positionType = ReflectionUtil.FindType("RackPosition");
		if (manager == null || positionType == null || !(ReflectionUtil.ReadMember(manager, "lastUsedRackPositionGlobalUID") is int)) return false;
		MethodInfo lookup = positionType.GetMethod("GetByUID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		if (lookup == null) return false;
		try
		{
			uid = NativeRackUidAllocator.Reserve(
				() => ToInt(ReflectionUtil.ReadMember(manager, "lastUsedRackPositionGlobalUID")),
				value => { if (!ReflectionUtil.WriteMember(manager, value, "lastUsedRackPositionGlobalUID")) throw new InvalidOperationException("Could not update the native rack UID counter."); },
				value => lookup.Invoke(null, new object[] { value }) != null,
				value => { if (!ReflectionUtil.InvokeMethod(rackPosition, "SetUID", value)) throw new InvalidOperationException("Native rack slot registration failed."); },
				() => ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID")));
			return uid > 0;
		}
		catch (Exception error)
		{
			Log.Error("Could not allocate the native rack reservation.", error);
			return false;
		}
	}

	private sealed class ManualRackMount
	{
		public object Rack { get; }

		public object RackPosition { get; }

		public ServerVariantSpec Spec { get; }

		public ManualRackMount(object rack, object rackPosition, ServerVariantSpec spec)
		{
			Rack = rack;
			RackPosition = rackPosition;
			Spec = spec;
		}
	}

	private void TrackManualRackMount(object server, object rack, object rackPosition, ServerVariantSpec spec)
	{
		if (server == null || rack == null || rackPosition == null || spec == null)
		{
			return;
		}
		_manualRackMountsByServer[server] = new ManualRackMount(rack, rackPosition, spec);
		RegisterComputerAppMountedServer(server);
	}

	private void TrackExistingManualRackMount(object server, ServerVariantSpec spec)
	{
		if (server == null || spec == null)
		{
			return;
		}
		if (!CanRestoreServerRack(server, ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID")))) return;
		object rackPosition;
		object rack;
		try
		{
			rackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
			if (rackPosition == null)
			{
				return;
			}
			rack = ReflectionUtil.ReadMember(rackPosition, "rack", "Rack");
		}
		catch (NullReferenceException)
		{
			return;
		}
		if (rackPosition != null && rack != null)
		{
			TrackManualRackMount(server, rack, rackPosition, spec);
		}
	}

	private bool RestorePersistedRackMountState(object server, ServerVariantSpec spec, string source)
	{
		if (server == null || spec == null || !spec.UsesLenovoCarrierProfile)
		{
			return false;
		}
		int rackUid = ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID"));
		rackUid = RackUidDuringLoad(server, rackUid);
		if (!CanRestoreServerRack(server, rackUid)) return false;
		object rackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
		GameObject currentRackPositionObject = ResolveGameObject(rackPosition);
		if (rackPosition == null
			|| currentRackPositionObject == null
			|| !currentRackPositionObject.scene.IsValid()
			|| !RuntimeOwnershipPolicy.IsMatchingRackReservation(rackUid, ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID")))
			|| ReflectionUtil.ReadMember(rackPosition, "rack", "Rack") == null)
		{
			GameObject serverObject = ResolveGameObject(server);
			if (serverObject == null) return false;
			ExactRackIndex<object> index = GetRackPositionRepairIndex();
			rackPosition = FindClosestExactRackPosition(serverObject, index.GetCandidates(rackUid), out float distance);
			if (distance > 2.25f) return false;
		}
		if (rackPosition == null || ReflectionUtil.ReadMember(rackPosition, "rack", "Rack") == null)
		{
			return false;
		}
		RestoreLenovoRackMountState(server, rackPosition, source + ".saved-rack");
		return true;
	}

	private bool IsManualRackRangeAvailable(object rack, int requestedIndex, int requestedUnits)
	{
		int requestedSize = Math.Max(1, requestedUnits);
		foreach (KeyValuePair<object, ManualRackMount> pair in _manualRackMountsByServer.ToArray())
		{
			object mountedServer = pair.Key;
			ManualRackMount mount = pair.Value;
			if (!IsTrackedManualRackMountStillValid(mountedServer, mount))
			{
				_manualRackMountsByServer.Remove(mountedServer);
				continue;
			}
			if (!SameUnityObject(mount.Rack, rack))
			{
				continue;
			}
			int mountedIndex = ToInt(ReflectionUtil.ReadMember(mount.RackPosition, "positionIndex", "PositionIndex"));
			int occupiedSize = Math.Max(1, mount.Spec.RackUnits);
			if (RuntimeOwnershipPolicy.RackRangesOverlap(requestedIndex, requestedSize, mountedIndex, occupiedSize))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsTrackedManualRackMountStillValid(object server, ManualRackMount mount)
	{
		if (server == null || mount == null || ResolveGameObject(server) == null)
		{
			return false;
		}
		try
		{
			object inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
			if (inHands is bool holding && holding)
			{
				return false;
			}
			object currentRackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
			if (!SameUnityObject(currentRackPosition, mount.RackPosition))
			{
				return false;
			}
			object currentRack = ReflectionUtil.ReadMember(currentRackPosition, "rack", "Rack");
			return SameUnityObject(currentRack, mount.Rack);
		}
		catch (NullReferenceException)
		{
			return false;
		}
	}

	private bool IsRackPositionAvailableForManualMount(object rack, int requestedIndex, int requestedUnits)
	{
		if (!IsManualRackRangeAvailable(rack, requestedIndex, requestedUnits))
		{
			return false;
		}
		object usedSlots = ReflectionUtil.ReadMember(rack, "isPositionUsed", "IsPositionUsed");
		int unitCount = Math.Max(1, requestedUnits);
		for (int i = 0; i < unitCount; i++)
		{
			if (ToInt(ReflectionUtil.GetIndexedItem(usedSlots, requestedIndex + i)) != 0)
			{
				return false;
			}
		}
		return true;
	}

	private void UntrackManualRackMount(object server, bool reconcileRack)
	{
		if (server == null || !_manualRackMountsByServer.TryGetValue(server, out ManualRackMount mount))
		{
			return;
		}
		_manualRackMountsByServer.Remove(server);
		if (reconcileRack)
		{
			ReconcileRackOccupancy(mount.Rack);
		}
	}

	private static void SetRootPickupColliderEnabled(object server, bool enabled)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return;
		}
		// Only the collider on the server root is the Lenovo chassis collider.
		// Enabling every child collider here reactivated Yellow1's hidden 3U
		// collision volumes after rack insertion, blocking the equipment above it.
		BoxCollider rootCollider = serverObject.GetComponent<BoxCollider>();
		if (rootCollider != null)
		{
			rootCollider.enabled = enabled;
		}
	}

	public void TryOverrideLenovoRackSlotSize(ref int sizeInU)
	{
		if (_reconcilingRackOccupancy)
		{
			return;
		}
		object heldServer = FindHeldModdedServer(out ServerVariantSpec heldSpec);
		if (heldServer != null)
		{
			sizeInU = heldSpec.RackUnits;
			return;
		}
		if (HasPendingLenovoRackInsertion())
		{
			ServerVariantSpec pendingSpec = ResolveSpecForLiveServer(_pendingLenovoRackInsertionServer);
			if (pendingSpec != null)
			{
				sizeInU = pendingSpec.RackUnits;
			}
		}
	}

	public bool TryOverrideLenovoRackPositionAllowed(object rackPosition, bool checkAvailability, ref bool result)
	{
		object heldServer = FindHeldModdedServer(out ServerVariantSpec spec);
		if (heldServer == null && HasPendingLenovoRackInsertion())
		{
			spec = ResolveSpecForLiveServer(_pendingLenovoRackInsertionServer);
		}
		if (spec == null)
		{
			return true;
		}
		object rack = ReflectionUtil.ReadMember(rackPosition, "rack", "Rack");
		if (rack == null)
		{
			return true;
		}
		int positionIndex = ToInt(ReflectionUtil.ReadMember(rackPosition, "positionIndex", "PositionIndex"));
		bool available = !checkAvailability || IsRackPositionAvailableForManualMount(rack, positionIndex, spec.RackUnits);
		result = available;
		return false;
	}

	private bool HasPendingLenovoRackInsertion()
	{
		if (_pendingLenovoRackInsertionServer == null || DateTime.UtcNow > _pendingLenovoRackInsertionUntil || ResolveGameObject(_pendingLenovoRackInsertionServer) == null)
		{
			_pendingLenovoRackInsertionServer = null;
			_pendingLenovoRackInsertionUntil = DateTime.MinValue;
			return false;
		}
		return true;
	}

	private void ReconcileRackOccupancy(object rack)
	{
		if (rack == null || _reconcilingRackOccupancy)
		{
			return;
		}
		try
		{
			_reconcilingRackOccupancy = true;
			// Do not clear isPositionUsed or call SetUsed(false) here. Those values
			// are the game's authoritative state for every stock server, switch and
			// rack accessory. Clearing them made a loaded rack look empty even while
			// its objects were visibly still mounted.
			int ensured = 0;
			foreach (KeyValuePair<object, ManualRackMount> pair in _manualRackMountsByServer.ToArray())
			{
				ManualRackMount mount = pair.Value;
				if (!IsTrackedManualRackMountStillValid(pair.Key, mount))
				{
					_manualRackMountsByServer.Remove(pair.Key);
					continue;
				}
				if (!SameUnityObject(mount.Rack, rack))
				{
					continue;
				}
				if (EnsureRackPositionOccupied(rack, mount.RackPosition, mount.Spec.RackUnits)) ensured++;
			}
			if (ensured > 0)
				Log.Info("Ensured manual mod rack occupancy without altering native rack state: mounts=" + ensured.ToString(CultureInfo.InvariantCulture) + ".");
		}
		finally
		{
			_reconcilingRackOccupancy = false;
		}
	}

	internal void RepairLoadedRackState(string source, bool force = false)
	{
		if (!force && DateTime.UtcNow - _lastRackStateRecoveryAt < TimeSpan.FromSeconds(2.0))
		{
			return;
		}
		_lastRackStateRecoveryAt = DateTime.UtcNow;
		ExactRackIndex<object> previousIndex = _rackPositionRepairIndex;
		_rackPositionRepairIndex ??= CreateRackPositionIndex();
		try
		{
			if (force)
			{
				Type serverType = ReflectionUtil.FindType("Server");
				if (serverType != null)
				{
					foreach (object server in FindUnityObjectsOfTypeAllDirect(serverType))
					{
						GameObject serverObject = ResolveGameObject(server);
						if (serverObject == null || !serverObject.scene.IsValid()) continue;
						if (ResolveExactOwnedSpec(server) == null) continue;
						object currentRackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
						if (currentRackPosition == null)
						{
							int rackUid = ToInt(ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID"));
							rackUid = RackUidDuringLoad(server, rackUid);
							if (CanRestoreServerRack(server, rackUid)) _pendingLoadedServerRackUids[server] = rackUid;
						}
					}
				}
			}
			int repairedReferences = 0;
			int repairedReservations = 0;
			foreach (KeyValuePair<object, int> pending in _pendingLoadedServerRackUids.ToArray())
			{
				object server = pending.Key;
				GameObject serverObject = ResolveGameObject(server);
				if (serverObject == null)
				{
					_pendingLoadedServerRackUids.Remove(server);
					continue;
				}
				object inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
				object onTrolley = ReflectionUtil.ReadMember(server, "isOnTrolley", "IsOnTrolley");
				if ((inHands is bool held && held) || (onTrolley is bool trolley && trolley)) continue;
				if (TryRestoreExactLoadedServerRackState(server, pending.Value, source))
				{
					_pendingLoadedServerRackUids.Remove(server);
					repairedReferences++;
					repairedReservations++;
				}
			}
			if (repairedReferences > 0 || repairedReservations > 0)
			{
				Log.Info("Recovered loaded rack state from " + source + ": references=" + repairedReferences.ToString(CultureInfo.InvariantCulture) + ", reservations=" + repairedReservations.ToString(CultureInfo.InvariantCulture) + ".");
			}
		}
		finally
		{
			_rackPositionRepairIndex = previousIndex;
		}
	}

	private ExactRackIndex<object> GetRackPositionRepairIndex()
	{
		if (_rackPositionRepairIndex != null) return _rackPositionRepairIndex;
		ExactRackIndex<object> index = CreateRackPositionIndex();
		// Native ServerInsertedInRack load callbacks arrive before CompleteLoad.
		// Share one bounded snapshot across those callbacks too; the late load
		// batch gets a fresh index after every native slot has been registered.
		if (_persistenceLoading) _rackPositionRepairIndex = index;
		return index;
	}

	private static ExactRackIndex<object> CreateRackPositionIndex() => new ExactRackIndex<object>(
		() => FindUnityObjectsOfTypeAllDirect(ReflectionUtil.FindType("RackPosition")),
		position => ToInt(ReflectionUtil.ReadMember(position, "rackPosGlobalUID", "RackPosGlobalUID")),
		position =>
		{
			GameObject gameObject = ResolveGameObject(position);
			return gameObject != null && gameObject.scene.IsValid() && ReflectionUtil.ReadMember(position, "rack", "Rack") != null;
		});

	private static object FindClosestExactRackPosition(GameObject serverObject, IReadOnlyList<object> candidates, out float selectedDistance)
	{
		selectedDistance = float.PositiveInfinity;
		object selected = null;
		Vector3 serverPosition = serverObject.transform.position;
		foreach (object candidate in candidates)
		{
			GameObject candidateObject = ResolveGameObject(candidate);
			if (candidateObject == null) continue;
			float distance = (candidateObject.transform.position - serverPosition).sqrMagnitude;
			if (distance < selectedDistance)
			{
				selected = candidate;
				selectedDistance = distance;
			}
		}
		return selected;
	}

	private bool TryRestoreExactLoadedServerRackState(object server, int rackPositionUid, string source)
	{
		if (server == null || !CanRestoreServerRack(server, rackPositionUid) || ResolveExactOwnedSpec(server) == null)
		{
			return false;
		}
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null || !serverObject.scene.IsValid())
		{
			return false;
		}
		// Resolve only the exact authoritative saved UID. Spatial ordering may
		// disambiguate duplicated copies of that UID, never select another slot.
		ExactRackIndex<object> index = GetRackPositionRepairIndex();
		object rackPosition = FindClosestExactRackPosition(serverObject, index.GetCandidates(rackPositionUid), out float selectedDistance);
		if (rackPosition == null)
		{
			return false;
		}
		if (selectedDistance > 2.25f)
		{
			return false;
		}
		object rack = ReflectionUtil.ReadMember(rackPosition, "rack", "Rack");
		if (rack == null)
		{
			return false;
		}
		int resolvedRackPositionUid = ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID"));
		ReflectionUtil.WriteMember(server, rackPosition, "currentRackPosition", "CurrentRackPosition");
		ReflectionUtil.WriteMember(server, resolvedRackPositionUid, "rackPositionUID", "RackPositionUID");
		ServerVariantSpec ownedSpec = ResolveSpecForServer(server);
		int sizeInU = ownedSpec?.RackUnits ?? Math.Max(1, ToInt(ReflectionUtil.ReadMember(server, "sizeInU", "SizeInU")));
		EnsureRackPositionOccupied(rack, rackPosition, sizeInU);
		if (ownedSpec != null)
		{
			TrackManualRackMount(server, rack, rackPosition, ownedSpec);
			RecordServerIdentity(server, ownedSpec);
			RuntimeGltfVisualLoader.EnsureVisualStillApplied(server, ownedSpec);
		}
		Log.Info("Restored loaded server to exact saved rack position from " + source + ": serverId='" + (ReadServerId(server) ?? string.Empty) + "', savedRackPositionUid=" + rackPositionUid.ToString(CultureInfo.InvariantCulture) + ", resolvedRackPositionUid=" + resolvedRackPositionUid.ToString(CultureInfo.InvariantCulture) + ", distance=" + Math.Sqrt(selectedDistance).ToString("0.000", CultureInfo.InvariantCulture) + ", modded=" + (ownedSpec != null).ToString(CultureInfo.InvariantCulture) + ".");
		return true;
	}

	private bool EnsureRackPositionOccupied(object rack, object rackPosition, int sizeInU)
	{
		if (rack == null || rackPosition == null)
		{
			return false;
		}
		int index = ToInt(ReflectionUtil.ReadMember(rackPosition, "positionIndex", "PositionIndex"));
		object usedSlots = ReflectionUtil.ReadMember(rack, "isPositionUsed", "IsPositionUsed");
		int slotCount = ReflectionUtil.GetCollectionCount(usedSlots);
		int units = Math.Max(1, sizeInU);
		int uid = ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID"));
		if (index < 0 || (long)index + units > slotCount) return false;
		var reservation = new int[units];
		for (int offset = 0; offset < units; offset++)
			reservation[offset] = ToInt(ReflectionUtil.GetIndexedItem(usedSlots, index + offset));
		if (!NativeRackUidAllocator.ShouldMark(uid, reservation, 0, units))
		{
			return false;
		}
		bool previousReconcileState = _reconcilingRackOccupancy;
		try
		{
			_reconcilingRackOccupancy = true;
			ReflectionUtil.InvokeMethod(rack, "MarkPositionAsUsed", index, units);
			ReflectionUtil.InvokeMethod(rackPosition, "SetUsed", true);
		}
		finally
		{
			_reconcilingRackOccupancy = previousReconcileState;
		}
		return true;
	}



	public bool TryOverrideManualRackValidation(object server, ref bool result)
	{
		if (server == null)
		{
			return true;
		}
		object inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
		if (inHands is bool holding && holding)
		{
			return true;
		}
		if (_manualRackMountsByServer.ContainsKey(server))
		{
			result = true;
			return false;
		}
		if (ResolveLiveOwnedSpec(server) != null && ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition") != null)
		{
			result = true;
			return false;
		}
		return true;
	}

	private static IEnumerable<object> EnumerateRackableObjects()
	{
		HashSet<object> yielded = new HashSet<object>(ReferenceEqualityComparer.Instance);
		string[] typeNames = new string[3] { "Server", "NetworkSwitch", "PatchPanel" };
		foreach (string typeName in typeNames)
		{
			Type type = ReflectionUtil.FindType(typeName);
			if (type == null)
			{
				continue;
			}
			foreach (object item in FindUnityObjectsOfTypeAll(type))
			{
				if (item != null && yielded.Add(item))
				{
					yield return item;
				}
			}
		}
	}

	private static object ResolveMountedRackPosition(object usableObject, object rackPositions)
	{
		object direct = ReflectionUtil.ReadMember(usableObject, "currentRackPosition", "CurrentRackPosition");
		if (direct != null)
		{
			return direct;
		}
		int rackUid = ToInt(ReflectionUtil.ReadMember(usableObject, "rackPositionUID", "RackPositionUID"));
		if (rackUid <= 0)
		{
			return null;
		}
		foreach (object rackPosition in ReflectionUtil.EnumerateCollection(rackPositions))
		{
			if (ToInt(ReflectionUtil.ReadMember(rackPosition, "rackPosGlobalUID", "RackPosGlobalUID")) == rackUid)
			{
				return rackPosition;
			}
		}
		return null;
	}

	private static bool SameUnityObject(object first, object second)
	{
		if (first == null || second == null)
		{
			return false;
		}
		if (ReferenceEquals(first, second) || first.Equals(second))
		{
			return true;
		}
		GameObject firstObject = ResolveGameObject(first);
		GameObject secondObject = ResolveGameObject(second);
		return firstObject != null && secondObject != null && firstObject == secondObject;
	}

	private object FindHeldModdedServer(out ServerVariantSpec modSpec)
	{
		modSpec = null;
		object player = Il2Cpp.PlayerManager.instance;
		Type serverType = ReflectionUtil.FindType("Server");
		if (player == null || serverType == null)
		{
			return null;
		}
		foreach (object heldObject in ReflectionUtil.EnumerateCollection(ReflectionUtil.ReadMember(player, "objectInHandGO")))
		{
			GameObject heldGameObject = ResolveGameObject(heldObject);
			if (heldGameObject == null)
			{
				continue;
			}
			object server = ReflectionUtil.GetComponent(heldGameObject, serverType) ?? ReflectionUtil.GetComponentsInChildren(heldGameObject, serverType).FirstOrDefault();
			if (server == null)
			{
				continue;
			}
			object inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
			if (!(inHands is bool held) || !held)
			{
				continue;
			}
			ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
			if (spec != null)
			{
				modSpec = spec;
				return server;
			}
		}
		return null;
	}

	private ServerVariantSpec ResolveLiveOwnedSpec(object server)
	{
		if (server == null)
		{
			return null;
		}
		if (_pendingSpecsByServer.TryGetValue(server, out ServerVariantSpec pendingSpec))
		{
			return pendingSpec;
		}
		if (_manualRackMountsByServer.TryGetValue(server, out ManualRackMount mount))
		{
			return mount.Spec;
		}
		ServerVariantSpec visualSpec = ResolveSpecFromCustomVisual(ResolveGameObject(server));
		if (visualSpec != null)
		{
			return visualSpec;
		}
		return ResolveExactOwnedSpec(server);
	}

	public bool ShouldAllowPowerButtonInteraction(string source, object server, bool forceState)
	{
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null || !spec.UsesLenovoCarrierProfile)
		{
			return true;
		}
		ConfigureLenovoPowerControls(server, spec, logWhenConfigured: false);
		if (!RuntimeOwnershipPolicy.AllowNativePower(isOwnedCarrier: true, IsServerInsertedInRack(server)))
		{
			LogBlockedRackInteractionOnce(
				"power|" + (ReadServerId(server) ?? ReflectionUtil.ReadUnityObjectName(server)),
				"Blocked Lenovo power button outside rack from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "'.");
			SyncLenovoPowerLed(server);
			return false;
		}
		// PowerButton owns duplicate-IP checks, notifications, timer registration,
		// and network route changes. Its common helpers alone omit those steps.
		return true;
	}

	public bool TryHandleLenovoConfigInteraction(string source, object server)
	{
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null || !spec.UsesLenovoCarrierProfile)
		{
			return true;
		}
		ConfigureLenovoIpConfigControls(server, spec, logWhenConfigured: false);
		if (!IsServerInsertedInRack(server))
		{
			return true;
		}
		if (!IsLookingAtLenovoIpConfigButton(server))
		{
			// Clear only stale reservations before the stock Server.InteractOnClick
			// takes this Lenovo into the player's hand.
			object currentRackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
			if (currentRackPosition != null)
			{
				ReconcileRackOccupancy(ReflectionUtil.ReadMember(currentRackPosition, "rack", "Rack"));
			}
			return true;
		}
		return OpenLenovoIpEditor(source, server);
	}

	public bool TryHandleLenovoConfigButtonInteraction(string source, object button)
	{
		GameObject buttonObject = ResolveGameObject(button);
		if (buttonObject == null || !buttonObject.name.StartsWith(LenovoIpConfigButtonName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		object server = FindServerOnParent(buttonObject.transform);
		if (server == null)
		{
			return true;
		}
		ServerVariantSpec spec = ResolveLiveOwnedSpec(server);
		if (spec == null || !spec.UsesLenovoCarrierProfile)
		{
			return true;
		}
		ConfigureLenovoIpConfigControls(server, spec, logWhenConfigured: false);
		// ButtonEdit's original UnityEvent targets the source server's power
		// method. Lenovo replaces only that event with its configuration action.
		return OpenLenovoIpEditor(source, server);
	}

	private bool OpenLenovoIpEditor(string source, object server)
	{
		if (!IsServerInsertedInRack(server))
		{
			return false;
		}
		OpenLenovoConfigurationPanel(source, server);
		return false;
	}

	private void OpenLenovoConfigurationPanel(string source, object server)
	{
		GameObject physicalCanvas = ResolveGameObject(ReflectionUtil.ReadMember(server, "canvas", "Canvas"));
		if (physicalCanvas != null)
		{
			physicalCanvas.SetActive(false);
		}
		if (_lenovoConfigurationPanel == null || _lenovoConfigurationPanel.Root == null)
		{
			_lenovoConfigurationPanel = CreateLenovoConfigurationPanel();
		}
		_lenovoConfigurationPanel.Server = server;
		_lenovoConfigurationPanel.Root.SetActive(true);
		ShowLenovoConfigurationCursor();
		RefreshLenovoConfigurationPanel();
		Log.Info("Opened Lenovo custom configuration panel from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "'.");
	}

	private LenovoConfigurationPanel CreateLenovoConfigurationPanel()
	{
		GameObject root = CreateUiObject("RealServers_LenovoConfiguration");
		Canvas canvas = root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 1000;
		CanvasScaler scaler = root.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		root.AddComponent<GraphicRaycaster>();
		SetStretch(GetRectTransform(root));

		RawImage shade = CreateRawImage("Shade", root.transform, Color.black * new Color(1f, 1f, 1f, 0.72f));
		SetStretch(GetRectTransform(shade.gameObject));
		RawImage panel = CreateRawImage("Panel", root.transform, new Color(0.055f, 0.065f, 0.075f, 0.98f));
		SetRect(GetRectTransform(panel.gameObject), new Vector2(0f, 0f), new Vector2(1160f, 660f));

		Texture2D logoTexture = GetLenovoConfigurationLogo();
		if (logoTexture != null)
		{
			RawImage logo = CreateRawImage("LenovoLogo", panel.transform, Color.white);
			logo.texture = logoTexture;
			SetRect(GetRectTransform(logo.gameObject), new Vector2(-455f, 265f), new Vector2(180f, 64f));
		}
		Text title = CreateText("Title", panel.transform, "Real Servers", 28, TextAnchor.MiddleLeft, new Vector2(-55f, 272f), new Vector2(550f, 38f), Color.white);
		CreateText("Subtitle", panel.transform, ModText.Get("ConfigurationSubtitle"), 16, TextAnchor.MiddleLeft, new Vector2(-55f, 233f), new Vector2(550f, 26f), new Color(0.72f, 0.78f, 0.84f, 1f));

		LenovoConfigurationPanel state = new LenovoConfigurationPanel(root);
		CreateButton("Close", panel.transform, ModText.Get("Close"), new Vector2(486f, 270f), new Vector2(112f, 42f), () => CloseLenovoConfigurationPanel(), state);

		RawImage customerCard = CreateRawImage("CustomerCard", panel.transform, new Color(0.02f, 0.025f, 0.03f, 1f));
		SetRect(GetRectTransform(customerCard.gameObject), new Vector2(-270f, -20f), new Vector2(440f, 440f));
		CreateText("CustomerTitle", customerCard.transform, ModText.Get("Customer"), 19, TextAnchor.MiddleLeft, new Vector2(0f, 186f), new Vector2(340f, 32f), Color.white);
		RawImage logoBackground = CreateRawImage("CustomerLogoBackground", customerCard.transform, Color.black);
		SetRect(GetRectTransform(logoBackground.gameObject), new Vector2(0f, 45f), new Vector2(340f, 190f));
		Image customerLogo = CreateImage("CustomerLogo", customerCard.transform, Color.white);
		customerLogo.preserveAspect = true;
		SetRect(GetRectTransform(customerLogo.gameObject), new Vector2(0f, 45f), new Vector2(310f, 165f));
		Text customerName = CreateText("CustomerName", customerCard.transform, ModText.Get("NoCustomer"), 20, TextAnchor.MiddleCenter, new Vector2(0f, -80f), new Vector2(360f, 38f), Color.white);
		CreateButton("PreviousCustomer", customerCard.transform, "<", new Vector2(-145f, -165f), new Vector2(82f, 46f), () => ChangeLenovoCustomer(false), state);
		CreateButton("NextCustomer", customerCard.transform, ">", new Vector2(145f, -165f), new Vector2(82f, 46f), () => ChangeLenovoCustomer(true), state);

		RawImage networkCard = CreateRawImage("NetworkCard", panel.transform, new Color(0.02f, 0.025f, 0.03f, 1f));
		SetRect(GetRectTransform(networkCard.gameObject), new Vector2(260f, -20f), new Vector2(580f, 440f));
		CreateText("NetworkTitle", networkCard.transform, ModText.Get("Network"), 19, TextAnchor.MiddleLeft, new Vector2(0f, 186f), new Vector2(480f, 32f), Color.white);
		CreateText("IpCaption", networkCard.transform, ModText.Get("CurrentIp"), 16, TextAnchor.MiddleLeft, new Vector2(0f, 112f), new Vector2(480f, 26f), new Color(0.72f, 0.78f, 0.84f, 1f));
		Text ipAddress = CreateText("IpAddress", networkCard.transform, "0.0.0.0", 40, TextAnchor.MiddleCenter, new Vector2(0f, 45f), new Vector2(480f, 64f), Color.white);
		CreateText("Description", networkCard.transform, ModText.Get("CustomerIpHint"), 16, TextAnchor.UpperLeft, new Vector2(0f, -65f), new Vector2(480f, 58f), new Color(0.82f, 0.86f, 0.9f, 1f));
		CreateButton("TogglePower", networkCard.transform, "\u23FB", new Vector2(-205f, -140f), new Vector2(60f, 60f), () => ToggleLenovoPanelPower(), state);
		CreateButton("ConfigureIp", networkCard.transform, ModText.Get("ConfigureIp"), new Vector2(40f, -140f), new Vector2(390f, 60f), () => OpenNativeLenovoIpEditor(), state);

		state.Title = title;
		state.PowerButtonLabel = networkCard.transform.Find("TogglePower/Label")?.GetComponent<Text>();
		if (state.PowerButtonLabel != null)
		{
			state.PowerButtonLabel.fontSize = 34;
		}
		state.PowerButtonBackground = networkCard.transform.Find("TogglePower")?.GetComponent<RawImage>();
		state.CustomerLogo = customerLogo;
		state.CustomerName = customerName;
		state.IpAddress = ipAddress;
		root.SetActive(false);
		return state;
	}

	private void ChangeLenovoCustomer(bool forward)
	{
		object server = _lenovoConfigurationPanel == null ? null : _lenovoConfigurationPanel.Server;
		if (server == null)
		{
			return;
		}
		ReflectionUtil.InvokeMethod(server, "ButtonClickChangeCustomer", forward);
		RefreshLenovoConfigurationPanel();
		_nextConfigurationPanelRefreshFrame = Time.frameCount + 2;
	}

	private void ToggleLenovoPanelPower()
	{
		object server = _lenovoConfigurationPanel == null ? null : _lenovoConfigurationPanel.Server;
		if (server == null)
		{
			return;
		}
		ToggleComputerAppPower(server);
		RefreshLenovoConfigurationPanel();
	}

	private void OpenNativeLenovoIpEditor()
	{
		object server = _lenovoConfigurationPanel == null ? null : _lenovoConfigurationPanel.Server;
		if (server == null)
		{
			return;
		}
		CloseLenovoConfigurationPanel(restorePlayerCursor: false);
		GameObject canvas = ResolveGameObject(ReflectionUtil.ReadMember(server, "canvas", "Canvas"));
		if (canvas != null)
		{
			canvas.SetActive(true);
		}
		if (!ReflectionUtil.InvokeMethod(server, "ButtonClickChangeIP"))
		{
			Log.Warning("Could not open native Lenovo IP editor: Server.ButtonClickChangeIP was not available.");
		}
		ShowLenovoConfigurationCursor();
	}

	private void CloseLenovoConfigurationPanel(bool restorePlayerCursor = true)
	{
		if (_lenovoConfigurationPanel != null && _lenovoConfigurationPanel.Root != null)
		{
			_lenovoConfigurationPanel.Root.SetActive(false);
		}
		if (restorePlayerCursor)
		{
			RestorePlayerCursor();
		}
	}

	public void MaintainLenovoConfigurationPanelInput()
	{
		if (_lenovoConfigurationPanel == null || _lenovoConfigurationPanel.Root == null || !_lenovoConfigurationPanel.Root.activeSelf)
		{
			return;
		}
		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			CloseLenovoConfigurationPanel();
		}
		else if (_nextConfigurationPanelRefreshFrame != 0 && Time.frameCount >= _nextConfigurationPanelRefreshFrame)
		{
			_nextConfigurationPanelRefreshFrame = 0;
			RefreshLenovoConfigurationPanel();
		}
	}

	private static void ShowLenovoConfigurationCursor()
	{
		try
		{
			Il2Cpp.PlayerManager.instance?.ConfinedCursorforUI();
		}
		catch (Exception exception)
		{
			Log.Warning("Could not enable the native UI cursor for the Lenovo configuration panel: " + exception.Message);
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.Confined;
		}
	}

	private static void RestorePlayerCursor()
	{
		try
		{
			Il2Cpp.PlayerManager.instance?.LockedCursorForPlayerMovement();
		}
		catch (Exception exception)
		{
			Log.Warning("Could not restore the native player cursor after closing the Lenovo configuration panel: " + exception.Message);
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
		}
	}

	private void RefreshLenovoConfigurationPanel()
	{
		LenovoConfigurationPanel panel = _lenovoConfigurationPanel;
		object server = panel == null ? null : panel.Server;
		if (panel == null || server == null)
		{
			return;
		}
		string ip = ReflectionUtil.ReadMember(server, "IP", "ip")?.ToString() ?? string.Empty;
		ServerVariantSpec spec = ResolveSpecForLiveServer(server);
		if (panel.Title != null)
		{
			panel.Title.text = spec?.VariantDisplayName ?? "Real Servers";
		}
		panel.IpAddress.text = string.IsNullOrWhiteSpace(ip) ? "0.0.0.0" : ip;
		int customerId = ConvertLenovoCustomerId(ReflectionUtil.InvokeMethodReturn(server, "GetCustomerID"));
		Image originalCustomerLogo = ReflectionUtil.ReadMember(server, "customerLogo", "CustomerLogo") as Image;
		Sprite sprite = originalCustomerLogo == null ? null : originalCustomerLogo.sprite;
		panel.CustomerLogo.sprite = sprite;
		panel.CustomerLogo.enabled = sprite != null && customerId >= 0;
		panel.CustomerName.text = customerId < 0 ? ModText.Get("NoCustomer") : ModText.Get("Customer") + " " + customerId.ToString(CultureInfo.InvariantCulture);
		if (panel.PowerButtonLabel != null)
		{
			panel.PowerButtonLabel.text = "\u23FB";
		}
		if (panel.PowerButtonBackground != null)
		{
			panel.PowerButtonBackground.color = IsServerPoweredOn(server)
				? new Color(0.5f, 0.15f, 0.12f, 1f)
				: new Color(0.08f, 0.31f, 0.43f, 1f);
		}
	}

	private static int ConvertLenovoCustomerId(object value)
	{
		try
		{
			return value == null ? -1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return -1;
		}
	}

	private Font GetLenovoConfigurationFont()
	{
		if (_lenovoConfigurationFont == null)
		{
			foreach (Text gameText in Resources.FindObjectsOfTypeAll<Text>())
			{
				if (gameText != null && gameText.font != null)
				{
					_lenovoConfigurationFont = gameText.font;
					break;
				}
			}
			_lenovoConfigurationFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}
		return _lenovoConfigurationFont;
	}

	private Texture2D GetLenovoConfigurationLogo()
	{
		if (_lenovoConfigurationLogo != null)
		{
			return _lenovoConfigurationLogo;
		}
		string assemblyPath = typeof(CatalogInjector).Assembly.Location;
		string modDirectory = string.IsNullOrWhiteSpace(assemblyPath) ? string.Empty : Path.GetDirectoryName(assemblyPath) ?? string.Empty;
		string[] candidates = { Path.Combine(modDirectory, "Assets", "lenovo.png"), Path.Combine(modDirectory, "lenovo.png"), Path.Combine(AppContext.BaseDirectory, "Mods", "RealServers", "Assets", "lenovo.png") };
		foreach (string path in candidates)
		{
			if (!File.Exists(path)) continue;
			try
			{
				Texture2D texture = new Texture2D(2, 2);
				if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
				{
					texture.name = "RealServers_LenovoLogo";
					_lenovoConfigurationLogo = texture;
					return texture;
				}
			}
			catch (Exception exception)
			{
				Log.Warning("Could not load Lenovo logo '" + path + "': " + exception.Message);
			}
		}
		return null;
	}

	private static GameObject CreateUiObject(string name)
	{
		return new GameObject(name, new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
	}

	private static RawImage CreateRawImage(string name, Transform parent, Color color)
	{
		GameObject obj = CreateUiObject(name);
		obj.transform.SetParent(parent, false);
		RawImage image = obj.AddComponent<RawImage>();
		image.texture = Texture2D.whiteTexture;
		image.color = color;
		return image;
	}

	private static Image CreateImage(string name, Transform parent, Color color)
	{
		GameObject obj = CreateUiObject(name);
		obj.transform.SetParent(parent, false);
		Image image = obj.AddComponent<Image>();
		image.color = color;
		return image;
	}

	private Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color)
	{
		GameObject obj = CreateUiObject(name);
		obj.transform.SetParent(parent, false);
		Text text = obj.AddComponent<Text>();
		text.font = GetLenovoConfigurationFont();
		text.text = value;
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		SetRect(GetRectTransform(obj), position, size);
		return text;
	}

	private void CreateButton(string name, Transform parent, string label, Vector2 position, Vector2 size, Action callback, LenovoConfigurationPanel state)
	{
		RawImage background = CreateRawImage(name, parent, new Color(0.08f, 0.31f, 0.43f, 1f));
		SetRect(GetRectTransform(background.gameObject), position, size);
		Button button = background.gameObject.AddComponent<Button>();
		button.targetGraphic = background;
		UnityAction action = (UnityAction)callback;
		button.onClick.AddListener(action);
		state.Actions.Add(action);
		CreateText("Label", background.transform, label, 18, TextAnchor.MiddleCenter, Vector2.zero, size, Color.white);
	}

	private static void SetStretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private static RectTransform GetRectTransform(GameObject obj)
	{
		RectTransform rect = obj == null ? null : obj.GetComponent<RectTransform>();
		if (rect == null)
		{
			throw new InvalidOperationException("UI object '" + (obj == null ? "<null>" : obj.name) + "' was created without a RectTransform.");
		}
		return rect;
	}

	private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
	{
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = position;
		rect.sizeDelta = size;
	}

	private sealed class LenovoConfigurationPanel
	{
		public LenovoConfigurationPanel(GameObject root) { Root = root; }
		public GameObject Root { get; }
		public object Server { get; set; }
	public Image CustomerLogo { get; set; }
	public Text Title { get; set; }
	public Text CustomerName { get; set; }
	public Text IpAddress { get; set; }
	public Text PowerButtonLabel { get; set; }
	public RawImage PowerButtonBackground { get; set; }
		public List<UnityAction> Actions { get; } = new List<UnityAction>();
	}

	private static object FindServerOnParent(Transform transform)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			return null;
		}
		for (Transform current = transform; current != null; current = current.parent)
		{
			object server = ReflectionUtil.GetComponent(current.gameObject, serverType);
			if (server != null)
			{
				return server;
			}
		}
		return null;
	}

	private void ConfigureLenovoIpConfigControls(object server, ServerVariantSpec spec, bool logWhenConfigured)
	{
		if (!spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		ConfigureServer(server, spec);
		GameObject serverObject = ResolveGameObject(server);
		Transform marker = FindLenovoMarker(server, "IpConfigButton");
		GameObject canvas = ResolveGameObject(ReflectionUtil.ReadMember(server, "canvas", "Canvas"));
		if (serverObject == null || marker == null)
		{
			return;
		}
		GameObject buttonObject = FindOriginalLenovoIpConfigButton(serverObject);
		if (buttonObject == null)
		{
			Log.Warning("Could not position Lenovo IP configuration button: original ButtonEdit was not found.");
			return;
		}
		buttonObject.layer = serverObject.layer;
		HideOriginalLenovoCustomerArrows(serverObject);
		buttonObject.SetActive(true);
		ShowRenderers(buttonObject);
		Quaternion originalLocalRotation = buttonObject.transform.localRotation;
		Vector3 originalLocalScale = buttonObject.transform.localScale;
		Vector3 buttonWorldPosition = marker.position + marker.parent.TransformVector(LenovoIpConfigButtonMarkerOffset);
		buttonObject.transform.SetParent(serverObject.transform, worldPositionStays: false);
		buttonObject.transform.localPosition = serverObject.transform.InverseTransformPoint(buttonWorldPosition);
		buttonObject.transform.localRotation = originalLocalRotation;
		buttonObject.transform.localScale = originalLocalScale;
		AlignLenovoPowerButtonRenderedCenter(buttonObject, buttonWorldPosition);
		EnsureLenovoPowerButtonCollider(buttonObject, buttonWorldPosition, serverObject.transform);
		if (canvas != null)
		{
			canvas.SetActive(false);
		}
		if (logWhenConfigured)
		{
			Log.Info("Positioned original Lenovo ButtonEdit from base server: marker=" + marker.name + ", world=" + FormatVector(buttonObject.transform.position) + ", physicalCanvas=" + (canvas == null ? "<missing>" : canvas.name) + ".");
		}
	}

	private static void HideOriginalLenovoCustomerArrows(GameObject serverObject)
	{
		foreach (Transform transform in serverObject.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			if (transform != null && transform.name.StartsWith("ButtonArrow", StringComparison.OrdinalIgnoreCase))
			{
				transform.gameObject.SetActive(false);
			}
		}
	}

	private static GameObject FindOriginalLenovoIpConfigButton(GameObject serverObject)
	{
		if (serverObject == null)
		{
			return null;
		}
		foreach (Transform transform in serverObject.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			if (transform == null || transform.gameObject == null)
			{
				continue;
			}
			if (transform.name.StartsWith("ButtonEdit", StringComparison.OrdinalIgnoreCase))
			{
				return transform.gameObject;
			}
		}
		return null;
	}

	private static bool IsLookingAtLenovoIpConfigButton(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		Camera camera = Camera.main;
		if (serverObject == null || camera == null)
		{
			return false;
		}
		if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 4f))
		{
			return false;
		}
		for (Transform current = hit.collider == null ? null : hit.collider.transform; current != null; current = current.parent)
		{
			if (current.name.StartsWith(LenovoIpConfigButtonName, StringComparison.OrdinalIgnoreCase))
			{
				return current.IsChildOf(serverObject.transform);
			}
		}
		return false;
	}


	private void ConfigureLenovoPowerControls(object server, ServerVariantSpec spec, bool logWhenConfigured = true)
	{
		if (!spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		_lenovoPowerControlSpecsByServer[server] = spec;
		Transform marker = FindLenovoMarker(server, "PowerButton");
		GameObject serverObject = ResolveGameObject(server);
		GameObject powerButtonObject = ResolveGameObject(ReflectionUtil.ReadMember(server, "powerButton", "PowerButton"));
		if (powerButtonObject == null && _lenovoPowerButtonObjectsByServer.TryGetValue(server, out GameObject cachedPowerButton))
		{
			powerButtonObject = cachedPowerButton;
		}
		if (powerButtonObject != null && serverObject != null)
		{
			powerButtonObject.SetActive(true);
			if (marker != null)
			{
				Quaternion originalLocalRotation = powerButtonObject.transform.localRotation;
				Vector3 originalLocalScale = powerButtonObject.transform.localScale;
				Vector3 markerLocalPosition = serverObject.transform.InverseTransformPoint(marker.position);
				powerButtonObject.transform.SetParent(serverObject.transform, worldPositionStays: false);
				powerButtonObject.transform.localPosition = markerLocalPosition + LenovoPowerButtonLocalOffset;
				powerButtonObject.transform.localRotation = originalLocalRotation;
				powerButtonObject.transform.localScale = originalLocalScale;
			}
			RemoveLenovoPowerButtonDebugOutline(powerButtonObject);
			ShowRenderers(powerButtonObject);
			// ButtonPower.001 has an internal mesh offset. Its transform origin is not
			// the visible button center, so align the rendered bounds to the Blender marker.
			if (marker != null)
			{
				Vector3 buttonWorldCenter = marker.position + serverObject.transform.TransformVector(LenovoPowerButtonLocalOffset);
				AlignLenovoPowerButtonRenderedCenter(powerButtonObject, buttonWorldCenter);
			}
			EnsureLenovoPowerButtonCollider(powerButtonObject, marker == null ? powerButtonObject.transform.position : marker.position, serverObject.transform);
			_lenovoPowerButtonObjectsByServer[server] = powerButtonObject;
			if (logWhenConfigured)
			{
				Log.Info("Positioned original Lenovo power button from base server: object='" + powerButtonObject.name + "', parent='" + (powerButtonObject.transform.parent == null ? "" : powerButtonObject.transform.parent.name) + "', marker=" + (marker == null ? "<none>" : marker.name) + ", world=" + FormatVector(powerButtonObject.transform.position) + ".");
			}
		}
		RemoveLenovoPowerLed(marker);
		SyncLenovoPowerLed(server);
	}

	private void MaintainLenovoPowerControls(string source)
	{
		if (_lenovoPowerControlSpecsByServer.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<object, ServerVariantSpec> item in _lenovoPowerControlSpecsByServer.ToArray())
		{
			if (ResolveGameObject(item.Key) == null)
			{
				_lenovoPowerControlSpecsByServer.Remove(item.Key);
				_lenovoPowerButtonObjectsByServer.Remove(item.Key);
			}
		}
	}

	private static void ShowRenderers(GameObject gameObject)
	{
		foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer != null)
			{
				renderer.enabled = true;
			}
		}
	}

	private static void RemoveLenovoPowerButtonDebugOutline(GameObject powerButtonObject)
	{
		if (powerButtonObject == null)
		{
			return;
		}
		Transform existing = powerButtonObject.transform.Find("RealServers_PowerButtonOutline");
		if (existing != null)
		{
			UnityEngine.Object.Destroy(existing.gameObject);
		}
	}

	private static void EnsureLenovoPowerButtonCollider(GameObject powerButtonObject, Vector3 markerWorldPosition, Transform serverTransform)
	{
		if (powerButtonObject == null)
		{
			return;
		}
		int enabledChildren = 0;
		foreach (Collider collider in powerButtonObject.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			if (collider == null)
			{
				continue;
			}
			if (collider.gameObject == powerButtonObject)
			{
				continue;
			}
			if (!collider.enabled)
			{
				collider.enabled = true;
				enabledChildren++;
			}
		}
		BoxCollider boxCollider = powerButtonObject.GetComponent<BoxCollider>();
		if (boxCollider == null)
		{
			boxCollider = powerButtonObject.AddComponent<BoxCollider>();
		}
		boxCollider.enabled = true;
		boxCollider.isTrigger = false;
		Vector3 frontDirection = serverTransform == null ? -powerButtonObject.transform.forward : serverTransform.TransformDirection(Vector3.back);
		Vector3 colliderWorldCenter = markerWorldPosition + frontDirection.normalized * LenovoPowerInteractionFrontOffset;
		boxCollider.center = powerButtonObject.transform.InverseTransformPoint(colliderWorldCenter);
		boxCollider.size = WorldSizeToLocalBoxSize(boxCollider.transform, LenovoPowerInteractionWorldSize);
		Log.Info("Ensured Lenovo power button collider on '" + powerButtonObject.name + "': enabledChildColliders=" + enabledChildren.ToString(CultureInfo.InvariantCulture) + ", markerWorld=" + FormatVector(markerWorldPosition) + ", colliderWorld=" + FormatVector(colliderWorldCenter) + ", localCenter=" + FormatVector(boxCollider.center) + ", localBoxSize=" + FormatVector(boxCollider.size) + ".");
	}

	private static void AlignLenovoPowerButtonRenderedCenter(GameObject powerButtonObject, Vector3 targetWorldPosition)
	{
		Bounds? worldBounds = TryGetWorldRendererBounds(powerButtonObject);
		if (worldBounds == null)
		{
			Log.Warning("Could not align Lenovo power button to marker: no renderer bounds found on '" + powerButtonObject.name + "'.");
			return;
		}
		Vector3 delta = targetWorldPosition - worldBounds.Value.center;
		if (delta.sqrMagnitude > 0.00000001f)
		{
			powerButtonObject.transform.position += delta;
		}
		Bounds? adjustedBounds = TryGetWorldRendererBounds(powerButtonObject);
		Log.Info("Aligned Lenovo power button rendered center to GLB marker: target=" + FormatVector(targetWorldPosition) + ", delta=" + FormatVector(delta) + ", before=" + FormatBounds(worldBounds.Value) + ", after=" + (adjustedBounds == null ? "<none>" : FormatBounds(adjustedBounds.Value)) + ".");
	}

	private static Bounds? TryGetWorldRendererBounds(GameObject root)
	{
		if (root == null)
		{
			return null;
		}
		bool hasBounds = false;
		Bounds bounds = default;
		foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null)
			{
				continue;
			}
			if (!hasBounds)
			{
				bounds = renderer.bounds;
				hasBounds = true;
			}
			else
			{
				bounds.Encapsulate(renderer.bounds);
			}
		}
		return hasBounds ? bounds : null;
	}

	private static Bounds? TryGetRendererBoundsInLocal(Transform localTransform, GameObject root)
	{
		if (localTransform == null || root == null)
		{
			return null;
		}
		bool hasBounds = false;
		Bounds localBounds = default;
		foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null)
			{
				continue;
			}
			Bounds worldBounds = renderer.bounds;
			Vector3 min = worldBounds.min;
			Vector3 max = worldBounds.max;
			Vector3[] corners =
			{
				new Vector3(min.x, min.y, min.z),
				new Vector3(min.x, min.y, max.z),
				new Vector3(min.x, max.y, min.z),
				new Vector3(min.x, max.y, max.z),
				new Vector3(max.x, min.y, min.z),
				new Vector3(max.x, min.y, max.z),
				new Vector3(max.x, max.y, min.z),
				new Vector3(max.x, max.y, max.z)
			};
			foreach (Vector3 corner in corners)
			{
				Vector3 localCorner = localTransform.InverseTransformPoint(corner);
				if (!hasBounds)
				{
					localBounds = new Bounds(localCorner, Vector3.zero);
					hasBounds = true;
				}
				else
				{
					localBounds.Encapsulate(localCorner);
				}
			}
		}
		return hasBounds ? localBounds : null;
	}

	private static void EnsureLenovoPowerLed(Transform marker)
	{
		if (marker == null)
		{
			return;
		}
		Transform existing = marker.Find("RealServers_PowerLed");
		GameObject ledObject;
		if (existing == null)
		{
			ledObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			ledObject.name = "RealServers_PowerLed";
			ledObject.transform.SetParent(marker, worldPositionStays: false);
			Collider collider = ledObject.GetComponent<Collider>();
			if (collider != null)
			{
				UnityEngine.Object.Destroy(collider);
			}
		}
		else
		{
			ledObject = existing.gameObject;
		}
		ledObject.transform.localPosition = LenovoPowerLedLocalOffset;
		ledObject.transform.localRotation = Quaternion.identity;
		ledObject.transform.localScale = WorldSizeToLocalBoxSize(ledObject.transform, new Vector3(0.006f, 0.006f, 0.006f));
		Renderer renderer = ledObject.GetComponent<Renderer>();
		if (renderer != null)
		{
			renderer.enabled = true;
			renderer.material = LenovoPowerLedMaterial();
		}
		Transform lightTransform = marker.Find("RealServers_PowerLedLight");
		if (lightTransform != null)
		{
			UnityEngine.Object.Destroy(lightTransform.gameObject);
		}
	}

	private static bool SyncLenovoPowerLed(object server)
	{
		Transform marker = FindLenovoMarker(server, "PowerButton");
		if (marker == null)
		{
			return false;
		}
		RemoveLenovoPowerLed(marker);
		return false;
	}

	private static void RemoveLenovoPowerLed(Transform marker)
	{
		if (marker == null)
		{
			return;
		}
		Transform led = marker.Find("RealServers_PowerLed");
		if (led != null)
		{
			UnityEngine.Object.Destroy(led.gameObject);
		}
		Transform light = marker.Find("RealServers_PowerLedLight");
		if (light != null)
		{
			UnityEngine.Object.Destroy(light.gameObject);
		}
	}

	private static Material LenovoPowerLedMaterial()
	{
		if (_lenovoPowerLedMaterial != null)
		{
			return _lenovoPowerLedMaterial;
		}
		Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
		_lenovoPowerLedMaterial = new Material(shader);
		_lenovoPowerLedMaterial.name = "RealServers_PowerLed_Green";
		_lenovoPowerLedMaterial.color = LenovoPowerLedColor;
		if (_lenovoPowerLedMaterial.HasProperty("_BaseColor"))
		{
			_lenovoPowerLedMaterial.SetColor("_BaseColor", LenovoPowerLedColor);
		}
		if (_lenovoPowerLedMaterial.HasProperty("_EmissionColor"))
		{
			_lenovoPowerLedMaterial.SetColor("_EmissionColor", LenovoPowerLedColor * 0.75f);
			_lenovoPowerLedMaterial.EnableKeyword("_EMISSION");
		}
		return _lenovoPowerLedMaterial;
	}

	private ServerVariantSpec? ResolveSpecFromTechnician(object technician)
	{
		object obj = ReflectionUtil.ReadMember(technician, "server");
		if (obj != null)
		{
			ServerVariantSpec serverVariantSpec = ResolveSpecForServer(obj);
			if (serverVariantSpec != null)
			{
				return serverVariantSpec;
			}
		}
		object obj2 = ReflectionUtil.ReadMember(technician, "currentJob", "CurrentJob");
		object obj3 = ((obj2 == null) ? null : ReflectionUtil.ReadMember(obj2, "server"));
		if (obj3 != null)
		{
			return ResolveSpecForServer(obj3);
		}
		return null;
	}

	private static IEnumerable<object> EnumerateTechnicianServerCandidates(object technician)
	{
		Type serverType = ReflectionUtil.FindType("Server");
		if (serverType == null)
		{
			yield break;
		}
		HashSet<object> yielded = new HashSet<object>(ReferenceEqualityComparer.Instance);
		string[] array = new string[2] { "server", "deviceInHand" };
		foreach (string text in array)
		{
			object candidate = ReflectionUtil.ReadMember(technician, text);
			if (candidate == null)
			{
				continue;
			}
			if (serverType.IsInstanceOfType(candidate) && yielded.Add(candidate))
			{
				yield return candidate;
			}
			foreach (object componentsInChild in ReflectionUtil.GetComponentsInChildren(candidate, serverType))
			{
				if (yielded.Add(componentsInChild))
				{
					yield return componentsInChild;
				}
			}
		}
	}

	private static bool ConfigureServer(object server, ServerVariantSpec spec)
	{
		bool flag = false;
		flag |= WriteMemberIfDifferent(server, spec.RuntimeProcessingSpeed, "maxProcessingSpeed");
		flag |= ClampRuntimeSpeed(server, spec.RuntimeProcessingSpeed, "currentProcessingSpeed");
		flag |= ClampRuntimeSpeed(server, spec.RuntimeProcessingSpeed, "effectiveProcessingSpeed");
		flag |= ConfigureRackUnitProfile(server, spec);
		return flag;
	}

	private static bool ConfigureRackUnitProfile(object target, ServerVariantSpec spec)
	{
		bool changed = false;
		changed |= WriteMemberIfDifferent(target, spec.RackUnits, "sizeInU", "SizeInU", "rackUnits", "RackUnits", "unitSize", "UnitSize", "uSize", "USize");
		changed |= WriteMemberIfDifferent(target, spec.RackUnits, "serverSizeInU", "ServerSizeInU", "rackHeightInU", "RackHeightInU");
		changed |= WriteMemberIfDifferent(target, (float)spec.RackUnits, "rackUnitHeight", "RackUnitHeight");
		if (changed)
		{
			Log.Info("Applied " + spec.RackUnits.ToString(CultureInfo.InvariantCulture) + "U rack profile for " + spec.VariantDisplayName + " on " + ReflectionUtil.SafeTypeName(target) + ".");
		}
		return changed;
	}

	private static void SetPlayerHandProfile(object server, ServerVariantSpec spec, string source)
	{
		bool flag = WriteMemberIfDifferent(server, spec.HeldObjectType, "objectInHandType", "ObjectInHandType");
		flag |= WriteMemberIfDifferent(server, spec.RackUnits, "sizeInU", "SizeInU");
		object player = Il2Cpp.PlayerManager.instance;
		if (player == null)
		{
			if (flag)
			{
				Log.Info("Set " + spec.VariantDisplayName + " usable-object rack profile to " + spec.RackUnits.ToString(CultureInfo.InvariantCulture) + "U from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "'.");
			}
			return;
		}
		flag |= WriteMemberIfDifferent(player, spec.HeldObjectType, "objectInHand");
		if (flag)
		{
			Log.Info("Set " + spec.VariantDisplayName + " held-object rack profile to " + spec.RackUnits.ToString(CultureInfo.InvariantCulture) + "U from " + source + ": serverId='" + (ReadServerId(server) ?? "") + "'.");
		}
	}

	private static void ForceServerDisplayRefresh(object server)
	{
		ReflectionUtil.WriteMember(server, -1f, "lastDisplayedMaxSpeed");
		ReflectionUtil.WriteMember(server, -1f, "lastDisplayedProcessingSpeed");
		ReflectionUtil.WriteMember(server, -1, "lastDisplayedEolMinute");
		ReflectionUtil.WriteMember(server, string.Empty, "lastDisplayedLabel");
	}

	private static bool ClampRuntimeSpeed(object server, float maxRuntimeSpeed, params string[] names)
	{
		object obj = ReflectionUtil.ReadMember(server, names);
		if (obj == null)
		{
			return false;
		}
		double num = ToDouble(obj);
		if (double.IsNaN(num) || double.IsInfinity(num) || num < 0.0 || num > (double)maxRuntimeSpeed)
		{
			return WriteMemberIfDifferent(server, 0f, names);
		}
		return false;
	}

	private static IEnumerable<object> EnumerateServerCableLinks(object server, Type cableLinkType)
	{
		HashSet<object> yielded = new HashSet<object>(ReferenceEqualityComparer.Instance);
		string[] array = new string[4] { "cablelinks", "activeLinks", "cableLinks", "CableLinks" };
		foreach (string text in array)
		{
			object value = ReflectionUtil.ReadMember(server, text);
			foreach (object item in ReflectionUtil.EnumerateCollection(value))
			{
				object obj = ReflectionUtil.TryCastIl2CppObject(item, cableLinkType) ?? item;
				if (cableLinkType.IsInstanceOfType(obj) && IsServerCableLinkForServer(obj, server) && yielded.Add(obj))
				{
					yield return obj;
				}
			}
		}
		foreach (object componentsInChild in ReflectionUtil.GetComponentsInChildren(server, cableLinkType))
		{
			if (IsServerCableLinkForServer(componentsInChild, server) && yielded.Add(componentsInChild))
			{
				yield return componentsInChild;
			}
		}
	}

	private static bool ConfigureCableLink(object link, ServerVariantSpec spec, object? parentServer, int portIndex)
	{
		if (parentServer == null || !IsServerCableLinkForServer(link, parentServer))
		{
			return false;
		}
		if (spec.UsesLenovoCarrierProfile && portIndex >= LenovoCablePortCount)
		{
			ReflectionUtil.SetGameObjectActive(link, false);
			Log.Info("Disabled extra Lenovo cable link for " + spec.VariantDisplayName + ": object='" + ReflectionUtil.ReadUnityObjectName(link) + "', index=" + portIndex.ToString(CultureInfo.InvariantCulture) + ".");
			return false;
		}
		ReflectionUtil.SetGameObjectActive(link, true);
		if (spec.UsesLenovoCarrierProfile)
		{
			bool changed = false;
			string before = DescribeCableLinkState(link);
			changed |= WriteMemberIfDifferent(link, false, "isSFPPort");
			changed |= WriteMemberIfDifferent(link, false, "isFibrePort");
			changed |= WriteMemberIfDifferent(link, 0, "sfpTypeSupported");
			changed |= WriteMemberIfDifferent(link, 0, "sfpTypeInserted");
			changed |= WriteMemberIfDifferent(link, null, "insertedSFP");
			changed |= WriteMemberIfDifferent(link, portIndex, "portIndex", "linkIndex");
			changed |= WriteMemberIfDifferent(link, parentServer, "parentServer");
			string serverId = ReadServerId(parentServer) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(serverId))
			{
				changed |= WriteMemberIfDifferent(link, serverId, "serverID", "ServerID", "serverId", "ServerId", "parentServerID", "parentServerId");
			}
			if (changed)
			{
				Log.Info($"Configured Lenovo test port as original copper server port for {spec.VariantDisplayName}: before=[{before}], after=[{DescribeCableLinkState(link)}].");
			}
			return changed;
		}
		bool flag = false;
		string value = DescribeCableLinkState(link);
		float runtimeNetworkSpeed = spec.RuntimeNetworkSpeed;
		int sfpType = spec.SfpType;
		bool flag2 = WriteMemberIfDifferent(link, runtimeNetworkSpeed, "connectionSpeed");
		flag = flag || flag2;
		if (flag2)
		{
			ReflectionUtil.InvokeMethod(link, "SetConnectionSpeed", runtimeNetworkSpeed);
		}
		flag |= WriteMemberIfDifferent(link, true, "isSFPPort");
		flag |= WriteMemberIfDifferent(link, true, "isFibrePort");
		flag |= WriteMemberIfDifferent(link, sfpType, "sfpTypeSupported");
		flag |= WriteMemberIfDifferent(link, sfpType, "sfpTypeInserted");
		flag |= WriteMemberIfDifferent(link, 0, "portIndex", "linkIndex");
		if (spec.DirectFiberPort)
		{
			flag |= WriteMemberIfDifferent(link, null, "insertedSFP");
		}
		flag |= WriteMemberIfDifferent(link, parentServer, "parentServer");
		if (flag)
		{
			Log.Info($"Configured modded server port for {spec.VariantDisplayName}: before=[{value}], after=[{DescribeCableLinkState(link)}], expected={spec.ConnectorHint}/{spec.NetworkSpeedGbps.ToString(CultureInfo.InvariantCulture)}G runtimeSpeed={spec.RuntimeNetworkSpeed.ToString(CultureInfo.InvariantCulture)} lanes={spec.FiberLaneCount}.");
		}
		return flag;
	}

	private static void ConfigureCableLinkTransform(object link, ServerVariantSpec spec, object server, int portIndex)
	{
		try
		{
			ConfigureCableLinkTransformUnsafe(link, spec, server, portIndex);
		}
		catch (Exception ex)
		{
			Log.Warning("Skipped Lenovo cable link transform for " + spec.VariantDisplayName + ": object='" + ReflectionUtil.ReadUnityObjectName(link) + "', index=" + portIndex.ToString(CultureInfo.InvariantCulture) + ", error=" + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void ConfigureCableLinkTransformUnsafe(object link, ServerVariantSpec spec, object server, int portIndex)
	{
		if (!spec.UsesLenovoCarrierProfile)
		{
			return;
		}
		if (link is not Component component)
		{
			return;
		}
		if (portIndex >= LenovoCablePortCount)
		{
			GameObject extraLinkObject = SafeGameObject(component);
			if (extraLinkObject != null)
			{
				extraLinkObject.SetActive(false);
			}
			return;
		}
		GameObject linkObject = SafeGameObject(component);
		if (linkObject == null)
		{
			return;
		}
		Transform transform = SafeTransform(component);
		if (transform == null)
		{
			return;
		}
		Vector3 before = transform.localPosition;
		Transform cableMarker = FindCablePortTransform(server, portIndex);
		Vector3 cableWorld = Vector3.zero;
		bool usesDellStaticCarrierProfile = spec.UsesDellStaticCarrierProfile;
		if (cableMarker != null)
		{
			transform.SetParent(cableMarker, worldPositionStays: false);
			transform.localPosition = usesDellStaticCarrierProfile ? new Vector3(0f, 0f, DellR440PortVisualInset) : Vector3.zero;
			transform.localRotation = usesDellStaticCarrierProfile
				? Quaternion.Euler(0f, 180f, 0f)
				: Quaternion.identity;
			if (usesDellStaticCarrierProfile)
			{
				// Scale only the native RJ45 visual so it fills the modeled Dell
				// socket. The port marker and interaction collider stay unchanged.
				transform.localScale = new Vector3(3f, 3f, 3f);
			}
			cableWorld = transform.position;
			Log.Info("Parented Lenovo cable link to GLB marker '" + cableMarker.name + "'.");
		}
		else
		{
			cableWorld = LenovoFallbackCablePortWorldPosition(server, portIndex);
			if (cableWorld != Vector3.zero)
			{
				transform.position = cableWorld;
			}
			transform.localRotation = Quaternion.identity;
		}
		if (cableWorld != Vector3.zero)
		{
			ReflectionUtil.WriteMember(link, transform, "linkTransform", "portTransform", "networkPort", "port");
			ReflectionUtil.WriteMember(link, cableWorld, "portPosition", "linkPosition");
		}
		EnsureLenovoCableLinkCollider(
			linkObject,
			usesDellStaticCarrierProfile ? DellR440PortVisualInset : 0f,
			usesDellStaticCarrierProfile ? DellR440CableInteractionWorldSize : LenovoCableInteractionWorldSize);
		Log.Info("Moved cable link for " + spec.VariantDisplayName + ": object='" + ReflectionUtil.ReadUnityObjectName(link) + "', index=" + portIndex.ToString(CultureInfo.InvariantCulture) + ", beforeLocal=" + FormatVector(before) + ", afterLocal=" + FormatVector(transform.localPosition) + ", cableWorld=" + FormatVector(cableWorld) + ".");
	}

	private static GameObject SafeGameObject(Component component)
	{
		try
		{
			return component == null ? null : component.gameObject;
		}
		catch
		{
			return null;
		}
	}

	private static Transform SafeTransform(Component component)
	{
		try
		{
			return component == null ? null : component.transform;
		}
		catch
		{
			return null;
		}
	}

	private static void EnsureLenovoCableLinkCollider(GameObject linkObject, float localForwardOffset, Vector3 interactionWorldSize)
	{
		if (linkObject == null)
		{
			return;
		}
		linkObject.SetActive(true);
		Transform staleInteraction = linkObject.transform.Find("RealServersCableInteraction");
		if (staleInteraction != null)
		{
			UnityEngine.Object.Destroy(staleInteraction.gameObject);
		}
		int disabled = 0;
		foreach (Collider collider in linkObject.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			if (collider == null)
			{
				continue;
			}
			if (collider.gameObject == linkObject)
			{
				continue;
			}
			collider.enabled = false;
			disabled++;
		}
		BoxCollider boxCollider = linkObject.GetComponent<BoxCollider>();
		if (boxCollider == null)
		{
			boxCollider = linkObject.AddComponent<BoxCollider>();
		}
		boxCollider.enabled = true;
		boxCollider.isTrigger = false;
		boxCollider.center = WorldVectorToLocal(boxCollider.transform, LenovoCableInteractionWorldCenter) + new Vector3(0f, 0f, localForwardOffset);
		boxCollider.size = WorldSizeToLocalBoxSize(boxCollider.transform, interactionWorldSize);
		Log.Info("Ensured Lenovo cable link collider on '" + linkObject.name + "': disabledChildColliders=" + disabled.ToString(CultureInfo.InvariantCulture) + ", center=" + FormatVector(boxCollider.center) + ", localBoxSize=" + FormatVector(boxCollider.size) + ", lossyScale=" + FormatVector(boxCollider.transform.lossyScale) + ".");
	}

	private static Vector3 WorldVectorToLocal(Transform transform, Vector3 worldVector)
	{
		Vector3 scale = transform == null ? Vector3.one : transform.lossyScale;
		return new Vector3(
			worldVector.x / Math.Max(Math.Abs(scale.x), 0.001f),
			worldVector.y / Math.Max(Math.Abs(scale.y), 0.001f),
			worldVector.z / Math.Max(Math.Abs(scale.z), 0.001f));
	}

	private static Vector3 WorldSizeToLocalBoxSize(Transform transform, Vector3 targetWorldSize)
	{
		return new Vector3(
			Math.Abs(WorldVectorToLocal(transform, targetWorldSize).x),
			Math.Abs(WorldVectorToLocal(transform, targetWorldSize).y),
			Math.Abs(WorldVectorToLocal(transform, targetWorldSize).z));
	}

	private static Transform FindCablePortTransform(object server, int portIndex)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return null;
		}
		string preferredName = "Port_" + (Math.Min(portIndex, 3) + 1).ToString("00", CultureInfo.InvariantCulture);
		Transform marker = FindChildRecursive(serverObject.transform, preferredName);
		if (marker == null && portIndex > 0)
		{
			marker = FindChildRecursive(serverObject.transform, "Port_01");
		}
		if (marker == null)
		{
			return null;
		}
		return marker;
	}

	private static Transform FindCableInteractionTransform(object server, int portIndex)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return null;
		}
		string preferredName = "Interaction_" + (Math.Min(portIndex, 3) + 1).ToString("00", CultureInfo.InvariantCulture);
		Transform marker = FindChildRecursive(serverObject.transform, preferredName);
		if (marker == null)
		{
			marker = FindCablePortTransform(server, portIndex);
		}
		return marker;
	}

	private static Transform FindLenovoVisualRoot(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return null;
		}
		return FindChildRecursiveByPrefix(serverObject.transform, "RealServers_VisualRoot");
	}

	private static Transform FindLenovoMarker(object server, string markerName)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return null;
		}
		return FindChildRecursive(serverObject.transform, markerName);
	}

	private static Vector3 LenovoFallbackCablePortWorldPosition(object server, int portIndex)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return Vector3.zero;
		}
		float x = -0.185f + Math.Min(portIndex, 1) * 0.031f;
		Vector3 local = new Vector3(x, -0.007f, -0.423f);
		return serverObject.transform.TransformPoint(local);
	}

	private static Transform FindChildRecursive(Transform root, string name)
	{
		if (root == null)
		{
			return null;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (child == null)
			{
				continue;
			}
			if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
			{
				return child;
			}
			Transform nested = FindChildRecursive(child, name);
			if (nested != null)
			{
				return nested;
			}
		}
		return null;
	}

	private static Transform FindChildRecursiveByPrefix(Transform root, string prefix)
	{
		if (root == null)
		{
			return null;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (child == null)
			{
				continue;
			}
			if (child.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return child;
			}
			Transform nested = FindChildRecursiveByPrefix(child, prefix);
			if (nested != null)
			{
				return nested;
			}
		}
		return null;
	}

	internal static GameObject ResolveGameObject(object unityObject)
	{
		if (unityObject == null)
		{
			return null;
		}
		try
		{
			if (unityObject is GameObject gameObject)
			{
				return gameObject;
			}
			if (unityObject is Transform transform)
			{
				return transform.gameObject;
			}
			if (unityObject is Component component)
			{
				return component.gameObject;
			}
			return ReflectionUtil.ReadMember(unityObject, "gameObject", "GameObject") as GameObject;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string FormatVector(Vector3 value)
	{
		return "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture) + ", " + value.y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + value.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
	}

	private static string FormatBounds(Bounds bounds)
	{
		return "center=" + FormatVector(bounds.center) + ", size=" + FormatVector(bounds.size);
	}

	public bool ShouldAllowCableLinkInteraction(string source, object cableLink)
	{
		Type type = ReflectionUtil.FindType("CableLink");
		if (type == null || !type.IsInstanceOfType(cableLink))
		{
			return true;
		}
		object obj = ReflectionUtil.ReadMember(cableLink, "parentServer");
		if (obj == null || !IsServerCableLinkForServer(cableLink, obj))
		{
			return true;
		}
		ServerVariantSpec serverVariantSpec = ResolveLiveOwnedSpec(obj);
		if (serverVariantSpec == null)
		{
			return true;
		}
		if (serverVariantSpec.UsesLenovoCarrierProfile)
		{
			if (IsServerInsertedInRack(obj))
			{
				return true;
			}
			LogBlockedRackInteractionOnce(
				"cable|" + (ReadServerId(obj) ?? ReflectionUtil.ReadUnityObjectName(obj)),
				"Blocked Lenovo cable interaction outside rack from " + source + ": serverId='" + (ReadServerId(obj) ?? "") + "'.");
			return false;
		}
		object obj2 = TryGetHeldCableSpinner();
		if (obj2 == null)
		{
			return true;
		}
		string text = BuildHeldCableSearchText(obj2);
		int num = ResolveCableLaneCount(text);
		if (num < 0)
		{
			string item = ReadServerId(obj) + "|" + text;
			if (_loggedUnknownCableFamilies.Add(item))
			{
				Log.Info($"Allowing unrecognized cable family on modded server port from {source}: server={ReadServerId(obj) ?? "<unknown>"}, expected={DescribeExpectedCableFamily(serverVariantSpec)}, held='{text}'.");
			}
			return true;
		}
		if (num == serverVariantSpec.FiberLaneCount)
		{
			return true;
		}
		Log.Info($"Rejected incompatible cable for {serverVariantSpec.VariantDisplayName} from {source}: expected={DescribeExpectedCableFamily(serverVariantSpec)}, heldLaneCount={num.ToString(CultureInfo.InvariantCulture)}, held='{text}', port=[{DescribeCableLinkState(cableLink)}].");
		ShowWrongCableTypeNotification();
		return false;
	}

	private static object? TryGetHeldCableSpinner()
	{
		Type type = ReflectionUtil.FindType("PlayerManager");
		Type type2 = ReflectionUtil.FindType("CableSpinner");
		if (type == null || type2 == null)
		{
			return null;
		}
		object obj = ReflectionUtil.ReadStaticMember(type, "instance", "Instance");
		if (obj == null)
		{
			return null;
		}
		bool flag = (ReflectionUtil.ReadMember(obj, "objectInHand", "ObjectInHand")?.ToString() ?? string.Empty).Contains("CableSpinner", StringComparison.OrdinalIgnoreCase);
		foreach (object item in EnumerateHeldObjectCandidates(obj))
		{
			object obj2 = ReflectionUtil.TryCastIl2CppObject(item, type2) ?? item;
			if (type2.IsInstanceOfType(obj2))
			{
				return obj2;
			}
			object component = ReflectionUtil.GetComponent(item, type2);
			if (component != null)
			{
				return component;
			}
			using IEnumerator<object> enumerator2 = ReflectionUtil.GetComponentsInChildren(item, type2).GetEnumerator();
			if (enumerator2.MoveNext())
			{
				return enumerator2.Current;
			}
		}
		if (!flag)
		{
			return null;
		}
		return obj;
	}

	private static IEnumerable<object> EnumerateHeldObjectCandidates(object playerManager)
	{
		string[] array = new string[3] { "objectInHandGO", "objectInHands", "deviceInHand" };
		foreach (string text in array)
		{
			object value = ReflectionUtil.ReadMember(playerManager, text);
			if (value == null)
			{
				continue;
			}
			int yielded = 0;
			foreach (object item in ReflectionUtil.EnumerateCollection(value))
			{
				yielded++;
				yield return item;
			}
			if (yielded == 0 && !(value is string))
			{
				yield return value;
			}
		}
	}

	private static string BuildHeldCableSearchText(object heldCable)
	{
		object obj = ReflectionUtil.ReadMember(heldCable, "gameObject", "GameObject");
		object obj2 = ReflectionUtil.ReadMember(heldCable, "cableType", "CableType");
		object obj3 = ReflectionUtil.ReadMember(heldCable, "objectInHandType", "ObjectInHandType");
		object obj4 = ReflectionUtil.ReadMember(heldCable, "shopItemSO", "ShopItemSO", "shopItemSo", "itemSO", "ItemSO");
		return string.Join(" ", ReflectionUtil.ReadUnityObjectName(heldCable), ReflectionUtil.ReadDisplayName(heldCable) ?? string.Empty, (obj == null) ? string.Empty : ReflectionUtil.ReadUnityObjectName(obj), (obj == null) ? string.Empty : (ReflectionUtil.ReadDisplayName(obj) ?? string.Empty), obj2?.ToString() ?? string.Empty, obj3?.ToString() ?? string.Empty, (obj4 == null) ? string.Empty : ReflectionUtil.ReadUnityObjectName(obj4), (obj4 == null) ? string.Empty : (ReflectionUtil.ReadDisplayName(obj4) ?? string.Empty)).Trim();
	}

	private static int ResolveCableLaneCount(string cableText)
	{
		if (string.IsNullOrWhiteSpace(cableText))
		{
			return -1;
		}
		string text = cableText.ToLowerInvariant();
		string text2 = ReflectionUtil.Normalize(cableText);
		if (text2.Contains("fiber4", StringComparison.Ordinal) || text2.Contains("fibre4", StringComparison.Ordinal) || text2.Contains("4lane", StringComparison.Ordinal) || text2.Contains("4lanes", StringComparison.Ordinal) || text.Contains("qsfp", StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}
		if (text2.Contains("fiber1", StringComparison.Ordinal) || text2.Contains("fibre1", StringComparison.Ordinal) || text2.Contains("1lane", StringComparison.Ordinal) || text2.Contains("1lanes", StringComparison.Ordinal) || text.Contains("sfp28", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (text2.Contains("baset", StringComparison.Ordinal) || text2.Contains("rj45", StringComparison.Ordinal) || text.Contains("copper", StringComparison.OrdinalIgnoreCase) || text.Contains("ethernet", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (text.Contains("fiber", StringComparison.OrdinalIgnoreCase) || text.Contains("fibre", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		return -1;
	}

	private static string DescribeExpectedCableFamily(ServerVariantSpec spec)
	{
		if (spec.FiberLaneCount != 4)
		{
			return "1-lane fiber/SFP28";
		}
		return "4-lane fiber/QSFP+";
	}

	private static void ShowWrongCableTypeNotification()
	{
		Type type = ReflectionUtil.FindType("StaticUIElements");
		object obj = ((type == null) ? null : ReflectionUtil.ReadStaticMember(type, "instance", "Instance"));
		if (obj != null)
		{
			ReflectionUtil.InvokeMethod(obj, "SetNotification", 77, null, string.Empty);
		}
	}

	private static bool IsServerCableLinkForServer(object link, object server)
	{
		string text = ReflectionUtil.ReadMember(link, "typeOfLink")?.ToString() ?? string.Empty;
		if (text.Length > 0 && !text.Contains("Server", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (ReflectionUtil.ReadMember(link, "parentSwitch", "switchParent", "SwitchParent") != null || ReflectionUtil.ReadMember(link, "parentPatchPanel", "patchPanel", "parentPatch", "PatchPanel") != null)
		{
			return false;
		}
		object obj = ReflectionUtil.ReadMember(link, "parentServer");
		if (obj != null)
		{
			return SameRuntimeObject(obj, server);
		}
		return true;
	}

	private static bool SameRuntimeObject(object left, object right)
	{
		if (left == right || object.Equals(left, right))
		{
			return true;
		}
		string text = ReadServerId(left);
		string text2 = ReadServerId(right);
		if (text != null && text2 != null)
		{
			return string.Equals(text, text2, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string DescribeCableLinkState(object link)
	{
		object obj = ReflectionUtil.ReadMember(link, "parentServer");
		string text = ((obj == null) ? null : ReadServerId(obj));
		return string.Join(", ", "object='" + ReflectionUtil.ReadUnityObjectName(link) + "'", "serverId='" + (text ?? string.Empty) + "'", "type=" + (ReflectionUtil.ReadMember(link, "typeOfLink") ?? string.Empty), "speed=" + (ReflectionUtil.ReadMember(link, "connectionSpeed") ?? string.Empty), "isSFP=" + (ReflectionUtil.ReadMember(link, "isSFPPort") ?? string.Empty), "isFibre=" + (ReflectionUtil.ReadMember(link, "isFibrePort") ?? string.Empty), "sfpSupported=" + (ReflectionUtil.ReadMember(link, "sfpTypeSupported") ?? string.Empty), "sfpInserted=" + (ReflectionUtil.ReadMember(link, "sfpTypeInserted") ?? string.Empty), "insertedSFP=" + ((ReflectionUtil.ReadMember(link, "insertedSFP") == null) ? "<null>" : "<set>"));
	}

	private static ServerVariantSpec? ResolveSpecFromCartItem(object cartItem)
	{
		string text2 = ReflectionUtil.ReadMember(cartItem, "itemName")?.ToString() ?? string.Empty;
		object obj = ReflectionUtil.ReadMember(cartItem, "txtItemName");
		string text3 = ((obj == null) ? string.Empty : ReflectionUtil.ReadTmpText(obj));
		string text = text2 + " " + text3 + " " + BuildSearchText(cartItem);
		ServerVariantSpec serverVariantSpec = ServerVariantSpec.All.FirstOrDefault((ServerVariantSpec s) => text.Contains(s.VariantDisplayName, StringComparison.OrdinalIgnoreCase) || text.Contains(s.VariantId, StringComparison.OrdinalIgnoreCase));
		if (serverVariantSpec != null)
		{
			return serverVariantSpec;
		}
		return null;
	}

	private static ServerVariantSpec? ResolveSpecFromPurchase(int itemId, int price, object? itemType, string? displayName)
	{
		return PurchaseRoutingPolicy.ResolvePurchase(itemId, price, itemType, displayName);
	}

	private static bool ItemTypeLooksLikeServer(object? itemType)
	{
		return (itemType?.ToString() ?? string.Empty).Contains("Server", StringComparison.OrdinalIgnoreCase);
	}

	private static int ReadNullableInt(object? value)
	{
		return PurchaseRoutingPolicy.ReadSpawnUid(value);
	}

	private static void LogUnresolvedPossibleVariantCartItem(string source, object cartItem, int uid)
	{
		int num = ToInt(ReflectionUtil.ReadMember(cartItem, "price", "Price"));
		string text = ReflectionUtil.ReadMember(cartItem, "itemType", "ItemType")?.ToString() ?? string.Empty;
		if (num == 20000 || num == 100000 || text.Contains("Server", StringComparison.OrdinalIgnoreCase))
		{
			int num2 = ToInt(ReflectionUtil.ReadMember(cartItem, "itemID", "ItemID"));
			string value = ReflectionUtil.ReadMember(cartItem, "itemName")?.ToString() ?? string.Empty;
			object obj = ReflectionUtil.ReadMember(cartItem, "txtItemName");
			string value2 = ((obj == null) ? string.Empty : ReflectionUtil.ReadTmpText(obj));
			Log.Info($"Could not resolve possible modded cart item from {source}: uid={uid.ToString(CultureInfo.InvariantCulture)}, itemID={num2.ToString(CultureInfo.InvariantCulture)}, price={num.ToString(CultureInfo.InvariantCulture)}, itemType={text}, itemName='{value}', visibleName='{value2}', object='{ReflectionUtil.DescribeObject(cartItem)}'.");
		}
	}

	private static ServerVariantSpec? ResolveSpecFromRuntimeObject(object runtimeObject)
	{
		string text = BuildDirectRuntimeSearchText(runtimeObject);
		return ServerVariantSpec.All.FirstOrDefault((ServerVariantSpec s) => text.Contains(s.VariantDisplayName, StringComparison.OrdinalIgnoreCase) || text.Contains(s.VariantId, StringComparison.OrdinalIgnoreCase));
	}

	private ServerVariantSpec? ResolveSpecForServer(object server)
	{
		string text = ReadServerId(server);
		return _variantRegistry.GetByLooseId(ReadLooseIdentity(server)) ?? _variantRegistry.Get(text);
	}

	private ServerVariantSpec? ResolveSpecForLiveServer(object server)
	{
		// The persistent registry is valid only for a unique native server id.
		// Never resolve Server.Green1/Yellow1 directly: those are shared stock
		// prefab families and would alter every original server of that colour.
		string serverId = ReadServerId(server);
		ServerVariantSpec spec = ResolveSpecForServer(server) ?? ResolveSpecFromRuntimeObject(server);
		if (spec == null && IsSpecificServerId(serverId))
		{
			spec = _variantRegistry.Get(serverId);
		}
		if (spec == null)
		{
			_lenovoPowerControlSpecsByServer.TryGetValue(server, out spec);
		}
		return spec;
	}

	private static bool IsServerInsertedInRack(object server)
	{
		if (server == null)
		{
			return false;
		}
		object inHands = ReflectionUtil.ReadMember(server, "objectInHands", "ObjectInHands");
		if (inHands is bool inHandsBool && inHandsBool)
		{
			return false;
		}
		object onTrolley = ReflectionUtil.ReadMember(server, "isOnTrolley", "IsOnTrolley");
		if (onTrolley is bool onTrolleyBool && onTrolleyBool)
		{
			return false;
		}
		object rackPosition = ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition");
		if (rackPosition != null)
		{
			return true;
		}
		object rackPositionUid = ReflectionUtil.ReadMember(server, "rackPositionUID", "RackPositionUID", "rackPositionUid", "RackPositionUid");
		if (rackPositionUid != null)
		{
			int uid = ToInt(rackPositionUid);
			return uid > 0;
		}
		return false;
	}

	private void LogBlockedRackInteractionOnce(string key, string message)
	{
		if (_loggedBlockedRackInteractions.Add(key))
		{
			Log.Info(message);
		}
	}

	private static string BuildDirectRuntimeSearchText(object runtimeObject)
	{
		return string.Join(" ", ReflectionUtil.ReadUnityObjectName(runtimeObject), ReflectionUtil.ReadMember(runtimeObject, "name")?.ToString() ?? string.Empty, ReflectionUtil.ReadMember(runtimeObject, "itemName")?.ToString() ?? string.Empty, ReflectionUtil.ReadMember(runtimeObject, "displayName", "DisplayName")?.ToString() ?? string.Empty, ReflectionUtil.ReadMember(runtimeObject, "ServerID", "serverID", "ServerId", "serverId")?.ToString() ?? string.Empty);
	}

	private static string? ReadServerId(object server)
	{
		string value = ReflectionUtil.ReadMember(server, "ServerID", "serverID", "ServerId", "serverId")?.ToString();
		value = NormalizeServerIdentity(value);
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return NormalizeServerIdentity(ReflectionUtil.ReadUnityObjectName(server));
	}

	private static bool IsSpecificServerId(string? serverId)
	{
		return ServerIdentityStore.IsUniqueServerIdentity(serverId);
	}

	private static string? NormalizeServerIdentity(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		int num = text.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			text = text.Substring(0, num).Trim();
		}
		if (!text.StartsWith("Server.", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return text;
	}

	private static bool WriteMemberIfDifferent(object instance, object? value, params string[] names)
	{
		if (ValuesEqual(ReflectionUtil.ReadMember(instance, names), value))
		{
			return false;
		}
		return ReflectionUtil.WriteMember(instance, value, names);
	}

	private static bool ValuesEqual(object? left, object? right)
	{
		if (left == null || right == null)
		{
			return left == right;
		}
		if (left == right)
		{
			return true;
		}
		try
		{
			Type type = right.GetType();
			return object.Equals(ReflectionUtil.CoerceValue(left, type), right);
		}
		catch
		{
			return object.Equals(left, right);
		}
	}

	private static int ToInt(object? value)
	{
		try
		{
			return (value == null) ? int.MinValue : Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return int.MinValue;
		}
	}

	private void TryRegisterShopUiVariants(string source, object computerShop)
	{
		if (!computerShop.GetType().Name.Equals("ComputerShop", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		object obj = ReflectionUtil.ReadMember(computerShop, "shopItems");
		if (obj == null)
		{
			Log.Warning("ComputerShop shopItems is null during " + source + ".");
			return;
		}
		List<object> list = (from item in ReflectionUtil.EnumerateCollection(obj)
			where item.GetType().Name.Equals("ShopItem", StringComparison.OrdinalIgnoreCase)
			select item).ToList();
		int previousCount = list.Count;
		for (int i = list.Count - 1; i >= 0; i--)
			if (HideUnavailableShopItem(list[i])) list.RemoveAt(i);
		if (previousCount != list.Count)
		{
			object filtered = ReflectionUtil.CreateSameTypedCollection(obj, list.Count);
			if (filtered != null)
			{
				bool copied = true;
				for (int i = 0; i < list.Count; i++)
					if (!ReflectionUtil.SetIndexedItem(filtered, i, list[i])) { copied = false; break; }
				if (copied && ReflectionUtil.WriteMember(computerShop, filtered, "shopItems")) obj = filtered;
			}
			Log.Info($"Removed {previousCount - list.Count} obsolete RealServers shop offers without custom models.");
		}
		if (list.Count == 0)
		{
			Log.Warning($"ComputerShop shopItems had no ShopItem entries during {source}; collection type {ReflectionUtil.SafeTypeName(obj)}.");
			return;
		}
		LogComputerShopDiagnostics(source, obj, list);
		int num = 0;
		ServerVariantSpec[] all = ServerCatalog.ShopVariants;
		foreach (ServerVariantSpec spec in all)
		{
			if (list.Any((object item) => ShopItemMatchesVariant(item, spec)))
			{
				_registeredIds.Add(spec.VariantId);
				continue;
			}
			object obj2 = list.FirstOrDefault((object item) => ResolveOwnedShopVariant(item) == null && MatchesBaseItem(GetShopItemSearchText(item), spec));
			if (obj2 == null)
			{
				continue;
			}
			object obj3 = CloneShopItemForVariant(obj2, spec);
			if (obj3 != null)
			{
				if (AppendShopItem(computerShop, obj, list, obj3))
				{
					list.Add(obj3);
					obj = ReflectionUtil.ReadMember(computerShop, "shopItems") ?? obj;
				}
				_registeredIds.Add(spec.VariantId);
				num++;
				Log.Info($"Registered shop UI item {spec.VariantDisplayName} by cloning {ReflectionUtil.DescribeObject(obj2)}.");
			}
		}
		if (num > 0)
		{
			Log.Info($"Shop UI registration from {source}: added {num}, status {_registeredIds.Count}/{ServerCatalog.ShopVariants.Length}.");
		}
	}

	private static ServerVariantSpec? ResolveOwnedShopVariant(object shopItem)
	{
		object payload = ReflectionUtil.ReadMember(shopItem, "shopItemSO");
		return ServerCatalog.FindOwnedShopVariant(
			ReflectionUtil.ReadMember(shopItem, "guid")?.ToString(),
			ReflectionUtil.ReadUnityObjectName(shopItem),
			payload == null ? null : ReflectionUtil.ReadUnityObjectName(payload));
	}

	private static bool HideUnavailableShopItem(object shopItem)
	{
		ServerVariantSpec spec = ResolveOwnedShopVariant(shopItem);
		if (spec == null || ServerCatalog.IsShopVariant(spec)) return false;
		// Only the generated shop card is disabled. Historical physical inventory
		// and its save identity remain valid through ServerVariantSpec.All.
		ReflectionUtil.SetGameObjectActive(shopItem, active: false);
		return true;
	}

	private void LogComputerShopDiagnostics(string source, object shopItemsArray, IReadOnlyList<object> shopItems)
	{
		if (_loggedComputerShopDiagnostics)
		{
			return;
		}
		_loggedComputerShopDiagnostics = true;
		Log.Info($"ComputerShop diagnostics from {source}: collection={ReflectionUtil.SafeTypeName(shopItemsArray)} count={ReflectionUtil.GetCollectionCount(shopItemsArray)} enumerated={shopItems.Count}.");
		if (!VerboseDiagnostics)
		{
			return;
		}
		foreach (object item in shopItems.Take(80))
		{
			object obj = ReflectionUtil.ReadMember(item, "shopItemSO");
			string value = ((obj == null) ? "<no shopItemSO>" : ReflectionUtil.DescribeObject(obj));
			object obj2 = ((obj == null) ? null : ReflectionUtil.ReadMember(obj, "price"));
			object obj3 = ((obj == null) ? null : ReflectionUtil.ReadMember(obj, "xpToUnlock"));
			object obj4 = ((obj == null) ? null : ReflectionUtil.ReadMember(obj, "itemType"));
			Log.Info($"ShopItem seen: {ReflectionUtil.DescribeObject(item)} so={value} price={obj2 ?? ""} xp={obj3 ?? ""} itemType={obj4 ?? ""} search='{GetShopItemSearchText(item)}'.");
		}
	}

	private object? CloneShopItemForVariant(object baseShopItem, ServerVariantSpec spec)
	{
		object source = ReflectionUtil.ReadMember(baseShopItem, "gameObject", "GameObject") ?? baseShopItem;
		object obj = ReflectionUtil.InstantiateUnityObject(source);
		if (obj == null)
		{
			Log.Warning($"Could not instantiate ShopItem for {spec.VariantDisplayName}: {ReflectionUtil.DescribeObject(baseShopItem)}.");
			return null;
		}
		Type type = ReflectionUtil.FindUnityType("GameObject");
		if (type != null)
		{
			obj = ReflectionUtil.TryCastIl2CppObject(obj, type) ?? obj;
		}
		object component = ReflectionUtil.GetComponent(obj, baseShopItem.GetType());
		if (component == null)
		{
			Log.Warning($"Cloned {ReflectionUtil.SafeTypeName(obj)} for {spec.VariantDisplayName}, but could not find a {ReflectionUtil.SafeTypeName(baseShopItem)} component on it.");
			return null;
		}
		ReflectionUtil.TrySetTransformParentLike(obj, source);
		ReflectionUtil.SetGameObjectActive(obj, active: true);
		object obj2 = ReflectionUtil.ReadMember(baseShopItem, "shopItemSO");
		object obj3 = null;
		if (obj2 != null)
		{
			obj3 = ReflectionUtil.InstantiateUnityObject(obj2);
			obj3 = ((obj3 == null) ? null : (ReflectionUtil.TryCastIl2CppObject(obj3, obj2.GetType()) ?? obj3));
			if (obj3 != null && !SameUnityObject(obj3, obj2))
			{
				ReflectionUtil.WriteUnityObjectName(obj3, spec.VariantId + "_so");
				if (!ReflectionUtil.WriteMember(component, obj3, "shopItemSO"))
				{
					Log.Warning("Could not assign cloned ShopItemSO for " + spec.VariantDisplayName + "; skipping shop data configuration to avoid changing the base item.");
					obj3 = null;
				}
			}
			else obj3 = null;
		}
		if (obj3 == null)
		{
			Log.Warning("Could not create an independent ShopItemSO for " + spec.VariantDisplayName + "; the stock payload was left untouched.");
			GameObject failedClone = ResolveGameObject(obj);
			if (failedClone != null) UnityEngine.Object.Destroy(failedClone);
			return null;
		}
		ReflectionUtil.WriteUnityObjectName(obj, spec.VariantId + "_button");
		ReflectionUtil.WriteUnityObjectName(component, spec.VariantId);
		ReflectionUtil.WriteMember(component, spec.VariantId, "guid");
		ReflectionUtil.WriteMember(component, spec.VariantDisplayName, "itemDisplayName");
		ConfigureDirectMembers(component, spec);
		if (obj3 != null)
		{
			ConfigureDirectMembers(obj3, spec);
		}
		RestoreShopPurchaseIdentity(component, spec);
		ReflectionUtil.InvokeMethod(component, "OnLoad");
		ReflectionUtil.InvokeMethod(component, "UpdateVisualState");
		UpdateShopItemText(component, obj, spec);
		return component;
	}

	private void UpdateShopItemText(object shopItem, object shopItemGameObject, ServerVariantSpec spec)
	{
		object obj = ReflectionUtil.ReadMember(shopItem, "txtName");
		object obj2 = ReflectionUtil.ReadMember(shopItem, "txtPrice");
		object obj3 = ReflectionUtil.ReadMember(shopItem, "txtXpToUnlock");
		if (obj != null && ReflectionUtil.SetTmpText(obj, spec.VariantDisplayName))
		{
			Log.Info("Updated ShopItem.txtName for " + spec.VariantDisplayName + ".");
		}
		if (obj2 != null)
		{
			ReflectionUtil.SetTmpText(obj2, spec.Price.ToString(CultureInfo.InvariantCulture) + " $");
		}
		if (obj3 != null)
		{
			ReflectionUtil.SetTmpText(obj3, "Unlock for: " + spec.XpToUnlock.ToString(CultureInfo.InvariantCulture) + " xp");
		}
		Type type = ReflectionUtil.FindType("TMP_Text") ?? ReflectionUtil.FindType("TextMeshProUGUI");
		if (type == null)
		{
			return;
		}
		foreach (object componentsInChild in ReflectionUtil.GetComponentsInChildren(shopItemGameObject, type))
		{
			string text = ReflectionUtil.ReadTmpText(componentsInChild);
			if (!string.IsNullOrWhiteSpace(text) && (text.Contains(spec.BaseDisplayName, StringComparison.OrdinalIgnoreCase) || text.Contains(spec.BaseDisplayName.Replace("IOPs", "IOPS", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase) || text.Contains(spec.BaseDisplayName.Replace("IOPS", "IOPs", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)))
			{
				ReflectionUtil.SetTmpText(componentsInChild, spec.VariantDisplayName);
				Log.Info($"Updated visible shop card name '{text}' -> '{spec.VariantDisplayName}'.");
			}
		}
		ApplyShopItemIcon(shopItem, shopItemGameObject, spec);
	}

	private static void RestoreShopPurchaseIdentity(object shopItem, ServerVariantSpec spec)
	{
		// Native Start recomputes itemDisplayName from stock family IDs. The
		// visible card and the hidden string sent to ButtonBuy must agree.
		ReflectionUtil.WriteMember(shopItem, spec.VariantDisplayName, "itemDisplayName", "displayName");
		object payload = ReflectionUtil.ReadMember(shopItem, "shopItemSO");
		if (payload == null) return;
		ReflectionUtil.WriteMember(payload, spec.BaseItemId, "itemID", "ItemID");
		ReflectionUtil.WriteMember(payload, spec.VariantDisplayName, "itemName", "ItemName");
	}

	private void ApplyShopItemIcon(object shopItem, object shopItemGameObject, ServerVariantSpec spec)
	{
		string iconPath = ServerShopIcons.GetRelativePath(spec.VariantId);
		if (iconPath == null)
		{
			return;
		}
		Texture2D texture = GetServerShopIcon(iconPath);
		if (texture == null)
		{
			return;
		}
		if (!_shopIconSprites.TryGetValue(iconPath, out Sprite sprite) || sprite == null)
			_shopIconSprites[iconPath] = sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
		GameObject imageObject = ResolveGameObject(ReflectionUtil.ReadMember(shopItem, "itemIcon", "ItemIcon", "imgItem", "itemImage", "imageItem", "icon", "Icon"));
		if (imageObject != null)
		{
			Image image = imageObject.GetComponent<Image>();
			if (image != null)
			{
				image.sprite = sprite;
				image.preserveAspect = true;
				return;
			}
			RawImage rawImage = imageObject.GetComponent<RawImage>();
			if (rawImage != null)
			{
				rawImage.texture = texture;
				return;
			}
		}
		foreach (Image image in ResolveGameObject(shopItemGameObject)?.GetComponentsInChildren<Image>(includeInactive: true) ?? Array.Empty<Image>())
		{
			string name = image.name ?? string.Empty;
			if (name.Contains("icon", StringComparison.OrdinalIgnoreCase) || name.Contains("image", StringComparison.OrdinalIgnoreCase) || name.Contains("preview", StringComparison.OrdinalIgnoreCase))
			{
				image.sprite = sprite;
				image.preserveAspect = true;
				return;
			}
		}
	}

	private Texture2D GetServerShopIcon(string relativeIcon)
	{
		if (_shopIconTextures.TryGetValue(relativeIcon, out Texture2D cached) && cached != null)
		{
			return cached;
		}
		string assemblyPath = typeof(CatalogInjector).Assembly.Location;
		string modDirectory = string.IsNullOrWhiteSpace(assemblyPath) ? string.Empty : Path.GetDirectoryName(assemblyPath) ?? string.Empty;
		string[] candidates =
		{
			Path.Combine(modDirectory, "RealServers", relativeIcon),
			Path.Combine(modDirectory, relativeIcon),
			Path.Combine(AppContext.BaseDirectory, "Mods", "RealServers", relativeIcon)
		};
		string path = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
		if (!File.Exists(path))
		{
			Log.Warning("Server shop icon was not found: " + path);
			return null;
		}
		try
		{
			Texture2D texture = new Texture2D(2, 2);
			if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true))
			{
				texture.name = "RealServers_ShopIcon_" + relativeIcon;
				_shopIconTextures[relativeIcon] = texture;
				return texture;
			}
		}
		catch (Exception exception)
		{
			Log.Warning("Could not load server shop icon: " + exception.Message);
		}
		return null;
	}

	private static bool AppendShopItem(object computerShop, object currentCollection, IReadOnlyList<object> existingItems, object clone)
	{
		object obj = ReflectionUtil.CreateSameTypedCollection(currentCollection, existingItems.Count + 1);
		if (obj == null)
		{
			Log.Warning("Could not resize ComputerShop.shopItems collection " + ReflectionUtil.SafeTypeName(currentCollection) + "; cloned UI object will still exist under the shop parent.");
			return false;
		}
		for (int i = 0; i < existingItems.Count; i++)
		{
			ReflectionUtil.SetIndexedItem(obj, i, existingItems[i]);
		}
		ReflectionUtil.SetIndexedItem(obj, existingItems.Count, clone);
		if (!ReflectionUtil.WriteMember(computerShop, obj, "shopItems"))
		{
			Log.Warning("Could not write resized ComputerShop.shopItems array; cloned UI object will still exist under the shop parent.");
			return false;
		}
		return true;
	}

	private static bool ShopItemMatchesVariant(object item, ServerVariantSpec spec)
	{
		string shopItemSearchText = GetShopItemSearchText(item);
		if (!shopItemSearchText.Contains(spec.VariantId, StringComparison.OrdinalIgnoreCase))
		{
			return shopItemSearchText.Contains(spec.VariantDisplayName, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string GetShopItemSearchText(object shopItem)
	{
		List<string> list = new List<string>();
		list.Add(ReflectionUtil.ReadUnityObjectName(shopItem));
		object obj = ReflectionUtil.ReadMember(shopItem, "guid");
		if (obj != null)
		{
			list.Add(obj.ToString() ?? string.Empty);
		}
		object obj2 = ReflectionUtil.ReadMember(shopItem, "itemDisplayName");
		if (obj2 != null)
		{
			list.Add(obj2.ToString() ?? string.Empty);
		}
		object obj3 = ReflectionUtil.ReadMember(shopItem, "shopItemSO");
		if (obj3 != null)
		{
			list.Add(ReflectionUtil.ReadUnityObjectName(obj3));
			object obj4 = ReflectionUtil.ReadMember(obj3, "itemName");
			if (obj4 != null)
			{
				list.Add(obj4.ToString() ?? string.Empty);
			}
		}
		return string.Join(" ", list);
	}

	private void Discover(string source, object? triggerInstance)
	{
		_visitedContainers.Clear();
		_visitedCandidates.Clear();
		bool flag = DateTime.UtcNow - _lastVerboseDiscoveryAt > TimeSpan.FromSeconds(30.0);
		if (flag)
		{
			_lastVerboseDiscoveryAt = DateTime.UtcNow;
			Log.Info("Discovery started from " + source + ".");
		}
		if (triggerInstance != null)
		{
			ScanObjectGraph(triggerInstance, "trigger:" + source, flag);
		}
		foreach (object item in EnumerateComputerShops())
		{
			TryRegisterShopUiVariants(source + ":scene-computershop", item);
			ScanObjectGraph(item, "scene-computershop", flag, 3);
		}
		foreach (object item2 in EnumerateLikelyStaticRoots(flag))
		{
			ScanObjectGraph(item2, "static-root", flag);
		}
		foreach (object item3 in EnumerateShopLikeResources())
		{
			RegisterCandidate(item3, null, "resource", flag);
			ScanObjectGraph(item3, "resource-graph", flag, 2);
		}
		foreach (object item4 in EnumerateSceneComponents())
		{
			ScanObjectGraph(item4, "scene", flag, 2);
		}
		if (flag)
		{
			string text = string.Join(", ", _baseItemsBySpecKey.Keys.OrderBy((string x) => x));
			Log.Info("Discovery base keys: " + ((text.Length == 0) ? "<none>" : text));
		}
	}

	private IEnumerable<object> EnumerateLikelyStaticRoots(bool verbose)
	{
		foreach (Type item in AppDomain.CurrentDomain.GetAssemblies().Where(IsLikelyGameAssembly).SelectMany(ReflectionUtil.SafeGetTypes))
		{
			if (!LooksLikeCatalogType(item.Name))
			{
				continue;
			}
			foreach (MemberInfo item2 in ReflectionUtil.DataMembers(item).Where(IsStaticMember))
			{
				Type type = ReflectionUtil.MemberType(item2);
				if (!IsPrimitiveLike(type) && !(type == typeof(string)) && !(type == typeof(Type)) && !(type == typeof(IntPtr)) && !typeof(MemberInfo).IsAssignableFrom(type))
				{
					object value = ReflectionUtil.GetValue(item2, null);
					bool flag = value == null || IsPrimitiveLike(value.GetType());
					if (!flag)
					{
						bool flag2 = ((value is string || value is Type || value is MemberInfo) ? true : false);
						flag = flag2;
					}
					if (!flag)
					{
						yield return value;
					}
				}
			}
		}
	}

	private IEnumerable<object> EnumerateShopLikeResources()
	{
		Type type = ReflectionUtil.FindType("ShopItemSO");
		Type type2 = ReflectionUtil.FindType("ModShopItem");
		Type[] array = new Type[2] { type, type2 }.Where((Type t) => t != null).Cast<Type>().ToArray();
		Type[] array2 = array;
		foreach (Type type3 in array2)
		{
			foreach (object item in FindUnityObjectsOfTypeAll(type3))
			{
				if (item != null)
				{
					yield return item;
				}
			}
		}
	}

	private IEnumerable<object> EnumerateComputerShops()
	{
		Type type = ReflectionUtil.FindType("ComputerShop");
		if (type == null)
		{
			yield break;
		}
		foreach (object item in FindUnityObjectsOfTypeAll(type))
		{
			if (item != null)
			{
				yield return item;
			}
		}
	}

	private IEnumerable<object> EnumerateSceneComponents()
	{
		Type componentType = ReflectionUtil.FindUnityType("Component");
		foreach (Type item in AppDomain.CurrentDomain.GetAssemblies().Where(IsLikelyGameAssembly).SelectMany(ReflectionUtil.SafeGetTypes))
		{
			if (componentType == null || !componentType.IsAssignableFrom(item) || !LooksLikeCatalogType(item.Name))
			{
				continue;
			}
			foreach (object item2 in FindUnityObjectsOfTypeAll(item))
			{
				if (item2 != null)
				{
					yield return item2;
				}
			}
		}
	}

	private void ScanObjectGraph(object root, string source, bool verbose, int maxDepth = 4)
	{
		Queue<(object, object, int)> queue = new Queue<(object, object, int)>();
		queue.Enqueue((root, null, 0));
		while (queue.Count > 0)
		{
			var (obj, obj2, num) = queue.Dequeue();
			if (!_visitedContainers.Add(obj) && num > 0)
			{
				continue;
			}
			RegisterCandidate(obj, obj2, source, verbose);
			if (num >= maxDepth || obj is string)
			{
				continue;
			}
			Type type = obj.GetType();
			if (IsPrimitiveLike(type))
			{
				continue;
			}
			if (obj is IEnumerable && !IsUnityComponentOrGameObject(obj))
			{
				foreach (object item in ReflectionUtil.EnumerateCollection(obj))
				{
					queue.Enqueue((item, obj, num + 1));
				}
			}
			if (!LooksLikeCatalogType(type.Name) && num > 1)
			{
				continue;
			}
			foreach (MemberInfo item2 in from m in ReflectionUtil.DataMembers(type)
				where !IsStaticMember(m)
				select m)
			{
				Type type2 = ReflectionUtil.MemberType(item2);
				if (IsPrimitiveLike(type2) || type2 == typeof(string))
				{
					continue;
				}
				object value = ReflectionUtil.GetValue(item2, obj);
				if (value == null)
				{
					continue;
				}
				if (value is IEnumerable && !(value is string))
				{
					foreach (object item3 in ReflectionUtil.EnumerateCollection(value))
					{
						queue.Enqueue((item3, value, num + 1));
					}
				}
				else if (LooksLikeCatalogType(item2.Name) || LooksLikeCatalogType(type2.Name))
				{
					queue.Enqueue((value, obj2, num + 1));
				}
			}
		}
	}

	private void RegisterCandidate(object candidate, object? ownerCollection, string source, bool verbose)
	{
		if (!_visitedCandidates.Add(candidate))
		{
			return;
		}
		string text = BuildSearchText(candidate);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		ServerVariantSpec[] all = ServerCatalog.ShopVariants;
		foreach (ServerVariantSpec serverVariantSpec in all)
		{
			string key = SpecKey(serverVariantSpec);
			if (!_baseItemsBySpecKey.ContainsKey(key) && MatchesBaseItem(text, serverVariantSpec))
			{
				_baseItemsBySpecKey[key] = candidate;
				if (ownerCollection != null)
				{
					_ownerCollectionByBaseItem[candidate] = ownerCollection;
				}
				Log.Info($"Found base for {serverVariantSpec.VariantDisplayName} via {source}: {ReflectionUtil.DescribeObject(candidate)}.");
				if (verbose)
				{
					LogCandidateMembers(candidate);
				}
			}
		}
	}

	private object? CloneAndConfigure(object baseItem, ServerVariantSpec spec)
	{
		object obj = ReflectionUtil.InstantiateUnityObject(baseItem);
		if (obj == null)
		{
			obj = TryMemberwiseClone(baseItem);
		}
		if (obj == null)
		{
			Log.Warning($"Could not clone base item for {spec.VariantDisplayName}: {ReflectionUtil.DescribeObject(baseItem)}.");
			return null;
		}
		ConfigureObjectGraph(obj, spec, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
		return obj;
	}

	private static object? TryMemberwiseClone(object baseItem)
	{
		MethodInfo method = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
		try
		{
			return method?.Invoke(baseItem, Array.Empty<object>());
		}
		catch
		{
			return null;
		}
	}

	private void ConfigureObjectGraph(object target, ServerVariantSpec spec, HashSet<object> visited, int depth)
	{
		if (!visited.Add(target) || depth > 3)
		{
			return;
		}
		ConfigureDirectMembers(target, spec);
		foreach (MemberInfo item in from m in ReflectionUtil.DataMembers(target.GetType())
			where !IsStaticMember(m)
			select m)
		{
			Type type = ReflectionUtil.MemberType(item);
			if (!IsPrimitiveLike(type) && !(type == typeof(string)))
			{
				object value = ReflectionUtil.GetValue(item, target);
				if (value != null && !IsUnityComponentOrGameObject(value) && (LooksLikeItemPayload(item.Name) || LooksLikeItemPayload(type.Name)))
				{
					ConfigureObjectGraph(value, spec, visited, depth + 1);
				}
			}
		}
	}

	private static void ConfigureDirectMembers(object target, ServerVariantSpec spec)
	{
		string name = target.GetType().Name;
		foreach (MemberInfo item in from m in ReflectionUtil.DataMembers(target.GetType())
			where !IsStaticMember(m)
			select m)
		{
			string name2 = item.Name;
			Type type = ReflectionUtil.MemberType(item);
			if (type == typeof(string) || type.FullName == "System.String")
			{
				string text = name2.ToLowerInvariant();
				string text2 = ReflectionUtil.GetValue(item, target)?.ToString() ?? string.Empty;
				if (IsIdName(text))
				{
					TrySet(item, target, spec.VariantId, "id");
				}
				else if (IsDisplayName(text))
				{
					TrySet(item, target, spec.VariantDisplayName, "display name");
				}
				else if (text.Contains("iop") || text2.Contains("IOP", StringComparison.OrdinalIgnoreCase))
				{
					TrySet(item, target, spec.IopsText, "iops text");
				}
				else if (text.Contains("sfp") || text.Contains("connector") || text.Contains("porttype") || text.Contains("moduletype"))
				{
					TrySet(item, target, spec.ConnectorHint, "connector text");
				}
				else if (text.Contains("description") || text.Contains("desc"))
				{
					string value = $"{spec.VariantDisplayName} - {spec.IopsText} IOPS, {spec.ConnectorHint} network profile.";
					TrySet(item, target, value, "description");
				}
			}
			else if (IsNumeric(type))
			{
				string text3 = name2.ToLowerInvariant();
				double value2 = ToDouble(ReflectionUtil.GetValue(item, target));
				if (text3.Contains("price") || text3.Contains("cost"))
				{
					TrySet(item, target, spec.Price, "price");
				}
				else if (text3.Contains("xptounlock") || (text3.Contains("xp") && text3.Contains("unlock")) || text3.Contains("unlockxp"))
				{
					TrySet(item, target, spec.XpToUnlock, "xp unlock");
				}
				else if (text3.Contains("gbps") || text3.Contains("linkspeed") || text3.Contains("networkspeed"))
				{
					TrySet(item, target, spec.NetworkSpeedGbps, "connector link speed");
				}
				else if (text3.Contains("iop") || text3.Contains("performance") || IsKnownBaseIops(value2))
				{
					TrySet(item, target, spec.Iops, "iops");
				}
			}
			else if (type.IsEnum)
			{
				string text4 = name2.ToLowerInvariant();
				if (text4.Contains("sfp") || text4.Contains("connector") || text4.Contains("porttype") || text4.Contains("moduletype"))
				{
					object obj = FindConnectorEnumValue(type, spec.ConnectorHint);
					if (obj != null)
					{
						TrySet(item, target, obj, "connector enum");
					}
				}
			}
			if (ReflectionUtil.IsUnityObject(target))
			{
				ReflectionUtil.WriteUnityObjectName(target, spec.VariantId);
			}
			if (name.Equals("ShopItemSO", StringComparison.OrdinalIgnoreCase) || name.Equals("ModShopItem", StringComparison.OrdinalIgnoreCase) || name.Contains("ShopItem", StringComparison.OrdinalIgnoreCase))
			{
				// Stock SO IDs remain safe for the game's native Start/name lookups.
				ReflectionUtil.WriteMember(target, spec.BaseItemId, "itemID", "ItemID");
				ReflectionUtil.WriteMember(target, spec.VariantDisplayName, "itemDisplayName", "displayName", "DisplayName", "itemName", "ItemName");
				ReflectionUtil.WriteMember(target, spec.Price, "price", "Price", "cost", "Cost");
				ReflectionUtil.WriteMember(target, spec.XpToUnlock, "xpToUnlock", "XPToUnlock", "unlockXP", "UnlockXP");
				ConfigureRackUnitProfile(target, spec);
			}
		}
	}

	private static bool TrySet(MemberInfo member, object target, object value, string reason)
	{
		if (ReflectionUtil.SetValue(member, target, value))
		{
			if (VerboseDiagnostics)
			{
				Log.Info($"Set {ReflectionUtil.SafeTypeName(target)}.{member.Name} = {value} ({reason}).");
			}
			return true;
		}
		return false;
	}

	private static bool CollectionAlreadyContains(object collection, string variantId, string displayName)
	{
		foreach (object item in ReflectionUtil.EnumerateCollection(collection))
		{
			string text = BuildSearchText(item);
			if (text.Contains(variantId, StringComparison.OrdinalIgnoreCase) || text.Contains(displayName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static string BuildSearchText(object candidate)
	{
		List<string> list = new List<string>();
		AppendObjectText(candidate, list, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
		return string.Join(" ", list);
	}

	private static void AppendObjectText(object? value, List<string> parts, HashSet<object> visited, int depth)
	{
		if (value == null || depth > 2)
		{
			return;
		}
		if (value is string item)
		{
			parts.Add(item);
		}
		else
		{
			if (!visited.Add(value))
			{
				return;
			}
			if (ReflectionUtil.IsUnityObject(value))
			{
				parts.Add(ReflectionUtil.ReadUnityObjectName(value));
			}
			Type type = value.GetType();
			if (!IsPrimitiveLike(type))
			{
				foreach (MemberInfo item2 in from m in ReflectionUtil.DataMembers(type)
					where !IsStaticMember(m)
					select m)
				{
					Type type2 = ReflectionUtil.MemberType(item2);
					object value2 = ReflectionUtil.GetValue(item2, value);
					if (value2 != null)
					{
						if (type2 == typeof(string) || IsPrimitiveLike(type2))
						{
							parts.Add(Convert.ToString(value2, CultureInfo.InvariantCulture) ?? string.Empty);
						}
						else if (LooksLikeItemPayload(item2.Name) || LooksLikeItemPayload(type2.Name))
						{
							AppendObjectText(value2, parts, visited, depth + 1);
						}
					}
				}
				return;
			}
			parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		}
	}

	private static bool MatchesBaseItem(string haystack, ServerVariantSpec spec)
	{
		string text = ReflectionUtil.Normalize(haystack);
		string value = ReflectionUtil.Normalize(spec.BaseDisplayName);
		string value2 = ReflectionUtil.Normalize(spec.BaseAssetName);
		if (!text.Contains(value, StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains(value2, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string SpecKey(ServerVariantSpec spec)
	{
		return spec.FamilyKey + ":" + ReflectionUtil.Normalize(spec.BaseDisplayName);
	}

	private static bool LooksLikeCatalogType(string name)
	{
		if (!name.Contains("Shop", StringComparison.OrdinalIgnoreCase) && !name.Contains("Item", StringComparison.OrdinalIgnoreCase) && !name.Contains("Catalog", StringComparison.OrdinalIgnoreCase) && !name.Contains("Store", StringComparison.OrdinalIgnoreCase) && !name.Contains("Inventory", StringComparison.OrdinalIgnoreCase) && !name.Contains("Computer", StringComparison.OrdinalIgnoreCase) && !name.Equals("ModLoader", StringComparison.OrdinalIgnoreCase) && !name.Equals("ModShopItem", StringComparison.OrdinalIgnoreCase))
		{
			return name.Equals("ModPackConfig", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool LooksLikeItemPayload(string name)
	{
		if (!name.Contains("Shop", StringComparison.OrdinalIgnoreCase) && !name.Contains("Item", StringComparison.OrdinalIgnoreCase) && !name.Contains("Server", StringComparison.OrdinalIgnoreCase) && !name.Contains("Object", StringComparison.OrdinalIgnoreCase) && !name.Contains("Data", StringComparison.OrdinalIgnoreCase) && !name.Contains("Config", StringComparison.OrdinalIgnoreCase))
		{
			return name.Contains("SO", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsStaticMember(MemberInfo member)
	{
		if (!(member is FieldInfo { IsStatic: var isStatic }))
		{
			if (member is PropertyInfo propertyInfo)
			{
				MethodInfo? getMethod = propertyInfo.GetMethod;
				return ((object)getMethod != null && getMethod.IsStatic) || (propertyInfo.SetMethod?.IsStatic ?? false);
			}
			return false;
		}
		return isStatic;
	}

	private static bool IsPrimitiveLike(Type type)
	{
		Type type2 = Nullable.GetUnderlyingType(type) ?? type;
		if (!type2.IsPrimitive && !type2.IsEnum && !(type2 == typeof(decimal)))
		{
			return type2 == typeof(DateTime);
		}
		return true;
	}

	private static bool IsLikelyGameAssembly(Assembly assembly)
	{
		string text = assembly.GetName().Name ?? string.Empty;
		if (!text.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsNumeric(Type type)
	{
		Type type2 = Nullable.GetUnderlyingType(type) ?? type;
		if (!(type2 == typeof(byte)) && !(type2 == typeof(short)) && !(type2 == typeof(int)) && !(type2 == typeof(long)) && !(type2 == typeof(float)) && !(type2 == typeof(double)))
		{
			return type2 == typeof(decimal);
		}
		return true;
	}

	private static bool IsKnownBaseIops(double value)
	{
		if (!(Math.Abs(value - 5000.0) < 0.01))
		{
			return Math.Abs(value - 12000.0) < 0.01;
		}
		return true;
	}

	private static double ToDouble(object? value)
	{
		if (value == null)
		{
			return double.NaN;
		}
		try
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return double.NaN;
		}
	}

	private static bool IsIdName(string lowerName)
	{
		bool flag;
		switch (lowerName)
		{
		case "id":
		case "itemid":
		case "serverid":
		case "objectid":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			if (lowerName.EndsWith("id", StringComparison.Ordinal))
			{
				return lowerName.Contains("item");
			}
			return false;
		}
		return true;
	}

	private static bool IsDisplayName(string lowerName)
	{
		bool flag;
		switch (lowerName)
		{
		case "name":
		case "itemname":
		case "itemdisplayname":
		case "displayname":
		case "title":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag && !lowerName.Contains("displayname"))
		{
			return lowerName.Contains("itemname");
		}
		return true;
	}

	private static void LogCandidateMembers(object candidate)
	{
		object candidate2 = candidate;
		string[] value = (from m in ReflectionUtil.DataMembers(candidate2.GetType())
			where !IsStaticMember(m)
			select m).Select(delegate(MemberInfo m)
		{
			object value2 = ReflectionUtil.GetValue(m, candidate2);
			string text = ((value2 == null || (value2 is IEnumerable enumerable && !(enumerable is string))) ? ReflectionUtil.MemberType(m).Name : (value2.ToString() ?? ""));
			if (text.Length > 80)
			{
				text = text.Substring(0, 80) + "...";
			}
			return m.Name + "=" + text;
		}).Take(40).ToArray();
		Log.Info("Base candidate members: " + string.Join("; ", value));
	}

	private static object? FindConnectorEnumValue(Type enumType, string connectorHint)
	{
		string value = connectorHint.Replace("+", "", StringComparison.OrdinalIgnoreCase);
		foreach (object value2 in Enum.GetValues(enumType))
		{
			if ((Enum.GetName(enumType, value2) ?? value2.ToString() ?? "").Replace("+", "", StringComparison.OrdinalIgnoreCase).Contains(value, StringComparison.OrdinalIgnoreCase))
			{
				return value2;
			}
		}
		if (connectorHint == "QSFP+")
		{
			foreach (object value3 in Enum.GetValues(enumType))
			{
				if ((Enum.GetName(enumType, value3) ?? value3.ToString() ?? "").Contains("QSFP", StringComparison.OrdinalIgnoreCase))
				{
					return value3;
				}
			}
		}
		return null;
	}

	private static IEnumerable<object> FindUnityObjectsOfTypeAll(Type type)
	{
		if (type == null)
		{
			yield break;
		}
		Type resourcesType = ReflectionUtil.FindUnityType("Resources");
		if (resourcesType == null)
		{
			yield break;
		}
		MethodInfo method = resourcesType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(candidate =>
		{
			if (!string.Equals(candidate.Name, "FindObjectsOfTypeAll", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = candidate.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType == typeof(Type);
		});
		if (method == null)
		{
			yield break;
		}
		object found;
		try
		{
			found = method.Invoke(null, new object[] { type });
		}
		catch
		{
			yield break;
		}
		foreach (object item in ReflectionUtil.EnumerateCollection(found))
		{
			if (item != null)
			{
				yield return item;
			}
		}
	}

	private static IEnumerable<object> FindUnityObjectsOfTypeAllDirect(Type type)
	{
		if (type == null)
		{
			yield break;
		}
		foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll(Il2CppType.From(type)))
		{
			if (item != null)
			{
				object typed = CastResourceObject(item, type);
				if (typed != null) yield return typed;
			}
		}
	}

	private static object CastResourceObject(object item, Type requestedType)
	{
		if (item == null || requestedType == null) return null;
		// Resources' nongeneric API returns Unity.Object wrappers. The interop
		// pool does not discover their native derived type, and TryCast creates
		// a new typed wrapper without updating that pool. Every scan therefore
		// needs its own explicit cast before reflection reads Server/rack fields.
		object typed = ReflectionUtil.TryCastIl2CppObject(item, requestedType);
		return typed != null && requestedType.IsInstanceOfType(typed) ? typed : null;
	}

	private static bool IsUnityComponentOrGameObject(object value)
	{
		Type type = ReflectionUtil.FindUnityType("Component");
		Type type2 = ReflectionUtil.FindUnityType("GameObject");
		if (!(type != null) || !type.IsInstanceOfType(value))
		{
			if (type2 != null)
			{
				return type2.IsInstanceOfType(value);
			}
			return false;
		}
		return true;
	}
}

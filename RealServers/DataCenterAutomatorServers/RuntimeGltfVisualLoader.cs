using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DataCenterAutomatorServers;

internal static class RuntimeGltfVisualLoader
{
	private const string VisualRootName = "RealServers_VisualRoot";

	private const string InteractionColliderName = "RealServers_InteractionCollider";

	private const string TopInteractionColliderName = "RealServers_TopInteractionCollider";

	private const string FrontInteractionColliderName = "RealServers_FrontInteractionCollider";

	private const float DellR440FrontColliderClearance = 0.06f;

	private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, GameObject> StandalonePurchasePrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<int> OutlinedVisualRoots = new HashSet<int>();

	private static readonly OriginalVisualBoundsCache<Bounds> OriginalSourceBounds = new OriginalVisualBoundsCache<Bounds>();

	private static Material? VisibleMaterialTemplate;

	private static readonly Dictionary<int, (Material Material, bool Opaque)> PreparedVisualMaterials = new();

	public static void BeginScene()
	{
		// Save loads recreate every server and its native outline components.
		OutlinedVisualRoots.Clear();
		OriginalSourceBounds.Clear();
	}

	public static bool IsCachedPurchasePrefab(GameObject item)
	{
		if (item == null)
		{
			return false;
		}
		// Unity object identity excludes only the cached prototype. Its spawned
		// clones may share a name and still need their own persistence identities.
		return StandalonePurchasePrefabCache.Values.Any(prefab => prefab != null && prefab == item)
			|| PrefabCache.Values.Any(prefab => prefab != null && prefab == item);
	}


	public static GameObject? CreateStandalonePurchasePrefab(ServerVariantSpec spec, GameObject? sourcePrefab)
	{
		if (sourcePrefab == null)
		{
			return null;
		}
		string assetRelativePath = GetAssetRelativePath(spec);
		if (assetRelativePath == null)
		{
			return null;
		}
		if (StandalonePurchasePrefabCache.TryGetValue(spec.VariantId, out GameObject cached) && cached != null)
		{
			return cached;
		}
		string assetPath = Path.Combine(ModDirectory(), assetRelativePath);
		if (!File.Exists(assetPath))
		{
			Log.Warning("Standalone visual asset not found for " + spec.VariantDisplayName + ": " + assetPath);
			return null;
		}
		GameObject loadedPrefab = LoadPrefab(assetPath, ShouldForceOpaque(spec));
		if (loadedPrefab == null)
		{
			return null;
		}
		GameObject root = Object.Instantiate(sourcePrefab);
		root.name = "RealServers_CarrierPrefab_" + spec.VariantId;
		root.transform.position = new Vector3(0f, -5000f, 0f);
		root.transform.rotation = Quaternion.identity;
		root.transform.localScale = Vector3.one;
		root.layer = sourcePrefab.layer;
		CaptureVisibleMaterialTemplate(root);
		int removedVisuals = RemoveExistingVisualRoots(root);
		int removedInteractionColliders = RemoveCustomInteractionColliders(root);
		SetOriginalRenderersEnabled(root, true);
		Bounds? sourceBounds = TryGetRendererBounds(root, null, enabledOnly: true);
		Bounds? sourceLocalBounds = GetOriginalSourceLocalBounds(root, null);

		GameObject visual = Object.Instantiate(loadedPrefab);
		visual.name = VisualRootName + "_" + spec.FamilyKey;
		visual.SetActive(true);
		visual.transform.SetParent(root.transform, worldPositionStays: false);
		visual.transform.localPosition = Vector3.zero;
		visual.transform.localRotation = VisualLocalRotation(spec);
		visual.transform.localScale = StandaloneVisualScale(spec);
		ApplyLayerRecursive(visual, root.layer);
		int rendererCount = EnsureVisualRenderers(visual, spec);
		HideOriginalRenderers(root, visual, spec);
		AdjustVisualScaleToSourceBounds(visual, sourceBounds, spec);
		AlignVisualBottomToSource(visual, root.transform, sourceLocalBounds, spec, isMountedInRack: false);
		ConfigureOriginalInteractionColliders(root, visual, sourceLocalBounds, spec);

		root.SetActive(sourcePrefab.activeSelf);
		Object.DontDestroyOnLoad(root);
		StandalonePurchasePrefabCache[spec.VariantId] = root;
		Log.Info("Created carrier purchase prefab for " + spec.VariantDisplayName + ": source='" + sourcePrefab.name + "', removedExistingVisuals=" + removedVisuals.ToString(CultureInfo.InvariantCulture) + ", removedInteractionColliders=" + removedInteractionColliders.ToString(CultureInfo.InvariantCulture) + ", rootActive=" + root.activeSelf + ", " + DescribeVisual(visual, rendererCount) + ".");
		return root;
	}

	public static bool ApplyVisualIfAvailable(object server, ServerVariantSpec spec, bool hideOriginalRenderers = false)
	{
		string assetRelativePath = GetAssetRelativePath(spec);
		if (assetRelativePath == null)
		{
			return false;
		}
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			Log.Warning("Could not resolve server GameObject for " + spec.VariantDisplayName + ".");
			return false;
		}
		CaptureVisibleMaterialTemplate(serverObject);
		GameObject existingVisual = FindExistingVisualRoot(serverObject);
		RemoveCustomInteractionColliders(serverObject);
		Bounds? sourceBounds = TryGetRendererBounds(serverObject, existingVisual == null ? null : existingVisual.transform, enabledOnly: true);
		Bounds? sourceLocalBounds = GetOriginalSourceLocalBounds(serverObject, existingVisual);
		if (existingVisual != null)
		{
			RefreshExistingVisualForServer(existingVisual, serverObject, spec, sourceLocalBounds, IsMountedInRack(server));
			int existingRendererCount = EnsureVisualRenderers(existingVisual, spec);
			if (hideOriginalRenderers)
			{
				HideOriginalRenderers(serverObject, existingVisual, spec);
			}
			AdjustVisualScaleToSourceBounds(existingVisual, sourceBounds, spec);
			ConfigureOriginalInteractionColliders(serverObject, existingVisual, sourceLocalBounds, spec);
			ConfigureNativeOutline(server, existingVisual, spec);
			Log.Info("Reapplied existing visual asset to " + spec.VariantDisplayName + ": " + DescribeVisual(existingVisual, existingRendererCount) + ".");
			return false;
		}
		string assetPath = Path.Combine(ModDirectory(), assetRelativePath);
		if (!File.Exists(assetPath))
		{
			Log.Warning("Visual asset not found for " + spec.VariantDisplayName + ": " + assetPath);
			return false;
		}
		GameObject prefab = LoadPrefab(assetPath, ShouldForceOpaque(spec));
		if (prefab == null)
		{
			return false;
		}
		GameObject visual = Object.Instantiate(prefab);
		visual.name = VisualRootName + "_" + spec.FamilyKey;
		PrepareVisualForServer(visual, serverObject, spec);
		int rendererCount = EnsureVisualRenderers(visual, spec);
		if (hideOriginalRenderers)
		{
			HideOriginalRenderers(serverObject, visual, spec);
		}
		AdjustVisualScaleToSourceBounds(visual, sourceBounds, spec);
		AlignVisualBottomToSource(visual, serverObject.transform, sourceLocalBounds, spec, IsMountedInRack(server));
		ConfigureOriginalInteractionColliders(serverObject, visual, sourceLocalBounds, spec);
		ConfigureNativeOutline(server, visual, spec);
		Log.Info("Applied visual asset " + assetRelativePath + " to " + spec.VariantDisplayName + ": " + DescribeVisual(visual, rendererCount) + ".");
		return true;
	}

	private static void ConfigureNativeOutline(object server, GameObject visual, ServerVariantSpec spec)
	{
		if (server == null || visual == null || !OutlinedVisualRoots.Add(visual.GetInstanceID()))
		{
			return;
		}
		object outline = ReflectionUtil.ReadMember(server, "outlineEffect", "OutlineEffect");
		if (outline == null)
		{
			OutlinedVisualRoots.Remove(visual.GetInstanceID());
			Log.Warning("Could not find native outline component for " + spec.VariantDisplayName + ".");
			return;
		}
		int added = 0;
		foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer != null && ReflectionUtil.InvokeMethod(outline, "AddRenderer", renderer, null))
			{
				added++;
			}
		}
		if (added == 0)
		{
			OutlinedVisualRoots.Remove(visual.GetInstanceID());
			Log.Warning("Could not add custom visual renderers to native outline for " + spec.VariantDisplayName + ".");
			return;
		}
		Log.Info("Linked " + added.ToString(CultureInfo.InvariantCulture) + " custom renderers to the native hover outline for " + spec.VariantDisplayName + ".");
	}

	// Save restoration can re-enable the stock carrier renderers after the mod has
	// already attached its visual. Keep this inexpensive guard separate from the
	// full import/configuration path so every persisted instance stays custom.
	public static bool EnsureVisualStillApplied(object server, ServerVariantSpec spec)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return false;
		}
		GameObject visual = FindExistingVisualRoot(serverObject);
		if (visual == null)
		{
			return ApplyVisualIfAvailable(server, spec, hideOriginalRenderers: true);
		}
		bool visualStateChanged = !visual.activeSelf || visual.transform.parent != serverObject.transform;
		LODGroup lodGroup = visual.GetComponent<LODGroup>();
		bool customRendererStateChanged = lodGroup == null
			&& visual.GetComponentsInChildren<Renderer>(includeInactive: true)
				.Any(renderer => renderer != null && (!renderer.enabled || renderer.forceRenderingOff));
		if (lodGroup != null && !lodGroup.enabled)
		{
			lodGroup.enabled = true;
			lodGroup.ForceLOD(-1);
		}
		if (visualStateChanged || customRendererStateChanged)
		{
			RefreshExistingVisualForServer(visual, serverObject, spec, GetOriginalSourceLocalBounds(serverObject, visual), IsMountedInRack(server));
			EnsureVisualRenderers(visual, spec);
		}
		// Native restore callbacks can re-enable carrier geometry after our first
		// pass. Enforce this state every maintenance scan instead of relying on a
		// renderer-name heuristic that missed complete Green carriers.
		int hiddenNativeRenderers = HideOriginalRenderers(serverObject, visual, spec);
		if (visualStateChanged || customRendererStateChanged || hiddenNativeRenderers > 0)
		{
			Log.Info("Reasserted custom visual after native restore for " + spec.VariantDisplayName + ".");
			return true;
		}
		return false;
	}

	private static Vector3 VisualLocalPosition(ServerVariantSpec spec)
	{
		if (string.Equals(spec.FamilyKey, "lenovo_sr650", StringComparison.OrdinalIgnoreCase))
		{
			return Vector3.zero;
		}
		return Vector3.zero;
	}

	private static Quaternion VisualLocalRotation(ServerVariantSpec spec)
	{
		// Unity Y is the vertical axis for the baked Dell visual. This turns the
		// chassis toward the rack front while retaining its top/bottom and UVs.
		return Quaternion.Euler(0f, VisualLocalYawDegrees(spec), 0f);
	}

	private static float VisualLocalYawDegrees(ServerVariantSpec spec) =>
		spec.UsesDellStaticCarrierProfile ? 180f : 0f;

	private static Vector3 VisualLocalScale(ServerVariantSpec spec)
	{
		(float x, float y, float z) = VisualLocalScaleComponents(spec);
		return new Vector3(x, y, z);
	}

	private static (float X, float Y, float Z) VisualLocalScaleComponents(ServerVariantSpec spec)
	{
		if (spec.UsesDellStaticCarrierProfile)
		{
			// The R440 and XE8640 assets are normalized to their intended physical
			// heights before this axis-safe runtime scale is applied.
			return (0.245f, 0.30f, 0.245f);
		}
		if (spec.UsesLenovoCarrierProfile)
		{
			// Consecutive physical rack slots are 45 mm apart in this game. The
			// imported chassis was 53 mm high at the previous scale, so it crossed
			// into the neighbouring 1U slot.
			return (0.245f, 0.21f, 0.245f);
		}
		return (1f, 1f, 1f);
	}

	private static Vector3 StandaloneVisualScale(ServerVariantSpec spec)
	{
		(float x, float y, float z) = VisualLocalScaleComponents(spec);
		return new Vector3(x, y, z);
	}

	private static void FitDellToNativeOneUSwitch(GameObject visual, Bounds? carrierBounds, ServerVariantSpec spec)
	{
		if (!string.Equals(spec.FamilyKey, "dell_r440", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		Bounds? switchBounds;
		try
		{
			switchBounds = TryGetNativeOneUSwitchBounds();
		}
		catch (Exception exception)
		{
			Log.Warning("Could not read native switch dimensions for Dell PowerEdge R440: " + exception.Message);
			return;
		}
		Bounds? visualBounds = TryGetLocalRendererBounds(visual.transform, visual, null, enabledOnly: false);
		if (switchBounds == null || visualBounds == null)
		{
			return;
		}
		Vector3 sourceSize = visualBounds.Value.size;
		Vector3 targetSize = switchBounds.Value.size;
		if (sourceSize.x <= 0.001f || sourceSize.y <= 0.001f || sourceSize.z <= 0.001f || targetSize.x <= 0.001f || targetSize.y <= 0.001f || targetSize.z <= 0.001f)
		{
			return;
		}
		Vector3 scale = visual.transform.localScale;
		visual.transform.localScale = new Vector3(scale.x * targetSize.x / sourceSize.x, scale.y * targetSize.y / sourceSize.y, scale.z * targetSize.z / sourceSize.z);
		Bounds? fittedBounds = TryGetLocalRendererBounds(visual.transform, visual, null, enabledOnly: false);
		if (fittedBounds == null)
		{
			return;
		}
		Vector3 targetCenter = carrierBounds?.center ?? switchBounds.Value.center;
		visual.transform.localPosition += targetCenter - fittedBounds.Value.center;
		Log.Info("Matched Dell PowerEdge R440 visual to native 1U switch: target=" + FormatBounds(switchBounds.Value) + ", fitted=" + FormatBounds(fittedBounds.Value) + ", carrierCenter=" + FormatVector(targetCenter) + ", localPosition=" + FormatVector(visual.transform.localPosition) + ", localScale=" + FormatVector(visual.transform.localScale) + ".");
	}

	private static Bounds? TryGetNativeOneUSwitchBounds()
	{
		Bounds? selected = FindLiveOneUSwitchBounds();
		if (selected != null)
		{
			return selected;
		}
		Type managerType = ReflectionUtil.FindType("MainGameManager");
		if (managerType == null)
		{
			return null;
		}
		object manager = ReflectionUtil.ReadStaticMember(managerType, "instance", "Instance");
		if (manager == null)
		{
			return null;
		}
		for (int switchType = 0; switchType < 8; switchType++)
		{
			GameObject candidate = ResolveGameObject(ReflectionUtil.InvokeMethodReturn(manager, "GetSwitchPrefab", switchType));
			if (candidate == null)
			{
				continue;
			}
			Bounds? candidateBounds = TryGetLocalRendererBounds(candidate.transform, candidate, null, enabledOnly: false);
			if (candidateBounds == null || candidateBounds.Value.size.y <= 0.001f)
			{
				continue;
			}
			if (selected == null || candidateBounds.Value.size.y < selected.Value.size.y)
			{
				selected = candidateBounds;
			}
		}
		return selected;
	}

	private static Bounds? FindLiveOneUSwitchBounds()
	{
		Type switchType = ReflectionUtil.FindType("NetworkSwitch");
		if (switchType == null)
		{
			return null;
		}
		Bounds? selected = null;
		foreach (UnityEngine.Object component in Resources.FindObjectsOfTypeAll(Il2CppType.From(switchType)))
		{
			GameObject candidate = ResolveGameObject(component);
			if (candidate == null)
			{
				continue;
			}
			Bounds? candidateBounds = TryGetLocalRendererBounds(candidate.transform, candidate, null, enabledOnly: false);
			if (candidateBounds == null || candidateBounds.Value.size.y <= 0.001f)
			{
				continue;
			}
			if (selected == null || candidateBounds.Value.size.y < selected.Value.size.y)
			{
				selected = candidateBounds;
			}
		}
		return selected;
	}

	private static string? GetAssetRelativePath(ServerVariantSpec spec)
	{
		return spec.ModelAssetRelativePath;
	}

	private static string ModDirectory()
	{
		string assemblyLocation = typeof(RuntimeGltfVisualLoader).Assembly.Location;
		if (!string.IsNullOrWhiteSpace(assemblyLocation))
		{
			return Path.GetDirectoryName(assemblyLocation) ?? ".";
		}
		return Directory.GetCurrentDirectory();
	}

	private static void PrepareVisualForServer(GameObject visual, GameObject serverObject, ServerVariantSpec spec)
	{
		visual.SetActive(true);
		visual.transform.SetParent(serverObject.transform, worldPositionStays: false);
		ApplyLayerRecursive(visual, serverObject.layer);
		visual.transform.localPosition = VisualLocalPosition(spec);
		visual.transform.localRotation = VisualLocalRotation(spec);
		visual.transform.localScale = VisualLocalScale(spec);
	}

	private static Bounds? GetOriginalSourceLocalBounds(GameObject serverObject, GameObject existingVisual)
	{
		int instanceId = serverObject.GetInstanceID();
		Bounds? known = OriginalSourceBounds.Get(instanceId);
		if (known != null) return known;
		// After hiding the carrier only interaction renderers may remain, and
		// their bounds cannot replace the original chassis alignment reference.
		// Cached carrier clones and restored visuals can outlive BeginScene's
		// cache. Disabled native chassis meshes still supply their original bounds;
		// exclude the custom visual, LOD and any ports parented underneath it.
		Bounds? original = existingVisual == null
			? TryGetLocalRendererBounds(serverObject.transform, serverObject, null, enabledOnly: true)
			: TryGetLocalRendererBounds(serverObject.transform, serverObject, existingVisual.transform, enabledOnly: false);
		return OriginalSourceBounds.Capture(instanceId, original);
	}

	private static void RefreshExistingVisualForServer(GameObject visual, GameObject serverObject, ServerVariantSpec spec, Bounds? sourceLocalBounds, bool isMountedInRack)
	{
		Vector3 previousLocalPosition = visual.transform.localPosition;
		bool sameParent = visual.transform.parent == serverObject.transform;
		visual.SetActive(true);
		if (!sameParent)
		{
			visual.transform.SetParent(serverObject.transform, worldPositionStays: false);
		}
		ApplyLayerRecursive(visual, serverObject.layer);
		bool canRealign = sourceLocalBounds != null || isMountedInRack || !sameParent;
		// A cloned or restored visual may predate this scene's bounds cache.
		// Preserve its existing loose offset rather than silently resetting it.
		visual.transform.localPosition = canRealign ? VisualLocalPosition(spec) : previousLocalPosition;
		visual.transform.localRotation = VisualLocalRotation(spec);
		visual.transform.localScale = VisualLocalScale(spec);
		if (canRealign)
		{
			AlignVisualBottomToSource(visual, serverObject.transform, sourceLocalBounds, spec, isMountedInRack);
		}
	}

	private static GameObject? FindExistingVisualRoot(object server)
	{
		GameObject serverObject = ResolveGameObject(server);
		if (serverObject == null)
		{
			return null;
		}
		for (int i = 0; i < serverObject.transform.childCount; i++)
		{
			Transform child = serverObject.transform.GetChild(i);
			if (child != null && child.name.StartsWith(VisualRootName, StringComparison.OrdinalIgnoreCase))
			{
				return child.gameObject;
			}
		}
		return null;
	}

	private static int RemoveExistingVisualRoots(GameObject serverObject)
	{
		int removed = 0;
		List<GameObject> existingVisuals = new List<GameObject>();
		for (int i = 0; i < serverObject.transform.childCount; i++)
		{
			Transform child = serverObject.transform.GetChild(i);
			if (child != null && child.name.StartsWith(VisualRootName, StringComparison.OrdinalIgnoreCase))
			{
				existingVisuals.Add(child.gameObject);
			}
		}
		foreach (GameObject existingVisual in existingVisuals)
		{
			if (existingVisual == null)
			{
				continue;
			}
			Object.DestroyImmediate(existingVisual);
			removed++;
		}
		return removed;
	}

	private static void SetOriginalRenderersEnabled(GameObject serverObject, bool enabled)
	{
		foreach (Renderer renderer in serverObject.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null)
			{
				continue;
			}
			renderer.enabled = enabled;
			renderer.forceRenderingOff = false;
		}
	}

	private static bool IsMountedInRack(object server)
	{
		return ReflectionUtil.ReadMember(server, "currentRackPosition", "CurrentRackPosition") != null;
	}

private static void AlignVisualBottomToSource(GameObject visual, Transform rootTransform, Bounds? sourceLocalBounds, ServerVariantSpec spec, bool isMountedInRack)
	{
		bool useFixedOneUFlow = spec.UsesLenovoCarrierProfile;
		if (!useFixedOneUFlow)
		{
			return;
		}
		Bounds? visualLocalBounds = TryGetLocalRendererBounds(rootTransform, visual, null, enabledOnly: false);
		if (visualLocalBounds == null)
		{
			return;
		}
		Bounds visualBounds = visualLocalBounds.Value;
		Vector3 position = visual.transform.localPosition;
		if (isMountedInRack)
		{
			// Do not use the oversized carrier's vertical bound after 1U insertion.
			visual.transform.localPosition = new Vector3(position.x, VisualLocalPosition(spec).y, position.z);
			return;
		}
		float targetBottom = sourceLocalBounds?.min.y ?? 0f;
		float visualBottom = visualBounds.min.y;
		float lift = targetBottom - visualBottom + 0.006f;
		if (Math.Abs(lift) < 0.002f)
		{
			return;
		}
		visual.transform.localPosition = new Vector3(position.x, position.y + lift, position.z);
		Bounds? adjustedBounds = TryGetLocalRendererBounds(rootTransform, visual, null, enabledOnly: false);
		Log.Info("Aligned visual bottom for " + spec.VariantDisplayName + ": targetBottom=" + targetBottom.ToString("0.###", CultureInfo.InvariantCulture) + ", beforeBottom=" + visualBottom.ToString("0.###", CultureInfo.InvariantCulture) + ", lift=" + lift.ToString("0.###", CultureInfo.InvariantCulture) + ", after=" + (adjustedBounds == null ? "<none>" : FormatBounds(adjustedBounds.Value)) + ".");
	}

private static void ConfigureOriginalInteractionColliders(GameObject serverObject, GameObject visual, Bounds? sourceLocalBounds, ServerVariantSpec spec)
	{
		if (string.IsNullOrWhiteSpace(spec.ModelAssetRelativePath))
		{
			return;
		}
		Bounds? visualLocalBounds = TryGetLocalRendererBounds(serverObject.transform, visual, null, enabledOnly: false);
		if (visualLocalBounds == null)
		{
			return;
		}
		Bounds visualBounds = visualLocalBounds.Value;
		// The root collider is the chassis itself. Its bounds intentionally match
		// the imported 3D model exactly: no pickup margin may consume another U.
		Bounds targetBounds = visualBounds;
		if (spec.UsesDellStaticCarrierProfile)
		{
			// The Dell mesh is turned 180 degrees inside the carrier. Its authored
			// front is -Z, so localRotation maps that face to +Z. Recess only that
			// face; rotating the complete server/rack later remains correct because
			// these bounds stay in server-local space.
			float frontLocalZ = (visual.transform.localRotation * Vector3.back).z;
			(float minZ, float maxZ) = InsetFrontDepth(
				visualBounds.min.z,
				visualBounds.max.z,
				frontLocalZ,
				DellR440FrontColliderClearance);
			Vector3 targetCenter = targetBounds.center;
			Vector3 targetSize = targetBounds.size;
			targetCenter.z = (minZ + maxZ) * 0.5f;
			targetSize.z = maxZ - minZ;
			targetBounds = new Bounds(targetCenter, targetSize);
		}

		int disabledChildColliders = DisableOriginalChildColliders(serverObject, visual);
		BoxCollider rootCollider = serverObject.GetComponent<BoxCollider>();
		if (rootCollider == null)
		{
			rootCollider = serverObject.AddComponent<BoxCollider>();
		}
		ApplyRootLocalBoundsToCollider(serverObject.transform, rootCollider, targetBounds);
		foreach (BoxCollider extraRootCollider in serverObject.GetComponents<BoxCollider>())
		{
			if (extraRootCollider != null && extraRootCollider != rootCollider)
			{
				Object.DestroyImmediate(extraRootCollider);
			}
		}
		Log.Info("Configured root pickup collider for " + spec.VariantDisplayName + ": disabledChildColliders=" + disabledChildColliders.ToString(CultureInfo.InvariantCulture) + ", targetBounds=" + FormatBounds(targetBounds) + ", visualBounds=" + FormatBounds(visualLocalBounds.Value) + ".");
	}

	private static (float MinZ, float MaxZ) InsetFrontDepth(float minZ, float maxZ, float frontLocalZ, float clearance)
	{
		float depth = Math.Max(0f, maxZ - minZ);
		float safeClearance = Math.Min(Math.Max(0f, clearance), Math.Max(0f, depth - 0.001f));
		return frontLocalZ >= 0f
			? (minZ, maxZ - safeClearance)
			: (minZ + safeClearance, maxZ);
	}

	private static void ConfigureFrontPickupCollider(GameObject serverObject, BoxCollider bodyCollider, Bounds visualBounds, Bounds bodyPickupBounds)
	{
		// The body collider intentionally leaves both faces clear for power and
		// cable controls. This thin plate restores pickup on the unused front
		// surface while staying behind their 12 mm interaction hitboxes.
		Bounds frontBounds = new Bounds(
			new Vector3(bodyPickupBounds.center.x, bodyPickupBounds.center.y, visualBounds.min.z + 0.015f),
			new Vector3(bodyPickupBounds.size.x, bodyPickupBounds.size.y, 0.03f));
		Transform staleChild = serverObject.transform.Find(FrontInteractionColliderName);
		if (staleChild != null)
		{
			Object.DestroyImmediate(staleChild.gameObject);
		}
		BoxCollider collider = null;
		foreach (BoxCollider candidate in serverObject.GetComponents<BoxCollider>())
		{
			if (candidate != null && candidate != bodyCollider && candidate.size.z <= 0.04f)
			{
				collider = candidate;
				break;
			}
		}
		if (collider == null)
		{
			collider = serverObject.AddComponent<BoxCollider>();
		}
		ApplyRootLocalBoundsToCollider(serverObject.transform, collider, frontBounds);
	}

	private static int DisableOriginalChildColliders(GameObject serverObject, GameObject visual)
	{
		int disabled = 0;
		foreach (Component collider in serverObject.GetComponentsInChildren<Component>(includeInactive: true))
		{
			if (collider == null || !collider.GetType().Name.EndsWith("Collider", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (collider.transform == serverObject.transform || collider.transform.IsChildOf(visual.transform) || IsOriginalInteractionCollider(collider))
			{
				continue;
			}
			if (ReflectionUtil.WriteMember(collider, false, "enabled"))
			{
				disabled++;
			}
		}
		return disabled;
	}

	private static bool IsOriginalInteractionCollider(Component collider)
	{
		for (Transform current = collider.transform; current != null; current = current.parent)
		{
			string name = current.name ?? string.Empty;
			if (name.Contains("ButtonPower", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("PowerButton", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("ButtonEdit", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("ButtonArrow", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("RJ45", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void ApplyRootLocalBoundsToCollider(Transform rootTransform, BoxCollider boxCollider, Bounds rootLocalBounds)
	{
		Vector3 min = rootLocalBounds.min;
		Vector3 max = rootLocalBounds.max;
		Vector3[] rootLocalCorners =
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
		bool hasBounds = false;
		Bounds colliderLocalBounds = default;
		foreach (Vector3 rootLocalCorner in rootLocalCorners)
		{
			Vector3 world = rootTransform.TransformPoint(rootLocalCorner);
			Vector3 colliderLocal = boxCollider.transform.InverseTransformPoint(world);
			if (!hasBounds)
			{
				colliderLocalBounds = new Bounds(colliderLocal, Vector3.zero);
				hasBounds = true;
			}
			else
			{
				colliderLocalBounds.Encapsulate(colliderLocal);
			}
		}
		boxCollider.enabled = true;
		boxCollider.isTrigger = false;
		boxCollider.center = colliderLocalBounds.center;
		boxCollider.size = colliderLocalBounds.size;
	}

	private static int RemoveCustomInteractionColliders(GameObject parent)
	{
		int removed = 0;
		List<GameObject> toRemove = new List<GameObject>();
		for (int i = 0; i < parent.transform.childCount; i++)
		{
			Transform child = parent.transform.GetChild(i);
			if (child != null && IsCustomInteractionCollider(child.gameObject))
			{
				toRemove.Add(child.gameObject);
			}
		}
		foreach (GameObject child in toRemove)
		{
			if (child == null)
			{
				continue;
			}
			Object.DestroyImmediate(child);
			removed++;
		}
		return removed;
	}

	private static bool IsCustomInteractionCollider(GameObject gameObject)
	{
		return gameObject.name.Equals(InteractionColliderName, StringComparison.OrdinalIgnoreCase)
			|| gameObject.name.Equals(TopInteractionColliderName, StringComparison.OrdinalIgnoreCase)
			|| gameObject.name.Equals(FrontInteractionColliderName, StringComparison.OrdinalIgnoreCase);
	}

	private static GameObject FindOrCreateChild(GameObject parent, string childName)
	{
		for (int i = 0; i < parent.transform.childCount; i++)
		{
			Transform child = parent.transform.GetChild(i);
			if (child != null && child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
			{
				child.gameObject.SetActive(true);
				return child.gameObject;
			}
		}
		GameObject childObject = new GameObject(childName);
		childObject.transform.SetParent(parent.transform, worldPositionStays: false);
		childObject.SetActive(true);
		return childObject;
	}

	private static GameObject ResolveGameObject(object unityObject)
	{
		if (unityObject == null)
		{
			return null;
		}
		if (unityObject is GameObject gameObject)
		{
			return gameObject;
		}
		if (unityObject is Component component)
		{
			return component.gameObject;
		}
		return ReflectionUtil.ReadMember(unityObject, "gameObject", "GameObject") as GameObject;
	}

	private static GameObject? LoadPrefab(string path, bool forceOpaque)
	{
		string cacheKey = path + (forceOpaque ? "|opaque" : "|original-alpha");
		if (PrefabCache.TryGetValue(cacheKey, out GameObject cached) && cached != null)
		{
			return cached;
		}
		try
		{
			GameObject prefab = GltfMeshReader.Load(path, forceOpaque);
			AttachDistanceLod(prefab, path, forceOpaque);
			prefab.name = "RealServers_Prefab_" + Path.GetFileNameWithoutExtension(path);
			prefab.SetActive(false);
			Object.DontDestroyOnLoad(prefab);
			PrefabCache[cacheKey] = prefab;
			return prefab;
		}
		catch (Exception exception)
		{
			Log.Error("Failed to load visual asset " + path + ".", exception);
			return null;
		}
	}

	private static void AttachDistanceLod(GameObject prefab, string sourcePath, bool forceOpaque)
	{
		string lodPath = Path.ChangeExtension(sourcePath, ".lod.glb");
		if (!File.Exists(lodPath))
		{
			return;
		}
		GameObject? distantVisual = null;
		LODGroup? group = null;
		try
		{
			var fullRenderers = prefab.GetComponentsInChildren<Renderer>(includeInactive: true);
			distantVisual = GltfMeshReader.Load(lodPath, forceOpaque);
			distantVisual.name = "RealServers_DistanceLOD";
			distantVisual.transform.SetParent(prefab.transform, worldPositionStays: false);
			var distantRenderers = distantVisual.GetComponentsInChildren<Renderer>(includeInactive: true);
			group = prefab.AddComponent<LODGroup>();
			var levels = new Il2CppReferenceArray<LOD>(2);
			// A giant datacenter should use full detail only while inspecting an
			// individual chassis. The low-detail asset keeps the same silhouette.
			levels[0] = new LOD(0.55f, new Il2CppReferenceArray<Renderer>(fullRenderers.ToArray()));
			levels[1] = new LOD(0.001f, new Il2CppReferenceArray<Renderer>(distantRenderers.ToArray()));
			group.SetLODs(levels);
			group.fadeMode = LODFadeMode.None;
			group.RecalculateBounds();
			Log.Info("Prepared distance LOD for " + Path.GetFileName(sourcePath) + ": close=" + fullRenderers.Length + " renderers, far=" + distantRenderers.Length + ".");
		}
		catch (Exception exception)
		{
			if (distantVisual != null)
			{
				distantVisual.SetActive(false);
				Object.Destroy(distantVisual);
			}
			if (group != null)
			{
				group.enabled = false;
				Object.Destroy(group);
			}
			// Full detail remains usable if an optional low-detail asset is absent
			// or incompatible; buying a server must not depend on this optimization.
			Log.Warning("Could not attach distance LOD to " + Path.GetFileName(sourcePath) + ": " + exception.Message);
		}
	}

	private static int HideOriginalRenderers(GameObject serverObject, GameObject visual, ServerVariantSpec spec)
	{
		bool keepNativeInteractionVisuals = spec.UsesLenovoCarrierProfile;
		bool usesDellStaticCarrierProfile = spec.UsesDellStaticCarrierProfile;
		bool forceHideCarrierVisuals = usesDellStaticCarrierProfile || string.Equals(spec.FamilyKey, "dell_r440", StringComparison.OrdinalIgnoreCase);
		int hiddenRenderers = 0;
		foreach (Renderer renderer in serverObject.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null || renderer.transform.IsChildOf(visual.transform) || (keepNativeInteractionVisuals && (!usesDellStaticCarrierProfile ? IsOriginalInteractionRenderer(renderer) : IsNativeRj45Renderer(renderer))))
			{
				continue;
			}
			bool changed = renderer.enabled || (forceHideCarrierVisuals && !renderer.forceRenderingOff);
			renderer.enabled = false;
			if (forceHideCarrierVisuals)
			{
				// Blue1 can re-enable its own renderers after the carrier is spawned.
				// forceRenderingOff keeps its geometry invisible without disabling the
				// native components that provide the server's gameplay behavior.
				renderer.forceRenderingOff = true;
			}
			if (changed)
			{
				hiddenRenderers++;
			}
		}
		if (hiddenRenderers > 0)
		{
			Log.Info("Hidden original server visuals under '" + serverObject.name + "': renderers=" + hiddenRenderers.ToString(CultureInfo.InvariantCulture) + ". Original colliders were kept for interaction.");
		}
		return hiddenRenderers;
	}

	private static bool IsOriginalInteractionRenderer(Renderer renderer)
	{
		if (renderer == null)
		{
			return false;
		}
		string name = renderer.name ?? string.Empty;
		string parentName = renderer.transform.parent == null ? string.Empty : renderer.transform.parent.name ?? string.Empty;
		return name.Contains("ButtonPower", StringComparison.OrdinalIgnoreCase)
			|| name.Contains("PowerButton", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("ButtonArrow", StringComparison.OrdinalIgnoreCase)
			|| name.Contains("RJ45", StringComparison.OrdinalIgnoreCase)
			|| parentName.Contains("ButtonPower", StringComparison.OrdinalIgnoreCase)
			|| parentName.Contains("PowerButton", StringComparison.OrdinalIgnoreCase)
			|| parentName.Contains("RJ45", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsNativeRj45Renderer(Renderer renderer)
	{
		if (renderer == null)
		{
			return false;
		}
		string name = renderer.name ?? string.Empty;
		string parentName = renderer.transform.parent == null ? string.Empty : renderer.transform.parent.name ?? string.Empty;
		return name.Contains("RJ45", StringComparison.OrdinalIgnoreCase)
			|| parentName.Contains("RJ45", StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureProceduralFallback(GameObject visual, ServerVariantSpec spec)
	{
		if (!string.Equals(spec.FamilyKey, "lenovo_sr650", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		for (int i = 0; i < visual.transform.childCount; i++)
		{
			Transform child = visual.transform.GetChild(i);
			if (child != null && child.name.Equals("RealServers_ProceduralLenovoFallback", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		GameObject fallback = new GameObject("RealServers_ProceduralLenovoFallback");
		fallback.transform.SetParent(visual.transform, worldPositionStays: false);
		fallback.transform.localPosition = Vector3.zero;
		fallback.transform.localRotation = Quaternion.identity;
		fallback.transform.localScale = Vector3.one;
		Material body = SolidMaterial("RealServers_LenovoFallback_Body", new Color(0.035f, 0.04f, 0.045f, 1f));
		Material front = SolidMaterial("RealServers_LenovoFallback_Front", new Color(0.005f, 0.005f, 0.006f, 1f));
		Material panel = SolidMaterial("RealServers_LenovoFallback_Panel", new Color(0.48f, 0.52f, 0.56f, 1f));
		Material red = SolidMaterial("RealServers_LenovoFallback_Red", new Color(0.9f, 0.08f, 0.04f, 1f));
		Material port = SolidMaterial("RealServers_LenovoFallback_Port", new Color(0.015f, 0.016f, 0.018f, 1f));
		CreateBox(fallback.transform, "Body", new Vector3(0f, 0.02f, 0.12f), new Vector3(2.04f, 0.24f, 3.22f), body);
		CreateBox(fallback.transform, "FrontFace", new Vector3(0f, -0.01f, -1.51f), new Vector3(2.08f, 0.28f, 0.08f), front);
		CreateBox(fallback.transform, "LeftVent", new Vector3(-0.55f, -0.01f, -1.56f), new Vector3(0.72f, 0.13f, 0.035f), port);
		CreateBox(fallback.transform, "RightBay", new Vector3(0.58f, -0.01f, -1.56f), new Vector3(0.5f, 0.18f, 0.04f), port);
		CreateBox(fallback.transform, "CenterPanel", new Vector3(0.08f, -0.01f, -1.565f), new Vector3(0.18f, 0.2f, 0.045f), panel);
		CreateBox(fallback.transform, "Badge", new Vector3(0.02f, 0.12f, -1.57f), new Vector3(0.22f, 0.045f, 0.05f), red);
		for (int i = 0; i < 6; i++)
		{
			CreateBox(fallback.transform, "Drive_" + i.ToString(CultureInfo.InvariantCulture), new Vector3(-0.9f + i * 0.14f, -0.005f, -1.575f), new Vector3(0.08f, 0.11f, 0.04f), panel);
		}
	}

	private static void CreateBox(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
	{
		GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
		box.name = name;
		box.transform.SetParent(parent, worldPositionStays: false);
		box.transform.localPosition = localPosition;
		box.transform.localRotation = Quaternion.identity;
		box.transform.localScale = localScale;
		Renderer renderer = box.GetComponent<Renderer>();
		if (renderer != null)
		{
			renderer.sharedMaterial = material;
			renderer.enabled = true;
			renderer.forceRenderingOff = false;
		}
		Component collider = box.GetComponent("BoxCollider");
		if (collider != null)
		{
			ApplyLayerRecursive(box, parent.gameObject.layer);
		}
	}

	private static Material SolidMaterial(string name, Color color)
	{
		Material material;
		if (VisibleMaterialTemplate != null)
		{
			material = new Material(VisibleMaterialTemplate);
			material.name = name;
		}
		else
		{
			material = GltfMeshReader.CreateCompatibleMaterial(name);
		}
		material.color = color;
		if (material.HasProperty("_BaseColor"))
		{
			material.SetColor("_BaseColor", color);
		}
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", color);
		}
		return material;
	}

	private static void AdjustVisualScaleToSourceBounds(GameObject visual, Bounds? sourceBounds, ServerVariantSpec spec)
	{
		if (spec.UsesLenovoCarrierProfile)
		{
			Log.Info("Skipped source-bound visual auto-fit for " + spec.VariantDisplayName + "; Lenovo rack visual uses fixed 1U scale.");
			return;
		}
		if (!string.Equals(spec.FamilyKey, "lenovo_sr650", StringComparison.OrdinalIgnoreCase) || sourceBounds == null)
		{
			return;
		}
		Bounds? visualBounds = TryGetRendererBounds(visual, null, enabledOnly: false);
		if (visualBounds == null)
		{
			return;
		}
		Vector3 sourceSize = sourceBounds.Value.size;
		Vector3 visualSize = visualBounds.Value.size;
		if (sourceSize.x <= 0.01f || sourceSize.z <= 0.01f || visualSize.x <= 0.01f || visualSize.z <= 0.01f)
		{
			return;
		}
		float fitX = sourceSize.x / visualSize.x;
		float fitZ = sourceSize.z / visualSize.z;
		float targetMultiplier = string.Equals(spec.FamilyKey, "lenovo_sr650", StringComparison.OrdinalIgnoreCase) ? 1.0f : 0.96f;
		float factor = Math.Min(fitX, fitZ) * targetMultiplier;
		factor = Math.Clamp(factor, 0.05f, 1.75f);
		if (Math.Abs(factor - 1f) < 0.03f)
		{
			Log.Info("Visual size already matches source for " + spec.VariantDisplayName + ": sourceSize=" + FormatVector(sourceSize) + ", visualSize=" + FormatVector(visualSize) + ".");
			return;
		}
		visual.transform.localScale = new Vector3(visual.transform.localScale.x * factor, visual.transform.localScale.y * factor, visual.transform.localScale.z * factor);
		Bounds? adjustedBounds = TryGetRendererBounds(visual, null, enabledOnly: false);
		Log.Info("Adjusted visual size for " + spec.VariantDisplayName + ": sourceSize=" + FormatVector(sourceSize) + ", beforeSize=" + FormatVector(visualSize) + ", factor=" + factor.ToString("0.###", CultureInfo.InvariantCulture) + ", afterSize=" + (adjustedBounds == null ? "<none>" : FormatVector(adjustedBounds.Value.size)) + ".");
	}

	private static Bounds? TryGetRendererBounds(GameObject root, Transform? excludedRoot, bool enabledOnly)
	{
		bool hasBounds = false;
		Bounds bounds = default;
		foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null || (enabledOnly && !renderer.enabled) || (excludedRoot != null && renderer.transform.IsChildOf(excludedRoot)))
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

	private static Bounds? TryGetLocalRendererBounds(Transform rootTransform, GameObject root, Transform? excludedRoot, bool enabledOnly)
	{
		bool hasBounds = false;
		Bounds localBounds = default;
		foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null || (enabledOnly && !renderer.enabled) || (excludedRoot != null && renderer.transform.IsChildOf(excludedRoot)))
			{
				continue;
			}
			MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
			bool hasLocalMeshBounds = meshFilter != null && meshFilter.sharedMesh != null;
			Bounds bounds = hasLocalMeshBounds ? meshFilter.sharedMesh.bounds : renderer.bounds;
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;
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
				Vector3 world = hasLocalMeshBounds ? renderer.transform.TransformPoint(corner) : corner;
				Vector3 local = rootTransform.InverseTransformPoint(world);
				if (!hasBounds)
				{
					localBounds = new Bounds(local, Vector3.zero);
					hasBounds = true;
				}
				else
				{
					localBounds.Encapsulate(local);
				}
			}
		}
		return hasBounds ? localBounds : null;
	}

	private static void CaptureVisibleMaterialTemplate(GameObject source)
	{
		if (VisibleMaterialTemplate != null || source == null)
		{
			return;
		}
		foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null || renderer.sharedMaterial == null)
			{
				continue;
			}
			VisibleMaterialTemplate = renderer.sharedMaterial;
			Log.Info("Captured visible material template for RealServers fallback: '" + VisibleMaterialTemplate.name + "' shader='" + (VisibleMaterialTemplate.shader == null ? "" : VisibleMaterialTemplate.shader.name) + "'.");
			return;
		}
	}

	private static void ApplyLayerRecursive(GameObject gameObject, int layer)
	{
		gameObject.layer = layer;
		for (int i = 0; i < gameObject.transform.childCount; i++)
		{
			Transform child = gameObject.transform.GetChild(i);
			if (child != null)
			{
				ApplyLayerRecursive(child.gameObject, layer);
			}
		}
	}

	private static bool ShouldForceOpaque(ServerVariantSpec spec) =>
		string.Equals(spec.FamilyKey, "dell_r440", StringComparison.OrdinalIgnoreCase)
		|| spec.UsesDellStaticCarrierProfile;

	private static int EnsureVisualRenderers(GameObject visual, ServerVariantSpec spec)
	{
		bool forceOpaque = ShouldForceOpaque(spec);
		int count = 0;
		LODGroup lodGroup = visual.GetComponent<LODGroup>();
		foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			if (renderer == null)
			{
				continue;
			}
			// LODGroup owns visibility once attached. Maintenance must not turn
			// every near/far renderer back on and bypass its distance selection.
			if (lodGroup == null)
			{
				renderer.enabled = true;
				renderer.forceRenderingOff = false;
			}
			foreach (Material material in renderer.sharedMaterials)
			{
				SetMaterialVisible(material, forceOpaque);
			}
			count++;
		}
		if (lodGroup != null)
		{
			lodGroup.enabled = true;
			lodGroup.ForceLOD(-1);
		}
		return count;
	}

	private static void SetMaterialVisible(Material material, bool forceOpaque)
	{
		if (material == null)
		{
			return;
		}
		int materialId = material.GetInstanceID();
		if (PreparedVisualMaterials.TryGetValue(materialId, out var prepared)
			&& prepared.Material == material && (!forceOpaque || prepared.Opaque)) return;
		material.doubleSidedGI = true;
		if (material.HasProperty("_Cull"))
		{
			material.SetFloat("_Cull", 0f);
		}
		if (forceOpaque)
		{
			material.renderQueue = -1;
			if (material.HasProperty("_SurfaceType"))
			{
				material.SetFloat("_SurfaceType", 0f);
			}
			if (material.HasProperty("_Surface"))
			{
				material.SetFloat("_Surface", 0f);
			}
			if (material.HasProperty("_ZWrite"))
			{
				material.SetFloat("_ZWrite", 1f);
			}
		}
		if (material.HasProperty("_Color"))
		{
			Color color = material.color;
			if (forceOpaque || color.a <= 0.01f)
			{
				color.a = 1f;
				material.color = color;
			}
		}
		if (material.HasProperty("_BaseColor"))
		{
			Color color = material.GetColor("_BaseColor");
			if (forceOpaque || color.a <= 0.01f)
			{
				color.a = 1f;
				material.SetColor("_BaseColor", color);
			}
		}
		PreparedVisualMaterials[materialId] = (material, forceOpaque);
	}

	private static string DescribeVisual(GameObject visual, int rendererCount)
	{
		bool hasBounds = false;
		Bounds bounds = default;
		foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(includeInactive: true))
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
		string transformText = "localPosition=" + FormatVector(visual.transform.localPosition) + ", localScale=" + FormatVector(visual.transform.localScale);
		if (!hasBounds)
		{
			return "renderers=" + rendererCount.ToString(CultureInfo.InvariantCulture) + ", " + transformText + ", bounds=<none>";
		}
		return "renderers=" + rendererCount.ToString(CultureInfo.InvariantCulture) + ", " + transformText + ", worldBoundsCenter=" + FormatVector(bounds.center) + ", worldBoundsSize=" + FormatVector(bounds.size);
	}

	private static string FormatVector(Vector3 value)
	{
		return "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture) + ", " + value.y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + value.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
	}

	private static string FormatBounds(Bounds bounds)
	{
		return "center=" + FormatVector(bounds.center) + ", size=" + FormatVector(bounds.size);
	}

	private sealed class GltfMeshReader
	{
		private readonly JsonDocument _document;
		private readonly JsonElement _root;
		private readonly byte[] _binaryChunk;
		private readonly byte[][] _buffers;
		private readonly Texture2D[] _textures;
		private readonly Material[] _materials;
		private readonly int[] _materialAliases;
		private readonly bool _forceOpaque;
		private Material? _defaultMaterial;
		private readonly Dictionary<int, Texture2D?> _images = new Dictionary<int, Texture2D?>();

		private GltfMeshReader(string path, bool forceOpaque)
		{
			_forceOpaque = forceOpaque;
			using FileStream stream = File.OpenRead(path);
			(_document, _binaryChunk) = ReadGlb(stream);
			_root = _document.RootElement;
			_buffers = LoadBuffers();
			try
			{
				_textures = LoadTextures();
				_materials = LoadMaterials(out _materialAliases);
			}
			catch
			{
				_document.Dispose();
				throw;
			}
		}

		public static GameObject Load(string path, bool forceOpaque)
		{
			var reader = new GltfMeshReader(path, forceOpaque);
			try
			{
				return reader.BuildScene();
			}
			finally
			{
				reader._document.Dispose();
			}
		}

		private GameObject BuildScene()
		{
			GameObject rootObject = new GameObject("RealServers_GltfRoot");
			int sceneIndex = _root.TryGetProperty("scene", out JsonElement sceneElement) ? sceneElement.GetInt32() : 0;
			JsonElement scene = _root.GetProperty("scenes")[sceneIndex];
			foreach (JsonElement nodeIndex in scene.GetProperty("nodes").EnumerateArray())
			{
				BuildNode(nodeIndex.GetInt32(), rootObject.transform);
			}
			return rootObject;
		}

		private void BuildNode(int nodeIndex, Transform parent)
		{
			JsonElement node = _root.GetProperty("nodes")[nodeIndex];
			string nodeName = node.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? ("Node_" + nodeIndex.ToString(CultureInfo.InvariantCulture)) : ("Node_" + nodeIndex.ToString(CultureInfo.InvariantCulture));
			GameObject nodeObject = new GameObject(nodeName);
			nodeObject.transform.SetParent(parent, worldPositionStays: false);
			ApplyTransform(nodeObject.transform, node);
			if (node.TryGetProperty("mesh", out JsonElement meshElement))
			{
				BuildMesh(meshElement.GetInt32(), nodeObject);
			}
			if (node.TryGetProperty("children", out JsonElement childrenElement))
			{
				foreach (JsonElement childIndex in childrenElement.EnumerateArray())
				{
					BuildNode(childIndex.GetInt32(), nodeObject.transform);
				}
			}
		}

		private static void ApplyTransform(Transform transform, JsonElement node)
		{
			if (node.TryGetProperty("translation", out JsonElement translation) && translation.GetArrayLength() == 3)
			{
				transform.localPosition = new Vector3(translation[0].GetSingle(), translation[1].GetSingle(), -translation[2].GetSingle());
			}
			if (node.TryGetProperty("rotation", out JsonElement rotation) && rotation.GetArrayLength() == 4)
			{
				// Positions and mesh vertices are reflected over Z when converting
				// glTF's right-handed space to Unity's left-handed space. Rotations
				// need the equivalent S * R * S conversion as well. Without this,
				// models with nested rotated parts import exploded or upside down.
				transform.localRotation = new Quaternion(
					-rotation[0].GetSingle(),
					-rotation[1].GetSingle(),
					rotation[2].GetSingle(),
					rotation[3].GetSingle());
			}
			if (node.TryGetProperty("scale", out JsonElement scale) && scale.GetArrayLength() == 3)
			{
				transform.localScale = new Vector3(scale[0].GetSingle(), scale[1].GetSingle(), scale[2].GetSingle());
			}
		}

		private void BuildMesh(int meshIndex, GameObject nodeObject)
		{
			JsonElement gltfMesh = _root.GetProperty("meshes")[meshIndex];
			var primitives = new List<GltfPrimitive<Vector3, Vector2>>();
			bool combineEqualMaterialSlots = true;
			int primitiveIndex = 0;
			foreach (JsonElement primitive in gltfMesh.GetProperty("primitives").EnumerateArray())
			{
				int mode = primitive.TryGetProperty("mode", out JsonElement modeElement) ? modeElement.GetInt32() : 4;
				if (mode != 4)
				{
					Log.Warning("Skipping unsupported glTF primitive mode " + mode.ToString(CultureInfo.InvariantCulture) + " in mesh " + meshIndex.ToString(CultureInfo.InvariantCulture) + ".");
					primitiveIndex++;
					continue;
				}
				JsonElement attributes = primitive.GetProperty("attributes");
				if (!attributes.TryGetProperty("POSITION", out JsonElement positionAccessor))
				{
					primitiveIndex++;
					continue;
				}
				Vector3[] vertices = ReadVec3Accessor(positionAccessor.GetInt32(), invertZ: true);
				Vector3[]? normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessor)
					? ReadVec3Accessor(normalAccessor.GetInt32(), invertZ: true) : null;
				Vector2[]? uvs = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAccessor)
					? ReadVec2Accessor(uvAccessor.GetInt32()) : null;
				int[] indices = primitive.TryGetProperty("indices", out JsonElement indicesAccessor)
					? ReverseTriangleWinding(ReadIndicesAccessor(indicesAccessor.GetInt32()))
					: ReverseTriangleWinding(Enumerable.Range(0, vertices.Length).ToArray());
				int materialIndex = primitive.TryGetProperty("material", out JsonElement materialElement) ? materialElement.GetInt32() : -1;
				if (materialIndex >= 0 && materialIndex < _materials.Length)
				{
					JsonElement sourceMaterial = _root.GetProperty("materials")[materialIndex];
					if (!_forceOpaque && sourceMaterial.TryGetProperty("alphaMode", out var alphaMode) && alphaMode.GetString() == "BLEND")
						combineEqualMaterialSlots = false;
					materialIndex = _materialAliases[materialIndex];
				}
				else materialIndex = -1;
				primitives.Add(new GltfPrimitive<Vector3, Vector2>(vertices, normals, uvs, indices, materialIndex));
				primitiveIndex++;
			}
			var batches = GltfMeshBatcher.Build(primitives, combineEqualMaterialSlots: combineEqualMaterialSlots);
			int batchIndex = 0;
			foreach (var batch in batches)
			{
				GameObject primitiveObject = new GameObject(nodeObject.name + "_Geometry_" + batchIndex.ToString(CultureInfo.InvariantCulture));
				primitiveObject.transform.SetParent(nodeObject.transform, worldPositionStays: false);
				Mesh mesh = new Mesh();
				mesh.name = primitiveObject.name + "_Mesh";
				if (batch.Uses32BitIndices) mesh.indexFormat = IndexFormat.UInt32;
				mesh.vertices = ToIl2Cpp(batch.Vertices);
				if (batch.Normals != null) mesh.normals = ToIl2Cpp(batch.Normals);
				if (batch.Uvs != null) mesh.uv = ToIl2Cpp(batch.Uvs);
				mesh.subMeshCount = batch.Submeshes.Length;
				var materials = new Il2CppReferenceArray<Material>(batch.Submeshes.Length);
				for (int slot = 0; slot < batch.Submeshes.Length; slot++)
				{
					var submesh = batch.Submeshes[slot];
					mesh.SetTriangles(ToIl2Cpp(submesh.Indices), slot, calculateBounds: false);
					materials[slot] = submesh.MaterialIndex >= 0 ? _materials[submesh.MaterialIndex] : (_defaultMaterial ??= DefaultMaterial());
				}
				mesh.RecalculateBounds();
				if (batch.Normals == null)
				{
					mesh.RecalculateNormals();
				}
				// Every instance shares this cached mesh. Bounds remain accessible
				// after releasing the duplicate CPU vertex/index storage.
				mesh.UploadMeshData(markNoLongerReadable: true);
				MeshFilter meshFilter = primitiveObject.AddComponent<MeshFilter>();
				meshFilter.sharedMesh = mesh;
				MeshRenderer meshRenderer = primitiveObject.AddComponent<MeshRenderer>();
				meshRenderer.sharedMaterials = materials;
				batchIndex++;
			}
			Log.Info("Batched glTF mesh '" + nodeObject.name + "': primitives=" + primitives.Count.ToString(CultureInfo.InvariantCulture)
				+ ", renderers=" + batches.Count.ToString(CultureInfo.InvariantCulture) + ". Geometry and material settings were retained.");
		}

		private Material[] LoadMaterials(out int[] aliases)
		{
			if (!_root.TryGetProperty("materials", out JsonElement materialsElement))
			{
				aliases = Array.Empty<int>();
				return Array.Empty<Material>();
			}
			aliases = new int[materialsElement.GetArrayLength()];
			List<Material> materials = new List<Material>();
			var knownMaterials = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (JsonElement materialElement in materialsElement.EnumerateArray())
			{
				string identity = GltfMaterialIdentity.Build(materialElement);
				if (knownMaterials.TryGetValue(identity, out int knownIndex))
				{
					aliases[materials.Count] = knownIndex;
					materials.Add(materials[knownIndex]);
					continue;
				}
				knownMaterials.Add(identity, materials.Count);
				aliases[materials.Count] = materials.Count;
				Material material = CreateCompatibleMaterial("RealServers_Material");
				if (materialElement.TryGetProperty("name", out JsonElement nameElement))
				{
					material.name = nameElement.GetString() ?? "RealServers_Material";
				}
				if (materialElement.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorFactor", out JsonElement color) && color.GetArrayLength() == 4)
				{
					material.color = new Color(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle(), color[3].GetSingle());
				}
				else if (material.name.Contains("GreyTexture", StringComparison.OrdinalIgnoreCase) || material.name.Contains("Metal", StringComparison.OrdinalIgnoreCase) || material.name.Contains("550", StringComparison.OrdinalIgnoreCase))
				{
					material.color = new Color(0.11f, 0.12f, 0.13f, 1f);
				}
				else if (material.name.Contains("Up", StringComparison.OrdinalIgnoreCase))
				{
					material.color = new Color(0.025f, 0.028f, 0.032f, 1f);
				}
				if (materialElement.TryGetProperty("pbrMetallicRoughness", out JsonElement pbrWithTexture) && pbrWithTexture.TryGetProperty("baseColorTexture", out JsonElement baseColorTexture) && baseColorTexture.TryGetProperty("index", out JsonElement textureIndexElement))
				{
					int textureIndex = textureIndexElement.GetInt32();
					if (textureIndex >= 0 && textureIndex < _textures.Length && _textures[textureIndex] != null)
					{
						ApplyMainTexture(material, _textures[textureIndex]);
						material.color = Color.white;
						if (material.HasProperty("_BaseColor"))
						{
							material.SetColor("_BaseColor", Color.white);
						}
						if (material.HasProperty("_Color"))
						{
							material.SetColor("_Color", Color.white);
						}
					}
				}
				SetMaterialVisible(material, _forceOpaque);
				materials.Add(material);
			}
			return materials.ToArray();
		}

		private Texture2D[] LoadTextures()
		{
			if (!_root.TryGetProperty("textures", out JsonElement texturesElement))
			{
				return Array.Empty<Texture2D>();
			}
			Texture2D[] textures = new Texture2D[texturesElement.GetArrayLength()];
			var usedTextureIndices = new HashSet<int>();
			if (_root.TryGetProperty("materials", out JsonElement materials))
			{
				foreach (JsonElement material in materials.EnumerateArray())
				{
					if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr)
						&& pbr.TryGetProperty("baseColorTexture", out JsonElement texture)
						&& texture.TryGetProperty("index", out JsonElement textureIndex))
					{
						usedTextureIndices.Add(textureIndex.GetInt32());
					}
				}
			}
			int index = 0;
			foreach (JsonElement textureElement in texturesElement.EnumerateArray())
			{
				if (usedTextureIndices.Contains(index) && textureElement.TryGetProperty("source", out JsonElement sourceElement))
				{
					textures[index] = LoadImageTexture(sourceElement.GetInt32());
				}
				index++;
			}
			return textures;
		}

		private Texture2D? LoadImageTexture(int imageIndex)
		{
			if (_images.TryGetValue(imageIndex, out Texture2D? cached))
			{
				return cached;
			}
			// Cache a failed decode too, preventing duplicate warnings/allocations
			// when several texture entries refer to the same embedded image.
			_images[imageIndex] = null;
			try
			{
				if (!_root.TryGetProperty("images", out JsonElement imagesElement) || imageIndex < 0 || imageIndex >= imagesElement.GetArrayLength())
				{
					return null;
				}
				JsonElement imageElement = imagesElement[imageIndex];
				if (!imageElement.TryGetProperty("bufferView", out JsonElement bufferViewElement))
				{
					Log.Warning("Skipping external glTF image for RealServers visual; only embedded GLB images are supported.");
					return null;
				}
				byte[] imageBytes = ReadBufferViewBytes(bufferViewElement.GetInt32());
				Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
				texture.name = imageElement.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? ("RealServers_Texture_" + imageIndex.ToString(CultureInfo.InvariantCulture)) : ("RealServers_Texture_" + imageIndex.ToString(CultureInfo.InvariantCulture));
				if (!ImageConversion.LoadImage(texture, imageBytes, markNonReadable: true))
				{
					Log.Warning("Could not decode embedded GLB texture '" + texture.name + "'.");
					Object.Destroy(texture);
					return null;
				}
				texture.wrapMode = TextureWrapMode.Repeat;
				texture.filterMode = FilterMode.Bilinear;
				_images[imageIndex] = texture;
				Log.Info("Loaded embedded GLB texture '" + texture.name + "' bytes=" + imageBytes.Length.ToString(CultureInfo.InvariantCulture) + ".");
				return texture;
			}
			catch (Exception exception)
			{
				Log.Warning("Failed to load embedded GLB texture " + imageIndex.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message);
				return null;
			}
		}

		private static void ApplyMainTexture(Material material, Texture2D texture)
		{
			if (material.HasProperty("_BaseColorMap"))
			{
				material.SetTexture("_BaseColorMap", texture);
			}
			if (material.HasProperty("_MainTex"))
			{
				material.SetTexture("_MainTex", texture);
			}
			if (material.HasProperty("_BaseMap"))
			{
				material.SetTexture("_BaseMap", texture);
			}
			material.mainTexture = texture;
			Log.Info("Applied embedded GLB texture '" + texture.name + "' to material '" + material.name + "'.");
		}

		private Vector3[] ReadVec3Accessor(int accessorIndex, bool invertZ)
		{
			JsonElement accessor = _root.GetProperty("accessors")[accessorIndex];
			int count = accessor.GetProperty("count").GetInt32();
			AccessorBytes bytes = AccessorBytesFor(accessor);
			Vector3[] values = new Vector3[count];
			for (int i = 0; i < count; i++)
			{
				int offset = bytes.Offset + i * bytes.Stride;
				float z = BitConverter.ToSingle(bytes.Buffer, offset + 8);
				values[i] = new Vector3(BitConverter.ToSingle(bytes.Buffer, offset), BitConverter.ToSingle(bytes.Buffer, offset + 4), invertZ ? -z : z);
			}
			return values;
		}

		private Vector2[] ReadVec2Accessor(int accessorIndex)
		{
			JsonElement accessor = _root.GetProperty("accessors")[accessorIndex];
			int count = accessor.GetProperty("count").GetInt32();
			AccessorBytes bytes = AccessorBytesFor(accessor);
			Vector2[] values = new Vector2[count];
			for (int i = 0; i < count; i++)
			{
				int offset = bytes.Offset + i * bytes.Stride;
				float u = BitConverter.ToSingle(bytes.Buffer, offset);
				float v = BitConverter.ToSingle(bytes.Buffer, offset + 4);
				// glTF's UV origin is the upper-left corner. Unity's loaded texture
				// samples with the opposite vertical origin.
				// Flip V once here so front-panel text and drive labels stay upright.
				values[i] = new Vector2(u, 1f - v);
			}
			return values;
		}

		private int[] ReadIndicesAccessor(int accessorIndex)
		{
			JsonElement accessor = _root.GetProperty("accessors")[accessorIndex];
			int count = accessor.GetProperty("count").GetInt32();
			int componentType = accessor.GetProperty("componentType").GetInt32();
			AccessorBytes bytes = AccessorBytesFor(accessor);
			int[] values = new int[count];
			for (int i = 0; i < count; i++)
			{
				int offset = bytes.Offset + i * bytes.Stride;
				values[i] = componentType switch
				{
					5121 => bytes.Buffer[offset],
					5123 => BitConverter.ToUInt16(bytes.Buffer, offset),
					5125 => checked((int)BitConverter.ToUInt32(bytes.Buffer, offset)),
					_ => throw new NotSupportedException("Unsupported glTF index component type " + componentType.ToString(CultureInfo.InvariantCulture))
				};
			}
			return values;
		}

		private static int[] ReverseTriangleWinding(int[] indices)
		{
			for (int i = 0; i + 2 < indices.Length; i += 3)
			{
				(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
			}
			return indices;
		}

		private AccessorBytes AccessorBytesFor(JsonElement accessor)
		{
			int bufferViewIndex = accessor.GetProperty("bufferView").GetInt32();
			JsonElement bufferView = _root.GetProperty("bufferViews")[bufferViewIndex];
			int bufferIndex = bufferView.TryGetProperty("buffer", out JsonElement bufferElement) ? bufferElement.GetInt32() : 0;
			int bufferViewOffset = bufferView.TryGetProperty("byteOffset", out JsonElement bufferViewOffsetElement) ? bufferViewOffsetElement.GetInt32() : 0;
			int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement accessorOffsetElement) ? accessorOffsetElement.GetInt32() : 0;
			int stride = bufferView.TryGetProperty("byteStride", out JsonElement strideElement) ? strideElement.GetInt32() : NaturalStride(accessor);
			return new AccessorBytes(_buffers[bufferIndex], bufferViewOffset + accessorOffset, stride);
		}

		private byte[] ReadBufferViewBytes(int bufferViewIndex)
		{
			JsonElement bufferView = _root.GetProperty("bufferViews")[bufferViewIndex];
			int bufferIndex = bufferView.TryGetProperty("buffer", out JsonElement bufferElement) ? bufferElement.GetInt32() : 0;
			int byteOffset = bufferView.TryGetProperty("byteOffset", out JsonElement byteOffsetElement) ? byteOffsetElement.GetInt32() : 0;
			int byteLength = bufferView.GetProperty("byteLength").GetInt32();
			byte[] bytes = new byte[byteLength];
			Buffer.BlockCopy(_buffers[bufferIndex], byteOffset, bytes, 0, byteLength);
			return bytes;
		}

		private static int NaturalStride(JsonElement accessor)
		{
			int componentSize = accessor.GetProperty("componentType").GetInt32() switch
			{
				5120 or 5121 => 1,
				5122 or 5123 => 2,
				5125 or 5126 => 4,
				_ => throw new NotSupportedException("Unsupported glTF component type.")
			};
			string type = accessor.GetProperty("type").GetString() ?? "SCALAR";
			int componentCount = type switch
			{
				"SCALAR" => 1,
				"VEC2" => 2,
				"VEC3" => 3,
				"VEC4" => 4,
				"MAT4" => 16,
				_ => throw new NotSupportedException("Unsupported glTF accessor type " + type)
			};
			return componentSize * componentCount;
		}

		private byte[][] LoadBuffers()
		{
			JsonElement buffersElement = _root.GetProperty("buffers");
			byte[][] buffers = new byte[buffersElement.GetArrayLength()][];
			for (int i = 0; i < buffers.Length; i++)
			{
				if (i == 0 && _binaryChunk.Length > 0)
				{
					buffers[i] = _binaryChunk;
				}
				else
				{
					throw new NotSupportedException("External glTF buffers are not supported by RealServers runtime loader.");
				}
			}
			return buffers;
		}

		private static (JsonDocument Document, byte[] BinaryChunk) ReadGlb(Stream stream)
		{
			using BinaryReader reader = new BinaryReader(stream);
			if (reader.ReadUInt32() != 0x46546C67)
			{
				throw new InvalidDataException("Only binary .glb assets are supported.");
			}
			reader.ReadUInt32();
			reader.ReadUInt32();
			JsonDocument document = null;
			byte[] binary = Array.Empty<byte>();
			while (stream.Position < stream.Length)
			{
				uint chunkLength = reader.ReadUInt32();
				uint chunkType = reader.ReadUInt32();
				byte[] chunk = reader.ReadBytes(checked((int)chunkLength));
				if (chunkType == 0x4E4F534A)
				{
					document = JsonDocument.Parse(chunk);
				}
				else if (chunkType == 0x004E4942)
				{
					binary = chunk;
				}
			}
			return (document ?? throw new InvalidDataException("GLB JSON chunk was not found."), binary);
		}

		private static Material DefaultMaterial()
		{
			Material material = CreateCompatibleMaterial("RealServers_DefaultMaterial");
			material.color = Color.gray;
			return material;
		}

		public static Material CreateCompatibleMaterial(string name)
		{
			string[] shaderNames = { "HDRP/Lit", "Standard", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Diffuse", "Unlit/Color" };
			foreach (string shaderName in shaderNames)
			{
				Shader shader = Shader.Find(shaderName);
				if (shader != null)
				{
					Material material = new Material(shader);
					material.name = name;
					if (SystemInfo.supportsInstancing)
					{
						material.enableInstancing = true;
					}
					return material;
				}
			}
			throw new InvalidOperationException("No compatible Unity shader was found for RealServers visual material.");
		}

		private static Il2CppStructArray<Vector3> ToIl2Cpp(Vector3[] values)
		{
			Il2CppStructArray<Vector3> array = new Il2CppStructArray<Vector3>(values.Length);
			for (int i = 0; i < values.Length; i++)
			{
				array[i] = values[i];
			}
			return array;
		}

		private static Il2CppStructArray<Vector2> ToIl2Cpp(Vector2[] values)
		{
			Il2CppStructArray<Vector2> array = new Il2CppStructArray<Vector2>(values.Length);
			for (int i = 0; i < values.Length; i++)
			{
				array[i] = values[i];
			}
			return array;
		}

		private static Il2CppStructArray<int> ToIl2Cpp(int[] values)
		{
			Il2CppStructArray<int> array = new Il2CppStructArray<int>(values.Length);
			for (int i = 0; i < values.Length; i++)
			{
				array[i] = values[i];
			}
			return array;
		}

		private readonly record struct AccessorBytes(byte[] Buffer, int Offset, int Stride);
	}
}

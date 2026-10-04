using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DataCenterAutomatorServers;

// The laptop is loaded by the game's native ModItem loader. It never receives a
// Server component; interaction only opens this lightweight management panel.
internal static class RealServersNotebook
{
	private const string FolderName = "Latitude_5400";
	private static readonly string RelativeFolder = Path.Combine("RealServers", "Assets", "Devices", "Dell", FolderName);

	private static bool _nativeLoadAttempted;
	private static readonly Dictionary<int, Vector3> NotebookTablePositions = new Dictionary<int, Vector3>();
	private static readonly Dictionary<int, Quaternion> NotebookTableRotations = new Dictionary<int, Quaternion>();
	private static readonly Dictionary<int, GameObject> KnownNotebooks = new Dictionary<int, GameObject>();
	private static DateTime _lastNotebookDiscoveryAt = DateTime.MinValue;
	private static DateTime _ownedRespawnNotBefore = DateTime.MinValue;
	private static DateTime _purchaseGuardUntil = DateTime.MinValue;
	private static GameObject _nativePrefab;
	private static bool _loggedMissingOwnedPrefab;
	private static bool _ownershipLoaded;
	private static bool _ownsNotebook;
	private static CatalogInjector _catalog;
	private static GameObject _root;
	private static Transform _content;
	private static readonly List<UnityAction> Actions = new List<UnityAction>();
	private static Font _font;
	// Four rows leave a dedicated footer for pagination. Five rows caused the
	// footer controls to overlap the last server entry at 16:9.
	private const int ServersPerPage = 4;
	private static int _serverPage;

	private static string OwnershipPath => Path.Combine(GameRoot, "UserData", "RealServers", "notebook-owned.flag");

	private static string GameRoot
	{
		get
		{
			foreach (string candidate in new[] { AppContext.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
			{
				if (!string.IsNullOrWhiteSpace(candidate))
				{
					string fullPath = Path.GetFullPath(candidate);
					if (File.Exists(Path.Combine(fullPath, "Data Center.exe")))
					{
						return fullPath;
					}
				}
			}
			return Path.GetFullPath(AppContext.BaseDirectory);
		}
	}

	internal static void BeginScene()
	{
		_nativeLoadAttempted = false;
		_nativePrefab = null;
		_loggedMissingOwnedPrefab = false;
		NotebookTablePositions.Clear();
		NotebookTableRotations.Clear();
		KnownNotebooks.Clear();
		_lastNotebookDiscoveryAt = DateTime.MinValue;
		_ownedRespawnNotBefore = DateTime.UtcNow.AddSeconds(8.0);
		_purchaseGuardUntil = DateTime.MinValue;
	}

	internal static void Ensure(CatalogInjector catalog)
	{
		_catalog = catalog;
		if (_nativeLoadAttempted)
		{
			return;
		}
		Type loaderType = ReflectionUtil.FindType("ModLoader");
		object loader = loaderType == null ? null : ReflectionUtil.ReadStaticMember(loaderType, "instance", "Instance");
		if (loader == null)
		{
			return;
		}
		string modsDirectory = Path.GetDirectoryName(typeof(RealServersNotebook).Assembly.Location) ?? string.Empty;
		string folderPath = Path.Combine(modsDirectory, RelativeFolder);
		if (!File.Exists(Path.Combine(folderPath, "config.json")))
		{
			Log.Warning("Notebook item folder was not found: " + folderPath);
			_nativeLoadAttempted = true;
			return;
		}
		_nativePrefab = ResolveNativePrefab(loader);
		if (_nativePrefab == null)
		{
			if (!ReflectionUtil.InvokeMethod(loader, "LoadModPack", folderPath))
			{
				Log.Warning("Could not register the Dell Latitude notebook with the native ModItem loader.");
				return;
			}
			_nativePrefab = ResolveNativePrefab(loader);
		}
		_nativeLoadAttempted = true;
		Log.Info("Registered native ModItem: Dell Latitude 5400 Real Servers Console.");
	}

	internal static bool TryHandleInteraction(object instance, CatalogInjector catalog)
	{
		if (!IsNotebook(instance))
		{
			return true;
		}
		_catalog = catalog;
		Open();
		return false;
	}

	internal static void Maintain(CatalogInjector catalog)
	{
		_catalog = catalog;
		foreach (KeyValuePair<int, GameObject> entry in new List<KeyValuePair<int, GameObject>>(KnownNotebooks))
		{
			if (entry.Value == null || !NotebookTablePositions.TryGetValue(entry.Key, out Vector3 position) || !NotebookTableRotations.TryGetValue(entry.Key, out Quaternion rotation))
			{
				KnownNotebooks.Remove(entry.Key);
				NotebookTablePositions.Remove(entry.Key);
				NotebookTableRotations.Remove(entry.Key);
				continue;
			}
			ApplyStaticTablePlacement(entry.Value, position, rotation);
		}
		if (DateTime.UtcNow - _lastNotebookDiscoveryAt < TimeSpan.FromSeconds(1.0))
		{
			return;
		}
		_lastNotebookDiscoveryAt = DateTime.UtcNow;
		Type usableObjectType = ReflectionUtil.FindType("UsableObject");
		if (usableObjectType == null)
		{
			return;
		}
		List<GameObject> discovered = new List<GameObject>();
		foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(Il2CppType.From(usableObjectType)))
		{
			if (candidate == null || !IsNotebook(candidate))
			{
				continue;
			}
			GameObject notebook = CatalogInjector.ResolveGameObject(candidate);
			if (notebook == null || IsNativePrefabTemplate(notebook) || !notebook.scene.IsValid() || !notebook.activeInHierarchy)
			{
				continue;
			}
			discovered.Add(notebook);
			MarkOwned();
			TryPlaceImmediately(candidate, catalog);
		}
		if (discovered.Count > 1)
		{
			for (int index = 1; index < discovered.Count; index++)
			{
				GameObject duplicate = discovered[index];
				KnownNotebooks.Remove(duplicate.GetInstanceID());
				NotebookTablePositions.Remove(duplicate.GetInstanceID());
				NotebookTableRotations.Remove(duplicate.GetInstanceID());
				UnityEngine.Object.Destroy(duplicate);
			}
			Log.Warning("Removed " + (discovered.Count - 1).ToString() + " duplicate Dell Latitude notebook instance(s).");
		}
		if (discovered.Count == 0 && HasOwnership() && DateTime.UtcNow >= _ownedRespawnNotBefore)
		{
			TrySpawnOwnedNotebook(catalog);
		}
	}

	internal static void TryPlaceImmediately(object instance, CatalogInjector catalog)
	{
		if (!IsNotebook(instance))
		{
			return;
		}
		_catalog = catalog;
		GameObject notebook = CatalogInjector.ResolveGameObject(instance);
		if (notebook == null)
		{
			return;
		}
		if (IsNativePrefabTemplate(notebook) || !notebook.scene.IsValid() || !notebook.activeInHierarchy)
		{
			return;
		}
		int instanceId = notebook.GetInstanceID();
		if (!NotebookTablePositions.TryGetValue(instanceId, out Vector3 tablePosition) ||
			!NotebookTableRotations.TryGetValue(instanceId, out Quaternion tableRotation))
		{
			if (!TryResolveTablePlacement(notebook, out tablePosition, out tableRotation))
			{
				return;
			}
			NotebookTablePositions[instanceId] = tablePosition;
			NotebookTableRotations[instanceId] = tableRotation;
			Log.Info("Resolved static Dell Latitude notebook table position at " + tablePosition + ".");
		}
		KnownNotebooks[instanceId] = notebook;
		MarkOwned();
		ApplyStaticTablePlacement(notebook, tablePosition, tableRotation);
	}

	internal static bool ShouldAllowPurchase(object instance)
	{
		if (!IsNotebookShopItem(instance))
		{
			return true;
		}
		// Ownership requests an automatic restore, but must not make the shop item
		// permanently unbuyable when restoration failed or belongs to another save.
		// Only a live notebook (or the short double-click guard) blocks a purchase.
		if (HasLiveNotebookInstance())
		{
			Log.Info("Blocked duplicate Dell Latitude notebook purchase; this save already owns the Real Servers console.");
			return false;
		}
		return true;
	}

	private static bool HasLiveNotebookInstance()
	{
		foreach (KeyValuePair<int, GameObject> entry in new List<KeyValuePair<int, GameObject>>(KnownNotebooks))
		{
			GameObject notebook = entry.Value;
			if (notebook != null && notebook.scene.IsValid() && notebook.activeInHierarchy)
			{
				return true;
			}
			KnownNotebooks.Remove(entry.Key);
			NotebookTablePositions.Remove(entry.Key);
			NotebookTableRotations.Remove(entry.Key);
		}
		return false;
	}

	private static bool IsNotebookShopItem(object instance)
	{
		if (instance == null)
		{
			return false;
		}
		object config = ReflectionUtil.ReadMember(instance, "config", "Config");
		if (config == null)
		{
			return false;
		}
		string itemName = ReflectionUtil.ReadMember(config, "itemName", "ItemName")?.ToString() ?? string.Empty;
		return itemName.Contains("Latitude 5400", StringComparison.OrdinalIgnoreCase) ||
			itemName.Contains("Real Servers Console", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasOwnership()
	{
		if (!_ownershipLoaded)
		{
			_ownershipLoaded = true;
			_ownsNotebook = File.Exists(OwnershipPath);
		}
		return _ownsNotebook;
	}

	private static void MarkOwned()
	{
		if (HasOwnership())
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(OwnershipPath) ?? ".");
			File.WriteAllText(OwnershipPath, "owned");
			_ownsNotebook = true;
			Log.Info("Persisted single Dell Latitude notebook ownership.");
		}
		catch (Exception exception)
		{
			Log.Warning("Could not persist Dell Latitude notebook ownership: " + exception.Message);
		}
	}

	private static void TrySpawnOwnedNotebook(CatalogInjector catalog)
	{
		_ownedRespawnNotBefore = DateTime.UtcNow.AddSeconds(10.0);
		Ensure(catalog);
		Type loaderType = ReflectionUtil.FindType("ModLoader");
		object loader = loaderType == null ? null : ReflectionUtil.ReadStaticMember(loaderType, "instance", "Instance");
		if (loader == null)
		{
			Log.Warning("Could not restore the owned Dell Latitude notebook because ModLoader is unavailable.");
			return;
		}
		GameObject prefab = ResolveNativePrefab(loader);
		if (prefab == null)
		{
			if (!_loggedMissingOwnedPrefab)
			{
				_loggedMissingOwnedPrefab = true;
				Log.Warning("Could not restore the owned Dell Latitude notebook because its native prefab is unavailable.");
			}
			return;
		}
		_loggedMissingOwnedPrefab = false;
		GameObject notebook = UnityEngine.Object.Instantiate(prefab);
		notebook.name = "RealServers_Dell_Latitude_5400";
		notebook.SetActive(true);
		Type usableObjectType = ReflectionUtil.FindType("UsableObject");
		object usableObject = usableObjectType == null ? null : ReflectionUtil.GetComponent(notebook, usableObjectType);
		if (usableObject == null)
		{
			UnityEngine.Object.Destroy(notebook);
			return;
		}
		ReflectionUtil.WriteMember(usableObject, FolderName, "modFolderName", "ModFolderName");
		ReflectionUtil.WriteMember(usableObject, true, "isModObject", "IsModObject");
		TryPlaceImmediately(usableObject, catalog);
		Log.Info("Restored the owned Dell Latitude notebook on the shop table.");
	}

	private static GameObject ResolveNativePrefab(object loader)
	{
		if (_nativePrefab != null)
		{
			return _nativePrefab;
		}
		if (loader == null)
		{
			return null;
		}
		foreach (string folderKey in new[]
		{
			FolderName,
			RelativeFolder,
			RelativeFolder.Replace('\\', '/'),
			Path.Combine(GameRoot, "Mods", RelativeFolder)
		})
		{
			GameObject byFolder = CatalogInjector.ResolveGameObject(ReflectionUtil.InvokeMethodReturn(loader, "GetModPrefabByFolder", folderKey));
			if (byFolder != null)
			{
				_nativePrefab = byFolder;
				return byFolder;
			}
		}

		Type shopItemType = ReflectionUtil.FindType("ModShopItem");
		if (shopItemType == null)
		{
			return null;
		}
		foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(Il2CppType.From(shopItemType)))
		{
			if (candidate == null || !IsNotebookShopItem(candidate))
			{
				continue;
			}
			object rawModId = ReflectionUtil.ReadMember(candidate, "modID", "ModID", "modId", "ModId");
			if (rawModId == null || !int.TryParse(rawModId.ToString(), out int modId))
			{
				continue;
			}
			GameObject byId = CatalogInjector.ResolveGameObject(ReflectionUtil.InvokeMethodReturn(loader, "GetModPrefab", modId));
			if (byId != null)
			{
				_nativePrefab = byId;
				Log.Info("Resolved Dell Latitude notebook native prefab through ModShopItem id " + modId.ToString() + ".");
				return byId;
			}
		}
		return null;
	}

	private static bool IsNativePrefabTemplate(GameObject candidate)
	{
		return candidate != null && _nativePrefab != null && candidate.GetInstanceID() == _nativePrefab.GetInstanceID();
	}

	private static bool IsNotebook(object instance)
	{
		string folder = ReflectionUtil.ReadMember(instance, "modFolderName", "ModFolderName")?.ToString() ?? string.Empty;
		// Native saves retain the ModItem folder, but depending on load order the
		// runtime may not have restored isModObject when our scan runs. The folder
		// is the persistent identity used by the game's ModItem save data.
		return string.Equals(folder, FolderName, StringComparison.OrdinalIgnoreCase) ||
			folder.EndsWith("/" + FolderName, StringComparison.OrdinalIgnoreCase) ||
			folder.EndsWith("\\" + FolderName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryResolveTablePlacement(GameObject notebook, out Vector3 tablePosition, out Quaternion tableRotation)
	{
		tablePosition = Vector3.zero;
		tableRotation = Quaternion.identity;
		Transform computerTransform = FindShopComputerTransform();
		if (computerTransform == null)
		{
			Log.Warning("Notebook was created, but the shop computer transform is not available yet.");
			return false;
		}
		Vector3 tablePoint = FindTablePoint(computerTransform, notebook);
		if (tablePoint == Vector3.negativeInfinity)
		{
			Log.Warning("Notebook was created, but no table surface beside the shop computer was found.");
			return false;
		}
		tablePosition = tablePoint + Vector3.up * 0.025f;
		// The imported Latitude model's screen axis is rotated relative to the
		// shop computer. Turn it right so the display faces the player at the desk.
		tableRotation = Quaternion.LookRotation(computerTransform.forward, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
		return true;
	}

	private static void ApplyStaticTablePlacement(GameObject notebook, Vector3 tablePosition, Quaternion tableRotation)
	{
		notebook.transform.position = tablePosition;
		notebook.transform.rotation = tableRotation;
		Rigidbody body = notebook.GetComponent<Rigidbody>();
		if (body != null)
		{
			body.velocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.useGravity = false;
			body.isKinematic = true;
		}
	}

	private static Transform FindShopComputerTransform()
	{
		Type computerShopType = ReflectionUtil.FindType("ComputerShop");
		if (computerShopType == null)
		{
			return null;
		}
		foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(Il2CppType.From(computerShopType)))
		{
			GameObject computer = CatalogInjector.ResolveGameObject(candidate);
			if (computer != null && computer.activeInHierarchy)
			{
				return computer.transform;
			}
		}
		return null;
	}

	private static Vector3 FindTablePoint(Transform computer, GameObject notebook)
	{
		Vector3[] offsets =
		{
			computer.right * 0.65f,
			-computer.right * 0.65f,
			computer.forward * 0.65f,
			-computer.forward * 0.65f
		};
		float bestHeight = float.NegativeInfinity;
		Vector3 bestPoint = Vector3.negativeInfinity;
		foreach (Vector3 offset in offsets)
		{
			Vector3 start = computer.position + offset + Vector3.up * 1.8f;
			RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, 3.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			foreach (RaycastHit hit in hits)
			{
				if (hit.collider == null || hit.collider.transform.IsChildOf(notebook.transform))
				{
					continue;
				}
				if (hit.point.y > bestHeight)
				{
					bestHeight = hit.point.y;
					bestPoint = hit.point;
				}
			}
		}
		return bestPoint;
	}

	private static void Open()
	{
		EnsureUi();
		if (_root == null || _catalog == null)
		{
			return;
		}
		_root.SetActive(true);
		Refresh();
		ShowUiCursor();
	}

	internal static void MaintainInput()
	{
		if (_root == null || !_root.activeSelf)
		{
			return;
		}
		ShowUiCursor();
		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			Close();
		}
	}

	private static void EnsureUi()
	{
		if (_root != null)
		{
			return;
		}
		_root = CreateUiObject("RealServers_NotebookPanel");
		Canvas canvas = _root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 950;
		CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		_root.AddComponent<GraphicRaycaster>();
		Stretch(_root.GetComponent<RectTransform>());

		RawImage shade = CreatePanel("Shade", _root.transform, new Color(0f, 0f, 0f, 0.7f));
		Stretch(shade.GetComponent<RectTransform>());
		RawImage panel = CreatePanel("Panel", _root.transform, new Color(0.025f, 0.04f, 0.07f, 0.99f));
		SetRect(panel.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1120f, 660f));
		RawImage header = CreatePanel("Header", panel.transform, new Color(0.035f, 0.09f, 0.16f, 1f));
		SetRect(header.GetComponent<RectTransform>(), new Vector2(0f, 278f), new Vector2(1120f, 104f));
		CreateText("Title", header.transform, "REAL SERVERS  |  DELL NOTEBOOK", 28, TextAnchor.MiddleLeft, new Vector2(-160f, 0f), new Vector2(760f, 54f), Color.white);
		CreateButton("Close", header.transform, ModText.Get("Close"), new Vector2(460f, 0f), new Vector2(120f, 50f), Close);

		RawImage content = CreatePanel("Content", panel.transform, new Color(0.01f, 0.017f, 0.03f, 0.8f));
		SetRect(content.GetComponent<RectTransform>(), new Vector2(0f, -55f), new Vector2(1040f, 460f));
		_content = content.transform;
		_root.SetActive(false);
	}

	private static void Refresh()
	{
		ClearContent();
		IReadOnlyList<CatalogInjector.ComputerAppServer> servers = _catalog.GetComputerAppServers();
		if (servers.Count == 0)
		{
			CreateText("Empty", _content, ModText.Get("NoMounted"), 24, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(940f, 80f), Color.white);
			return;
		}
		CreateText("Heading", _content, ModText.Get("Servers"), 20, TextAnchor.MiddleLeft, new Vector2(-186f, 185f), new Vector2(620f, 36f), new Color(0.7f, 0.82f, 0.95f, 1f));
		int pageCount = Math.Max(1, (servers.Count + ServersPerPage - 1) / ServersPerPage);
		_serverPage = Mathf.Clamp(_serverPage, 0, pageCount - 1);
		int first = _serverPage * ServersPerPage;
		int count = Math.Min(ServersPerPage, servers.Count - first);
		for (int index = 0; index < count; index++)
		{
			CatalogInjector.ComputerAppServer server = servers[first + index];
			float y = 108f - index * 75f;
			RawImage row = CreatePanel("Server_" + index, _content, new Color(0.055f, 0.09f, 0.14f, 1f));
			SetRect(row.GetComponent<RectTransform>(), new Vector2(0f, y), new Vector2(940f, 68f));
			CreateText("Name", row.transform, server.DisplayName, 18, TextAnchor.MiddleLeft, new Vector2(-133f, 12f), new Vector2(630f, 28f), Color.white);
			CreateText("State", row.transform, "IP " + server.IpAddress + "  |  " + ModText.Get(server.IsPoweredOn ? "PoweredOn" : "PoweredOff"), 14, TextAnchor.MiddleLeft, new Vector2(-133f, -15f), new Vector2(630f, 24f), new Color(0.62f, 0.77f, 0.9f, 1f));
			CatalogInjector.ComputerAppServer selected = server;
			CreateButton("Configure", row.transform, ModText.Get("Configure"), new Vector2(385f, 0f), new Vector2(140f, 42f), () => _catalog.OpenComputerAppConfigurationPanel(selected.Server));
		}
		if (pageCount > 1)
		{
			CreateButton("PreviousPage", _content, "<", new Vector2(-410f, -185f), new Vector2(60f, 38f), () => { _serverPage = Mathf.Max(0, _serverPage - 1); Refresh(); });
			CreateText("Page", _content, (_serverPage + 1).ToString() + " / " + pageCount.ToString(), 15, TextAnchor.MiddleCenter, new Vector2(0f, -185f), new Vector2(150f, 38f), new Color(0.7f, 0.82f, 0.95f, 1f));
			CreateButton("NextPage", _content, ">", new Vector2(410f, -185f), new Vector2(60f, 38f), () => { _serverPage = Mathf.Min(pageCount - 1, _serverPage + 1); Refresh(); });
		}
	}

	private static void Close()
	{
		if (_root != null)
		{
			_root.SetActive(false);
		}
		try
		{
			Il2Cpp.PlayerManager.instance?.LockedCursorForPlayerMovement();
		}
		catch
		{
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
		}
	}

	private static void ShowUiCursor()
	{
		try
		{
			Il2Cpp.PlayerManager.instance?.ConfinedCursorforUI();
		}
		catch
		{
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.Confined;
		}
	}

	private static void ClearContent()
	{
		for (int index = _content.childCount - 1; index >= 0; index--)
		{
			UnityEngine.Object.Destroy(_content.GetChild(index).gameObject);
		}
		Actions.Clear();
	}

	private static RawImage CreatePanel(string name, Transform parent, Color color)
	{
		GameObject obj = CreateUiObject(name);
		obj.transform.SetParent(parent, false);
		RawImage image = obj.AddComponent<RawImage>();
		image.color = color;
		return image;
	}

	private static void CreateButton(string name, Transform parent, string label, Vector2 position, Vector2 size, Action callback)
	{
		RawImage image = CreatePanel(name, parent, new Color(0.08f, 0.31f, 0.43f, 1f));
		SetRect(image.GetComponent<RectTransform>(), position, size);
		EventTrigger trigger = image.gameObject.AddComponent<EventTrigger>();
		EventTrigger.Entry entry = new EventTrigger.Entry();
		entry.eventID = EventTriggerType.PointerClick;
		entry.callback = new EventTrigger.TriggerEvent();
		entry.callback.AddListener((UnityAction<BaseEventData>)(_ => callback()));
		Il2CppSystem.Collections.Generic.List<EventTrigger.Entry> entries = new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
		entries.Add(entry);
		trigger.triggers = entries;
		CreateText("Label", image.transform, label, 17, TextAnchor.MiddleCenter, Vector2.zero, size, Color.white);
	}

	private static Text CreateText(string name, Transform parent, string value, int size, TextAnchor alignment, Vector2 position, Vector2 dimensions, Color color)
	{
		GameObject obj = CreateUiObject(name);
		obj.transform.SetParent(parent, false);
		Text text = obj.AddComponent<Text>();
		text.font = Font;
		text.text = value;
		text.fontSize = size;
		text.alignment = alignment;
		text.color = color;
		SetRect(obj.GetComponent<RectTransform>(), position, dimensions);
		return text;
	}

	private static Font Font
	{
		get
		{
			if (_font != null)
			{
				return _font;
			}
			foreach (Text gameText in Resources.FindObjectsOfTypeAll<Text>())
			{
				if (gameText != null && gameText.font != null)
				{
					_font = gameText.font;
					return _font;
				}
			}
			_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			return _font;
		}
	}

	private static GameObject CreateUiObject(string name)
	{
		return new GameObject(name, new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
	}

	private static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
	{
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = position;
		rect.sizeDelta = size;
	}
}

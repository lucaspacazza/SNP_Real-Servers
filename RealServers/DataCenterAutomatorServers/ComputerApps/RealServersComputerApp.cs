using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DataCenterAutomatorServers;

// Lightweight ComputerShop application. It creates one native Unity UI tree per
// computer and rebuilds its content only when the player opens a page.
internal static class RealServersComputerApp
{
	private const string IconName = "RealServers_ComputerAppIcon";
	private const string ScreenName = "RealServers_ComputerAppScreen";

	private static readonly Dictionary<GameObject, AppState> States = new Dictionary<GameObject, AppState>();
	private static readonly HashSet<GameObject> PreparedDesktopIcons = new HashSet<GameObject>();
	private static Font _font;

	internal static void Ensure(object computerShop, CatalogInjector catalog)
	{
		GameObject computerObject = CatalogInjector.ResolveGameObject(computerShop);
		GameObject canvas = CatalogInjector.ResolveGameObject(ReflectionUtil.ReadMember(computerShop, "canvasComputerShop", "CanvasComputerShop"));
		GameObject mainScreen = CatalogInjector.ResolveGameObject(ReflectionUtil.ReadMember(computerShop, "mainScreen", "MainScreen"));
		if (computerObject == null || canvas == null || mainScreen == null)
		{
			Log.Warning("Real Servers computer app is waiting for ComputerShop UI fields: computer=" + (computerObject != null).ToString() + ", canvas=" + (canvas != null).ToString() + ", mainScreen=" + (mainScreen != null).ToString() + ".");
			return;
		}

		Button nativeButtonTemplate = FindDesktopTileTemplate(mainScreen);
		if (!States.TryGetValue(computerObject, out AppState state) || state.Root == null)
		{
			state = CreateState(computerObject, mainScreen, catalog, nativeButtonTemplate);
			States[computerObject] = state;
			Log.Info("Created Real Servers computer app screen under '" + mainScreen.name + "'.");
		}
		EnsureDesktopIcon(mainScreen, state);
	}

	private static AppState CreateState(GameObject computerObject, GameObject mainScreen, CatalogInjector catalog, Button nativeButtonTemplate)
	{
		GameObject root = CreateUiObject(ScreenName);
		root.transform.SetParent(mainScreen.transform.parent, false);
		CopyRect(GetRect(mainScreen), GetRect(root));
		RawImage backdrop = root.AddComponent<RawImage>();
		backdrop.color = new Color(0.025f, 0.04f, 0.075f, 0.985f);

		AppState state = new AppState(computerObject, mainScreen, root, catalog, nativeButtonTemplate);
		RawImage topBar = CreatePanel("TopBar", root.transform, new Color(0.03f, 0.08f, 0.14f, 1f));
		SetAnchoredRegion(GetRect(topBar.gameObject), new Vector2(0f, 0.86f), new Vector2(1f, 1f));
		GameObject back = CreateButton(state, "Back", topBar.transform, "<", Vector2.zero, new Vector2(54f, 50f), () => state.BackAction?.Invoke(), new Color(0.19f, 0.24f, 0.3f, 1f));
		SetAnchoredRegion(GetRect(back), new Vector2(0.03f, 0.2f), new Vector2(0.075f, 0.8f));
		state.Title = CreateText("Title", topBar.transform, "REAL SERVERS", 32, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
		SetAnchoredRegion(GetRect(state.Title.gameObject), new Vector2(0.10f, 0.12f), new Vector2(0.72f, 0.88f));
		GameObject close = CreateButton(state, "Close", topBar.transform, ModText.Get("Close"), Vector2.zero, new Vector2(138f, 50f), () => Close(state), new Color(0.19f, 0.24f, 0.3f, 1f));
		SetAnchoredRegion(GetRect(close), new Vector2(0.84f, 0.2f), new Vector2(0.96f, 0.8f));

		RawImage content = CreatePanel("Content", root.transform, new Color(0.015f, 0.025f, 0.05f, 0.28f));
		SetAnchoredRegion(GetRect(content.gameObject), new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.84f));
		state.Content = content.transform;
		root.SetActive(false);
		return state;
	}

	private static void EnsureDesktopIcon(GameObject mainScreen, AppState state)
	{
		Transform existing = mainScreen.transform.Find(IconName);
		if (existing != null && PreparedDesktopIcons.Contains(mainScreen))
		{
			return;
		}
		if (existing != null)
		{
			UnityEngine.Object.Destroy(existing.gameObject);
		}

		RawImage icon = CreatePanel(IconName, mainScreen.transform, new Color(0.055f, 0.32f, 0.43f, 1f));
		SetRect(GetRect(icon.gameObject), new Vector2(-40f, 255f), new Vector2(105f, 105f));
		MakeClickable(icon.gameObject, () => Open(state));
		CreateText("Mark", icon.transform, "RS", 25, TextAnchor.MiddleCenter, new Vector2(0f, 8f), new Vector2(95f, 55f), Color.white);
		CreateText("Label", icon.transform, "Real Servers", 13, TextAnchor.MiddleCenter, new Vector2(0f, -43f), new Vector2(150f, 28f), Color.white);
		PreparedDesktopIcons.Add(mainScreen);
		Log.Info("Created Real Servers desktop icon on ComputerShop main screen.");
	}

	private static Button FindDesktopTileTemplate(GameObject mainScreen)
	{
		foreach (Button button in mainScreen.GetComponentsInChildren<Button>(includeInactive: true))
		{
			if (button == null || button.gameObject.name.StartsWith(IconName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			RectTransform rect = button.GetComponent<RectTransform>();
			if (rect != null && rect.sizeDelta.x > 50f && rect.sizeDelta.y > 50f)
			{
				return button;
			}
		}
		return null;
	}

	private static void Open(AppState state)
	{
		state.MainScreen.SetActive(false);
		SetDesktopIconVisible(state, false);
		state.Root.SetActive(true);
		ShowBrands(state);
		ClearUiSelection();
		TryShowCursor();
	}

	private static void Close(AppState state)
	{
		state.Root.SetActive(false);
		state.MainScreen.SetActive(true);
		SetDesktopIconVisible(state, true);
		ClearUiSelection();
	}

	private static void ShowBrands(AppState state)
	{
		state.BackAction = () => Close(state);
		ClearChildren(state.Content);
		state.Title.text = "REAL SERVERS";
		IReadOnlyList<CatalogInjector.ComputerAppServer> servers = state.Catalog.GetComputerAppServers();
		Dictionary<string, List<CatalogInjector.ComputerAppServer>> byBrand = new Dictionary<string, List<CatalogInjector.ComputerAppServer>>(StringComparer.OrdinalIgnoreCase)
		{
			["Lenovo"] = new List<CatalogInjector.ComputerAppServer>(),
			["Dell"] = new List<CatalogInjector.ComputerAppServer>()
		};
		foreach (CatalogInjector.ComputerAppServer server in servers)
		{
			if (!byBrand.TryGetValue(server.Brand, out List<CatalogInjector.ComputerAppServer> list))
			{
				list = new List<CatalogInjector.ComputerAppServer>();
				byBrand[server.Brand] = list;
			}
			list.Add(server);
		}

		int index = 0;
		foreach (KeyValuePair<string, List<CatalogInjector.ComputerAppServer>> pair in byBrand)
		{
			int cardIndex = index++;
			string brand = pair.Key;
			List<CatalogInjector.ComputerAppServer> brandServers = pair.Value;
			Color color = BrandColor(brand);
			RawImage card = CreatePanel("Brand_" + brand, state.Content, new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 1f));
			float left = 0.035f + cardIndex * 0.12f;
			SetAnchoredRegion(GetRect(card.gameObject), new Vector2(left, 0.72f), new Vector2(left + 0.09f, 0.92f));
			MakeClickable(card.gameObject, () => ShowServers(state, brand));
			Text wordmark = CreateText("Wordmark", card.transform, brand.ToUpperInvariant(), 19, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, color);
			SetAnchoredRegion(GetRect(wordmark.gameObject), new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.75f));
		}
	}

	private static void ShowServers(AppState state, string brand)
	{
		state.BackAction = () => ShowBrands(state);
		ClearChildren(state.Content);
		state.Title.text = "REAL SERVERS  |  " + brand.ToUpperInvariant();
		List<CatalogInjector.ComputerAppServer> servers = state.Catalog.GetComputerAppServers()
			.Where(server => string.Equals(server.Brand, brand, StringComparison.OrdinalIgnoreCase))
			.ToList();
		int index = 0;
		float gap = 0.018f;
		float rowHeight = Mathf.Clamp(0.78f / Mathf.Max(1, servers.Count) - gap, 0.075f, 0.16f);
		foreach (CatalogInjector.ComputerAppServer server in servers)
		{
			int row = index++;
			RawImage item = CreatePanel("Server_" + row.ToString(CultureInfo.InvariantCulture), state.Content, new Color(0.045f, 0.075f, 0.12f, 1f));
			float top = 0.92f - row * (rowHeight + gap);
			float bottom = top - rowHeight;
			SetAnchoredRegion(GetRect(item.gameObject), new Vector2(0.03f, bottom), new Vector2(0.97f, top));
			CatalogInjector.ComputerAppServer selected = server;
			MakeClickable(item.gameObject, () => OpenServerPanel(state, selected));
			Text name = CreateText("Name", item.transform, server.DisplayName, 18, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
			SetAnchoredRegion(GetRect(name.gameObject), new Vector2(0.04f, 0.52f), new Vector2(0.62f, 0.92f));
			Text details = CreateText("Details", item.transform, "IP " + server.IpAddress + "  |  " + ModText.Get(server.IsPoweredOn ? "PoweredOn" : "PoweredOff"), 13, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, new Color(0.65f, 0.78f, 0.9f, 1f));
			SetAnchoredRegion(GetRect(details.gameObject), new Vector2(0.04f, 0.10f), new Vector2(0.68f, 0.52f));
			Text open = CreateText("Open", item.transform, ModText.Get("Configure") + " >", 16, TextAnchor.MiddleRight, Vector2.zero, Vector2.zero, BrandColor(brand));
			SetAnchoredRegion(GetRect(open.gameObject), new Vector2(0.72f, 0.18f), new Vector2(0.96f, 0.82f));
		}
		if (index == 0)
		{
			CreateText("Empty", state.Content, ModText.Get("NoMounted"), 24, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1000f, 80f), Color.white);
		}
	}

	private static void ShowServerConfiguration(AppState state, CatalogInjector.ComputerAppServer selected)
	{
		state.BackAction = () => ShowServers(state, selected.Brand);
		ClearChildren(state.Content);
		CatalogInjector.ComputerAppServer refreshed = state.Catalog.RefreshComputerAppServer(selected.Server) ?? selected;
		state.Title.text = "REAL SERVERS  |  " + refreshed.Brand.ToUpperInvariant();
		Text name = CreateText("Name", state.Content, refreshed.DisplayName, 23, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
		SetAnchoredRegion(GetRect(name.gameObject), new Vector2(0.04f, 0.82f), new Vector2(0.70f, 0.95f));
		Text status = CreateText("Status", state.Content, ModText.Get(refreshed.IsPoweredOn ? "PoweredOn" : "PoweredOff"), 15, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, refreshed.IsPoweredOn ? new Color(0.35f, 1f, 0.55f, 1f) : new Color(1f, 0.5f, 0.35f, 1f));
		SetAnchoredRegion(GetRect(status.gameObject), new Vector2(0.04f, 0.74f), new Vector2(0.45f, 0.82f));

		RawImage overview = CreatePanel("Overview", state.Content, new Color(0.025f, 0.045f, 0.08f, 1f));
		SetAnchoredRegion(GetRect(overview.gameObject), new Vector2(0.03f, 0.08f), new Vector2(0.49f, 0.70f));
		Text infoTitle = CreateText("InfoTitle", overview.transform, ModText.Get("ServerStatus"), 17, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
		SetAnchoredRegion(GetRect(infoTitle.gameObject), new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f));
		Text ip = CreateText("Ip", overview.transform, ModText.Get("IpAddress") + "\n" + refreshed.IpAddress, 22, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, new Color(0.78f, 0.88f, 0.98f, 1f));
		SetAnchoredRegion(GetRect(ip.gameObject), new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.72f));
		Text customer = CreateText("Customer", overview.transform, refreshed.CustomerId < 0 ? ModText.Get("NoCustomer") : ModText.Get("Customer") + " " + refreshed.CustomerId.ToString(CultureInfo.InvariantCulture), 17, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
		SetAnchoredRegion(GetRect(customer.gameObject), new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.30f));

		RawImage actions = CreatePanel("Actions", state.Content, new Color(0.025f, 0.045f, 0.08f, 1f));
		SetAnchoredRegion(GetRect(actions.gameObject), new Vector2(0.51f, 0.08f), new Vector2(0.97f, 0.70f));
		Text actionsTitle = CreateText("ActionsTitle", actions.transform, ModText.Get("Configuration"), 17, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, Color.white);
		SetAnchoredRegion(GetRect(actionsTitle.gameObject), new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f));
		GameObject power = CreateButton(state, "Power", actions.transform, "\u23FB", Vector2.zero, Vector2.zero, () => { state.Catalog.ToggleComputerAppPower(refreshed.Server); ShowServerConfiguration(state, refreshed); }, refreshed.IsPoweredOn ? new Color(0.48f, 0.18f, 0.14f, 1f) : new Color(0.13f, 0.36f, 0.22f, 1f));
		SetAnchoredRegion(GetRect(power), new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.70f));
		GameObject previous = CreateButton(state, "Previous", actions.transform, "<", Vector2.zero, Vector2.zero, () => { state.Catalog.ChangeComputerAppCustomer(refreshed.Server, false); ShowServerConfiguration(state, refreshed); }, new Color(0.11f, 0.31f, 0.47f, 1f));
		SetAnchoredRegion(GetRect(previous), new Vector2(0.08f, 0.38f), new Vector2(0.47f, 0.50f));
		GameObject next = CreateButton(state, "Next", actions.transform, ">", Vector2.zero, Vector2.zero, () => { state.Catalog.ChangeComputerAppCustomer(refreshed.Server, true); ShowServerConfiguration(state, refreshed); }, new Color(0.11f, 0.31f, 0.47f, 1f));
		SetAnchoredRegion(GetRect(next), new Vector2(0.53f, 0.38f), new Vector2(0.92f, 0.50f));
		GameObject configureIp = CreateButton(state, "Ip", actions.transform, ModText.Get("ConfigureIp"), Vector2.zero, Vector2.zero, () => OpenNativeIpEditor(state, refreshed.Server), new Color(0.11f, 0.42f, 0.62f, 1f));
		SetAnchoredRegion(GetRect(configureIp), new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.26f));
	}

	private static void OpenServerPanel(AppState state, CatalogInjector.ComputerAppServer selected)
	{
		state.Catalog.OpenComputerAppConfigurationPanel(selected.Server);
	}

	private static void OpenNativeIpEditor(AppState state, object server)
	{
		state.Root.SetActive(false);
		state.Catalog.OpenComputerAppIpEditor(server);
	}

	private static RawImage CreatePanel(string name, Transform parent, Color color)
	{
		GameObject objectForUi = CreateUiObject(name);
		objectForUi.transform.SetParent(parent, false);
		RawImage image = objectForUi.AddComponent<RawImage>();
		image.texture = Texture2D.whiteTexture;
		image.color = color;
		return image;
	}

	private static GameObject CreateButton(AppState state, string name, Transform parent, string label, Vector2 position, Vector2 size, Action callback, Color color)
	{
		RawImage image = CreatePanel(name, parent, color);
		GameObject buttonObject = image.gameObject;
		SetRect(GetRect(buttonObject), position, size);
		MakeClickable(buttonObject, callback);
		Text labelText = CreateText("Label", buttonObject.transform, label, 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, Color.white);
		SetStretch(GetRect(labelText.gameObject));
		return buttonObject;
	}

	private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color)
	{
		GameObject objectForUi = CreateUiObject(name);
		objectForUi.transform.SetParent(parent, false);
		Text text = objectForUi.AddComponent<Text>();
		text.font = GetFont();
		text.text = value;
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		SetRect(GetRect(objectForUi), position, size);
		return text;
	}

	private static Font GetFont()
	{
		if (_font == null)
		{
			_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}
		return _font;
	}

	private static GameObject CreateUiObject(string name)
	{
		return new GameObject(name, new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
	}

	private static RectTransform GetRect(GameObject gameObject)
	{
		return gameObject.GetComponent<RectTransform>();
	}

	private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
	{
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = position;
		rect.sizeDelta = size;
	}

	private static void SetStretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private static void CopyRect(RectTransform source, RectTransform target)
	{
		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.anchoredPosition = source.anchoredPosition;
		target.sizeDelta = source.sizeDelta;
		target.localScale = source.localScale;
	}

	private static void SetAnchoredRegion(RectTransform rect, Vector2 minimum, Vector2 maximum)
	{
		rect.anchorMin = minimum;
		rect.anchorMax = maximum;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private static void ClearChildren(Transform parent)
	{
		for (int index = parent.childCount - 1; index >= 0; index--)
		{
			UnityEngine.Object.Destroy(parent.GetChild(index).gameObject);
		}
	}

	private static Color BrandColor(string brand)
	{
		return string.Equals(brand, "Lenovo", StringComparison.OrdinalIgnoreCase)
			? new Color(1f, 0.2f, 0.16f, 1f)
			: new Color(0.25f, 0.72f, 1f, 1f);
	}

	private static void TryShowCursor()
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

	private static void ClearUiSelection()
	{
		if (EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}

	private static void MakeClickable(GameObject target, Action callback)
	{
		EventTrigger trigger = target.AddComponent<EventTrigger>();
		EventTrigger.Entry entry = new EventTrigger.Entry();
		entry.eventID = EventTriggerType.PointerClick;
		entry.callback = new EventTrigger.TriggerEvent();
		entry.callback.AddListener((UnityAction<BaseEventData>)(_ => callback()));
		Il2CppSystem.Collections.Generic.List<EventTrigger.Entry> entries = new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
		entries.Add(entry);
		trigger.triggers = entries;
	}

	private static void SetDesktopIconVisible(AppState state, bool visible)
	{
		Transform icon = state.MainScreen.transform.Find(IconName);
		if (icon != null)
		{
			icon.gameObject.SetActive(visible);
		}
	}

	private sealed class AppState
	{
		internal AppState(GameObject computerObject, GameObject mainScreen, GameObject root, CatalogInjector catalog, Button nativeButtonTemplate)
		{
			ComputerObject = computerObject;
			MainScreen = mainScreen;
			Root = root;
			Catalog = catalog;
			NativeButtonTemplate = nativeButtonTemplate;
		}

		internal readonly GameObject ComputerObject;
		internal readonly GameObject MainScreen;
		internal readonly GameObject Root;
		internal readonly CatalogInjector Catalog;
		internal readonly Button NativeButtonTemplate;
		internal Transform Content;
		internal Text Title;
		internal Action BackAction;
	}
}

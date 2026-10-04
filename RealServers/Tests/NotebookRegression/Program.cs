using System.Reflection;
using System.Reflection.Emit;

internal static class Program
{
	private static int _failed;

	private static int Main()
	{
		Assembly assembly = Assembly.Load("RealServers");
		Assembly gameAssembly = Assembly.Load("Assembly-CSharp");

		Run("The production assembly contains no notebook implementation", () =>
		{
			Type notebook = assembly.GetType("DataCenterAutomatorServers.RealServersNotebook", throwOnError: false);
			Assert(notebook == null, "RealServersNotebook is still compiled into RealServers.dll.");
		});

		Run("The Melon entry point contains no notebook callbacks", () =>
		{
			AssertHasNoNotebookMethods(RequireType(assembly, "DataCenterAutomatorServers.DataCenterAutomatorServersMod"));
		});

		Run("The catalog injector contains no notebook maintenance path", () =>
		{
			AssertHasNoNotebookMethods(RequireType(assembly, "DataCenterAutomatorServers.CatalogInjector"));
		});

		Run("The Harmony registry contains no notebook or ModShopItem purchase hook", () =>
		{
			Type patchRegistry = RequireType(assembly, "DataCenterAutomatorServers.PatchRegistry");
			AssertHasNoNotebookMethods(patchRegistry);
			string[] methodNames = GetDeclaredMethodNames(patchRegistry);
			Assert(!methodNames.Contains("PatchModShopItemPurchaseMethods"), "The ModShopItem purchase patch is still present.");
			Assert(!methodNames.Contains("ModShopItemPurchasePrefix"), "The ModShopItem purchase prefix is still present.");
		});

		Run("Scheduled maintenance limits rack retries to the pending save-load window", () =>
		{
			Type catalogInjector = RequireType(assembly, "DataCenterAutomatorServers.CatalogInjector");
			MethodInfo maintain = catalogInjector.GetMethod(
				"MaintainSpawnedVariants",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(catalogInjector.FullName, "MaintainSpawnedVariants");
			string[] calledMethods = GetCalledMethodNames(maintain);
			Assert(!calledMethods.Contains("RepairPersistedSceneVariants"),
				"The one-second maintenance loop still scans every persisted server.");
			Assert(calledMethods.Contains("ShouldRetryLoadedRacks"),
				"Rack retries must stop when no saved reservations remain or the bounded window expires.");
		});

		Run("Dell R440 front collider clearance follows 0 and 180 degree local facing", () =>
		{
			Type visualLoader = RequireType(assembly, "DataCenterAutomatorServers.RuntimeGltfVisualLoader");
			MethodInfo insetFrontDepth = visualLoader.GetMethod(
				"InsetFrontDepth",
				BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "InsetFrontDepth");
			MethodInfo configureColliders = visualLoader.GetMethod(
				"ConfigureOriginalInteractionColliders",
				BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "ConfigureOriginalInteractionColliders");
			Assert(GetCalledMethodNames(configureColliders).Contains("InsetFrontDepth"),
				"The Dell collider configuration does not apply the tested front-depth clearance.");

			const float minZ = -0.3925f;
			const float maxZ = 0.3925f;
			const float clearance = 0.06f;
			(float MinZ, float MaxZ) unrotated = ((float MinZ, float MaxZ))insetFrontDepth.Invoke(
				null,
				new object[] { minZ, maxZ, -1f, clearance });
			(float MinZ, float MaxZ) rotated180 = ((float MinZ, float MaxZ))insetFrontDepth.Invoke(
				null,
				new object[] { minZ, maxZ, 1f, clearance });

			Assert(NearlyEqual(unrotated.MinZ, minZ + clearance) && NearlyEqual(unrotated.MaxZ, maxZ),
				"The unrotated -Z front face was not inset toward the chassis body.");
			Assert(NearlyEqual(rotated180.MinZ, minZ) && NearlyEqual(rotated180.MaxZ, maxZ - clearance),
				"The Dell's 180-degree +Z front face was not inset toward the chassis body.");
			Assert(NearlyEqual(rotated180.MaxZ - rotated180.MinZ, (maxZ - minZ) - clearance),
				"The front clearance changed the wrong amount of chassis depth.");
		});

		Run("Dell XE8640 uses the proven Dell carrier flow as a 4U server", () =>
		{
			Type specType = RequireType(assembly, "DataCenterAutomatorServers.ServerVariantSpec");
			Array all = (Array)(specType.GetField("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
				?? throw new MissingFieldException(specType.FullName, "All"));
			object xe = all.Cast<object>().SingleOrDefault(candidate =>
				string.Equals(ReadProperty<string>(candidate, "FamilyKey"), "dell_xe8640_lenovo_carrier", StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException("Dell XE8640 is missing from ServerVariantSpec.All.");

			Assert(ReadProperty<int>(xe, "RackUnits") == 4, "Dell XE8640 does not reserve four rack units.");
			Assert(ReadProperty<int>(xe, "HeldObjectType") == 3, "Dell XE8640 does not use the game's supported large-server held pose.");
			Assert(ReadProperty<int>(xe, "BaseItemId") == 5, "Dell XE8640 is not based on the native large Green2 server.");
			Assert(ReadProperty<string>(xe, "BaseAssetName") == "ShopItemSO_Server_Green2", "Dell XE8640 is not cloned from the native large Green2 shop item.");
			Assert(ReadProperty<bool>(xe, "UsesLenovoCarrierProfile"), "Dell XE8640 does not share the R440 purchase/rack/save interaction flow.");
			Assert(ReadProperty<bool>(xe, "UsesDellStaticCarrierProfile"), "Dell XE8640 does not share the R440 orientation/collider/rendering corrections.");
			Assert(ReadProperty<string>(xe, "ModelAssetRelativePath").EndsWith("PowerEdge_XE8640_runtime_compatible.glb", StringComparison.OrdinalIgnoreCase),
				"Dell XE8640 points at the wrong runtime GLB.");

			Type visualLoader = RequireType(assembly, "DataCenterAutomatorServers.RuntimeGltfVisualLoader");
			MethodInfo visualRotation = visualLoader.GetMethod("VisualLocalRotation", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalRotation");
			MethodInfo visualYaw = visualLoader.GetMethod("VisualLocalYawDegrees", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalYawDegrees");
			MethodInfo visualScale = visualLoader.GetMethod("VisualLocalScale", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalScale");
			MethodInfo visualScaleComponents = visualLoader.GetMethod("VisualLocalScaleComponents", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalScaleComponents");
			float yaw = (float)visualYaw.Invoke(null, new[] { xe });
			(float X, float Y, float Z) scale = ((float X, float Y, float Z))visualScaleComponents.Invoke(null, new[] { xe });
			Assert(NearlyEqual(yaw, 180f), "Dell XE8640 does not copy the R440 180-degree front/back correction.");
			Assert(GetCalledMethodNames(visualRotation).Contains("VisualLocalYawDegrees"), "The runtime rotation bypasses the tested Dell yaw correction.");
			Assert(GetCalledMethodNames(visualScale).Contains("VisualLocalScaleComponents"), "The runtime scale bypasses the tested Dell axis scale.");
			Assert(NearlyEqual(scale.X, 0.245f) && NearlyEqual(scale.Y, 0.30f) && NearlyEqual(scale.Z, 0.245f),
				"Dell XE8640 does not use the R440's axis-safe runtime scale.");

			MethodInfo configureColliders = visualLoader.GetMethod("ConfigureOriginalInteractionColliders", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "ConfigureOriginalInteractionColliders");
			Assert(GetCalledMethodNames(configureColliders).Contains("get_UsesDellStaticCarrierProfile"),
				"The XE8640/R440 shared profile is not used by the front-collider correction.");

			Type catalogInjector = RequireType(assembly, "DataCenterAutomatorServers.CatalogInjector");
			MethodInfo persistedIdentity = catalogInjector.GetMethod("PersistedServerMatchesDellCarrierSpec", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(catalogInjector.FullName, "PersistedServerMatchesDellCarrierSpec");
			Assert((bool)persistedIdentity.Invoke(null, new object[] { xe, "Server.Green2_42", 1 }),
				"The Green2 carrier compatibility predicate rejected a compatible workload family.");
			Assert(!(bool)persistedIdentity.Invoke(null, new object[] { xe, "Server.Green2_42", 0 }),
				"The carrier compatibility predicate accepts the wrong workload family.");
			MethodInfo recoverIdentity = catalogInjector.GetMethod("RecoverLoadedServerVariantIdentity", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(catalogInjector.FullName, "RecoverLoadedServerVariantIdentity");
			Assert(!GetCalledMethodNames(recoverIdentity).Contains("PersistedServerMatchesDellCarrierSpec"),
				"Carrier compatibility alone must never prove Dell ownership of a saved Green/Yellow server.");
		});

		Run("Dell R760 uses the proven Dell static carrier flow as a 2U server", () =>
		{
			Type specType = RequireType(assembly, "DataCenterAutomatorServers.ServerVariantSpec");
			Array all = (Array)(specType.GetField("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
				?? throw new MissingFieldException(specType.FullName, "All"));
			object r760 = all.Cast<object>().SingleOrDefault(candidate =>
				string.Equals(ReadProperty<string>(candidate, "FamilyKey"), "dell_r760", StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException("Dell R760 is missing from ServerVariantSpec.All.");

			Assert(ReadProperty<int>(r760, "RackUnits") == 2, "Dell R760 does not reserve two rack units.");
			Assert(ReadProperty<int>(r760, "HeldObjectType") == 2, "Dell R760 does not use the game's supported 2U held pose.");
			Assert(ReadProperty<bool>(r760, "UsesLenovoCarrierProfile"), "Dell R760 does not share the proven Dell purchase/rack/save interaction flow.");
			Assert(ReadProperty<bool>(r760, "UsesDellStaticCarrierProfile"), "Dell R760 does not share the R440 orientation/collider/rendering corrections.");
			Assert(ReadProperty<string>(r760, "ModelAssetRelativePath").EndsWith("PowerEdge_R760_static_game_ready.glb", StringComparison.OrdinalIgnoreCase),
				"Dell R760 points at the wrong runtime GLB.");

			Type visualLoader = RequireType(assembly, "DataCenterAutomatorServers.RuntimeGltfVisualLoader");
			MethodInfo visualYaw = visualLoader.GetMethod("VisualLocalYawDegrees", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalYawDegrees");
			MethodInfo visualScaleComponents = visualLoader.GetMethod("VisualLocalScaleComponents", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(visualLoader.FullName, "VisualLocalScaleComponents");
			float yaw = (float)visualYaw.Invoke(null, new[] { r760 });
			(float X, float Y, float Z) scale = ((float X, float Y, float Z))visualScaleComponents.Invoke(null, new[] { r760 });
			Assert(NearlyEqual(yaw, 180f), "Dell R760 does not copy the R440 180-degree front/back correction.");
			Assert(NearlyEqual(scale.X, 0.245f) && NearlyEqual(scale.Y, 0.30f) && NearlyEqual(scale.Z, 0.245f),
				"Dell R760 does not use the R440's axis-safe runtime scale.");
		});

		Run("Requested R440, Lenovo and XE8640 shop performance and prices are exact", () =>
		{
			Type specType = RequireType(assembly, "DataCenterAutomatorServers.ServerVariantSpec");
			Array all = (Array)(specType.GetField("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
				?? throw new MissingFieldException(specType.FullName, "All"));
			object Find(string familyKey) => all.Cast<object>().Single(candidate =>
				string.Equals(ReadProperty<string>(candidate, "FamilyKey"), familyKey, StringComparison.OrdinalIgnoreCase));

			object r440 = Find("dell_r440_lenovo_carrier");
			object lenovo = Find("lenovo_sr650");
			object xe8640 = Find("dell_xe8640_lenovo_carrier");

			Assert(ReadProperty<int>(r440, "Iops") == 5000 && ReadProperty<int>(r440, "Price") == 7000,
				"Dell R440 must be 5000 IOPS for 7000 dollars.");
			Assert(ReadProperty<string>(r440, "VariantDisplayName").Contains("5000 IOPS", StringComparison.Ordinal),
				"Dell R440 shop name does not show 5000 IOPS.");
			Assert(ReadProperty<int>(lenovo, "Iops") == 5000 && ReadProperty<int>(lenovo, "Price") == 7000,
				"Lenovo 1U must be 5000 IOPS for 7000 dollars.");
			Assert(ReadProperty<string>(lenovo, "VariantDisplayName").Contains("5000 IOPS", StringComparison.Ordinal),
				"Lenovo shop name does not show 5000 IOPS.");
			Assert(ReadProperty<int>(xe8640, "Iops") == 20000 && ReadProperty<int>(xe8640, "Price") == 15000,
				"Dell XE8640 must be 20000 IOPS for 15000 dollars.");
			Assert(ReadProperty<string>(xe8640, "VariantDisplayName").Contains("20000 IOPS", StringComparison.Ordinal),
				"Dell XE8640 shop name does not show 20000 IOPS.");
		});

		Run("Modded server identity is persisted before rack insertion and restored after native loading", () =>
		{
			Type gameServer = RequireType(gameAssembly, "Il2Cpp.Server");
			Assert(gameServer.GetMethod("OnLoadingComplete", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null,
				"This game build has no Server.OnLoadingComplete lifecycle method for deterministic post-save restoration.");
			MethodInfo generateUniqueId = gameServer.GetMethod("GenerateUniqueServerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(gameServer.FullName, "GenerateUniqueServerId");
			Assert(generateUniqueId.ReturnType == typeof(string) && generateUniqueId.GetParameters().Length == 0,
				"The native unique-server-ID method no longer matches the loose-server persistence hook.");

			Type patchRegistry = RequireType(assembly, "DataCenterAutomatorServers.PatchRegistry");
			MethodInfo apply = patchRegistry.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(patchRegistry.FullName, "Apply");
			Assert(GetStringLiterals(apply).Contains("OnLoadingComplete"),
				"Server.OnLoadingComplete is not patched, so the game can overwrite the restored variant after loading a save.");

			MethodInfo lifecyclePostfix = patchRegistry.GetMethod("ServerLifecycleRepairPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(patchRegistry.FullName, "ServerLifecycleRepairPostfix");
			Assert(GetCalledMethodNames(lifecyclePostfix).Contains("TryPersistKnownServerIdentityFromPatch"),
				"The lifecycle hook does not persist a purchased server's unique ID until it is inserted in a rack.");

			Type injector = RequireType(assembly, "DataCenterAutomatorServers.CatalogInjector");
			MethodInfo persistIdentity = injector.GetMethod("PersistKnownServerIdentity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(injector.FullName, "PersistKnownServerIdentity");
			Assert(GetCalledMethodNames(persistIdentity).Contains("EnsureUniqueServerIdentity"),
				"A loose modded server keeps the shared Server.Green2 base ID and cannot be distinguished in the save.");
		});

		Run("Loaded cables are rebuilt only after modded server ports are restored", () =>
		{
			Type cablePositions = RequireType(gameAssembly, "Il2Cpp.CablePositions");
			MethodInfo gameLoadCable = cablePositions.GetMethod("LoadCable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(cablePositions.FullName, "LoadCable");
			MethodInfo gameRedrawCable = cablePositions.GetMethod("RedrawCable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(cablePositions.FullName, "RedrawCable");
			Assert(gameLoadCable.GetParameters().Length == 1 && gameLoadCable.GetParameters()[0].Name == "cableData",
				"CablePositions.LoadCable no longer exposes the cableData argument expected by the Harmony hook.");
			Assert(gameRedrawCable.GetParameters().Length == 1 && gameRedrawCable.GetParameters()[0].ParameterType == typeof(int),
				"CablePositions.RedrawCable no longer accepts a cable ID integer.");

			Type patchRegistry = RequireType(assembly, "DataCenterAutomatorServers.PatchRegistry");
			MethodInfo apply = patchRegistry.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(patchRegistry.FullName, "Apply");
			Assert(GetCalledMethodNames(apply).Contains("PatchCableLoadMethods"),
				"CablePositions.LoadCable is not patched, so saved endpoints are rebuilt against the native port positions.");

			MethodInfo prefix = patchRegistry.GetMethod("CableLoadPrefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(patchRegistry.FullName, "CableLoadPrefix");
			MethodInfo postfix = patchRegistry.GetMethod("CableLoadPostfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(patchRegistry.FullName, "CableLoadPostfix");
			Assert(GetCalledMethodNames(prefix).Contains("TryPrepareLoadedCableFromPatch"),
				"Loaded cable endpoints are not prepared against restored modded servers.");
			Assert(GetCalledMethodNames(postfix).Contains("TryFinalizeLoadedCableFromPatch"),
				"Loaded cables are not queued for a final redraw.");

			Type injector = RequireType(assembly, "DataCenterAutomatorServers.CatalogInjector");
			MethodInfo redraw = injector.GetMethod("RedrawLoadedCablesAfterRestore", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(injector.FullName, "RedrawLoadedCablesAfterRestore");
			MethodInfo redrawOne = injector.GetMethod("RedrawLoadedCable", BindingFlags.Static | BindingFlags.NonPublic)
				?? throw new MissingMethodException(injector.FullName, "RedrawLoadedCable");
			Assert(GetCalledMethodNames(redrawOne).Contains("InvokeMethod"),
				"The post-load cable repair never invokes the game's native RedrawCable path.");
		});

		Console.WriteLine($"Real Servers regression tests: {11 - _failed}/11 passed.");
		return _failed == 0 ? 0 : 1;
	}

	private static bool NearlyEqual(float left, float right) => Math.Abs(left - right) < 0.0001f;

	private static T ReadProperty<T>(object instance, string name) =>
		(T)(instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
			?? throw new MissingMemberException(instance.GetType().FullName, name));

	private static void AssertHasNoNotebookMethods(Type type)
	{
		string[] methods = GetDeclaredMethodNames(type);
		string[] notebookMethods = methods
			.Where(name => name.Contains("Notebook", StringComparison.OrdinalIgnoreCase))
			.ToArray();
		Assert(notebookMethods.Length == 0,
			$"{type.FullName} still exposes notebook methods: {string.Join(", ", notebookMethods)}.");
	}

	private static string[] GetDeclaredMethodNames(Type type) =>
		type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
			.Select(method => method.Name)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

	private static string[] GetCalledMethodNames(MethodInfo method)
	{
		byte[] il = method.GetMethodBody()?.GetILAsByteArray()
			?? throw new InvalidOperationException($"{method.Name} has no IL body.");
		Dictionary<short, OpCode> opCodes = typeof(OpCodes)
			.GetFields(BindingFlags.Static | BindingFlags.Public)
			.Where(field => field.FieldType == typeof(OpCode))
			.Select(field => (OpCode)field.GetValue(null))
			.ToDictionary(opCode => opCode.Value);
		List<string> calledMethods = new();
		int offset = 0;
		while (offset < il.Length)
		{
			short value = il[offset++] == 0xFE
				? (short)(0xFE00 | il[offset++])
				: (short)il[offset - 1];
			OpCode opCode = opCodes[value];
			if (opCode.OperandType == OperandType.InlineMethod)
			{
				int token = BitConverter.ToInt32(il, offset);
				MethodBase called = method.Module.ResolveMethod(
					token,
					method.DeclaringType?.GetGenericArguments(),
					method.IsGenericMethod ? method.GetGenericArguments() : null);
				calledMethods.Add(called.Name);
			}
			offset += OperandSize(opCode.OperandType, il, offset);
		}
		return calledMethods.Distinct(StringComparer.Ordinal).ToArray();
	}

	private static string[] GetStringLiterals(MethodInfo method)
	{
		byte[] il = method.GetMethodBody()?.GetILAsByteArray()
			?? throw new InvalidOperationException($"{method.Name} has no IL body.");
		Dictionary<short, OpCode> opCodes = typeof(OpCodes)
			.GetFields(BindingFlags.Static | BindingFlags.Public)
			.Where(field => field.FieldType == typeof(OpCode))
			.Select(field => (OpCode)field.GetValue(null))
			.ToDictionary(opCode => opCode.Value);
		List<string> values = new();
		int offset = 0;
		while (offset < il.Length)
		{
			short value = il[offset++] == 0xFE
				? (short)(0xFE00 | il[offset++])
				: (short)il[offset - 1];
			OpCode opCode = opCodes[value];
			if (opCode == OpCodes.Ldstr)
			{
				values.Add(method.Module.ResolveString(BitConverter.ToInt32(il, offset)));
			}
			offset += OperandSize(opCode.OperandType, il, offset);
		}
		return values.Distinct(StringComparer.Ordinal).ToArray();
	}

	private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
	{
		OperandType.InlineNone => 0,
		OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
		OperandType.InlineVar => 2,
		OperandType.InlineI or OperandType.InlineBrTarget or OperandType.ShortInlineR or
		OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig or
		OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType => 4,
		OperandType.InlineI8 or OperandType.InlineR => 8,
		OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
		_ => throw new NotSupportedException($"Unsupported IL operand: {operandType}"),
	};

	private static Type RequireType(Assembly assembly, string name) =>
		assembly.GetType(name, throwOnError: true)
		?? throw new TypeLoadException(name);

	private static void Run(string name, Action test)
	{
		try
		{
			test();
			Console.WriteLine("PASS " + name);
		}
		catch (Exception exception)
		{
			_failed++;
			Console.WriteLine("FAIL " + name);
			Console.WriteLine("     " + exception.GetType().Name + ": " + exception.Message);
		}
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
